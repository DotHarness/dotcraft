import type { FeishuChatInfo } from "./feishu-types.js";
import { errorMessage, logWarn, shortId } from "./logging.js";

const DEFAULT_TTL_MS = 10 * 60 * 1000;

export type ChatInfoClient = {
  getChatInfo(chatId: string): Promise<FeishuChatInfo>;
};

export function isThreadCapableChat(info: FeishuChatInfo): boolean {
  return info.chatMode === "topic" || info.groupMessageType === "thread";
}

// The inbound event never says whether a group uses topics, so this is looked up and cached per chat.
export class FeishuChatInfoCache {
  private readonly entries = new Map<string, { threadCapable: boolean; expiresAt: number }>();
  private readonly ttlMs: number;
  private readonly now: () => number;

  constructor(
    private readonly client: ChatInfoClient,
    options: { ttlMs?: number; now?: () => number } = {},
  ) {
    this.ttlMs = options.ttlMs ?? DEFAULT_TTL_MS;
    this.now = options.now ?? (() => Date.now());
  }

  async isThreadCapable(chatId: string): Promise<boolean> {
    const cached = this.entries.get(chatId);
    if (cached && cached.expiresAt > this.now()) return cached.threadCapable;
    try {
      const threadCapable = isThreadCapableChat(await this.client.getChatInfo(chatId));
      this.entries.set(chatId, { threadCapable, expiresAt: this.now() + this.ttlMs });
      return threadCapable;
    } catch (error) {
      logWarn("chat_info.lookup_failed", {
        chatId: shortId(chatId),
        message: errorMessage(error),
      });
      return false;
    }
  }
}
