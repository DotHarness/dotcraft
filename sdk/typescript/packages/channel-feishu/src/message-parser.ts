import {
  localImagePart,
  textPart,
} from "@dotcraft/channel";
import { deriveConversationTarget } from "./conversation-target.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuMention, FeishuMessageEvent, ParsedInboundMessage } from "./feishu-types.js";
import { logInfo, shortId } from "./logging.js";
import { stripMentionKeys } from "./mention.js";

export interface ParseInboundOptions {
  threadCapable?: boolean;
}

export async function parseInboundMessage(
  client: FeishuClient,
  event: FeishuMessageEvent,
  botOpenId: string,
  downloadDir?: string,
  options: ParseInboundOptions = {},
): Promise<ParsedInboundMessage | null> {
  const senderId = event.sender.sender_id.open_id ?? "";
  if (!senderId || senderId === botOpenId) {
    logInfo("parse.skip_sender", {
      messageId: shortId(event.message.message_id),
      reason: !senderId ? "missing_sender_open_id" : "sender_is_bot",
    });
    return null;
  }

  const conversation = deriveConversationTarget(event, senderId, options.threadCapable === true);
  const base = {
    userId: senderId,
    userName: senderId,
    threadUserId: conversation.threadUserId,
    channelContext: conversation.channelContext,
    ...(conversation.threadKey ? { threadKey: conversation.threadKey } : {}),
    chatId: event.message.chat_id,
    chatType: event.message.chat_type,
    messageId: event.message.message_id,
    parentId: event.message.parent_id,
    rootId: event.message.root_id,
    mentions: event.message.mentions ?? [],
    sender: {
      openId: event.sender.sender_id.open_id,
      userId: event.sender.sender_id.user_id,
      unionId: event.sender.sender_id.union_id,
    },
  } as const;

  if (event.message.message_type === "text") {
    const payload = safeParseJson(event.message.content);
    const rawText = String(payload.text ?? "");
    const text = stripMentionKeys(rawText, event.message.mentions ?? []);
    logInfo("parse.text", {
      messageId: shortId(event.message.message_id),
      chatType: event.message.chat_type,
      textChars: text.length,
    });
    return {
      ...base,
      kind: "text",
      text,
      parts: [textPart(text)],
    };
  }

  if (event.message.message_type === "post") {
    const text = extractPostText(event.message.content, event.message.mentions ?? [], botOpenId);
    logInfo("parse.post", {
      messageId: shortId(event.message.message_id),
      chatType: event.message.chat_type,
      contentChars: event.message.content.length,
      textChars: text.length,
    });
    return {
      ...base,
      kind: "text",
      text,
      parts: [textPart(text)],
    };
  }

  if (event.message.message_type === "image") {
    const payload = safeParseJson(event.message.content);
    const imageKey = String(payload.image_key ?? "");
    if (!imageKey) {
      throw new Error("Image message did not include image_key");
    }
    const localPath = await client.downloadMessageImage(event.message.message_id, imageKey, downloadDir);
    logInfo("parse.image", {
      messageId: shortId(event.message.message_id),
      chatType: event.message.chat_type,
      imageKey: shortId(imageKey),
      localPath: localPath.split(/[\\/]/).pop() ?? "image",
    });
    const caption = event.message.chat_type === "group" ? "Group user sent an image." : "User sent an image.";
    return {
      ...base,
      kind: "parts",
      text: caption,
      parts: [textPart(caption), localImagePart(localPath)],
    };
  }

  logInfo("parse.unsupported", {
    messageId: shortId(event.message.message_id),
    messageType: event.message.message_type,
  });
  return null;
}

function extractPostText(content: string, mentions: FeishuMention[], botOpenId: string): string {
  const post = resolvePostBody(safeParseJson(content));
  if (!post) return "";

  const lines: string[] = [];
  const title = String(post.title ?? "").trim();
  if (title) lines.push(title);
  for (const paragraph of post.content) {
    if (!Array.isArray(paragraph)) continue;
    const parts: string[] = [];
    for (const item of paragraph) {
      if (!item || typeof item !== "object") continue;
      const record = item as Record<string, unknown>;
      const tag = String(record.tag ?? "");
      if (tag === "text" || tag === "md") {
        parts.push(String(record.text ?? ""));
      } else if (tag === "a") {
        const text = String(record.text ?? "link");
        const href = String(record.href ?? "");
        parts.push(href ? `[${text}](${href})` : text);
      } else if (tag === "at") {
        const name = resolveAtName(record, mentions, botOpenId);
        if (name) parts.push(name);
      } else if (tag === "img") {
        parts.push("[image]");
      }
    }
    const line = parts.join("").trim();
    if (line) lines.push(line);
  }

  return lines.join("\n").trim();
}

/** Received post content is flat; the locale-wrapped shape is the send format. Both appear in the wild. */
function resolvePostBody(payload: Record<string, unknown>): { title?: unknown; content: unknown[] } | null {
  if (Array.isArray(payload.content)) return { title: payload.title, content: payload.content };
  for (const value of Object.values(payload)) {
    if (!value || typeof value !== "object") continue;
    const locale = value as Record<string, unknown>;
    if (Array.isArray(locale.content)) return { title: locale.title, content: locale.content };
  }
  return null;
}

/** Post `at` tags carry the mention key rather than a name; the bot's own mention is dropped. */
function resolveAtName(record: Record<string, unknown>, mentions: FeishuMention[], botOpenId: string): string {
  const reference = String(record.user_id ?? "").trim();
  const mention = mentions.find((entry) => entry.key === reference || entry.id.open_id === reference);
  const openId = mention?.id.open_id ?? (reference.startsWith("ou_") ? reference : "");
  if (botOpenId && openId === botOpenId) return "";
  return (mention?.name ?? String(record.user_name ?? "")).trim();
}

function safeParseJson(input: string): Record<string, unknown> {
  try {
    return JSON.parse(input) as Record<string, unknown>;
  } catch {
    return {};
  }
}
