import {
  UnaddressedMessageBuffer,
  historyFailure,
  quotedMessagePart,
  type ChannelHistoryMessage,
  type ChannelHistoryPage,
  type ChannelHistoryReadRequest,
  type ChatContextMessage,
  type InputPart,
} from "@dotcraft/channel";

import { conversationTargetBase, conversationTargetThreadKey, parseConversationTarget } from "./conversation-target.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuChatMessageItem } from "./feishu-types.js";
import { errorMessage, logWarn } from "./logging.js";
import { renderMessageContent } from "./message-parser.js";

export const FEISHU_HISTORY_MAX_MESSAGES = 50;

const MAX_PAGES_PER_READ = 4;

export interface FeishuHistorySettings {
  client: () => FeishuClient;
  botOpenId: () => string;
}

export class FeishuHistory {
  private readonly unaddressed: UnaddressedMessageBuffer;

  constructor(
    private readonly settings: FeishuHistorySettings,
    groupContextMessages: number,
  ) {
    this.unaddressed = new UnaddressedMessageBuffer(groupContextMessages);
  }

  private readonly directChats = new Map<string, string>();

  rememberDirectChat(openId: string, chatId: string): void {
    if (openId && chatId) this.directChats.set(openId, chatId);
  }

  rememberUnaddressed(channelContext: string, message: ChatContextMessage): void {
    this.unaddressed.remember(channelContext, message);
  }

  snapshot(channelContext: string): ReturnType<UnaddressedMessageBuffer["snapshot"]> {
    return this.unaddressed.snapshot(channelContext);
  }

  reset(channelContext: string): void {
    this.unaddressed.reset(channelContext);
  }

  /** Builds the quoted-message part for a reply, reading the parent message with the bot's identity. */
  async quotedPart(parentId: string | undefined, chatId: string): Promise<InputPart | undefined> {
    if (!parentId || !chatId) return undefined;
    try {
      const page = await this.settings.client().listChatMessages(chatId, {
        startTime: "0",
        pageSize: 1,
        sortType: "ByCreateTimeAsc",
        messageId: parentId,
      });
      const item = page.items[0];
      const text = item ? renderHistoryItem(item, this.settings.botOpenId()) : "";
      return text ? quotedMessagePart(item!.sender.openId ?? item!.sender.userId ?? "", text) : undefined;
    } catch (error) {
      logWarn("history.quoted_lookup_failed", { messageId: parentId, message: errorMessage(error) });
      return undefined;
    }
  }

  async read(request: ChannelHistoryReadRequest): Promise<ChannelHistoryPage> {
    const target = parseConversationTarget(request.target);
    if (!target) return historyFailure("UnsupportedTarget", `'${request.target}' is not a Feishu chat.`);

    const chatId = target.kind === "dm" ? this.directChats.get(target.id) : conversationTargetBase(request.target);
    if (!chatId) {
      return historyFailure("UnsupportedTarget", "This direct chat has no history yet; send a message in it first.");
    }

    if (request.before !== undefined && !/^\d+$/.test(request.before)) {
      return historyFailure("InvalidCursor", "The history cursor is not a Feishu timestamp.");
    }

    const limit = Math.min(request.limit, FEISHU_HISTORY_MAX_MESSAGES);
    const threadKey = target.kind === "group" ? conversationTargetThreadKey(request.target) : "";
    const botOpenId = this.settings.botOpenId();
    const messages: ChannelHistoryMessage[] = [];
    const seen = new Set<string>();
    let endTime = request.before;

    try {
      for (let page = 0; page < MAX_PAGES_PER_READ && messages.length < limit; page++) {
        const result = await this.settings.client().listChatMessages(chatId, {
          startTime: "0",
          pageSize: limit,
          sortType: "ByCreateTimeDesc",
          ...(endTime ? { endTime } : {}),
        });
        const items = result.items;
        if (items.length === 0) break;
        for (const item of items) {
          if (seen.has(item.messageId)) continue;
          seen.add(item.messageId);
          if (endTime && Number(item.createTime ?? 0) >= Number(endTime)) continue;
          if (threadKey && item.rootId !== threadKey) continue;
          const text = renderHistoryItem(item, botOpenId);
          if (!text) continue;
          messages.push({
            messageId: item.messageId,
            text,
            ...(item.sender.openId ? { senderId: item.sender.openId } : item.sender.userId ? { senderId: item.sender.userId } : {}),
            ...(item.createTime ? { sentAt: new Date(Number(item.createTime)).toISOString() } : {}),
            ...(botOpenId && (item.sender.openId === botOpenId || item.sender.senderType === "app") ? { fromBot: true } : {}),
          });
        }
        const oldest = items.reduce((min, item) => Math.min(min, Number(item.createTime ?? 0)), Number.MAX_SAFE_INTEGER);
        if (!Number.isFinite(oldest) || oldest === Number.MAX_SAFE_INTEGER) break;
        endTime = String(oldest);
        if (!result.nextPageToken && items.length < limit) break;
      }
    } catch (error) {
      return historyFailure("HistoryUnavailable", errorMessage(error));
    }

    messages.sort((a, b) => Number(a.sentAt ? Date.parse(a.sentAt) : 0) - Number(b.sentAt ? Date.parse(b.sentAt) : 0));
    const pageMessages = messages.slice(Math.max(0, messages.length - limit));
    if (messages.length <= limit) return { messages: pageMessages };

    const oldestTime = pageMessages[0]?.sentAt;
    return {
      messages: pageMessages,
      ...(oldestTime ? { before: String(Date.parse(oldestTime)) } : {}),
    };
  }
}

/** Plain-text rendering of a stored message; non-text content becomes a short placeholder. */
export function renderHistoryItem(item: FeishuChatMessageItem, botOpenId: string): string {
  return renderMessageContent(item.messageType, item.rawContent, item.mentions, botOpenId).replace(/\s+$/u, "");
}
