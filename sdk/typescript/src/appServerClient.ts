/** High-level AppServer operations built on the pure JSON-RPC Wire client. */

import { DotCraftWireClient, type WireResponseHandlers } from "./client.js";
import { captureThreadEvents } from "./threadEvents.js";
import { JsonRpcMessage } from "./models.js";
import type {
  ClientRequestMethods,
  RuntimeAdditionalContextEntry,
  SessionThread,
  SessionTurn,
  ThreadItemsListResult,
  ThreadResumeParams,
  ThreadResumeResult,
  ThreadForkParams,
  ThreadListResult,
  ThreadSummary,
  ThreadTurnsListResult,
} from "./generated/appserver/index.js";

export class InternalAppServerClient extends DotCraftWireClient {
  private readonly attachedThreads = new Set<string>();

  constructor(...args: ConstructorParameters<typeof DotCraftWireClient>) {
    super(...args);
    this.onStateChanged((state) => {
      if (state !== "ready") this.attachedThreads.clear();
    });
  }

  override async requestRaw<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null, responseHandlers?: WireResponseHandlers<T>): Promise<T> {
    return await super.requestRaw<T>(method, params, timeoutMs, { ...responseHandlers, onResponse: (result) => {
      if (method === "thread/unsubscribe") {
        this.attachedThreads.delete((params as { threadId: string }).threadId);
      } else if (method === "thread/start" || method === "thread/resume" || method === "thread/fork") {
        const thread = (result as { thread?: SessionThread }).thread;
        if (thread) this.attachedThreads.add(thread.id);
      }
      responseHandlers?.onResponse?.(result);
    } });
  }

  async ensureThreadAttached(threadId: string, params?: Parameters<InternalAppServerClient["threadResume"]>[1]): Promise<ThreadResumeResult | undefined> {
    if (!this.attachedThreads.has(threadId)) return await this.threadResume(threadId, params);
    return undefined;
  }

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
  }, onResponse?: (thread: SessionThread) => void): Promise<SessionThread> {
    const identity: Record<string, unknown> = { channelName: params.channelName, userId: params.userId };
    if (params.workspacePath) identity.workspacePath = params.workspacePath;
    if (params.channelContext) identity.channelContext = params.channelContext;
    const payload: Record<string, unknown> = { identity, historyMode: params.historyMode ?? "server" };
    if (params.displayName != null) payload.displayName = params.displayName;
    if (params.config) payload.config = params.config;
    if (params.dynamicTools != null) payload.dynamicTools = params.dynamicTools;
    if (params.additionalContext) payload.additionalContext = params.additionalContext;
    const result = await this.request("thread/start", payload as ClientRequestMethods["thread/start"]["params"], undefined, {
      onResponse: (result) => onResponse?.(result.thread),
    });
    return result.thread;
  }

  async threadResume(threadId: string, params?: Omit<ThreadResumeParams, "threadId" | "dynamicTools"> & {
    dynamicTools?: unknown[] | null;
  }, onResponse?: (result: ThreadResumeResult) => void): Promise<ThreadResumeResult> {
    return await this.request("thread/resume", { ...params, threadId } as ThreadResumeParams, undefined, { onResponse });
  }

  async threadFork(threadId: string, params: Omit<ThreadForkParams, "threadId"> = {}, onResponse?: (thread: SessionThread) => void): Promise<SessionThread> {
    const result = await this.request("thread/fork", { ...params, threadId }, undefined, {
      onResponse: (result) => {
        if (!result.thread) throw new Error("thread/fork did not return a thread.");
        onResponse?.(result.thread);
      },
    });
    if (!result.thread) throw new Error("thread/fork did not return a thread.");
    return result.thread;
  }

  async threadList(params: Parameters<InternalAppServerClient["threadListPage"]>[0]): Promise<ThreadSummary[]> {
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

  async threadRead(threadId: string): Promise<SessionThread> {
    const result = await this.request("thread/read", { threadId });
    return result.thread;
  }

  async threadTurnsList(params: {
    threadId: string;
    cursor?: string;
    limit?: number;
    sortDirection?: "ascending" | "descending";
  }): Promise<ThreadTurnsListResult> {
    return await this.request("thread/turns/list", params);
  }

  async threadItemsList(params: {
    threadId: string;
    turnId?: string;
    cursor?: string;
    limit?: number;
    sortDirection?: "ascending" | "descending";
  }): Promise<ThreadItemsListResult> {
    return await this.request("thread/items/list", params);
  }

  async threadUnsubscribe(threadId: string): Promise<void> { await this.request("thread/unsubscribe", { threadId }); }
  async threadArchive(threadId: string): Promise<void> { await this.request("thread/archive", { threadId }); }
  async threadDelete(threadId: string): Promise<void> { await this.request("thread/delete", { threadId }); }
  async threadSetMode(threadId: string, mode: string): Promise<void> { await this.request("thread/mode/set", { threadId, mode }); }

  async turnStart(
    threadId: string,
    input: Record<string, unknown>[],
    sender?: Record<string, unknown> | null,
    settings: Pick<ClientRequestMethods["turn/start"]["params"], "providerId" | "model" | "reasoning" | "speed"> = {},
  ): Promise<SessionTurn> {
    const payload: Record<string, unknown> = { ...settings, threadId, input };
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

  streamEvents(threadId: string): AsyncIterableIterator<JsonRpcMessage> {
    return captureThreadEvents(this, threadId);
  }
}
