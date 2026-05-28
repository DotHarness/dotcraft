import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { extname, join } from "node:path";

import type { OneBotActionResponse } from "./onebot.js";
import {
  isActionOk,
  recordSegment,
  sendGroupMessageAction,
  sendPrivateMessageAction,
  textSegment,
  uploadGroupFileAction,
  uploadPrivateFileAction,
  videoSegment,
  type OneBotMessageSegment,
  type OneBotReverseWsServer,
} from "./onebot.js";
import { parseQQTarget, type QQTarget } from "./target.js";

export const QQ_SEND_GROUP_VOICE_TOOL = "QQSendGroupVoice";
export const QQ_SEND_PRIVATE_VOICE_TOOL = "QQSendPrivateVoice";
export const QQ_SEND_GROUP_VIDEO_TOOL = "QQSendGroupVideo";
export const QQ_SEND_PRIVATE_VIDEO_TOOL = "QQSendPrivateVideo";
export const QQ_UPLOAD_GROUP_FILE_TOOL = "QQUploadGroupFile";
export const QQ_UPLOAD_PRIVATE_FILE_TOOL = "QQUploadPrivateFile";

export class QQMediaError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "QQMediaError";
    this.code = code;
  }
}

export class QQMediaTools {
  getDeliveryCapabilities(): Record<string, unknown> {
    return {
      structuredDelivery: true,
      media: {
        audio: {
          supportsHostPath: true,
          supportsUrl: true,
          supportsBase64: true,
        },
        file: {
          supportsHostPath: true,
          supportsBase64: true,
        },
        video: {
          supportsHostPath: true,
          supportsUrl: true,
        },
      },
    };
  }

  getChannelTools(): Record<string, unknown>[] {
    return [
      {
        name: QQ_SEND_GROUP_VOICE_TOOL,
        description:
          "Send a voice/audio message to a QQ group chat. The file can be a local absolute path, an HTTP URL, or a base64:// string.",
        requiresChatContext: false,
        display: { icon: "\u{1F3A4}", title: "Send voice to QQ group" },
        inputSchema: {
          type: "object",
          properties: {
            groupId: { type: "integer" },
            file: { type: "string" },
          },
          required: ["groupId", "file"],
        },
      },
      {
        name: QQ_SEND_PRIVATE_VOICE_TOOL,
        description:
          "Send a voice/audio message to a QQ private chat. The file can be a local absolute path, an HTTP URL, or a base64:// string.",
        requiresChatContext: false,
        display: { icon: "\u{1F3A4}", title: "Send voice to QQ user" },
        inputSchema: {
          type: "object",
          properties: {
            userId: { type: "integer" },
            file: { type: "string" },
          },
          required: ["userId", "file"],
        },
      },
      {
        name: QQ_SEND_GROUP_VIDEO_TOOL,
        description: "Send a video message to a QQ group chat. The file can be a local absolute path or an HTTP URL.",
        requiresChatContext: false,
        display: { icon: "\u{1F39E}", title: "Send video to QQ group" },
        inputSchema: {
          type: "object",
          properties: {
            groupId: { type: "integer" },
            file: { type: "string" },
          },
          required: ["groupId", "file"],
        },
      },
      {
        name: QQ_SEND_PRIVATE_VIDEO_TOOL,
        description: "Send a video message to a QQ private chat. The file can be a local absolute path or an HTTP URL.",
        requiresChatContext: false,
        display: { icon: "\u{1F39E}", title: "Send video to QQ user" },
        inputSchema: {
          type: "object",
          properties: {
            userId: { type: "integer" },
            file: { type: "string" },
          },
          required: ["userId", "file"],
        },
      },
      {
        name: QQ_UPLOAD_GROUP_FILE_TOOL,
        description: "Upload a file to a QQ group using upload_group_file. The file must be a local absolute path.",
        requiresChatContext: false,
        display: { icon: "\u{1F4CE}", title: "Upload file to QQ group" },
        inputSchema: {
          type: "object",
          properties: {
            groupId: { type: "integer" },
            filePath: { type: "string" },
            fileName: { type: "string" },
            folder: { type: "string" },
          },
          required: ["groupId", "filePath", "fileName"],
        },
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
      },
      {
        name: QQ_UPLOAD_PRIVATE_FILE_TOOL,
        description: "Upload a file to a QQ private chat using upload_private_file. The file must be a local absolute path.",
        requiresChatContext: false,
        display: { icon: "\u{1F4CE}", title: "Upload file to QQ user" },
        inputSchema: {
          type: "object",
          properties: {
            userId: { type: "integer" },
            filePath: { type: "string" },
            fileName: { type: "string" },
          },
          required: ["userId", "filePath", "fileName"],
        },
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
      },
    ];
  }

  async sendStructuredMessage(
    server: OneBotReverseWsServer,
    target: string,
    message: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const parsed = parseQQTarget(target);
    if (!parsed) {
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: `Invalid QQ target '${target}'.`,
      };
    }

    const kind = String(message.kind ?? "");
    if (kind === "text") {
      const response = await this.sendText(server, parsed, String(message.text ?? ""));
      return toDeliveryResult(response);
    }
    if (kind === "audio") {
      const file = await this.resolveAudioSource(asRecord(message.source), String(message.fileName ?? "audio.bin"));
      const response = await this.sendMessage(server, parsed, [recordSegment(file)]);
      return toDeliveryResult(response);
    }
    if (kind === "video") {
      const file = this.resolveVideoSource(asRecord(message.source));
      const response = await this.sendMessage(server, parsed, [videoSegment(file)]);
      return toDeliveryResult(response);
    }
    if (kind === "file") {
      return await this.sendFile(server, parsed, message);
    }

    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `QQ channel does not support '${kind}' delivery.`,
    };
  }

  async executeToolCall(
    server: OneBotReverseWsServer,
    toolName: string,
    args: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const request = await this.createToolDelivery(toolName, args);
    if (!request) {
      return {
        success: false,
        errorCode: "UnsupportedChannelTool",
        errorMessage: `QQ does not expose tool '${toolName}'.`,
      };
    }

    const result = await this.sendStructuredMessage(server, request.target, request.message);
    return {
      success: Boolean(result.delivered),
      contentItems: [
        {
          type: "text",
          text: result.delivered ? "Message sent." : String(result.errorMessage ?? "Tool execution failed."),
        },
      ],
      structuredResult: {
        delivered: Boolean(result.delivered),
        errorCode: result.errorCode ?? null,
        target: request.target,
      },
      errorCode: result.errorCode,
      errorMessage: result.errorMessage,
    };
  }

  private async createToolDelivery(
    toolName: string,
    args: Record<string, unknown>,
  ): Promise<{ target: string; message: Record<string, unknown> } | null> {
    switch (toolName) {
      case QQ_SEND_GROUP_VOICE_TOOL:
        return { target: `group:${requiredId(args.groupId, "groupId")}`, message: mediaMessage("audio", parseLegacyFileSource(args.file)) };
      case QQ_SEND_PRIVATE_VOICE_TOOL:
        return { target: requiredId(args.userId, "userId"), message: mediaMessage("audio", parseLegacyFileSource(args.file)) };
      case QQ_SEND_GROUP_VIDEO_TOOL:
        return { target: `group:${requiredId(args.groupId, "groupId")}`, message: mediaMessage("video", parseLegacyFileSource(args.file)) };
      case QQ_SEND_PRIVATE_VIDEO_TOOL:
        return { target: requiredId(args.userId, "userId"), message: mediaMessage("video", parseLegacyFileSource(args.file)) };
      case QQ_UPLOAD_GROUP_FILE_TOOL:
        return {
          target: `group:${requiredId(args.groupId, "groupId")}`,
          message: {
            kind: "file",
            fileName: requiredText(args.fileName, "fileName"),
            folder: optionalText(args.folder),
            source: { kind: "hostPath", hostPath: requiredText(args.filePath, "filePath") },
          },
        };
      case QQ_UPLOAD_PRIVATE_FILE_TOOL:
        return {
          target: requiredId(args.userId, "userId"),
          message: {
            kind: "file",
            fileName: requiredText(args.fileName, "fileName"),
            source: { kind: "hostPath", hostPath: requiredText(args.filePath, "filePath") },
          },
        };
      default:
        return null;
    }
  }

  private async sendText(server: OneBotReverseWsServer, target: QQTarget, text: string): Promise<OneBotActionResponse> {
    return await this.sendMessage(server, target, [textSegment(text)]);
  }

  private async sendMessage(
    server: OneBotReverseWsServer,
    target: QQTarget,
    message: OneBotMessageSegment[],
  ): Promise<OneBotActionResponse> {
    const action = target.kind === "group"
      ? sendGroupMessageAction(target.id, message)
      : sendPrivateMessageAction(target.id, message);
    return await server.sendAction(action);
  }

  private async sendFile(
    server: OneBotReverseWsServer,
    target: QQTarget,
    message: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const source = asRecord(message.source);
    const fileName = optionalText(message.fileName) ?? inferFileName(source) ?? "attachment.bin";
    const folder = optionalText(message.folder);
    let cleanupPath: string | undefined;
    try {
      const file = await resolveFileSource(source, fileName);
      cleanupPath = file.cleanupPath;
      const response = target.kind === "group"
        ? await server.sendAction(uploadGroupFileAction(target.id, file.path, fileName, folder))
        : await server.sendAction(uploadPrivateFileAction(target.id, file.path, fileName));
      return toDeliveryResult(response);
    } finally {
      if (cleanupPath) await rm(cleanupPath, { force: true }).catch(() => undefined);
    }
  }

  private async resolveAudioSource(source: Record<string, unknown>, fileName: string): Promise<string> {
    const kind = String(source.kind ?? "");
    if (kind === "hostPath") {
      return `base64://${await fileToBase64(requiredText(source.hostPath, "hostPath"))}`;
    }
    if (kind === "url") return requiredText(source.url, "url");
    if (kind === "dataBase64") return `base64://${requiredText(source.dataBase64, "dataBase64")}`;
    if (kind === "") return await this.resolveAudioSource(parseLegacyFileSource(fileName).source as Record<string, unknown>, fileName);
    throw new QQMediaError("UnsupportedMediaSource", `Unsupported QQ audio source kind '${kind}'.`);
  }

  private resolveVideoSource(source: Record<string, unknown>): string {
    const kind = String(source.kind ?? "");
    if (kind === "hostPath") return requiredText(source.hostPath, "hostPath");
    if (kind === "url") return requiredText(source.url, "url");
    throw new QQMediaError("UnsupportedMediaSource", `Unsupported QQ video source kind '${kind}'.`);
  }
}

function mediaMessage(kind: string, payload: Record<string, unknown>): Record<string, unknown> {
  return { kind, source: payload.source };
}

function parseLegacyFileSource(value: unknown): Record<string, unknown> {
  const file = requiredText(value, "file");
  if (file.toLowerCase().startsWith("base64://")) {
    return { source: { kind: "dataBase64", dataBase64: file.slice("base64://".length) } };
  }
  if (/^https?:\/\//i.test(file)) {
    return { source: { kind: "url", url: file } };
  }
  return { source: { kind: "hostPath", hostPath: file } };
}

async function resolveFileSource(source: Record<string, unknown>, fileName: string): Promise<{ path: string; cleanupPath?: string }> {
  const kind = String(source.kind ?? "");
  if (kind === "hostPath") return { path: requiredText(source.hostPath, "hostPath") };
  if (kind === "dataBase64") {
    const dir = await mkdtemp(join(tmpdir(), "dotcraft-qq-file-"));
    const path = join(dir, fileName);
    await writeFile(path, Buffer.from(requiredText(source.dataBase64, "dataBase64"), "base64"));
    return { path, cleanupPath: dir };
  }
  throw new QQMediaError("UnsupportedMediaSource", `QQ file delivery only supports hostPath or dataBase64, got '${kind}'.`);
}

async function fileToBase64(path: string): Promise<string> {
  const { readFile } = await import("node:fs/promises");
  return (await readFile(path)).toString("base64");
}

function inferFileName(source: Record<string, unknown>): string | null {
  const hostPath = optionalText(source.hostPath);
  if (hostPath) {
    const name = hostPath.split(/[\\/]/).pop();
    if (name) return name;
  }
  const ext = extname(optionalText(source.url) ?? "");
  return ext ? `attachment${ext}` : null;
}

function toDeliveryResult(response: OneBotActionResponse): Record<string, unknown> {
  return {
    delivered: isActionOk(response),
    errorCode: isActionOk(response) ? undefined : "AdapterDeliveryFailed",
    errorMessage: isActionOk(response) ? undefined : response.message ?? response.wording ?? `retcode=${response.retcode ?? "unknown"}`,
    raw: response,
  };
}

function requiredText(value: unknown, field: string): string {
  const text = String(value ?? "").trim();
  if (!text) throw new QQMediaError("InvalidArguments", `${field} is required.`);
  return text;
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}

function requiredId(value: unknown, field: string): string {
  const text = requiredText(value, field);
  if (!/^\d+$/.test(text)) throw new QQMediaError("InvalidArguments", `${field} must be an integer.`);
  return text;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}
