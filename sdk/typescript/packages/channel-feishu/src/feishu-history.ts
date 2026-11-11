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

import { parseConversationTarget } from "./conversation-target.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuChatMessageItem } from "./feishu-types.js";
import { errorMessage, logWarn, shortId } from "./logging.js";
import { renderMessageContent } from "./message-parser.js";

export const FEISHU_HISTORY_MAX_MESSAGES = 50;

export interface FeishuHistorySettings {
  client: () => Pick<FeishuClient, "getMessage" | "listChatMessages">;
  botAppId: () => string;
  botOpenId: () => string;
}

export class FeishuHistory {
  private readonly unaddressed: UnaddressedMessageBuffer;
  private readonly directChats = new Map<string, string>();
  private readonly topicThreads = new Map<string, string>();

  constructor(
    private readonly settings: FeishuHistorySettings,
    groupContextMessages: number,
  ) {
    this.unaddressed = new UnaddressedMessageBuffer(groupContextMessages);
  }

  /** Call only for direct messages, so a group never answers a direct chat's history read. */
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

  async quotedPart(parentId: string | undefined): Promise<InputPart | undefined> {
    if (!parentId) return undefined;
    try {
      const item = await this.settings.client().getMessage(parentId);
      const text = item ? renderHistoryItem(item, this.settings.botOpenId()) : "";
      return item && text ? quotedMessagePart(senderLabel(item), text) : undefined;
    } catch (error) {
      logWarn("history.quoted_lookup_failed", { messageId: shortId(parentId), message: errorMessage(error) });
      return undefined;
    }
  }

  async read(request: ChannelHistoryReadRequest): Promise<ChannelHistoryPage> {
    const target = parseConversationTarget(request.target);
    if (!target) return historyFailure("UnsupportedTarget", `'${request.target}' is not a Feishu chat.`);

    try {
      const container = await this.resolveContainer(target);
      if (!container) {
        return historyFailure("UnsupportedTarget", "This chat has no readable history yet.");
      }

      const page = await this.settings.client().listChatMessages(container.id, {
        containerType: container.type,
        sortType: "ByCreateTimeDesc",
        pageSize: Math.min(request.limit, FEISHU_HISTORY_MAX_MESSAGES),
        ...(request.before ? { pageToken: request.before } : {}),
      });
      const messages = page.items
        .map((item) => this.toHistoryMessage(item))
        .filter((message): message is ChannelHistoryMessage => message !== undefined)
        .reverse();
      return {
        messages,
        ...(page.hasMore && page.nextPageToken ? { before: page.nextPageToken } : {}),
      };
    } catch (error) {
      return historyFailure("HistoryUnavailable", errorMessage(error));
    }
  }

  private async resolveContainer(
    target: NonNullable<ReturnType<typeof parseConversationTarget>>,
  ): Promise<{ type: "chat" | "thread"; id: string } | undefined> {
    if (target.kind === "dm") {
      const chatId = this.directChats.get(target.id);
      return chatId ? { type: "chat", id: chatId } : undefined;
    }
    if (!target.threadKey) return { type: "chat", id: target.id };

    let threadId = this.topicThreads.get(target.threadKey);
    if (!threadId) {
      threadId = (await this.settings.client().getMessage(target.threadKey))?.threadId;
      if (threadId) this.topicThreads.set(target.threadKey, threadId);
    }
    return threadId ? { type: "thread", id: threadId } : undefined;
  }

  private toHistoryMessage(item: FeishuChatMessageItem): ChannelHistoryMessage | undefined {
    const text = renderHistoryItem(item, this.settings.botOpenId());
    if (!text) return undefined;
    const senderId = item.sender.openId ?? item.sender.appId ?? item.sender.userId;
    const appId = this.settings.botAppId();
    return {
      messageId: item.messageId,
      text,
      ...(senderId ? { senderId } : {}),
      ...(item.createTime ? { sentAt: new Date(Number(item.createTime)).toISOString() } : {}),
      ...(appId && item.sender.appId === appId ? { fromBot: true } : {}),
    };
  }
}

function senderLabel(item: FeishuChatMessageItem): string {
  return item.sender.openId ?? item.sender.appId ?? item.sender.userId ?? "";
}

export function renderHistoryItem(item: FeishuChatMessageItem, botOpenId: string): string {
  return renderMessageContent(item.messageType, item.rawContent, item.mentions, botOpenId).replace(/\s+$/u, "");
}
