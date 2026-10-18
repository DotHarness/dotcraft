import type { ChannelDeliveryCapabilities } from "@dotcraft/channel";

import { WeixinMediaError } from "./weixin-media.js";

interface CaptionDeliveryResult {
  attempted: boolean;
  delivered?: boolean;
  errorMessage?: string;
}

export interface WeixinDeliveryDeps {
  getContextToken(userId: string): string | undefined;
  sendText(userId: string, text: string): Promise<void>;
  sendMedia(userId: string, message: Record<string, unknown>): Promise<Record<string, unknown>>;
}

const mediaConstraints = {
  supportsHostPath: true,
  supportsUrl: false,
  supportsBase64: true,
  supportsCaption: false,
};

export function weixinDeliveryCapabilities(): ChannelDeliveryCapabilities {
  return {
    text: { format: "plain" },
    targets: { group: false, direct: "knownOnly" },
    media: {
      image: { ...mediaConstraints },
      file: { ...mediaConstraints },
    },
  };
}

export async function deliverWeixinMessage(
  deps: WeixinDeliveryDeps,
  target: string,
  message: Record<string, unknown>,
): Promise<Record<string, unknown>> {
  if (!target.trim()) {
    return unsupportedTarget("Weixin target must be a Weixin user id.");
  }
  if (!deps.getContextToken(target)) {
    return unsupportedTarget(`Weixin user '${target}' has not messaged the bot, so it cannot be reached.`);
  }

  const kind = String(message.kind ?? "");
  if (kind === "text") {
    try {
      await deps.sendText(target, String(message.text ?? ""));
      return { delivered: true };
    } catch (error) {
      return deliveryFailure(error);
    }
  }

  if (kind !== "image" && kind !== "file") {
    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `Weixin channel does not support '${kind}' delivery.`,
    };
  }

  const caption = await sendCaptionIfPresent(deps.sendText, target, String(message.caption ?? ""));
  try {
    return attachCaptionInfo(await deps.sendMedia(target, message), caption);
  } catch (error) {
    return attachCaptionInfo(deliveryFailure(error), caption);
  }
}

async function sendCaptionIfPresent(
  sendText: (userId: string, text: string) => Promise<void>,
  toUserId: string,
  caption: string,
): Promise<CaptionDeliveryResult> {
  if (!caption.trim()) return { attempted: false };
  try {
    await sendText(toUserId, caption);
    return { attempted: true, delivered: true };
  } catch (error) {
    console.error("caption send failed:", error);
    return { attempted: true, delivered: false, errorMessage: errorText(error) };
  }
}

function attachCaptionInfo(
  result: Record<string, unknown>,
  caption: CaptionDeliveryResult,
): Record<string, unknown> {
  if (!caption.attempted) return result;

  const updated: Record<string, unknown> = {
    ...result,
    captionDelivered: caption.delivered === true,
  };
  if (caption.errorMessage) {
    updated.captionError = caption.errorMessage;
  }
  return updated;
}

function unsupportedTarget(errorMessage: string): Record<string, unknown> {
  return { delivered: false, errorCode: "UnsupportedTarget", errorMessage };
}

function deliveryFailure(error: unknown): Record<string, unknown> {
  const errorCode = error instanceof WeixinMediaError && error.code === "UnsupportedMediaSource"
    ? error.code
    : "AdapterDeliveryFailed";
  return { delivered: false, errorCode, errorMessage: errorText(error) };
}

function errorText(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}
