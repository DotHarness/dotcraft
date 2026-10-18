import { GrammyError, HttpError, type InputFile } from "grammy";
import type { ChannelDeliveryCapabilities } from "@dotcraft/channel";

import { markdownToTelegramHtml, splitTelegramMessage } from "./formatting.js";
import {
  TelegramMediaError,
  asRecord,
  isVoiceFileName,
  largestPhotoFileId,
  mediaExtension,
  optionalText,
  prepareTelegramMedia,
  type TelegramApiLike,
  type TelegramMediaKind,
  type TelegramMessageLike,
} from "./telegram-media.js";

const MB = 1024 * 1024;
const TEXT_MAX_LENGTH = 4000;
const CAPTION_MAX_LENGTH = 1024;

const UPLOADABLE = {
  supportsHostPath: true,
  supportsUrl: true,
  supportsBase64: true,
  supportsCaption: true,
};

export const TELEGRAM_DELIVERY_CAPABILITIES: ChannelDeliveryCapabilities = {
  text: { format: "markdown" },
  targets: { group: true, direct: "knownOnly" },
  media: {
    image: { ...UPLOADABLE, maxBytes: 10 * MB, allowedExtensions: [".jpg", ".jpeg", ".png", ".webp"] },
    file: { ...UPLOADABLE, maxBytes: 50 * MB },
    audio: { ...UPLOADABLE, maxBytes: 50 * MB, allowedExtensions: [".ogg", ".oga", ".mp3", ".m4a"] },
    video: { ...UPLOADABLE, maxBytes: 50 * MB, allowedExtensions: [".mp4"] },
  },
};

type MediaMethod = {
  kind: TelegramMediaKind;
  fallbackFileName: string;
  send: (
    api: TelegramApiLike,
    chatId: number,
    media: InputFile | string,
    other: Record<string, unknown>,
  ) => Promise<TelegramMessageLike>;
  mediaId: (message: TelegramMessageLike) => string | null;
};

const PHOTO: MediaMethod = {
  kind: "photo",
  fallbackFileName: "image.png",
  send: (api, chatId, media, other) => api.sendPhoto(chatId, media, other),
  mediaId: largestPhotoFileId,
};

const DOCUMENT: MediaMethod = {
  kind: "document",
  fallbackFileName: "attachment",
  send: (api, chatId, media, other) => api.sendDocument(chatId, media, other),
  mediaId: (message) => message.document?.file_id ?? null,
};

const VOICE: MediaMethod = {
  kind: "voice",
  fallbackFileName: "voice.ogg",
  send: (api, chatId, media, other) => api.sendVoice(chatId, media, other),
  mediaId: (message) => message.voice?.file_id ?? null,
};

const AUDIO: MediaMethod = {
  kind: "audio",
  fallbackFileName: "audio.mp3",
  send: (api, chatId, media, other) => api.sendAudio(chatId, media, other),
  mediaId: (message) => message.audio?.file_id ?? null,
};

const VIDEO: MediaMethod = {
  kind: "video",
  fallbackFileName: "video.mp4",
  send: (api, chatId, media, other) => api.sendVideo(chatId, media, other),
  mediaId: (message) => message.video?.file_id ?? null,
};

export function parseTargetChatId(target: string): number | null {
  const match = /^(?:(group|user):)?(-?\d+)$/.exec(String(target).trim());
  if (!match) return null;
  const chatId = Number(match[2]);
  if (!Number.isSafeInteger(chatId) || chatId === 0) return null;
  if (match[1] === "group" && chatId > 0) return null;
  if (match[1] === "user" && chatId < 0) return null;
  return chatId;
}

export async function sendTelegramText(
  api: TelegramApiLike,
  chatId: number,
  content: string,
): Promise<TelegramMessageLike[]> {
  const sent: TelegramMessageLike[] = [];
  if (!content) {
    return sent;
  }

  for (const chunk of splitTelegramMessage(content, TEXT_MAX_LENGTH)) {
    try {
      sent.push(await api.sendMessage(chatId, markdownToTelegramHtml(chunk), { parse_mode: "HTML" }));
    } catch (error) {
      console.warn(`[telegram] html send failed, retrying as plain text: ${describeTelegramError(error)}`);
      sent.push(await api.sendMessage(chatId, chunk));
    }
  }
  return sent;
}

export async function sendTelegramMessage(
  api: TelegramApiLike | null,
  target: string,
  message: Record<string, unknown>,
): Promise<Record<string, unknown>> {
  const chatId = parseTargetChatId(target);
  if (chatId === null) {
    return failure("UnsupportedTarget", `'${target}' is not a Telegram chat id, user:<id> or group:<id>.`);
  }
  if (!api) {
    return failure("AdapterDeliveryFailed", "Telegram bot is not connected.");
  }

  const kind = String(message.kind ?? "");
  try {
    if (kind === "text") {
      const text = String(message.text ?? "");
      if (!text.trim()) {
        return failure("AdapterProtocolViolation", "A text delivery needs text.");
      }
      const sent = await sendTelegramText(api, chatId, text);
      return delivered(sent[0], null);
    }
    return await sendTelegramMedia(api, chatId, kind, message);
  } catch (error) {
    if (error instanceof TelegramMediaError) {
      return failure(error.code, error.message);
    }
    return failure("AdapterDeliveryFailed", describeTelegramError(error));
  }
}

export function describeTelegramError(error: unknown): string {
  if (error instanceof GrammyError) {
    const code =
      typeof (error as unknown as { error_code?: unknown }).error_code === "number"
        ? Number((error as unknown as { error_code?: unknown }).error_code)
        : undefined;
    return code ? `[${code}] ${error.description}` : error.description;
  }
  if (error instanceof HttpError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return String(error);
}

async function sendTelegramMedia(
  api: TelegramApiLike,
  chatId: number,
  kind: string,
  message: Record<string, unknown>,
): Promise<Record<string, unknown>> {
  const source = asRecord(message.source);
  const fileName = optionalText(message.fileName) ?? undefined;
  const nameHint = fileName ?? optionalText(source.hostPath) ?? optionalText(source.url) ?? "";
  const method = mediaMethod(kind, nameHint, optionalText(message.mediaType));
  if (!method) {
    return failure("UnsupportedDeliveryKind", `Telegram cannot deliver '${kind}' messages.`);
  }

  const prepared = await prepareTelegramMedia(source, fileName, method.fallbackFileName, method.kind);
  const caption = optionalText(message.caption) ?? undefined;
  const captionFits = !caption || caption.length <= CAPTION_MAX_LENGTH;
  if (caption && !captionFits) {
    await sendTelegramText(api, chatId, caption);
  }
  const remote = await sendCaptioned(
    captionFits ? caption : undefined,
    (other) => method.send(api, chatId, prepared.media, other),
  );
  return delivered(remote, method.mediaId(remote));
}

function mediaMethod(kind: string, nameHint: string, mediaType: string | null): MediaMethod | null {
  switch (kind) {
    case "image":
      return PHOTO;
    case "file":
      return DOCUMENT;
    case "video":
      return VIDEO;
    case "audio":
      if (mediaExtension(nameHint)) return isVoiceFileName(nameHint) ? VOICE : AUDIO;
      return !mediaType || mediaType === "audio/ogg" ? VOICE : AUDIO;
    default:
      return null;
  }
}

async function sendCaptioned(
  caption: string | undefined,
  send: (other: Record<string, unknown>) => Promise<TelegramMessageLike>,
): Promise<TelegramMessageLike> {
  if (!caption) {
    return await send({});
  }
  try {
    return await send({ caption: markdownToTelegramHtml(caption), parse_mode: "HTML" });
  } catch (error) {
    if (!(error instanceof GrammyError) || !/can't parse entities/i.test(error.description)) {
      throw error;
    }
    return await send({ caption });
  }
}

function delivered(remote: TelegramMessageLike | undefined, remoteMediaId: string | null): Record<string, unknown> {
  return {
    delivered: true,
    remoteMessageId: remote?.message_id === undefined ? null : String(remote.message_id),
    remoteMediaId,
  };
}

function failure(errorCode: string, errorMessage: string): Record<string, unknown> {
  return { delivered: false, errorCode, errorMessage };
}
