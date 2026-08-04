/** High-level AppServer operations built on the pure JSON-RPC Wire client. */

import { DotCraftWireClient, JsonRpcMessage, type NotificationHandler } from "@dotcraft/sdk/wire";
import type {
  ClientRequestMethods,
  RuntimeAdditionalContextEntry,
  SessionThread,
  SessionTurn,
  ThreadListResult,
  ThreadSummary,
} from "@dotcraft/sdk/contracts";

export class ChannelAppServerClient extends DotCraftWireClient {
  override async initialize(opts: Parameters<DotCraftWireClient["initialize"]>[0]) {
    if (opts.approvalSupport && !this.hasServerRequestHandler("item/approval/request")) {
      throw new Error("approvalSupport requires an approval request handler");
    }
    if (opts.requestUserInputSupport && !this.hasServerRequestHandler("item/tool/requestUserInput")) {
      throw new Error("requestUserInputSupport requires a user-input request handler");
    }
    return await super.initialize(opts);
  }

  async threadStart(params: {
    channelName: string;
    userId: string;
    workspacePath?: string;
    channelContext?: string;
    displayName?: string | null;
    historyMode?: string;
    config?: Record<string, unknown> | null;
    dynamicTools?: unknown[] | null;
    additionalContext?: Record<string, RuntimeAdditionalContextEntry> | null;
  }): Promise<SessionThread> {
    const identity: Record<string, unknown> = { channelName: params.channelName, userId: params.userId };
    if (params.workspacePath) identity.workspacePath = params.workspacePath;
    if (params.channelContext) identity.channelContext = params.channelContext;
    const payload: Record<string, unknown> = { identity, historyMode: params.historyMode ?? "server" };
    if (params.displayName != null) payload.displayName = params.displayName;
    if (params.config) payload.config = params.config;
    if (params.dynamicTools != null) payload.dynamicTools = params.dynamicTools;
    if (params.additionalContext) payload.additionalContext = params.additionalContext;
    const result = await this.request("thread/start", payload as ClientRequestMethods["thread/start"]["params"]);
    return result.thread;
  }

  async threadResume(threadId: string, params?: {
    dynamicTools?: unknown[] | null;
    additionalContext?: Record<string, RuntimeAdditionalContextEntry> | null;
  }): Promise<SessionThread> {
    const payload: Record<string, unknown> = { threadId };
    if (params?.dynamicTools != null) payload.dynamicTools = params.dynamicTools;
    if (params?.additionalContext != null) payload.additionalContext = params.additionalContext;
    const result = await this.request("thread/resume", payload as ClientRequestMethods["thread/resume"]["params"]);
    return result.thread;
  }

  async threadList(params: Parameters<ChannelAppServerClient["threadListPage"]>[0]): Promise<ThreadSummary[]> {
    return (await this.threadListPage(params)).data;
  }

  async threadListPage(params: {
    channelName: string;
    userId: string;
    workspacePath?: string;
    channelContext?: string;
    scope?: "identity" | "workspace";
    includeArchived?: boolean;
    query?: string;
    limit?: number;
    cursor?: string;
  }): Promise<ThreadListResult> {
    const identity: Record<string, unknown> = { channelName: params.channelName, userId: params.userId };
    if (params.workspacePath) identity.workspacePath = params.workspacePath;
    if (params.channelContext) identity.channelContext = params.channelContext;
    const payload: Record<string, unknown> = { identity, includeArchived: params.includeArchived ?? false };
    if (params.scope) payload.scope = params.scope;
    if (params.query) payload.query = params.query;
    if (params.limit != null) payload.limit = params.limit;
    if (params.cursor) payload.cursor = params.cursor;
    return await this.request("thread/list", payload as ClientRequestMethods["thread/list"]["params"]);
  }

  async threadRead(threadId: string, includeTurns = false, params?: { turnLimit?: number; cursor?: string }): Promise<SessionThread> {
    const payload: Record<string, unknown> = { threadId, includeTurns };
    if (params?.turnLimit != null) payload.turnLimit = params.turnLimit;
    if (params?.cursor) payload.cursor = params.cursor;
    const result = await this.request("thread/read", payload as ClientRequestMethods["thread/read"]["params"]);
    return result.thread;
  }

  async threadSubscribe(threadId: string, replayRecent = false): Promise<void> { await this.request("thread/subscribe", { threadId, replayRecent }); }
  async threadUnsubscribe(threadId: string): Promise<void> { await this.request("thread/unsubscribe", { threadId }); }
  async threadPause(threadId: string): Promise<void> { await this.request("thread/pause", { threadId }); }
  async threadArchive(threadId: string): Promise<void> { await this.request("thread/archive", { threadId }); }
  async threadDelete(threadId: string): Promise<void> { await this.request("thread/delete", { threadId }); }
  async threadSetMode(threadId: string, mode: string): Promise<void> { await this.request("thread/mode/set", { threadId, mode }); }

  async turnStart(threadId: string, input: Record<string, unknown>[], sender?: Record<string, unknown> | null): Promise<SessionTurn> {
    const payload: Record<string, unknown> = { threadId, input };
    if (sender) payload.sender = sender;
    const result = await this.request("turn/start", payload as ClientRequestMethods["turn/start"]["params"]);
    return result.turn;
  }

  async turnInterrupt(threadId: string, turnId: string): Promise<void> { await this.request("turn/interrupt", { threadId, turnId }); }

  async turnEnqueue(threadId: string, input: Record<string, unknown>[], sender?: Record<string, unknown> | null): Promise<Record<string, unknown>> {
    const payload: Record<string, unknown> = { threadId, input };
    if (sender) payload.sender = sender;
    return await this.request("turn/enqueue", payload as ClientRequestMethods["turn/enqueue"]["params"]) as Record<string, unknown>;
  }

  async commandList(): Promise<Record<string, unknown>[]> {
    const result = await this.request("command/list", {}) as Record<string, unknown>;
    return (result.commands as Record<string, unknown>[]) ?? [];
  }

  async commandExecute(params: { threadId: string; command: string; arguments?: string[]; sender?: Record<string, unknown> | null }): Promise<Record<string, unknown>> {
    const payload: Record<string, unknown> = { threadId: params.threadId, command: params.command };
    if (params.arguments) payload.arguments = params.arguments;
    if (params.sender) payload.sender = params.sender;
    return await this.request("command/execute", payload) as Record<string, unknown>;
  }

  streamEvents(threadId: string, terminalMethods: readonly string[] = ["turn/completed", "turn/failed", "turn/cancelled"]): AsyncIterableIterator<JsonRpcMessage> {
    const queue: JsonRpcMessage[] = [];
    let resolveWait: (() => void) | null = null;
    const methods = [
      "thread/started", "thread/renamed", "thread/resumed", "thread/statusChanged", "thread/runtimeChanged",
      "thread/queue/updated", "turn/started", "turn/completed", "turn/failed", "turn/cancelled", "item/started",
      "item/completed", "item/agentMessage/delta", "item/reasoning/delta", "item/toolCall/argumentsDelta",
      "item/approval/resolved", "subagent/progress", "item/usage/delta", "system/event", "plan/updated",
    ];
    const unsubscribe = methods.map((method) => this.onRaw(method, (async (params) => {
      if ("threadId" in params && params.threadId !== threadId) return;
      queue.push(JsonRpcMessage.fromDict({ method, params }));
      resolveWait?.();
      resolveWait = null;
    }) satisfies NotificationHandler));
    let finished = false;
    const cleanup = (): void => {
      if (finished) return;
      finished = true;
      unsubscribe.forEach((remove) => remove());
    };
    return {
      next: async () => {
        while (queue.length === 0) await new Promise<void>((resolve) => { resolveWait = resolve; });
        const value = queue.shift()!;
        if (value.method && terminalMethods.includes(value.method)) cleanup();
        return { value, done: false };
      },
      return: async () => { cleanup(); return { value: undefined, done: true }; },
      [Symbol.asyncIterator]() { return this; },
    };
  }
}
