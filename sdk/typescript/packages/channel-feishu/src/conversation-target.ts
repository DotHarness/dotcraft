import type { ChannelChatDescriptor } from "@dotcraft/channel";

import type { FeishuMessageEvent } from "./feishu-types.js";

const THREAD_SEPARATOR = "/thread:";

export interface FeishuConversationTarget {
  kind: "group" | "dm";
  id: string;
  threadKey?: string;
}

export function formatConversationTarget(target: FeishuConversationTarget): string {
  const base = `${target.kind}:${target.id}`;
  return target.threadKey ? `${base}${THREAD_SEPARATOR}${target.threadKey}` : base;
}

export function parseConversationTarget(raw: string): FeishuConversationTarget | null {
  const match = /^(?:group:(oc_[\w-]+)(?:\/thread:(om_[\w-]+))?|dm:(ou_[\w-]+))$/.exec(raw.trim());
  if (!match) return null;
  const [, groupId, threadKey, openId] = match;
  if (openId) return { kind: "dm", id: openId };
  return threadKey ? { kind: "group", id: groupId, threadKey } : { kind: "group", id: groupId };
}

export function directConversationTarget(openId: string): string | undefined {
  const target = formatConversationTarget({ kind: "dm", id: openId });
  return parseConversationTarget(target) ? target : undefined;
}

export function describeConversation(channelContext: string): ChannelChatDescriptor {
  const target = parseConversationTarget(channelContext);
  const kind = target?.kind === "dm" ? "direct" : target?.threadKey ? "topic" : "group";
  return { kind, membership: "shared" };
}

export function conversationTargetBase(raw: string): string {
  const separatorIndex = raw.indexOf(THREAD_SEPARATOR);
  return separatorIndex >= 0 ? raw.slice(0, separatorIndex) : raw;
}

export function conversationTargetThreadKey(raw: string): string {
  const separatorIndex = raw.indexOf(THREAD_SEPARATOR);
  return separatorIndex >= 0 ? raw.slice(separatorIndex + THREAD_SEPARATOR.length) : "";
}

// Keyed by the topic root message id: roots carry `thread_id` without `root_id`, replies carry
// `root_id`. Topic-mode groups may omit `thread_id` on replies, hence the `root_id === parent_id` path.
export function resolveTopicKey(
  message: FeishuMessageEvent["message"],
  threadCapable: boolean,
): string | undefined {
  const rootId = message.root_id?.trim() ?? "";
  if (message.thread_id?.trim()) {
    return rootId || message.message_id;
  }
  if (threadCapable && rootId && rootId === message.parent_id?.trim()) {
    return rootId;
  }
  return undefined;
}

export function needsThreadCapabilityLookup(message: FeishuMessageEvent["message"]): boolean {
  if (message.chat_type !== "group" || message.thread_id?.trim()) return false;
  const rootId = message.root_id?.trim() ?? "";
  return Boolean(rootId) && rootId === message.parent_id?.trim();
}

export function deriveConversationTarget(
  event: FeishuMessageEvent,
  senderOpenId: string,
  threadCapable: boolean,
): { channelContext: string; threadUserId: string; threadKey?: string } {
  if (event.message.chat_type !== "group") {
    return { channelContext: `dm:${senderOpenId}`, threadUserId: senderOpenId };
  }
  const threadKey = resolveTopicKey(event.message, threadCapable);
  const channelContext = formatConversationTarget({
    kind: "group",
    id: event.message.chat_id,
    ...(threadKey ? { threadKey } : {}),
  });
  return threadKey
    ? { channelContext, threadUserId: channelContext, threadKey }
    : { channelContext, threadUserId: channelContext };
}
