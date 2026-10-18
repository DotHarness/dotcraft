import type { ChannelDeliveryCapabilities, ChannelMediaConstraints } from "@dotcraft/channel";
import { prepareMediaBytes } from "@dotcraft/channel/media";

import type { WeComPusher } from "./wecom-pusher.js";

const WE_COM_IMAGE_MAX_BYTES = 2 * 1024 * 1024;
const WE_COM_FILE_MAX_BYTES = 20 * 1024 * 1024;
const WE_COM_VOICE_MAX_BYTES = 2 * 1024 * 1024;

const sourceForms = { supportsHostPath: true, supportsUrl: true, supportsBase64: true } satisfies ChannelMediaConstraints;

export const weComDeliveryCapabilities: ChannelDeliveryCapabilities = {
  text: { format: "plain" },
  targets: { group: false, direct: "knownOnly" },
  media: {
    image: {
      maxBytes: WE_COM_IMAGE_MAX_BYTES,
      allowedMimeTypes: ["image/jpeg", "image/png"],
      allowedExtensions: [".jpg", ".jpeg", ".png"],
      ...sourceForms,
    },
    file: {
      maxBytes: WE_COM_FILE_MAX_BYTES,
      ...sourceForms,
    },
    audio: {
      maxBytes: WE_COM_VOICE_MAX_BYTES,
      allowedMimeTypes: ["audio/amr"],
      allowedExtensions: [".amr"],
      ...sourceForms,
    },
  },
};

type WeComMediaKind = "image" | "file" | "audio";

const mediaLimits: Record<WeComMediaKind, { maxBytes: number; fallbackFileName: string }> = {
  image: { maxBytes: WE_COM_IMAGE_MAX_BYTES, fallbackFileName: "image.png" },
  file: { maxBytes: WE_COM_FILE_MAX_BYTES, fallbackFileName: "file.bin" },
  audio: { maxBytes: WE_COM_VOICE_MAX_BYTES, fallbackFileName: "voice.amr" },
};

export class WeComMediaError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "WeComMediaError";
    this.code = code;
  }
}

export async function deliverWeComMessage(
  pusher: WeComPusher,
  message: Record<string, unknown>,
): Promise<Record<string, unknown>> {
  const kind = String(message.kind ?? "");
  if (kind === "text") {
    await pusher.pushText(String(message.text ?? ""));
    return { delivered: true };
  }
  if (kind !== "image" && kind !== "file" && kind !== "audio") {
    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `WeCom channel does not support '${kind}' delivery.`,
    };
  }

  const prepared = await prepareMediaBytes(asRecord(message.source), {
    fileName: optionalText(message.fileName),
    fallbackFileName: mediaLimits[kind].fallbackFileName,
    maxBytes: mediaLimits[kind].maxBytes,
    allowUrl: true,
    errorFactory: weComMediaError,
  });
  if (kind === "audio" && !isAmr(prepared.bytes)) {
    throw new WeComMediaError("MediaTypeNotAllowed", "WeCom voice delivery only supports AMR files.");
  }

  const caption = optionalText(message.caption);
  if (caption) await pusher.pushText(caption);

  if (kind === "image" && isJpegOrPng(prepared.bytes)) {
    await pusher.pushImage(prepared.bytes);
    return { delivered: true };
  }

  const uploadType = kind === "audio" ? "voice" : "file";
  const mediaId = await pusher.uploadMedia(prepared.bytes, prepared.fileName, uploadType);
  if (uploadType === "voice") await pusher.pushVoice(mediaId);
  else await pusher.pushFile(mediaId);
  return { delivered: true, remoteMediaId: mediaId };
}

function isJpegOrPng(bytes: Buffer): boolean {
  const isPng = bytes.length >= 8 && bytes.subarray(0, 8).equals(Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]));
  const isJpeg = bytes.length >= 3 && bytes[0] === 0xff && bytes[1] === 0xd8 && bytes[2] === 0xff;
  return isPng || isJpeg;
}

function isAmr(bytes: Buffer): boolean {
  return bytes.subarray(0, 5).toString("latin1") === "#!AMR";
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function weComMediaError(code: string, message: string): WeComMediaError {
  return new WeComMediaError(code, message);
}
