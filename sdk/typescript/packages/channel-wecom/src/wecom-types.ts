import { extractXmlTag } from "./wecom-crypto.js";

export const WeComMsgType = {
  Event: "event",
  Text: "text",
  Image: "image",
  Attachment: "attachment",
  Mixed: "mixed",
  Voice: "voice",
  File: "file",
} as const;

export const WeComEventType = {
  AddToChat: "add_to_chat",
  DeleteFromChat: "delete_from_chat",
  EnterChat: "enter_chat",
} as const;

export const WeComChatType = {
  Single: "single",
  Group: "group",
} as const;

export interface WeComFrom {
  userId: string;
  name: string;
  alias: string;
}

export interface WeComMessage {
  from?: WeComFrom;
  webhookUrl: string;
  chatId: string;
  postId?: string;
  getChatInfoUrl?: string;
  msgId: string;
  chatType: string;
  msgType: string;
  text?: { content: string };
  event?: { eventType: string };
  image?: { imageUrl: string };
  attachment?: { callbackId: string; actions?: { name: string; value: string; type: string } };
  mixedMessage?: { msgItems: Array<{ msgType: string; text?: { content: string }; image?: { imageUrl: string } }> };
  voice?: { content: string };
  file?: { url: string };
  responseUrl?: string;
}

export function parseWeComMessage(content: string): WeComMessage | null {
  const trimmed = content.trimStart();
  if (!trimmed) return null;
  if (trimmed.startsWith("{")) return parseJsonMessage(trimmed);
  return parseXmlMessage(trimmed);
}

export function validateWeComMessage(message: WeComMessage): boolean {
  switch (message.msgType) {
    case WeComMsgType.Text:
      return Boolean(message.text);
    case WeComMsgType.Image:
      return Boolean(message.image);
    case WeComMsgType.Event:
      return Boolean(message.event);
    case WeComMsgType.Attachment:
      return Boolean(message.attachment);
    case WeComMsgType.Mixed:
      return Boolean(message.mixedMessage);
    case WeComMsgType.Voice:
      return Boolean(message.voice);
    case WeComMsgType.File:
      return Boolean(message.file);
    default:
      return false;
  }
}

export function normalizeWeComTextContent(content: string): string {
  return content.replace(/\r/g, " ").replace(/\n/g, " ").replace(/\s+/g, " ").trim();
}

export function parseWeComParameters(content: string, chatType: string): string[] {
  const parts = content.split(" ").filter(Boolean);
  if (chatType === WeComChatType.Group && parts[0]?.startsWith("@")) return parts.slice(1);
  return parts;
}

function parseXmlMessage(xml: string): WeComMessage | null {
  const from: WeComFrom = {
    userId: tag(xml, "UserId"),
    name: tag(xml, "Name"),
    alias: tag(xml, "Alias"),
  };
  const msgType = tag(xml, "MsgType");
  const message: WeComMessage = {
    from,
    webhookUrl: tag(xml, "WebhookUrl"),
    chatId: tag(xml, "ChatId"),
    postId: tag(xml, "PostId") || undefined,
    getChatInfoUrl: tag(xml, "GetChatInfoUrl") || undefined,
    msgId: tag(xml, "MsgId"),
    chatType: tag(xml, "ChatType"),
    msgType,
  };

  if (msgType === WeComMsgType.Text) message.text = { content: tag(xml, "Content") };
  if (msgType === WeComMsgType.Event) message.event = { eventType: tag(xml, "EventType") };
  if (msgType === WeComMsgType.Image) message.image = { imageUrl: tag(xml, "ImageUrl") };
  if (msgType === WeComMsgType.Voice) message.voice = { content: tag(xml, "Content") };
  if (msgType === WeComMsgType.File) message.file = { url: tag(xml, "Url") };
  if (msgType === WeComMsgType.Attachment) {
    message.attachment = {
      callbackId: tag(xml, "CallbackId"),
      actions: {
        name: tag(xml, "Name"),
        value: tag(xml, "Value"),
        type: tag(xml, "Type"),
      },
    };
  }
  if (msgType === WeComMsgType.Mixed) {
    message.mixedMessage = { msgItems: parseXmlMixedItems(xml) };
  }

  return message;
}

function parseJsonMessage(json: string): WeComMessage | null {
  const root = JSON.parse(json) as Record<string, unknown>;
  const from = asRecord(root.from);
  const msgType = text(root.msgtype);
  const message: WeComMessage = {
    msgId: text(root.msgid),
    chatType: text(root.chattype),
    msgType,
    responseUrl: optionalText(root.response_url),
    chatId: text(root.chatid),
    webhookUrl: text(root.webhook_url),
    from: {
      userId: text(from.userid),
      name: text(from.name),
      alias: optionalText(from.alias) ?? text(from.userid),
    },
  };

  if (msgType === WeComMsgType.Text) {
    const obj = asRecord(root.text);
    message.text = { content: text(obj.content) };
  }
  if (msgType === WeComMsgType.Image) {
    const obj = asRecord(root.image);
    message.image = { imageUrl: text(obj.url) };
  }
  if (msgType === WeComMsgType.Voice) {
    const obj = asRecord(root.voice);
    message.voice = { content: text(obj.content) };
  }
  if (msgType === WeComMsgType.File) {
    const obj = asRecord(root.file);
    message.file = { url: text(obj.url) };
  }
  if (msgType === WeComMsgType.Mixed) {
    const obj = asRecord(root.mixed);
    const items = Array.isArray(obj.msg_item) ? obj.msg_item : [];
    message.mixedMessage = {
      msgItems: items.map((item) => parseJsonMixedItem(asRecord(item))),
    };
  }
  if (msgType === WeComMsgType.Event) {
    const obj = asRecord(root.event);
    message.event = { eventType: text(obj.eventtype ?? obj.event_type) };
  }
  if (msgType === WeComMsgType.Attachment) {
    const obj = asRecord(root.attachment);
    message.attachment = { callbackId: text(obj.callback_id) };
  }

  return message;
}

function parseJsonMixedItem(item: Record<string, unknown>): { msgType: string; text?: { content: string }; image?: { imageUrl: string } } {
  const msgType = text(item.msgtype);
  const result: { msgType: string; text?: { content: string }; image?: { imageUrl: string } } = { msgType };
  if (msgType === WeComMsgType.Text) {
    result.text = { content: text(asRecord(item.text).content) };
  }
  if (msgType === WeComMsgType.Image) {
    result.image = { imageUrl: text(asRecord(item.image).url) };
  }
  return result;
}

function parseXmlMixedItems(xml: string): Array<{ msgType: string; text?: { content: string }; image?: { imageUrl: string } }> {
  const matches = [...xml.matchAll(/<MsgItem>([\s\S]*?)<\/MsgItem>/gi)];
  return matches.map((match) => {
    const item = match[1] ?? "";
    const msgType = tag(item, "MsgType");
    const result: { msgType: string; text?: { content: string }; image?: { imageUrl: string } } = { msgType };
    if (msgType === WeComMsgType.Text) result.text = { content: tag(item, "Content") };
    if (msgType === WeComMsgType.Image) result.image = { imageUrl: tag(item, "ImageUrl") };
    return result;
  });
}

function tag(xml: string, name: string): string {
  return extractXmlTag(xml, name) ?? "";
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function text(value: unknown): string {
  return String(value ?? "");
}

function optionalText(value: unknown): string | undefined {
  const valueText = text(value);
  return valueText || undefined;
}

