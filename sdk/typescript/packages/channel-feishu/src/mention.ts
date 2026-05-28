import type { FeishuMention, FeishuMessageEvent } from "./feishu-types.js";

function isMentionAll(mention: FeishuMention): boolean {
  return mention.key === "@_all";
}

export function isBotMentioned(
  event: FeishuMessageEvent,
  botOpenId: string,
  botName?: string,
): boolean {
  return (event.message.mentions ?? []).some((mention) => {
    if (mention.id.open_id !== botOpenId) return false;
    // Guard against Feishu WS open_id remapping in multi-app groups.
    if (botName && mention.name && mention.name !== botName) return false;
    return true;
  });
}

export function stripMentionKeys(text: string, mentions: FeishuMention[]): string {
  let result = text;
  for (const mention of mentions) {
    result = result.replaceAll(mention.key, "");
  }
  return result.replace(/\s+/g, " ").trim();
}

export function shouldHandleMessage(
  event: FeishuMessageEvent,
  botOpenId: string,
  botName: string | undefined,
  groupMentionRequired: boolean,
): boolean {
  return shouldHandleMessageWithReason(event, botOpenId, botName, groupMentionRequired).allow;
}

export function shouldHandleMessageWithReason(
  event: FeishuMessageEvent,
  botOpenId: string,
  botName: string | undefined,
  groupMentionRequired: boolean,
): { allow: boolean; reason: "dm" | "group_no_gate" | "mentioned_bot" | "not_mentioned" | "missing_bot_identity" } {
  if (event.message.chat_type === "p2p") return { allow: true, reason: "dm" };
  if (!groupMentionRequired) return { allow: true, reason: "group_no_gate" };
  if (!botOpenId) return { allow: false, reason: "missing_bot_identity" };
  if (isBotMentioned(event, botOpenId, botName)) return { allow: true, reason: "mentioned_bot" };
  return { allow: false, reason: "not_mentioned" };
}

export function extractTextMentions(
  mentions: FeishuMention[],
): { plainNames: string[]; containsMentionAll: boolean } {
  const plainNames: string[] = [];
  let containsMentionAll = false;
  for (const mention of mentions) {
    if (isMentionAll(mention)) {
      containsMentionAll = true;
      continue;
    }
    plainNames.push(mention.name);
  }
  return { plainNames, containsMentionAll };
}
