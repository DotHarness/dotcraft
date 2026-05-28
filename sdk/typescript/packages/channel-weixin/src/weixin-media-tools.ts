import { createCipheriv, createHash, randomBytes } from "node:crypto";
import { readFile } from "node:fs/promises";
import { basename, resolve } from "node:path";

import {
  buildFileMessageReq,
  buildImageMessageReq,
  getUploadUrl,
  sendMessage,
  type WeixinApiOptions,
} from "./weixin-api.js";
import { UploadMediaType, type CDNMedia, type GetUploadUrlReq, type GetUploadUrlResp, type SendMessageReq } from "./weixin-types.js";

export const WEIXIN_SEND_FILE_TOOL = "WeixinSendFileToCurrentChat";
export const WEIXIN_SEND_IMAGE_TOOL = "WeixinSendImageToCurrentChat";

const DEFAULT_CDN_BASE_URL = "https://novac2c.cdn.weixin.qq.com/c2c";
const MAX_CDN_UPLOAD_ATTEMPTS = 3;

export class WeixinMediaError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "WeixinMediaError";
    this.code = code;
  }
}

export interface WeixinMediaApi {
  getUploadUrl(opts: WeixinApiOptions & { body: GetUploadUrlReq }): Promise<GetUploadUrlResp>;
  sendMessage(opts: WeixinApiOptions & { body: SendMessageReq }): Promise<void>;
}

export interface WeixinMediaDeliveryOptions {
  baseUrl: string;
  token?: string;
  toUserId: string;
  contextToken?: string;
  clientId: string;
  message: Record<string, unknown>;
}

export class WeixinMediaTools {
  constructor(
    private readonly api: WeixinMediaApi = { getUploadUrl, sendMessage },
    private readonly cdnBaseUrl = DEFAULT_CDN_BASE_URL,
  ) {}

  getDeliveryCapabilities(): Record<string, unknown> {
    return {
      structuredDelivery: true,
      media: {
        file: {
          supportsHostPath: true,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
        },
        image: {
          supportsHostPath: true,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
        },
      },
    };
  }

  getChannelTools(): Record<string, unknown>[] {
    return [
      {
        name: WEIXIN_SEND_FILE_TOOL,
        description: "Send a real file attachment to the current Weixin chat.",
        requiresChatContext: true,
        display: { icon: "\u{1F4CE}", title: "Send file to current Weixin chat" },
        approval: {
          required: true,
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
        inputSchema: {
          type: "object",
          properties: {
            filePath: { type: "string" },
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["filePath"],
        },
      },
      {
        name: WEIXIN_SEND_IMAGE_TOOL,
        description: "Send a real image to the current Weixin chat.",
        requiresChatContext: true,
        display: { icon: "\u{1F5BC}", title: "Send image to current Weixin chat" },
        approval: {
          required: true,
          kind: "file",
          targetArgument: "imagePath",
          operation: "read",
        },
        inputSchema: {
          type: "object",
          properties: {
            imagePath: { type: "string" },
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["imagePath"],
        },
      },
    ];
  }

  async sendStructuredMessage(opts: WeixinMediaDeliveryOptions): Promise<Record<string, unknown>> {
    const kind = String(opts.message.kind ?? "");
    if (kind !== "file" && kind !== "image") {
      return {
        delivered: false,
        errorCode: "UnsupportedDeliveryKind",
        errorMessage: `Weixin channel does not support '${kind}' media delivery.`,
      };
    }

    const fallbackName = kind === "image" ? "image.jpg" : "attachment";
    const prepared = await prepareMediaSource(
      asRecord(opts.message.source),
      optionalText(opts.message.fileName),
      fallbackName,
    );
    const uploaded = kind === "image"
      ? await this.uploadImage(opts, prepared)
      : await this.uploadFile(opts, prepared);
    await this.api.sendMessage({
      baseUrl: opts.baseUrl,
      token: opts.token,
      body: uploaded.body,
    });
    return {
      delivered: true,
      remoteMediaId: uploaded.media.encrypt_query_param ?? null,
      effectiveSourceKind: prepared.sourceKind,
      uploadStage: "completed",
      sendStage: "completed",
      mediaKind: kind,
      fileName: prepared.fileName,
      md5: prepared.md5,
      bytes: prepared.bytes.length,
    };
  }

  async executeToolCall(
    opts: Omit<WeixinMediaDeliveryOptions, "message" | "clientId"> & {
      toolName: string;
      args: Record<string, unknown>;
      clientId: string;
    },
  ): Promise<Record<string, unknown>> {
    const message = this.createToolMessage(opts.toolName, opts.args);
    const result = await this.sendStructuredMessage({ ...opts, message });
    const delivered = Boolean(result.delivered);
    const fileName = String(result.fileName ?? message.fileName ?? "attachment");
    const noun = String(message.kind ?? "") === "image" ? "image" : "file";
    return {
      success: delivered,
      contentItems: [
        {
          type: "text",
          text: delivered
            ? `Sent ${noun} '${fileName}' to the current Weixin chat.`
            : String(result.errorMessage ?? "Tool execution failed."),
        },
      ],
      structuredResult: {
        delivered,
        errorCode: result.errorCode ?? null,
        mediaId: result.remoteMediaId ?? null,
        fileName,
      },
      errorCode: result.errorCode,
      errorMessage: result.errorMessage,
    };
  }

  private createToolMessage(toolName: string, args: Record<string, unknown>): Record<string, unknown> {
    if (toolName === WEIXIN_SEND_FILE_TOOL) {
      const filePath = requiredText(args.filePath, "filePath");
      return {
        kind: "file",
        fileName: optionalText(args.fileName) ?? basename(filePath),
        caption: optionalText(args.caption),
        source: { kind: "hostPath", hostPath: filePath },
      };
    }
    if (toolName === WEIXIN_SEND_IMAGE_TOOL) {
      const imagePath = requiredText(args.imagePath, "imagePath");
      return {
        kind: "image",
        fileName: optionalText(args.fileName) ?? basename(imagePath),
        caption: optionalText(args.caption),
        source: { kind: "hostPath", hostPath: imagePath },
      };
    }
    throw new WeixinMediaError("UnsupportedTool", `Unknown tool '${toolName}'.`);
  }

  private async uploadFile(
    opts: WeixinMediaDeliveryOptions,
    prepared: PreparedMedia,
  ): Promise<{ body: SendMessageReq; media: CDNMedia }> {
    const aesKey = randomBytes(16);
    const aesKeyHex = aesKey.toString("hex");
    const mediaAesKey = encodeMediaAesKey(aesKeyHex);
    const upload = await this.requestUploadUrl(opts, prepared, UploadMediaType.FILE, aesKeyHex);
    const media = await this.uploadBufferToCdn({
      buf: prepared.bytes,
      uploadFullUrl: upload.upload_full_url,
      uploadParam: upload.upload_param,
      filekey: prepared.fileKey,
      label: "file",
      aesKey,
      mediaAesKey,
    });
    return {
      media,
      body: buildFileMessageReq({
        toUserId: opts.toUserId,
        contextToken: opts.contextToken,
        clientId: opts.clientId,
        fileName: prepared.fileName,
        media,
        byteLength: prepared.bytes.length,
      }),
    };
  }

  private async uploadImage(
    opts: WeixinMediaDeliveryOptions,
    prepared: PreparedMedia,
  ): Promise<{ body: SendMessageReq; media: CDNMedia }> {
    const aesKey = randomBytes(16);
    const aesKeyHex = aesKey.toString("hex");
    const mediaAesKey = encodeMediaAesKey(aesKeyHex);
    const upload = await this.requestUploadUrl(opts, prepared, UploadMediaType.IMAGE, aesKeyHex);
    const media = await this.uploadBufferToCdn({
      buf: prepared.bytes,
      uploadFullUrl: upload.upload_full_url,
      uploadParam: upload.upload_param,
      filekey: prepared.fileKey,
      label: "image",
      aesKey,
      mediaAesKey,
    });
    return {
      media,
      body: buildImageMessageReq({
        toUserId: opts.toUserId,
        contextToken: opts.contextToken,
        clientId: opts.clientId,
        media,
        ciphertextByteLength: aesEcbPaddedSize(prepared.bytes.length),
      }),
    };
  }

  private async requestUploadUrl(
    opts: WeixinMediaDeliveryOptions,
    prepared: PreparedMedia,
    mediaType: number,
    aesKeyHex: string,
  ): Promise<GetUploadUrlResp> {
    const body: GetUploadUrlReq = {
      filekey: prepared.fileKey,
      media_type: mediaType,
      to_user_id: opts.toUserId,
      rawsize: prepared.bytes.length,
      rawfilemd5: prepared.md5,
      filesize: aesEcbPaddedSize(prepared.bytes.length),
      no_need_thumb: true,
      aeskey: aesKeyHex,
    };
    return await this.api.getUploadUrl({
      baseUrl: opts.baseUrl,
      token: opts.token,
      body,
    });
  }

  private async uploadBufferToCdn(params: {
    buf: Buffer;
    uploadFullUrl?: string;
    uploadParam?: string;
    filekey: string;
    label: string;
    aesKey: Buffer;
    mediaAesKey: string;
  }): Promise<CDNMedia> {
    const uploadUrl = buildCdnUploadUrl({
      cdnBaseUrl: this.cdnBaseUrl,
      filekey: params.filekey,
      uploadFullUrl: params.uploadFullUrl,
      uploadParam: params.uploadParam,
    });
    const encrypted = encryptAesEcb(params.buf, params.aesKey);
    let lastFailure = "";
    for (let attempt = 1; attempt <= MAX_CDN_UPLOAD_ATTEMPTS; attempt += 1) {
      const response = await fetch(uploadUrl, {
        method: "POST",
        headers: { "Content-Type": "application/octet-stream" },
        body: new Uint8Array(encrypted),
      });
      if (response.ok) {
        const downloadParam = response.headers.get("x-encrypted-param") ?? "";
        if (!downloadParam) {
          throw new WeixinMediaError("CdnUploadFailed", `${params.label} upload response missing x-encrypted-param.`);
        }
        return {
          encrypt_query_param: downloadParam,
          aes_key: params.mediaAesKey,
          encrypt_type: 1,
        };
      }

      const detail = await formatCdnErrorDetail(response);
      lastFailure = `${params.label} upload failed: HTTP ${response.status}${detail ? ` ${detail}` : ""}`;
      if (response.status < 500 || response.status >= 600 || attempt === MAX_CDN_UPLOAD_ATTEMPTS) {
        break;
      }
    }
    throw new WeixinMediaError("CdnUploadFailed", lastFailure);
  }
}

interface PreparedMedia {
  bytes: Buffer;
  fileName: string;
  fileKey: string;
  md5: string;
  sourceKind: string;
}

async function prepareMediaSource(
  source: Record<string, unknown>,
  requestedFileName: string | undefined,
  fallbackFileName: string,
): Promise<PreparedMedia> {
  const kind = String(source.kind ?? "");
  let bytes: Buffer;
  let fileName = requestedFileName ?? (fallbackFileName.trim() || "attachment");
  if (kind === "hostPath") {
    const path = resolve(requiredText(source.hostPath, "hostPath"));
    bytes = await readFile(path).catch((error: unknown) => {
      throw new WeixinMediaError(
        "InvalidArguments",
        `Cannot read file '${path}': ${error instanceof Error ? error.message : String(error)}`,
      );
    });
    if (!requestedFileName) fileName = basename(path);
  } else if (kind === "dataBase64") {
    bytes = decodeBase64(requiredText(source.dataBase64, "dataBase64"));
  } else {
    throw new WeixinMediaError("UnsupportedMediaSource", `Weixin media delivery only supports hostPath or dataBase64, got '${kind || "unknown"}'.`);
  }
  return {
    bytes,
    fileName,
    fileKey: randomBytes(16).toString("hex"),
    md5: md5Hex(bytes),
    sourceKind: kind,
  };
}

export function encryptAesEcb(plaintext: Buffer, key: Buffer): Buffer {
  const cipher = createCipheriv("aes-128-ecb", key, null);
  return Buffer.concat([cipher.update(plaintext), cipher.final()]);
}

export function aesEcbPaddedSize(plaintextSize: number): number {
  return Math.ceil((plaintextSize + 1) / 16) * 16;
}

export function md5Hex(bytes: Buffer): string {
  return createHash("md5").update(bytes).digest("hex");
}

export function buildCdnUploadUrl(params: {
  cdnBaseUrl: string;
  uploadFullUrl?: string;
  uploadParam?: string;
  filekey: string;
}): string {
  const full = params.uploadFullUrl?.trim();
  if (full) return full;
  const uploadParam = params.uploadParam?.trim();
  if (!uploadParam) {
    throw new WeixinMediaError("CdnUploadUrlMissing", "CDN upload URL missing; expected upload_full_url or upload_param.");
  }
  return `${params.cdnBaseUrl}/upload?encrypted_query_param=${encodeURIComponent(uploadParam)}&filekey=${encodeURIComponent(params.filekey)}`;
}

function encodeMediaAesKey(aesKeyHex: string): string {
  return Buffer.from(aesKeyHex, "utf-8").toString("base64");
}

async function formatCdnErrorDetail(response: Response): Promise<string> {
  const header = response.headers.get("x-error-message")?.trim();
  const body = (await response.text()).trim();
  return [header, body].filter(Boolean).join(" ");
}

function decodeBase64(value: string): Buffer {
  const normalized = value.replace(/\s+/g, "");
  if (!normalized || normalized.length % 4 === 1 || /[^A-Za-z0-9+/=]/.test(normalized)) {
    throw new WeixinMediaError("InvalidArguments", "dataBase64 did not contain valid base64.");
  }
  return Buffer.from(normalized, "base64");
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function requiredText(value: unknown, field: string): string {
  const text = optionalText(value);
  if (!text) throw new WeixinMediaError("InvalidArguments", `${field} is required.`);
  return text;
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}
