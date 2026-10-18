import { createCipheriv, createHash, randomBytes } from "node:crypto";

import { prepareMediaBytes, type PreparedMediaBytes } from "@dotcraft/channel/media";
import {
  buildFileMessageReq,
  buildImageMessageReq,
  getUploadUrl,
  sendMessage,
  type WeixinApiOptions,
} from "./weixin-api.js";
import { UploadMediaType, type CDNMedia, type GetUploadUrlReq, type GetUploadUrlResp, type SendMessageReq } from "./weixin-types.js";

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

function formatErrorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

function wrapMediaStageError(stage: string, code: string, error: unknown): WeixinMediaError {
  if (error instanceof WeixinMediaError) return error;
  return new WeixinMediaError(code, `${stage} failed: ${formatErrorMessage(error)}`);
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

export class WeixinMediaSender {
  constructor(
    private readonly api: WeixinMediaApi = { getUploadUrl, sendMessage },
    private readonly cdnBaseUrl = DEFAULT_CDN_BASE_URL,
  ) {}

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
      optionalText(opts.message.mediaType),
      fallbackName,
    );
    const uploaded = kind === "image"
      ? await this.uploadImage(opts, prepared)
      : await this.uploadFile(opts, prepared);
    try {
      await this.api.sendMessage({
        baseUrl: opts.baseUrl,
        token: opts.token,
        body: uploaded.body,
      });
    } catch (error) {
      throw wrapMediaStageError("sendMessage", "MediaMessageSendFailed", error);
    }
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
    try {
      return await this.api.getUploadUrl({
        baseUrl: opts.baseUrl,
        token: opts.token,
        body,
      });
    } catch (error) {
      throw wrapMediaStageError("getUploadUrl", "UploadUrlRequestFailed", error);
    }
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
      let response: Response;
      try {
        response = await fetch(uploadUrl, {
          method: "POST",
          headers: { "Content-Type": "application/octet-stream" },
          body: new Uint8Array(encrypted),
        });
      } catch (error) {
        lastFailure = `${params.label} CDN upload failed: ${formatErrorMessage(error)}`;
        if (attempt === MAX_CDN_UPLOAD_ATTEMPTS) break;
        continue;
      }
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

interface PreparedMedia extends PreparedMediaBytes {
  fileKey: string;
}

async function prepareMediaSource(
  source: Record<string, unknown>,
  requestedFileName: string | undefined,
  mediaType: string | undefined,
  fallbackFileName: string,
): Promise<PreparedMedia> {
  const prepared = await prepareMediaBytes(source, {
    fileName: requestedFileName,
    mediaType,
    fallbackFileName,
    errorFactory: weixinMediaError,
  });
  return {
    ...prepared,
    fileKey: randomBytes(16).toString("hex"),
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

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}

function weixinMediaError(code: string, message: string): WeixinMediaError {
  return new WeixinMediaError(code, message);
}
