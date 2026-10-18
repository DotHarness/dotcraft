import type { ChannelDeliveryCapabilities } from "@dotcraft/channel";

import { sendReplyCards } from "./card-sender.js";
import { parseConversationTarget } from "./conversation-target.js";
import { errorMessage, logError, logInfo, shortId } from "./logging.js";
import { FEISHU_MEDIA_CAPABILITIES, type FeishuMediaDelivery } from "./media-delivery.js";
import type { FeishuOutboundRouter } from "./outbound-router.js";

export const FEISHU_DELIVERY_CAPABILITIES: ChannelDeliveryCapabilities = {
  text: { format: "markdown" },
  targets: { group: true, direct: "any" },
  media: FEISHU_MEDIA_CAPABILITIES,
};

export class FeishuChannelSender {
  constructor(
    private readonly options: {
      router: FeishuOutboundRouter;
      media: Pick<FeishuMediaDelivery, "send">;
      cardTitle: () => string;
    },
  ) {}

  async send(target: string, message: Record<string, unknown>): Promise<Record<string, unknown>> {
    if (!parseConversationTarget(target)) {
      return {
        delivered: false,
        errorCode: "UnsupportedTarget",
        errorMessage: `'${target}' is not a Feishu target. Use group:<chat_id>, group:<chat_id>/thread:<message_id> or dm:<open_id>.`,
      };
    }
    if (String(message.kind ?? "") !== "text") {
      return await this.options.media.send(target, message);
    }
    return await this.sendText(target, String(message.text ?? ""));
  }

  async sendText(target: string, text: string): Promise<Record<string, unknown>> {
    logInfo("outbound.deliver.start", {
      target: shortId(target),
      contentChars: text.length,
    });
    try {
      const sent = await sendReplyCards(this.options.router, target, text, this.options.cardTitle());
      if (sent.length === 0) {
        return { delivered: false, errorCode: "AdapterProtocolViolation", errorMessage: "Text message is empty." };
      }
      logInfo("outbound.deliver.success", {
        target: shortId(target),
      });
      return { delivered: true, remoteMessageId: sent[0].messageId };
    } catch (error) {
      logError("outbound.deliver.failed", {
        target: shortId(target),
        message: errorMessage(error),
      });
      return { delivered: false, errorCode: "AdapterDeliveryFailed", errorMessage: errorMessage(error) };
    }
  }
}
