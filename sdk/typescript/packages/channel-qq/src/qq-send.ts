import { prepareMediaUploadUri } from "@dotcraft/channel/media";
import type { ChannelDeliveryCapabilities, ChannelMediaConstraints } from "@dotcraft/channel";

import {
  imageSegment,
  isActionOk,
  recordSegment,
  sendGroupMessageAction,
  sendPrivateMessageAction,
  textSegment,
  uploadGroupFileAction,
  uploadPrivateFileAction,
  videoSegment,
  type OneBotActionResponse,
  type OneBotMessageSegment,
  type OneBotReverseWsServer,
} from "./onebot.js";
import { parseQQTarget, type QQTarget } from "./target.js";

export type QQDeliveryResult = {
  delivered: boolean;
  remoteMessageId?: string;
  errorCode?: string;
  errorMessage?: string;
};

export class QQMediaError extends Error {
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.name = "QQMediaError";
    this.code = code;
  }
}

const mediaSources: ChannelMediaConstraints = {
  supportsHostPath: true,
  supportsUrl: true,
  supportsBase64: true,
};

export function qqDeliveryCapabilities(): ChannelDeliveryCapabilities {
  return {
    text: { format: "plain" },
    targets: { group: true, direct: "knownOnly" },
    media: {
      image: { ...mediaSources, supportsCaption: true },
      file: { ...mediaSources },
      audio: { ...mediaSources },
      video: { ...mediaSources },
    },
  };
}

export async function sendQQMessage(
  server: Pick<OneBotReverseWsServer, "sendAction">,
  target: string,
  message: Record<string, unknown>,
): Promise<QQDeliveryResult> {
  const parsed = parseQQTarget(target);
  if (!parsed) {
    return {
      delivered: false,
      errorCode: "UnsupportedTarget",
      errorMessage: `'${target}' is not a QQ target; use group:<groupId> or user:<qq>.`,
    };
  }

  try {
    return await deliver(server, parsed, message);
  } catch (error) {
    if (error instanceof QQMediaError) {
      return { delivered: false, errorCode: error.code, errorMessage: error.message };
    }
    return {
      delivered: false,
      errorCode: "AdapterDeliveryFailed",
      errorMessage: error instanceof Error ? error.message : String(error),
    };
  }
}

async function deliver(
  server: Pick<OneBotReverseWsServer, "sendAction">,
  target: QQTarget,
  message: Record<string, unknown>,
): Promise<QQDeliveryResult> {
  const kind = String(message.kind ?? "");
  switch (kind) {
    case "text":
      return await sendSegments(server, target, [textSegment(String(message.text ?? ""))]);
    case "image": {
      const file = await resolveMediaUri(message, "image.png");
      const caption = optionalText(message.caption);
      return await sendSegments(server, target, [
        ...(caption ? [textSegment(caption)] : []),
        imageSegment(file.uri),
      ]);
    }
    case "audio":
      return await sendSegments(server, target, [recordSegment((await resolveMediaUri(message, "audio.bin")).uri)]);
    case "video":
      return await sendSegments(server, target, [videoSegment((await resolveMediaUri(message, "video.mp4")).uri)]);
    case "file": {
      const file = await resolveMediaUri(message, "attachment.bin");
      const folder = optionalText(message.folder);
      const action = target.kind === "group"
        ? uploadGroupFileAction(target.id, file.uri, file.fileName, folder)
        : uploadPrivateFileAction(target.id, file.uri, file.fileName);
      return toDeliveryResult(await server.sendAction(action));
    }
    default:
      return {
        delivered: false,
        errorCode: "UnsupportedDeliveryKind",
        errorMessage: `QQ channel does not support '${kind}' delivery.`,
      };
  }
}

async function sendSegments(
  server: Pick<OneBotReverseWsServer, "sendAction">,
  target: QQTarget,
  segments: OneBotMessageSegment[],
): Promise<QQDeliveryResult> {
  const action = target.kind === "group"
    ? sendGroupMessageAction(target.id, segments)
    : sendPrivateMessageAction(target.id, segments);
  return toDeliveryResult(await server.sendAction(action));
}

async function resolveMediaUri(message: Record<string, unknown>, fallbackFileName: string) {
  const source = asRecord(message.source);
  const kind = String(source.kind ?? "");
  if (kind !== "hostPath" && kind !== "url" && kind !== "dataBase64") {
    throw new QQMediaError("UnsupportedMediaSource", `Unsupported QQ media source kind '${kind}'.`);
  }
  return await prepareMediaUploadUri(source, {
    fileName: optionalText(message.fileName),
    fallbackFileName,
    mediaType: optionalText(message.mediaType),
    errorFactory: qqMediaError,
  });
}

function toDeliveryResult(response: OneBotActionResponse): QQDeliveryResult {
  if (!isActionOk(response)) {
    return {
      delivered: false,
      errorCode: "AdapterDeliveryFailed",
      errorMessage: response.message ?? response.wording ?? `retcode=${response.retcode ?? "unknown"}`,
    };
  }
  const messageId = asRecord(response.data).message_id;
  return messageId === undefined || messageId === null
    ? { delivered: true }
    : { delivered: true, remoteMessageId: String(messageId) };
}

function optionalText(value: unknown): string | undefined {
  const text = String(value ?? "").trim();
  return text || undefined;
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

export function qqMediaError(code: string, message: string): QQMediaError {
  return new QQMediaError(code, message);
}
