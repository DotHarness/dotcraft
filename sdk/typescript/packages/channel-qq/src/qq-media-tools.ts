import {
  mediaSourceFromToolBase64,
  mediaSourceFromToolPath,
  mediaSourceFromToolUrl,
  prepareMediaUploadUri,
} from "@dotcraft/channel/media";
import type { ChannelToolDescriptor } from "@dotcraft/channel";

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
          supportsUrl: true,
          supportsBase64: true,
        },
        video: {
          supportsHostPath: true,
          supportsUrl: true,
          supportsBase64: true,
        },
      },
    };
  }

  getChannelTools(): ChannelToolDescriptor[] {
    const mediaSourceProperties = {
      filePath: { type: "string" },
      fileUrl: { type: "string" },
      fileBase64: { type: "string" },
      file: { type: "string" },
    };
    const filePathApproval = {
      kind: "file",
      targetArgument: "filePath",
      operation: "read",
    };

    return [
      {
        name: QQ_SEND_GROUP_VOICE_TOOL,
        description:
          "Send a voice/audio message to a QQ group chat. Use filePath for local files, fileUrl for HTTP(S), fileBase64 for raw base64, or file for URL/base64:// sources.",
        requiresChatContext: false,
        display: { icon: "\u{1F3A4}", title: "Send voice to QQ group" },
        approval: filePathApproval,
        inputSchema: {
          type: "object",
          properties: {
            groupId: { type: "integer" },
            ...mediaSourceProperties,
          },
          required: ["groupId"],
        },
      },
      {
        name: QQ_SEND_PRIVATE_VOICE_TOOL,
        description:
          "Send a voice/audio message to a QQ private chat. Use filePath for local files, fileUrl for HTTP(S), fileBase64 for raw base64, or file for URL/base64:// sources.",
        requiresChatContext: false,
        display: { icon: "\u{1F3A4}", title: "Send voice to QQ user" },
        approval: filePathApproval,
        inputSchema: {
          type: "object",
          properties: {
            userId: { type: "integer" },
            ...mediaSourceProperties,
          },
          required: ["userId"],
        },
      },
      {
        name: QQ_SEND_GROUP_VIDEO_TOOL,
        description:
          "Send a video message to a QQ group chat. Use filePath for local files, fileUrl for HTTP(S), fileBase64 for raw base64, or file for URL/base64:// sources.",
        requiresChatContext: false,
        display: { icon: "\u{1F39E}", title: "Send video to QQ group" },
        approval: filePathApproval,
        inputSchema: {
          type: "object",
          properties: {
            groupId: { type: "integer" },
            ...mediaSourceProperties,
          },
          required: ["groupId"],
        },
      },
      {
        name: QQ_SEND_PRIVATE_VIDEO_TOOL,
        description:
          "Send a video message to a QQ private chat. Use filePath for local files, fileUrl for HTTP(S), fileBase64 for raw base64, or file for URL/base64:// sources.",
        requiresChatContext: false,
        display: { icon: "\u{1F39E}", title: "Send video to QQ user" },
        approval: filePathApproval,
        inputSchema: {
          type: "object",
          properties: {
            userId: { type: "integer" },
            ...mediaSourceProperties,
          },
          required: ["userId"],
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
      const file = await this.resolveVideoSource(asRecord(message.source), String(message.fileName ?? "video.mp4"));
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
      structuredContent: {
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
        return { target: `group:${requiredId(args.groupId, "groupId")}`, message: mediaMessage("audio", parseToolMediaSource(args)) };
      case QQ_SEND_PRIVATE_VOICE_TOOL:
        return { target: requiredId(args.userId, "userId"), message: mediaMessage("audio", parseToolMediaSource(args)) };
      case QQ_SEND_GROUP_VIDEO_TOOL:
        return { target: `group:${requiredId(args.groupId, "groupId")}`, message: mediaMessage("video", parseToolMediaSource(args)) };
      case QQ_SEND_PRIVATE_VIDEO_TOOL:
        return { target: requiredId(args.userId, "userId"), message: mediaMessage("video", parseToolMediaSource(args)) };
      case QQ_UPLOAD_GROUP_FILE_TOOL:
        return {
          target: `group:${requiredId(args.groupId, "groupId")}`,
          message: {
            kind: "file",
            fileName: requiredText(args.fileName, "fileName"),
            folder: optionalText(args.folder),
            source: mediaSourceFromToolPath(args.filePath, { fieldName: "filePath", errorFactory: qqMediaError }),
          },
        };
      case QQ_UPLOAD_PRIVATE_FILE_TOOL:
        return {
          target: requiredId(args.userId, "userId"),
          message: {
            kind: "file",
            fileName: requiredText(args.fileName, "fileName"),
            source: mediaSourceFromToolPath(args.filePath, { fieldName: "filePath", errorFactory: qqMediaError }),
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
    const folder = optionalText(message.folder);
    const file = await prepareMediaUploadUri(source, {
      fileName: optionalText(message.fileName),
      fallbackFileName: "attachment.bin",
      errorFactory: qqMediaError,
    });
    const response = target.kind === "group"
      ? await server.sendAction(uploadGroupFileAction(target.id, file.uri, file.fileName, folder))
      : await server.sendAction(uploadPrivateFileAction(target.id, file.uri, file.fileName));
    return toDeliveryResult(response);
  }

  private async resolveAudioSource(source: Record<string, unknown>, fileName: string): Promise<string> {
    const kind = String(source.kind ?? "");
    if (kind === "hostPath" || kind === "url" || kind === "dataBase64") {
      return (await prepareMediaUploadUri(source, {
        fileName,
        fallbackFileName: fileName,
        errorFactory: qqMediaError,
      })).uri;
    }
    if (kind === "") return await this.resolveAudioSource(parseLegacyFileSource(fileName).source as Record<string, unknown>, fileName);
    throw new QQMediaError("UnsupportedMediaSource", `Unsupported QQ audio source kind '${kind}'.`);
  }

  private async resolveVideoSource(source: Record<string, unknown>, fileName: string): Promise<string> {
    const kind = String(source.kind ?? "");
    if (kind === "hostPath" || kind === "url" || kind === "dataBase64") {
      return (await prepareMediaUploadUri(source, {
        fileName,
        fallbackFileName: fileName,
        errorFactory: qqMediaError,
      })).uri;
    }
    throw new QQMediaError("UnsupportedMediaSource", `Unsupported QQ video source kind '${kind}'.`);
  }
}

function mediaMessage(kind: string, payload: Record<string, unknown>): Record<string, unknown> {
  return { kind, source: payload.source };
}

function parseToolMediaSource(args: Record<string, unknown>): Record<string, unknown> {
  const sourceValues = {
    filePath: optionalText(args.filePath),
    fileUrl: optionalText(args.fileUrl),
    fileBase64: optionalText(args.fileBase64),
    file: optionalText(args.file),
  };
  const populated = Object.entries(sourceValues).filter(([, value]) => Boolean(value));
  if (populated.length !== 1) {
    throw new QQMediaError(
      "InvalidArguments",
      "Exactly one of filePath, fileUrl, fileBase64, or file must be provided.",
    );
  }

  const [sourceName, value] = populated[0] as [string, string];
  if (sourceName === "filePath") {
    return { source: mediaSourceFromToolPath(value, { fieldName: "filePath", errorFactory: qqMediaError }) };
  }
  if (sourceName === "fileUrl") {
    return { source: mediaSourceFromToolUrl(value, { fieldName: "fileUrl", errorFactory: qqMediaError }) };
  }
  if (sourceName === "fileBase64") {
    return { source: mediaSourceFromToolBase64(value, { fieldName: "fileBase64", errorFactory: qqMediaError }) };
  }
  return parseLegacyFileSource(value);
}

function parseLegacyFileSource(value: unknown): Record<string, unknown> {
  const file = requiredText(value, "file");
  if (file.toLowerCase().startsWith("base64://")) {
    return { source: { kind: "dataBase64", dataBase64: file.slice("base64://".length) } };
  }
  if (/^https?:\/\//i.test(file)) {
    return { source: { kind: "url", url: file } };
  }
  throw new QQMediaError(
    "InvalidArguments",
    "The file argument only supports HTTP(S) URLs or base64:// payloads. Use filePath for local files.",
  );
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

function qqMediaError(code: string, message: string): QQMediaError {
  return new QQMediaError(code, message);
}
