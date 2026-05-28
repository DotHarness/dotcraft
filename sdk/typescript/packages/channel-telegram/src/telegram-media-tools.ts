import { stat } from "node:fs/promises";
import { resolve } from "node:path";

import { InputFile } from "grammy";

export const DOCUMENT_TOOL_NAME = "TelegramSendDocumentToCurrentChat";
export const VOICE_TOOL_NAME = "TelegramSendVoiceToCurrentChat";

const DOCUMENT_URL_EXTENSIONS = new Set([".pdf", ".zip"]);
const VOICE_EXTENSIONS = new Set([".ogg", ".oga"]);

export class TelegramMediaError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "TelegramMediaError";
    this.code = code;
  }
}

export interface TelegramPreparedMedia {
  sourceKind: string;
  media: InputFile | string;
  fileName?: string;
}

export interface TelegramMessageLike {
  message_id?: number;
  document?: { file_id?: string };
  voice?: { file_id?: string };
}

export interface TelegramApiLike {
  sendMessage(
    chatId: number | string,
    text: string,
    other?: Record<string, unknown>,
  ): Promise<TelegramMessageLike>;
  sendDocument(
    chatId: number | string,
    document: InputFile | string,
    other?: Record<string, unknown>,
  ): Promise<TelegramMessageLike>;
  sendVoice(
    chatId: number | string,
    voice: InputFile | string,
    other?: Record<string, unknown>,
  ): Promise<TelegramMessageLike>;
}

export class TelegramMediaTools {
  getDeliveryCapabilities(): Record<string, unknown> {
    return {
      structuredDelivery: true,
      media: {
        file: {
          supportsHostPath: true,
          supportsUrl: true,
          supportsBase64: true,
          supportsCaption: true,
        },
        audio: {
          supportsHostPath: true,
          supportsUrl: true,
          supportsBase64: true,
          supportsCaption: true,
        },
      },
    };
  }

  getChannelTools(): Record<string, unknown>[] {
    const sourceProperties = {
      filePath: { type: "string" },
      fileUrl: { type: "string" },
      fileBase64: { type: "string" },
      telegramFileId: { type: "string" },
    };

    return [
      {
        name: DOCUMENT_TOOL_NAME,
        description: "Send a document to the current Telegram chat using the official sendDocument API.",
        requiresChatContext: true,
        display: {
          icon: "\u{1F4CE}",
          title: "Send document to current Telegram chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            ...sourceProperties,
            fileName: { type: "string" },
            caption: { type: "string" },
            disableContentTypeDetection: { type: "boolean" },
          },
        },
      },
      {
        name: VOICE_TOOL_NAME,
        description: "Send a voice note to the current Telegram chat using the official sendVoice API.",
        requiresChatContext: true,
        display: {
          icon: "\u{1F3A4}",
          title: "Send voice to current Telegram chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            ...sourceProperties,
            fileName: { type: "string" },
            caption: { type: "string" },
            duration: { type: "integer" },
          },
        },
      },
    ];
  }

  async sendStructuredMessage(
    api: TelegramApiLike,
    chatId: number,
    message: Record<string, unknown>,
    metadata?: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "file") {
      const source = asRecord(message.source);
      const caption = optionalText(message.caption);
      const fileName = optionalText(message.fileName) ?? "attachment";
      const disableDetection = Boolean(metadata?.disableContentTypeDetection);
      const prepared = await this.prepareAdapterMedia(source, fileName, "document");
      const remoteMessage = await api.sendDocument(chatId, prepared.media, {
        caption,
        disable_content_type_detection: disableDetection || undefined,
      });
      return {
        delivered: true,
        remoteMessageId: String(remoteMessage.message_id ?? ""),
        remoteMediaId: remoteMessage.document?.file_id ?? null,
        effectiveSourceKind: prepared.sourceKind,
        fileName: prepared.fileName,
      };
    }

    if (kind === "audio") {
      const source = asRecord(message.source);
      const caption = optionalText(message.caption);
      const fileName = optionalText(message.fileName) ?? "voice.ogg";
      const duration = optionalInteger(message.duration);
      const prepared = await this.prepareAdapterMedia(source, fileName, "voice");
      const remoteMessage = await api.sendVoice(chatId, prepared.media, {
        caption,
        duration: duration ?? undefined,
      });
      return {
        delivered: true,
        remoteMessageId: String(remoteMessage.message_id ?? ""),
        remoteMediaId: remoteMessage.voice?.file_id ?? null,
        effectiveSourceKind: prepared.sourceKind,
        fileName: prepared.fileName,
      };
    }

    throw new TelegramMediaError(
      "UnsupportedDeliveryKind",
      `Telegram example does not implement structured '${kind}' delivery.`,
    );
  }

  async executeToolCall(
    api: TelegramApiLike,
    toolName: string,
    chatId: number,
    args: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    if (toolName === DOCUMENT_TOOL_NAME) {
      const caption = optionalText(args.caption);
      const fileName = optionalText(args.fileName) ?? "attachment";
      const disableDetection = Boolean(args.disableContentTypeDetection);
      const prepared = await this.prepareToolMedia(args, fileName, "document");
      const remoteMessage = await api.sendDocument(chatId, prepared.media, {
        caption,
        disable_content_type_detection: disableDetection || undefined,
      });
      return this.toolSuccessResult(
        "document",
        String(remoteMessage.message_id ?? ""),
        remoteMessage.document?.file_id ?? null,
        prepared,
      );
    }

    if (toolName === VOICE_TOOL_NAME) {
      const caption = optionalText(args.caption);
      const fileName = optionalText(args.fileName) ?? "voice.ogg";
      const duration = optionalInteger(args.duration);
      const prepared = await this.prepareToolMedia(args, fileName, "voice");
      const remoteMessage = await api.sendVoice(chatId, prepared.media, {
        caption,
        duration: duration ?? undefined,
      });
      return this.toolSuccessResult(
        "voice note",
        String(remoteMessage.message_id ?? ""),
        remoteMessage.voice?.file_id ?? null,
        prepared,
      );
    }

    throw new TelegramMediaError("UnsupportedTool", `Unknown tool '${toolName}'.`);
  }

  private async prepareToolMedia(
    args: Record<string, unknown>,
    defaultFileName: string,
    expected: "document" | "voice",
  ): Promise<TelegramPreparedMedia> {
    const sourceValues = {
      filePath: optionalText(args.filePath),
      fileUrl: optionalText(args.fileUrl),
      fileBase64: optionalText(args.fileBase64),
      telegramFileId: optionalText(args.telegramFileId),
    };
    const populated = Object.entries(sourceValues).filter(([, value]) => Boolean(value));
    if (populated.length !== 1) {
      throw new TelegramMediaError(
        "InvalidArguments",
        "Exactly one of filePath, fileUrl, fileBase64, or telegramFileId must be provided.",
      );
    }

    const [sourceName, value] = populated[0] as [string, string];
    const fileName = optionalText(args.fileName) ?? defaultFileName;
    return await this.prepareCommonMedia(sourceName, value, fileName, expected);
  }

  private async prepareAdapterMedia(
    source: Record<string, unknown>,
    defaultFileName: string,
    expected: "document" | "voice",
  ): Promise<TelegramPreparedMedia> {
    const sourceKind = String(source.kind ?? "");
    if (sourceKind === "hostPath") {
      return await this.prepareCommonMedia(
        "filePath",
        requiredText(source.hostPath, "hostPath source requires hostPath."),
        defaultFileName,
        expected,
      );
    }
    if (sourceKind === "url") {
      return await this.prepareCommonMedia(
        "fileUrl",
        requiredText(source.url, "url source requires url."),
        defaultFileName,
        expected,
      );
    }
    if (sourceKind === "dataBase64") {
      return await this.prepareCommonMedia(
        "fileBase64",
        requiredText(source.dataBase64, "dataBase64 source requires dataBase64."),
        defaultFileName,
        expected,
      );
    }

    throw new TelegramMediaError(
      "UnsupportedMediaSource",
      `Telegram example cannot send source kind '${sourceKind}'.`,
    );
  }

  private async prepareCommonMedia(
    sourceName: string,
    value: string,
    fileName: string,
    expected: "document" | "voice",
  ): Promise<TelegramPreparedMedia> {
    if (sourceName === "filePath") {
      const fullPath = resolve(value);
      const fileStats = await stat(fullPath).catch(() => null);
      if (!fileStats?.isFile()) {
        throw new TelegramMediaError("InvalidArguments", `File '${fullPath}' does not exist.`);
      }

      const actualName = fileName || fileNameFromPath(fullPath);
      this.validateSource(expected, actualName, fullPath);
      return {
        sourceKind: "filePath",
        media: new InputFile(fullPath, actualName),
        fileName: actualName,
      };
    }

    if (sourceName === "fileUrl") {
      this.validateSource(expected, fileName, value);
      return {
        sourceKind: "fileUrl",
        media: value,
        fileName,
      };
    }

    if (sourceName === "fileBase64") {
      const raw = decodeBase64(value);
      this.validateSource(expected, fileName, fileName);
      return {
        sourceKind: "fileBase64",
        media: new InputFile(raw, fileName),
        fileName,
      };
    }

    if (sourceName === "telegramFileId") {
      return {
        sourceKind: "telegramFileId",
        media: value,
        fileName,
      };
    }

    throw new TelegramMediaError("InvalidArguments", `Unsupported source field '${sourceName}'.`);
  }

  private validateSource(expected: "document" | "voice", fileName: string, locationHint: string): void {
    const fileNameExt = extension(fileName);
    const locationExt = extension(locationHint);

    if (expected === "document") {
      if (isHttpUrl(locationHint) && !DOCUMENT_URL_EXTENSIONS.has(locationExt)) {
        throw new TelegramMediaError(
          "InvalidArguments",
          "Telegram sendDocument URL mode currently works reliably for .pdf and .zip files only.",
        );
      }
      return;
    }

    if (fileNameExt && !VOICE_EXTENSIONS.has(fileNameExt)) {
      throw new TelegramMediaError(
        "InvalidArguments",
        "Telegram voice notes should use OGG/Opus (.ogg/.oga). Use document delivery for other audio formats.",
      );
    }
    if (locationExt && !VOICE_EXTENSIONS.has(locationExt)) {
      throw new TelegramMediaError(
        "InvalidArguments",
        "Telegram voice notes should use OGG/Opus (.ogg/.oga). Use document delivery for other audio formats.",
      );
    }
    if (isHttpUrl(locationHint) && !VOICE_EXTENSIONS.has(locationExt)) {
      throw new TelegramMediaError(
        "InvalidArguments",
        "Telegram sendVoice URL mode requires an OGG voice source.",
      );
    }
  }

  private toolSuccessResult(
    noun: string,
    messageId: string,
    mediaId: string | null,
    prepared: TelegramPreparedMedia,
  ): Record<string, unknown> {
    return {
      success: true,
      contentItems: [
        {
          type: "text",
          text: `Sent ${noun} '${prepared.fileName ?? "attachment"}' to the current Telegram chat.`,
        },
      ],
      structuredResult: {
        delivered: true,
        messageId,
        mediaId,
        effectiveSourceKind: prepared.sourceKind,
        fileName: prepared.fileName,
      },
    };
  }
}

function asRecord(value: unknown): Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

function optionalText(value: unknown): string | null {
  if (value == null) {
    return null;
  }
  const text = String(value).trim();
  return text || null;
}

function requiredText(value: unknown, message: string): string {
  const text = optionalText(value);
  if (!text) {
    throw new TelegramMediaError("InvalidArguments", message);
  }
  return text;
}

function optionalInteger(value: unknown): number | null {
  if (value == null || value === "") {
    return null;
  }
  const parsed = Number(value);
  if (!Number.isInteger(parsed)) {
    throw new TelegramMediaError("InvalidArguments", `Value '${value}' is not a valid integer.`);
  }
  return parsed;
}

function fileNameFromPath(fullPath: string): string {
  const parts = fullPath.replaceAll("\\", "/").split("/");
  return parts[parts.length - 1] ?? "attachment";
}

function extension(value: string): string {
  let normalized = value.toLowerCase();
  try {
    const url = new URL(normalized);
    normalized = url.pathname;
  } catch {
    // Not a full URL; strip query/fragment manually
    const qIndex = normalized.indexOf("?");
    if (qIndex >= 0) normalized = normalized.slice(0, qIndex);
    const hIndex = normalized.indexOf("#");
    if (hIndex >= 0) normalized = normalized.slice(0, hIndex);
  }
  const index = normalized.lastIndexOf(".");
  if (index < 0) {
    return "";
  }
  return normalized.slice(index);
}

function isHttpUrl(value: string): boolean {
  return value.startsWith("http://") || value.startsWith("https://");
}

function decodeBase64(value: string): Uint8Array {
  const normalized = value.replace(/\s+/g, "");
  if (!normalized || normalized.length % 4 === 1 || /[^A-Za-z0-9+/=]/.test(normalized)) {
    throw new TelegramMediaError("InvalidArguments", "fileBase64 did not contain valid base64.");
  }
  try {
    return Buffer.from(normalized, "base64");
  } catch {
    throw new TelegramMediaError("InvalidArguments", "fileBase64 did not contain valid base64.");
  }
}
