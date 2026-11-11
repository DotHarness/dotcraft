import {
  historyFailure,
  quotedMessagePart,
  type ChannelHistoryMessage,
  type ChannelHistoryPage,
  type ChannelHistoryReadRequest,
  type InputPart,
} from "@dotcraft/channel";

import {
  getSenderName,
  isActionOk,
  normalizeMessageSegments,
  type OneBotActionResponse,
  type OneBotMessageEvent,
  type OneBotMessageSegment,
  type OneBotReverseWsServer,
} from "./onebot.js";
import { parseQQTarget } from "./target.js";

export const QQ_HISTORY_MAX_MESSAGES = 50;

type ActionSender = Pick<OneBotReverseWsServer, "sendAction">;

type HistoryEvent = OneBotMessageEvent & { message_seq?: number; time?: number };

const PLACEHOLDERS: Record<string, string> = {
  image: "[image]",
  record: "[voice]",
  video: "[video]",
  file: "[file]",
  face: "[emoji]",
  mface: "[sticker]",
  forward: "[forwarded messages]",
  json: "[card]",
  xml: "[card]",
};

export function describeSegments(message: OneBotMessageSegment[] | string): string {
  return normalizeMessageSegments(message)
    .map((segment) => {
      if (segment.type === "text") return String(segment.data?.text ?? "");
      if (segment.type === "at") return `@${String(segment.data?.name ?? segment.data?.qq ?? "")}`;
      if (segment.type === "reply") return "";
      return PLACEHOLDERS[segment.type] ?? `[${segment.type}]`;
    })
    .join("")
    .trim();
}

function getReplyMessageId(message: OneBotMessageSegment[] | string): string | undefined {
  const reply = normalizeMessageSegments(message).find((segment) => segment.type === "reply");
  const id = reply?.data?.id;
  return id === undefined || id === null || String(id) === "" ? undefined : String(id);
}

export async function resolveQuotedPart(server: ActionSender, message: OneBotMessageSegment[] | string): Promise<InputPart | undefined> {
  const replyId = getReplyMessageId(message);
  if (!replyId) return undefined;
  try {
    const response = await server.sendAction({ action: "get_msg", params: { message_id: toActionId(replyId) } }, 10_000);
    if (!isActionOk(response)) return undefined;
    const quoted = response.data as HistoryEvent | undefined;
    if (!quoted?.message) return undefined;
    const text = describeSegments(quoted.message);
    return text ? quotedMessagePart(getSenderName(quoted), text) : undefined;
  } catch (error) {
    console.warn("[qq] failed to fetch quoted message:", error instanceof Error ? error.message : error);
    return undefined;
  }
}

export async function readQQHistory(
  server: ActionSender,
  request: ChannelHistoryReadRequest,
  selfId: string | undefined,
): Promise<ChannelHistoryPage> {
  const target = parseQQTarget(request.target);
  if (!target) return historyFailure("UnsupportedTarget", `'${request.target}' is not a QQ chat.`);
  if (request.before !== undefined && !/^-?\d+$/.test(request.before)) {
    return historyFailure("InvalidCursor", "The history cursor is not a QQ message sequence.");
  }

  const limit = Math.min(request.limit, QQ_HISTORY_MAX_MESSAGES);
  const params: Record<string, unknown> = target.kind === "group"
    ? { group_id: Number(target.id) }
    : { user_id: Number(target.id) };
  // A cursor read walks toward older messages and includes the cursor message, so ask for one extra.
  params.count = request.before === undefined ? limit : limit + 1;
  if (request.before !== undefined) {
    params.message_seq = Number(request.before);
    params.reverse_order = true;
    params.reverseOrder = true;
  }

  let response: OneBotActionResponse;
  try {
    response = await server.sendAction({
      action: target.kind === "group" ? "get_group_msg_history" : "get_friend_msg_history",
      params,
    });
  } catch (error) {
    return historyFailure("HistoryUnavailable", error instanceof Error ? error.message : String(error));
  }
  if (!isActionOk(response)) {
    return historyFailure(
      "HistoryUnavailable",
      response.wording || response.message || `The QQ client refused the history read (retcode ${response.retcode ?? "unknown"}).`,
    );
  }

  const data = response.data as { messages?: HistoryEvent[] } | undefined;
  const returned = Array.isArray(data?.messages) ? data.messages : [];
  const events = returned
    .filter((event) => request.before === undefined || String(messageSeq(event)) !== request.before)
    .sort((a, b) => (a.time ?? 0) - (b.time ?? 0));
  const page = events.slice(Math.max(0, events.length - limit));
  const messages = page.map((event) => toHistoryMessage(event, selfId));
  // A short page means the chat's start was reached; reading past it fails on some OneBot servers.
  const oldest = page[0];
  const hasMore = returned.length >= Number(params.count);
  return {
    messages,
    ...(hasMore && oldest && messageSeq(oldest) ? { before: messageSeq(oldest) } : {}),
  };
}

function toHistoryMessage(event: HistoryEvent, selfId: string | undefined): ChannelHistoryMessage {
  const senderId = String(event.sender?.user_id ?? event.user_id ?? "");
  return {
    text: describeSegments(event.message ?? ""),
    ...(event.message_id !== undefined ? { messageId: String(event.message_id) } : {}),
    ...(senderId ? { senderId } : {}),
    senderName: getSenderName(event),
    ...(event.time ? { sentAt: new Date(event.time * 1000).toISOString() } : {}),
    ...(selfId && senderId === selfId ? { fromBot: true } : {}),
  };
}

function messageSeq(event: HistoryEvent): string | undefined {
  const seq = event.message_seq ?? event.message_id;
  return seq === undefined ? undefined : String(seq);
}

function toActionId(id: string): number | string {
  return /^-?\d+$/.test(id) ? Number(id) : id;
}
