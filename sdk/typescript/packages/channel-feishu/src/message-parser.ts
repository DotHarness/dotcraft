import { textPart, type InputPart } from "@dotcraft/channel";
import { saveInboundFile, saveInboundImage } from "@dotcraft/channel/media";
import { deriveConversationTarget } from "./conversation-target.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuMention, FeishuMessageEvent, ParsedInboundMessage } from "./feishu-types.js";
import { errorMessage, logInfo, logWarn, shortId } from "./logging.js";
import { stripMentionKeys } from "./mention.js";

export interface ParseInboundOptions {
  threadCapable?: boolean;
}

export async function parseInboundMessage(
  client: FeishuClient,
  event: FeishuMessageEvent,
  botOpenId: string,
  craftPath: string,
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
    const pieces = extractPostPieces(event.message.content, event.message.mentions ?? [], botOpenId);
    const media = pieces.filter((piece): piece is MediaPiece => typeof piece !== "string");
    const input = await resolvePieces(client, event.message.message_id, craftPath, pieces);
    logInfo("parse.post", {
      messageId: shortId(event.message.message_id),
      chatType: event.message.chat_type,
      contentChars: event.message.content.length,
      textChars: input.text.length,
      images: media.filter((piece) => piece.kind === "image").length,
      videos: media.filter((piece) => piece.kind === "video").length,
    });
    return {
      ...base,
      kind: media.length ? "parts" : "text",
      ...input,
    };
  }

  const standalone = standaloneMedia(event.message.message_type, safeParseJson(event.message.content));
  if (standalone) {
    if (!standalone.key) {
      throw new Error(`Feishu ${event.message.message_type} message did not include a resource key`);
    }
    const who = event.message.chat_type === "group" ? "Group user" : "User";
    const caption = `${who} sent ${standalone.kind === "image" ? "an image" : `a ${standalone.kind}`}.`;
    const input = await resolvePieces(client, event.message.message_id, craftPath, [`${caption}\n`, standalone]);
    logInfo(`parse.${standalone.kind}`, {
      messageId: shortId(event.message.message_id),
      chatType: event.message.chat_type,
      key: shortId(standalone.key),
      parts: input.parts.length,
    });
    return {
      ...base,
      kind: "parts",
      ...input,
    };
  }

  logInfo("parse.unsupported", {
    messageId: shortId(event.message.message_id),
    messageType: event.message.message_type,
  });
  return null;
}

interface MediaPiece {
  kind: "image" | "video" | "file";
  key: string;
  fileName?: string;
}

type Piece = string | MediaPiece;

function standaloneMedia(messageType: string, payload: Record<string, unknown>): MediaPiece | null {
  switch (messageType) {
    case "image":
      return { kind: "image", key: String(payload.image_key ?? "") };
    case "media":
    case "video":
      return { kind: "video", key: String(payload.file_key ?? ""), fileName: optionalString(payload.file_name) };
    case "file":
      return { kind: "file", key: String(payload.file_key ?? ""), fileName: optionalString(payload.file_name) };
    default:
      return null;
  }
}

function extractPostPieces(content: string, mentions: FeishuMention[], botOpenId: string): Piece[] {
  const post = resolvePostBody(safeParseJson(content));
  if (!post) return [];

  const lines: Piece[][] = [];
  const title = String(post.title ?? "").trim();
  if (title) lines.push([title]);
  for (const paragraph of post.content) {
    if (!Array.isArray(paragraph)) continue;
    const pieces: Piece[] = [];
    for (const item of paragraph) {
      if (!item || typeof item !== "object") continue;
      const record = item as Record<string, unknown>;
      const tag = String(record.tag ?? "");
      if (tag === "text" || tag === "md") {
        pieces.push(String(record.text ?? ""));
      } else if (tag === "a") {
        const text = String(record.text ?? "link");
        const href = String(record.href ?? "");
        pieces.push(href ? `[${text}](${href})` : text);
      } else if (tag === "at") {
        const name = resolveAtName(record, mentions, botOpenId);
        if (name) pieces.push(name);
      } else if (tag === "img") {
        const key = String(record.image_key ?? "").trim();
        if (key) pieces.push({ kind: "image", key });
      } else if (tag === "media") {
        const key = String(record.file_key ?? "").trim();
        if (key) pieces.push({ kind: "video", key, fileName: optionalString(record.file_name) });
      }
    }
    const line = trimLine(pieces);
    if (line.length) lines.push(line);
  }

  return lines.flatMap((line, index) => (index === 0 ? line : ["\n", ...line]));
}

function trimLine(pieces: Piece[]): Piece[] {
  const line = [...pieces];
  const first = line[0];
  if (typeof first === "string") line[0] = first.trimStart();
  const last = line[line.length - 1];
  if (typeof last === "string") line[line.length - 1] = last.trimEnd();
  return line.some((piece) => typeof piece !== "string" || piece.length > 0) ? line : [];
}

async function resolvePieces(
  client: FeishuClient,
  messageId: string,
  craftPath: string,
  pieces: Piece[],
): Promise<{ text: string; parts: InputPart[] }> {
  const resolved = await Promise.all(
    pieces.map((piece) => (typeof piece === "string" ? piece : resolveMedia(client, messageId, craftPath, piece))),
  );

  const parts: InputPart[] = [];
  let pending = "";
  const flush = (): void => {
    if (pending.trim()) parts.push(textPart(pending));
    pending = "";
  };
  for (const entry of resolved) {
    if (typeof entry === "string") {
      pending += entry;
      continue;
    }
    pending += entry.prefix;
    if (entry.part) {
      flush();
      parts.push(entry.part);
    }
  }
  flush();

  const text = resolved.map((entry) => (typeof entry === "string" ? entry : entry.label)).join("").trim();
  return { text, parts };
}

interface ResolvedMedia {
  label: string;
  prefix: string;
  part?: InputPart;
}

async function resolveMedia(
  client: FeishuClient,
  messageId: string,
  craftPath: string,
  media: MediaPiece,
): Promise<ResolvedMedia> {
  const label = media.fileName ? `[${media.kind}: ${media.fileName}]` : `[${media.kind}]`;
  try {
    if (media.kind === "image") {
      const { bytes, mediaType } = await client.downloadMessageResource(messageId, media.key, "image");
      return { label, prefix: "", part: await saveInboundImage(craftPath, bytes, mediaType) };
    }
    const { bytes } = await client.downloadMessageResource(messageId, media.key, "file");
    const fileName = media.fileName ?? (media.kind === "video" ? "video.mp4" : "file");
    return { label, prefix: `${label} `, part: await saveInboundFile(craftPath, bytes, fileName) };
  } catch (error) {
    logWarn("parse.media_download_failed", {
      messageId: shortId(messageId),
      kind: media.kind,
      key: shortId(media.key),
      error: errorMessage(error),
    });
    const note = `[${media.kind} could not be downloaded: message ${messageId}, key ${media.key}]`;
    return { label: note, prefix: note };
  }
}

function optionalString(value: unknown): string | undefined {
  const text = typeof value === "string" ? value.trim() : "";
  return text || undefined;
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
