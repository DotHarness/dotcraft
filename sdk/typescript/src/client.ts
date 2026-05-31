/**
 * DotCraftWireClient: JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol.
 */

import {
  InitializeResult,
  JsonRpcMessage,
  Thread,
  Turn,
} from "./models.js";
import { DotCraftError, toDotCraftError } from "./errors.js";
import { Transport, TransportClosed, WebSocketTransport } from "./transport.js";

export type NotificationHandler = (params: Record<string, unknown>) => void | Promise<void>;
export type ServerRequestHandler = (
  requestId: string | number,
  params: Record<string, unknown>,
) => unknown | Promise<unknown>;
export type Unsubscribe = () => void;

export class DotCraftWireClient {
  private readonly transport: Transport;
  private nextId = 1;
  private readonly pending = new Map<
    string | number,
    { resolve: (v: unknown) => void; reject: (e: unknown) => void }
  >();
  private readonly handlers = new Map<string, NotificationHandler[]>();
  private readonly requestHandlers = new Map<string, ServerRequestHandler>();
  private approvalHandler: ServerRequestHandler | null = null;
  private readerPromise: Promise<void> | null = null;
  private initialized = false;

  constructor(transport: Transport) {
    this.transport = transport;
  }

  async connect(): Promise<void> {
    if (this.transport instanceof WebSocketTransport) {
      await this.transport.connect();
    }
  }

  async start(): Promise<void> {
    if (this.readerPromise) return;
    this.readerPromise = this.readerLoop();
  }

  async stop(): Promise<void> {
    await this.transport.close();
    this.readerPromise = null;
  }

  async initialize(opts: {
    clientName: string;
    clientVersion: string;
    clientTitle?: string | null;
    approvalSupport?: boolean;
    requestUserInputSupport?: boolean;
    streamingSupport?: boolean;
    configChange?: boolean;
    optOutNotifications?: string[] | null;
    channelName?: string | null;
    deliverySupport?: boolean;
    deliveryCapabilities?: Record<string, unknown> | null;
    channelTools?: Record<string, unknown>[] | null;
    extraCapabilities?: Record<string, unknown> | null;
  }): Promise<InitializeResult> {
    if (!this.readerPromise) await this.start();

    const capabilities: Record<string, unknown> = {
      approvalSupport: opts.approvalSupport ?? true,
      requestUserInputSupport: opts.requestUserInputSupport ?? false,
      streamingSupport: opts.streamingSupport ?? true,
      configChange: opts.configChange ?? false,
    };
    if (opts.extraCapabilities) {
      Object.assign(capabilities, opts.extraCapabilities);
    }
    if (opts.optOutNotifications?.length)
      capabilities.optOutNotificationMethods = opts.optOutNotifications;
    if (opts.channelName) {
      capabilities.channelAdapter = {
        channelName: opts.channelName,
        deliverySupport: opts.deliverySupport ?? true,
      };
      if (opts.deliveryCapabilities) {
        (capabilities.channelAdapter as Record<string, unknown>).deliveryCapabilities =
          opts.deliveryCapabilities;
      }
      if (opts.channelTools?.length) {
        (capabilities.channelAdapter as Record<string, unknown>).channelTools =
          opts.channelTools;
      }
    }

    const clientInfo: Record<string, unknown> = {
      name: opts.clientName,
      version: opts.clientVersion,
    };
    if (opts.clientTitle) clientInfo.title = opts.clientTitle;

    const result = await this.request("initialize", {
      clientInfo,
      capabilities,
    });
    await this.notify("initialized", {});
    this.initialized = true;
    return InitializeResult.fromWire(result as Record<string, unknown>);
  }

  async threadStart(params: {
    channelName: string;
    userId: string;
    workspacePath?: string;
    channelContext?: string;
    displayName?: string | null;
    historyMode?: string;
    config?: Record<string, unknown> | null;
    dynamicTools?: Record<string, unknown>[] | null;
  }): Promise<Thread> {
    const identity: Record<string, unknown> = {
      channelName: params.channelName,
      userId: params.userId,
    };
    if (params.workspacePath) identity.workspacePath = params.workspacePath;
    if (params.channelContext) identity.channelContext = params.channelContext;

    const p: Record<string, unknown> = {
      identity,
      historyMode: params.historyMode ?? "server",
    };
    if (params.displayName !== undefined && params.displayName !== null)
      p.displayName = params.displayName;
    if (params.config) p.config = params.config;
    if (params.dynamicTools?.length) p.dynamicTools = params.dynamicTools;

    const result = (await this.request("thread/start", p)) as Record<string, unknown>;
    return Thread.fromWire((result.thread as Record<string, unknown>) ?? {});
  }

  async threadResume(
    threadId: string,
    params?: { dynamicTools?: Record<string, unknown>[] | null },
  ): Promise<Thread> {
    const payload: Record<string, unknown> = { threadId };
    if (params?.dynamicTools?.length) payload.dynamicTools = params.dynamicTools;
    const result = (await this.request("thread/resume", payload)) as Record<string, unknown>;
    return Thread.fromWire((result.thread as Record<string, unknown>) ?? {});
  }

  async threadList(params: {
    channelName: string;
    userId: string;
    workspacePath?: string;
    channelContext?: string;
    includeArchived?: boolean;
  }): Promise<Thread[]> {
    const identity: Record<string, unknown> = {
      channelName: params.channelName,
      userId: params.userId,
    };
    if (params.workspacePath) identity.workspacePath = params.workspacePath;
    if (params.channelContext) identity.channelContext = params.channelContext;

    const result = (await this.request("thread/list", {
      identity,
      includeArchived: params.includeArchived ?? false,
    })) as Record<string, unknown>;
    const data = (result.data as Record<string, unknown>[]) ?? [];
    return data.map((t) => Thread.fromWire(t));
  }

  async threadRead(threadId: string, includeTurns = false): Promise<Thread> {
    const result = (await this.request("thread/read", {
      threadId,
      includeTurns,
    })) as Record<string, unknown>;
    return Thread.fromWire((result.thread as Record<string, unknown>) ?? {});
  }

  async threadSubscribe(threadId: string, replayRecent = false): Promise<void> {
    await this.request("thread/subscribe", { threadId, replayRecent });
  }

  async threadUnsubscribe(threadId: string): Promise<void> {
    await this.request("thread/unsubscribe", { threadId });
  }

  async threadPause(threadId: string): Promise<void> {
    await this.request("thread/pause", { threadId });
  }

  async threadArchive(threadId: string): Promise<void> {
    await this.request("thread/archive", { threadId });
  }

  async threadDelete(threadId: string): Promise<void> {
    await this.request("thread/delete", { threadId });
  }

  async threadSetMode(threadId: string, mode: string): Promise<void> {
    await this.request("thread/mode/set", { threadId, mode });
  }

  async turnStart(
    threadId: string,
    input: Record<string, unknown>[],
    sender?: Record<string, unknown> | null,
  ): Promise<Turn> {
    const p: Record<string, unknown> = { threadId, input };
    if (sender) p.sender = sender;
    const result = (await this.request("turn/start", p)) as Record<string, unknown>;
    return Turn.fromWire((result.turn as Record<string, unknown>) ?? {});
  }

  async turnInterrupt(threadId: string, turnId: string): Promise<void> {
    await this.request("turn/interrupt", { threadId, turnId });
  }

  async turnEnqueue(
    threadId: string,
    input: Record<string, unknown>[],
    sender?: Record<string, unknown> | null,
  ): Promise<Record<string, unknown>> {
    const p: Record<string, unknown> = { threadId, input };
    if (sender) p.sender = sender;
    return (await this.request("turn/enqueue", p)) as Record<string, unknown>;
  }

  async commandList(): Promise<Record<string, unknown>[]> {
    const result = (await this.request("command/list", {})) as Record<string, unknown>;
    return ((result.commands as Record<string, unknown>[]) ?? []);
  }

  async commandExecute(params: {
    threadId: string;
    command: string;
    arguments?: string[];
    sender?: Record<string, unknown> | null;
  }): Promise<Record<string, unknown>> {
    const payload: Record<string, unknown> = {
      threadId: params.threadId,
      command: params.command,
    };
    if (params.arguments) payload.arguments = params.arguments;
    if (params.sender) payload.sender = params.sender;
    return (await this.request("command/execute", payload)) as Record<string, unknown>;
  }

  on(method: string, fn: NotificationHandler): Unsubscribe {
    const list = this.handlers.get(method) ?? [];
    list.push(fn);
    this.handlers.set(method, list);
    return () => this.unregisterHandler(method, fn);
  }

  registerHandler(method: string, fn: NotificationHandler): void {
    this.on(method, fn);
  }

  unregisterHandler(method: string, fn: NotificationHandler): void {
    const list = this.handlers.get(method);
    if (!list) return;
    const i = list.indexOf(fn);
    if (i >= 0) list.splice(i, 1);
  }

  registerServerRequestHandler(method: string, fn: ServerRequestHandler): void {
    this.requestHandlers.set(method, fn);
  }

  setApprovalHandler(fn: ServerRequestHandler | null): void {
    this.approvalHandler = fn;
  }

  /**
   * Subscribe to thread-scoped notifications before starting a turn so early deltas are not lost.
   * Handlers are registered when this method returns; call before {@link turnStart}.
   */
  streamEvents(
    threadId: string,
    terminalMethods: readonly string[] = ["turn/completed", "turn/failed", "turn/cancelled"],
  ): AsyncIterableIterator<JsonRpcMessage> {
    const queue: JsonRpcMessage[] = [];
    let resolveWait: (() => void) | null = null;
    const allMethods = [
      "thread/started",
      "thread/renamed",
      "thread/resumed",
      "thread/statusChanged",
      "thread/runtimeChanged",
      "thread/queue/updated",
      "turn/started",
      "turn/completed",
      "turn/failed",
      "turn/cancelled",
      "item/started",
      "item/completed",
      "item/agentMessage/delta",
      "item/reasoning/delta",
      "item/toolCall/argumentsDelta",
      "item/approval/resolved",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ];

    const handlers: Array<{ method: string; fn: NotificationHandler }> = [];
    for (const methodName of allMethods) {
      const fn: NotificationHandler = async (params) => {
        if ("threadId" in params && params.threadId !== threadId) return;
        queue.push(JsonRpcMessage.fromDict({ method: methodName, params }));
        resolveWait?.();
        resolveWait = null;
      };
      handlers.push({ method: methodName, fn });
      this.registerHandler(methodName, fn);
    }

    let finished = false;
    const cleanup = (): void => {
      if (finished) return;
      finished = true;
      for (const { method, fn } of handlers) {
        this.unregisterHandler(method, fn);
      }
    };

    const iterator: AsyncIterableIterator<JsonRpcMessage> = {
      next: async (): Promise<IteratorResult<JsonRpcMessage>> => {
        while (queue.length === 0) {
          await new Promise<void>((r) => {
            resolveWait = r;
          });
        }
        const msg = queue.shift()!;
        if (msg.method && terminalMethods.includes(msg.method)) {
          cleanup();
        }
        return { value: msg, done: false };
      },
      return: async (): Promise<IteratorResult<JsonRpcMessage>> => {
        cleanup();
        return { value: undefined, done: true };
      },
      [Symbol.asyncIterator](): AsyncIterableIterator<JsonRpcMessage> {
        return this;
      },
    };

    return iterator;
  }

  private nextRequestId(): number {
    return this.nextId++;
  }

  async request<T = unknown>(method: string, params?: unknown): Promise<T> {
    const id = this.nextRequestId();
    return new Promise<T>((resolve, reject) => {
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
      const msg = new JsonRpcMessage({
        method,
        id,
        params: (params as Record<string, unknown> | undefined) ?? {},
      });
      void this.transport.writeMessage(msg.toDict()).catch((error) => {
        this.pending.delete(id);
        reject(error);
      });
    });
  }

  async notify(method: string, params: Record<string, unknown> = {}): Promise<void> {
    const msg = new JsonRpcMessage({ method, params });
    await this.transport.writeMessage(msg.toDict());
  }

  private async sendResponse(requestId: string | number, result: unknown): Promise<void> {
    const msg = new JsonRpcMessage({ id: requestId, result });
    await this.transport.writeMessage(msg.toDict());
  }

  private async sendErrorResponse(
    requestId: string | number,
    code: number,
    message: string,
  ): Promise<void> {
    const msg = new JsonRpcMessage({
      id: requestId,
      error: { code, message },
    });
    await this.transport.writeMessage(msg.toDict());
  }

  private async readerLoop(): Promise<void> {
    try {
      // eslint-disable-next-line no-constant-condition
      while (true) {
        let raw: Record<string, unknown>;
        try {
          raw = await this.transport.readMessage();
        } catch (e) {
          if (e instanceof TransportClosed) {
            for (const [, { reject }] of this.pending) {
              reject(e);
            }
            this.pending.clear();
            break;
          }
          throw e;
        }
        const msg = JsonRpcMessage.fromDict(raw);
        await this.dispatch(msg);
      }
    } catch {
      // Reader ended
    }
  }

  private async dispatch(msg: JsonRpcMessage): Promise<void> {
    if (msg.isResponse) {
      const fut = this.pending.get(msg.id as string | number);
      if (!fut) return;
      this.pending.delete(msg.id as string | number);
      if (msg.error) {
        const code = (msg.error.code ?? -1) as number;
        const m = String(msg.error.message ?? "Unknown error");
        fut.reject(toDotCraftError(code, m, msg.error.data));
      } else {
        fut.resolve(msg.result);
      }
      return;
    }
    if (msg.isNotification) {
      await this.dispatchNotification(msg);
      return;
    }
    if (msg.isRequest) {
      // Fire-and-forget: server requests (approval, heartbeat, deliver) must not block the
      // reader loop — otherwise long-running approval waits prevent reading heartbeat frames
      // and the server times out the connection (see ExternalChannelHost.SendHeartbeatAsync).
      void this.dispatchServerRequest(msg).catch((e) =>
        console.error("Error in server request handler:", e),
      );
    }
  }

  private async dispatchNotification(msg: JsonRpcMessage): Promise<void> {
    const method = msg.method ?? "";
    const params = (msg.params as Record<string, unknown>) ?? {};
    const list = this.handlers.get(method) ?? [];
    for (const h of [...list]) {
      void Promise.resolve(h(params)).catch(() => {
        /* logged in adapter */
      });
    }
  }

  private async dispatchServerRequest(msg: JsonRpcMessage): Promise<void> {
    const method = msg.method ?? "";
    const params = (msg.params as Record<string, unknown>) ?? {};
    const requestId = msg.id as string | number;

    if (method === "item/approval/request") {
      const handler = this.approvalHandler;
      if (!handler) {
        await this.sendResponse(requestId, { decision: "accept" });
        return;
      }
      try {
        const decision = await handler(requestId, params);
        await this.sendResponse(requestId, { decision });
      } catch {
        await this.sendResponse(requestId, { decision: "cancel" });
      }
      return;
    }

    if (method === "ext/channel/heartbeat") {
      await this.sendResponse(requestId, {});
      return;
    }

    const handler = this.requestHandlers.get(method);
    if (!handler) {
      await this.sendErrorResponse(requestId, -32601, `Method not handled: ${method}`);
      return;
    }
    try {
      const result = await handler(requestId, params);
      await this.sendResponse(requestId, result ?? {});
    } catch (e) {
      await this.sendErrorResponse(requestId, -32603, String(e));
    }
  }
}
