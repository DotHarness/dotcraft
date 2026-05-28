import {
  localImagePart,
  textPart,
} from "@dotcraft/sdk";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuMessageEvent, ParsedInboundMessage } from "./feishu-types.js";
import { logInfo, shortId } from "./logging.js";
import { stripMentionKeys } from "./mention.js";

export async function parseInboundMessage(
  client: FeishuClient,
  event: FeishuMessageEvent,
  botOpenId: string,
  downloadDir?: string,
): Promise<ParsedInboundMessage | null> {
  const senderId = event.sender.sender_id.open_id ?? "";
  if (!senderId || senderId === botOpenId) {
    logInfo("parse.skip_sender", {
      messageId: shortId(event.message.message_id),
      reason: !senderId ? "missing_sender_open_id" : "sender_is_bot",
    });
    return null;
  }

  const base = {
    userId: senderId,
    userName: senderId,
    threadUserId:
      event.message.chat_type === "group" ? `group:${event.message.chat_id}` : senderId,
    channelContext:
      event.message.chat_type === "group"
        ? `group:${event.message.chat_id}`
        : `dm:${senderId}`,
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
    const text = extractPostText(event.message.content);
    logInfo("parse.post", {
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

function extractPostText(content: string): string {
  const payload = safeParseJson(content);
  const locales = Object.values(payload);
  if (!locales.length) return "";
  const locale = (locales[0] as { content?: unknown }).content;
  if (!Array.isArray(locale)) return "";

  const lines: string[] = [];
  for (const paragraph of locale) {
    if (!Array.isArray(paragraph)) continue;
    const parts: string[] = [];
    for (const item of paragraph) {
      if (!item || typeof item !== "object") continue;
      const tag = String((item as Record<string, unknown>).tag ?? "");
      if (tag === "text") {
        parts.push(String((item as Record<string, unknown>).text ?? ""));
      } else if (tag === "a") {
        const text = String((item as Record<string, unknown>).text ?? "link");
        const href = String((item as Record<string, unknown>).href ?? "");
        parts.push(href ? `[${text}](${href})` : text);
      } else if (tag === "at") {
        const userName = String((item as Record<string, unknown>).user_name ?? "@user");
        parts.push(userName);
      } else if (tag === "img") {
        parts.push("[image]");
      }
    }
    const line = parts.join("").trim();
    if (line) lines.push(line);
  }

  const raw = lines.join("\n").trim();
  return raw;
}

function safeParseJson(input: string): Record<string, unknown> {
  try {
    return JSON.parse(input) as Record<string, unknown>;
  } catch {
    return {};
  }
}
