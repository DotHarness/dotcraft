import { buildErrorCard } from "./card-builder.js";
import { sendSingleCard } from "./card-sender.js";
import type { FeishuAdapter } from "./feishu-adapter.js";
import type { FeishuClient } from "./feishu-client.js";
import type {
  AppConfig,
  FeishuBotInfo,
  FeishuCardActionEvent,
  FeishuMessageEvent,
} from "./feishu-types.js";
import { buildCardActionDedupKey } from "./card-action-dedup.js";
import { shouldHandleMessageWithReason } from "./mention.js";
import { parseInboundMessage } from "./message-parser.js";
import { errorMessage, logInfo, logWarn, shortId } from "./logging.js";

const DEFAULT_ACK_REACTION_EMOJI = "GLANCE";

export function createFeishuEventHandlers(params: {
  adapter: FeishuAdapter;
  client: FeishuClient;
  bot: FeishuBotInfo;
  config: AppConfig["feishu"];
}) {
  const dedup = new Map<string, number>();
  const dedupTtlMs = 5 * 60 * 1000;
  let loggedMissingBotIdentityWarning = false;
  const ackReactionEmoji = (params.config.ackReactionEmoji ?? DEFAULT_ACK_REACTION_EMOJI).trim() || DEFAULT_ACK_REACTION_EMOJI;

  const remember = (key: string): boolean => {
    const now = Date.now();
    for (const [existing, timestamp] of dedup) {
      if (now - timestamp > dedupTtlMs) dedup.delete(existing);
    }
    if (dedup.has(key)) return false;
    dedup.set(key, now);
    return true;
  };

  return {
    onMessage: async (event: FeishuMessageEvent): Promise<void> => {
      const messageId = event.message?.message_id ?? "";
      if (!messageId) return;
      logInfo("inbound.message.received", {
        messageId: shortId(messageId),
        chatType: event.message.chat_type,
        messageType: event.message.message_type,
      });
      if (!remember(`message:${messageId}`)) {
        logInfo("inbound.message.deduped", {
          messageId: shortId(messageId),
        });
        return;
      }
      const mentionDecision = shouldHandleMessageWithReason(
        event,
        params.bot.openId,
        params.bot.botName,
        params.config.groupMentionRequired !== false,
      );
      logInfo("gate.mention", {
        messageId: shortId(messageId),
        allow: mentionDecision.allow,
        reason: mentionDecision.reason,
      });
      if (!mentionDecision.allow) {
        if (mentionDecision.reason === "missing_bot_identity" && !loggedMissingBotIdentityWarning) {
          loggedMissingBotIdentityWarning = true;
          logWarn("gate.mention_missing_bot_identity", {
            hint:
              "Enable/publish Bot capability, or set groupMentionRequired=false to allow all group messages.",
          });
        }
        return;
      }

      const parsed = await parseInboundMessage(
        params.client,
        event,
        params.bot.openId,
        params.config.downloadDir,
      );
      if (!parsed) return;

      if (!parsed.text.trim() && parsed.kind === "text") {
        logInfo("inbound.message.empty_after_parse", {
          messageId: shortId(messageId),
          chatType: event.message.chat_type,
        });
        return;
      }

      if (!parsed.parts.length) {
        logWarn("inbound.message.unsupported", {
          messageId: shortId(messageId),
          messageType: event.message.message_type,
        });
        await sendSingleCard(
          params.client,
          parsed.channelContext,
          buildErrorCard("Unsupported Message", `Message type \`${event.message.message_type}\` is not supported yet.`),
        );
        return;
      }

      if (!looksLikeFeishuReactionEmojiType(ackReactionEmoji)) {
        logWarn("inbound.message.reaction_skipped_invalid_config", {
          messageId: shortId(parsed.messageId),
          emojiType: ackReactionEmoji,
          hint: "ackReactionEmoji must be a Feishu emoji_type such as GLANCE, SMILE, or OnIt.",
        });
        await params.adapter.handleInboundMessage(parsed);
        return;
      }

      try {
        await params.client.addMessageReaction(parsed.messageId, ackReactionEmoji);
        logInfo("inbound.message.reaction_added", {
          messageId: shortId(parsed.messageId),
          emojiType: ackReactionEmoji,
        });
      } catch (error) {
        logWarn("inbound.message.reaction_failed", {
          messageId: shortId(parsed.messageId),
          emojiType: ackReactionEmoji,
          message: errorMessage(error),
        });
      }

      await params.adapter.handleInboundMessage(parsed);
    },
    onCardAction: async (event: FeishuCardActionEvent): Promise<void> => {
      const { key: dedupKey, weak: weakDedupKey } = buildCardActionDedupKey(event);
      if (weakDedupKey) {
        logWarn("approval.action.weak_dedup_key", {
          eventId: shortId(event.event_id ?? ""),
          openMessageId: shortId(event.context?.open_message_id ?? ""),
          hint: "Card action missing event_id and insufficient fields for a strong dedup key; retries may be misclassified.",
        });
      }
      if (!remember(dedupKey)) {
        logInfo("approval.action.deduped", {
          eventId: shortId(event.event_id ?? ""),
          dedupKeyKind: weakDedupKey ? "weak" : "strong",
        });
        return;
      }
      logInfo("approval.action.received", {
        eventId: shortId(event.event_id ?? ""),
        actionTag: event.action?.tag ?? "unknown",
        openMessageId: shortId(event.context?.open_message_id ?? ""),
      });
      const handled = params.adapter.handleCardAction(event);
      if (!handled) {
        logWarn("approval.action_ignored", {
          eventId: shortId(event.event_id ?? ""),
          openMessageId: shortId(event.context?.open_message_id ?? ""),
          hint: "Adapter returned false (unknown action, no pending waiter, or open_message_id mismatch).",
        });
      }
    },
  };
}

function looksLikeFeishuReactionEmojiType(value: string): boolean {
  return /^[A-Z][A-Za-z0-9]*$/.test(value);
}
