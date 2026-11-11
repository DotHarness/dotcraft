import type { ChannelHistoryMessage } from "@dotcraft/sdk/contracts";
import { resolveClient, type ClientProvider } from "./channelRuntime.js";

export type { ChannelHistoryMessage };

export type ChannelHistoryReadRequest = {
  target: string;
  limit: number;
  before?: string;
};

export type ChannelHistoryPage = {
  /** Oldest first. */
  messages: ChannelHistoryMessage[];
  /** Cursor for the page before `messages`; omit when no older messages exist. */
  before?: string;
  errorCode?: string;
  errorMessage?: string;
};

export function historyFailure(errorCode: string, errorMessage: string): ChannelHistoryPage {
  return { messages: [], errorCode, errorMessage };
}

export class HistoryDispatcher {
  constructor(
    private readonly client: ClientProvider,
    private readonly onReadHistory: (request: ChannelHistoryReadRequest) => Promise<ChannelHistoryPage>,
  ) {}

  register(): void {
    resolveClient(this.client).registerServerRequestHandler("ext/channel/history", async (_id, params) => {
      const before = typeof params.before === "string" && params.before ? params.before : undefined;
      const request = {
        target: String(params.target ?? ""),
        limit: Math.max(1, Number(params.limit) || 1),
        ...(before ? { before } : {}),
      };
      try {
        return await this.onReadHistory(request);
      } catch (error) {
        console.error("onReadHistory raised:", error);
        return historyFailure("HistoryUnavailable", error instanceof Error ? error.message : String(error));
      }
    });
  }
}
