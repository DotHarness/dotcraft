import { prepareMediaBytes } from "@dotcraft/channel/media";
import type { ChannelDeliveryCapabilities } from "@dotcraft/channel";

import type { FeishuFileMessageType } from "./feishu-types.js";
import { errorMessage, logError, logInfo, shortId } from "./logging.js";
import type { FeishuOutboundRouter } from "./outbound-router.js";

const FILE_MAX_BYTES = 30 * 1024 * 1024;
const IMAGE_MAX_BYTES = 10 * 1024 * 1024;

const base64Only = { supportsHostPath: false, supportsUrl: false, supportsBase64: true, supportsCaption: false };

export const FEISHU_MEDIA_CAPABILITIES = {
  image: {
    maxBytes: IMAGE_MAX_BYTES,
    allowedMimeTypes: ["image/jpeg", "image/png", "image/webp", "image/gif", "image/tiff", "image/bmp", "image/x-icon"],
    ...base64Only,
  },
  file: { maxBytes: FILE_MAX_BYTES, ...base64Only },
  audio: {
    maxBytes: FILE_MAX_BYTES,
    allowedMimeTypes: ["audio/opus"],
    allowedExtensions: [".opus"],
    ...base64Only,
  },
  video: {
    maxBytes: FILE_MAX_BYTES,
    allowedMimeTypes: ["video/mp4"],
    allowedExtensions: [".mp4"],
    ...base64Only,
  },
} satisfies NonNullable<ChannelDeliveryCapabilities["media"]>;

type MediaKind = keyof typeof FEISHU_MEDIA_CAPABILITIES;
type MediaRouter = Pick<FeishuOutboundRouter, "sendFile" | "sendImage">;

const FILE_MESSAGE_TYPES: Record<Exclude<MediaKind, "image">, FeishuFileMessageType> = {
  file: "file",
  audio: "audio",
  video: "media",
};

const DEFAULT_FILE_NAMES: Record<MediaKind, string> = {
  image: "image.png",
  file: "attachment",
  audio: "audio.opus",
  video: "video.mp4",
};

function isMediaKind(kind: string): kind is MediaKind {
  return Object.hasOwn(FEISHU_MEDIA_CAPABILITIES, kind);
}

export class FeishuMediaDelivery {
  constructor(private readonly router: MediaRouter) {}

  async send(target: string, message: Record<string, unknown>): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (!isMediaKind(kind)) {
      return {
        delivered: false,
        errorCode: "UnsupportedDeliveryKind",
        errorMessage: `Feishu cannot deliver '${kind}' messages.`,
      };
    }

    const fileName = String(message.fileName ?? DEFAULT_FILE_NAMES[kind]);
    try {
      const sent = await this.deliver(target, kind, {
        source: (message.source as Record<string, unknown> | undefined) ?? {},
        fileName,
        mediaType: String(message.mediaType ?? "").trim() || undefined,
      });
      return {
        delivered: true,
        remoteMessageId: sent.messageId,
        remoteMediaId: sent.mediaKey,
      };
    } catch (error) {
      logError(`outbound.send.${kind}.failed`, {
        target: shortId(target),
        fileName,
        message: errorMessage(error),
      });
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: errorMessage(error),
      };
    }
  }

  private async deliver(
    target: string,
    kind: MediaKind,
    payload: {
      source: Record<string, unknown>;
      fileName?: string;
      mediaType?: string;
    },
  ): Promise<{ messageId: string; mediaKey: string }> {
    const prepared = await prepareMediaBytes(payload.source, {
      fileName: payload.fileName,
      mediaType: payload.mediaType,
      maxBytes: FEISHU_MEDIA_CAPABILITIES[kind].maxBytes,
    });
    logInfo(`outbound.send.${kind}`, {
      target: shortId(target),
      fileName: prepared.fileName,
      bytes: prepared.bytes.length,
    });

    if (kind === "image") {
      const result = await this.router.sendImage(target, { data: prepared.bytes });
      return { messageId: result.messageId, mediaKey: result.imageKey };
    }
    const result = await this.router.sendFile(target, {
      fileName: prepared.fileName,
      data: prepared.bytes,
      mediaType: prepared.mediaType,
    }, FILE_MESSAGE_TYPES[kind]);
    return { messageId: result.messageId, mediaKey: result.fileKey };
  }
}
