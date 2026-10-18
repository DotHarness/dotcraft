import {
  mediaSourceFromToolBase64,
  mediaSourceFromToolPath,
  mediaSourceFromToolUrl,
  prepareMediaBytes,
} from "@dotcraft/channel/media";
import { InputFile } from "grammy";

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
  audio?: { file_id?: string };
  video?: { file_id?: string };
  photo?: Array<{ file_id?: string }>;
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
  sendPhoto(
    chatId: number | string,
    photo: InputFile | string,
    other?: Record<string, unknown>,
  ): Promise<TelegramMessageLike>;
  sendAudio(
    chatId: number | string,
    audio: InputFile | string,
    other?: Record<string, unknown>,
  ): Promise<TelegramMessageLike>;
  sendVideo(
    chatId: number | string,
    video: InputFile | string,
    other?: Record<string, unknown>,
  ): Promise<TelegramMessageLike>;
}

export type TelegramMediaKind = "document" | "voice" | "audio" | "photo" | "video";

export async function prepareTelegramMedia(
  source: Record<string, unknown>,
  fileName: string | undefined,
  fallbackFileName: string,
  expected: TelegramMediaKind,
): Promise<TelegramPreparedMedia> {
  const sourceKind = String(source.kind ?? "");
  if (sourceKind === "hostPath") {
    return await prepareCommonMedia(
      "filePath",
      requiredText(source.hostPath, "hostPath source requires hostPath."),
      fileName,
      fallbackFileName,
      expected,
    );
  }
  if (sourceKind === "url") {
    return await prepareCommonMedia(
      "fileUrl",
      requiredText(source.url, "url source requires url."),
      fileName,
      fallbackFileName,
      expected,
    );
  }
  if (sourceKind === "dataBase64") {
    return await prepareCommonMedia(
      "fileBase64",
      requiredText(source.dataBase64, "dataBase64 source requires dataBase64."),
      fileName,
      fallbackFileName,
      expected,
    );
  }

  throw new TelegramMediaError(
    "UnsupportedMediaSource",
    `Telegram cannot send source kind '${sourceKind}'.`,
  );
}

export function isVoiceFileName(fileName: string): boolean {
  return VOICE_EXTENSIONS.has(mediaExtension(fileName));
}

async function prepareCommonMedia(
  sourceName: string,
  value: string,
  fileName: string | undefined,
  fallbackFileName: string,
  expected: TelegramMediaKind,
): Promise<TelegramPreparedMedia> {
  if (sourceName === "filePath") {
    const prepared = await prepareMediaBytes(
      mediaSourceFromToolPath(value, { fieldName: "filePath", errorFactory: telegramMediaError }),
      {
        fileName,
        fallbackFileName,
        errorFactory: telegramMediaError,
      },
    );
    const actualName = prepared.fileName;
    validateSource(expected, actualName, prepared.resolvedPath ?? actualName);
    return {
      sourceKind: "filePath",
      media: new InputFile(prepared.bytes, actualName),
      fileName: actualName,
    };
  }

  const name = fileName ?? fallbackFileName;
  if (sourceName === "fileUrl") {
    mediaSourceFromToolUrl(value, { fieldName: "fileUrl", errorFactory: telegramMediaError });
    validateSource(expected, name, value);
    return {
      sourceKind: "fileUrl",
      media: value,
      fileName: name,
    };
  }

  if (sourceName === "fileBase64") {
    const prepared = await prepareMediaBytes(
      mediaSourceFromToolBase64(value, { fieldName: "fileBase64", errorFactory: telegramMediaError }),
      {
        fileName: name,
        fallbackFileName,
        errorFactory: telegramMediaError,
      },
    );
    validateSource(expected, name, name);
    return {
      sourceKind: "fileBase64",
      media: new InputFile(prepared.bytes, name),
      fileName: name,
    };
  }

  throw new TelegramMediaError("InvalidArguments", `Unsupported source field '${sourceName}'.`);
}

function validateSource(expected: TelegramMediaKind, fileName: string, locationHint: string): void {
  const fileNameExt = mediaExtension(fileName);
  const locationExt = mediaExtension(locationHint);

  if (expected === "photo" || expected === "audio" || expected === "video") {
    return;
  }

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

export function largestPhotoFileId(message: TelegramMessageLike): string | null {
  return message.photo?.[message.photo.length - 1]?.file_id ?? null;
}

export function asRecord(value: unknown): Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

export function optionalText(value: unknown): string | null {
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

export function mediaExtension(value: string): string {
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

function telegramMediaError(code: string, message: string): TelegramMediaError {
  return new TelegramMediaError(code, message);
}
