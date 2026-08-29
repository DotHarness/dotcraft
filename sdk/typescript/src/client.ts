/**
 * DotCraftWireClient: JSON-RPC 2.0 client for the DotCraft AppServer Wire Protocol.
 */

import { JsonRpcMessage } from "./models.js";
import { ReconnectQueueFullError, RequestTimeoutError, toJsonRpcError } from "./errors.js";
import { Transport, TransportClosed, WebSocketTransport } from "./transport.js";
import type {
  ChannelToolDescriptor,
  ClientNotificationMethods,
  ClientRequestMethods,
  ServerNotificationMethods,
  ServerRequestMethods,
  InitializeResult,
} from "./generated/appserver/index.js";

export type NotificationHandler = (params: Record<string, unknown>) => void | Promise<void>;
export type ServerRequestHandler = (
  requestId: string | number,
  params: Record<string, unknown>,
) => unknown | Promise<unknown>;
export type Unsubscribe = () => void;
export type WireConnectionState =
  | "connecting"
  | "initializing"
  | "ready"
  | "disconnected"
  | "reconnecting"
  | "reconnectError"
  | "closed";

export interface DotCraftWireClientOptions {
  autoReconnect?: boolean;
  defaultTimeoutMs?: number;
  initializeTimeoutMs?: number | null;
  maxReconnectQueueSize?: number;
  reconnectBaseDelayMs?: number;
  reconnectMaxDelayMs?: number;
  random?: () => number;
}

export class DotCraftWireClient {
  private readonly transport: Transport;
  private readonly options: Required<Omit<DotCraftWireClientOptions, "initializeTimeoutMs">> & {
    initializeTimeoutMs: number | null;
  };
  private nextId = 1;
  private readonly pending = new Map<
    string | number,
    { resolve: (v: unknown) => void; reject: (e: unknown) => void; timer: ReturnType<typeof setTimeout> | null; written: boolean }
  >();
  private readonly handlers = new Map<string, NotificationHandler[]>();
  private readonly rawNotificationHandlers = new Set<
    (method: string, params: Record<string, unknown>) => void | Promise<void>
  >();
  private readonly requestHandlers = new Map<string, ServerRequestHandler>();
  private rawServerRequestFallback: ((
    method: string,
    requestId: string | number,
    params: Record<string, unknown>,
  ) => unknown | Promise<unknown>) | null = null;
  private readerPromise: Promise<void> | null = null;
  private initialized = false;
  private initializeResultValue: InitializeResult | null = null;
  private initializeOptions: Parameters<DotCraftWireClient["initialize"]>[0] | null = null;
  private reconnecting = false;
  private explicitlyClosed = false;
  private reconnectAttempt = 0;
  private queuedWrites: Array<{
    message: Record<string, unknown>;
    requestId?: string | number;
    resolve: () => void;
    reject: (error: unknown) => void;
  }> = [];
  private stateValue: WireConnectionState = "disconnected";
  private readonly stateHandlers = new Set<(state: WireConnectionState, error?: unknown) => void>();

  constructor(transport: Transport, options: DotCraftWireClientOptions = {}) {
    this.transport = transport;
    this.options = {
      autoReconnect: options.autoReconnect ?? false,
      defaultTimeoutMs: options.defaultTimeoutMs ?? 30_000,
      initializeTimeoutMs: options.initializeTimeoutMs ?? null,
      maxReconnectQueueSize: options.maxReconnectQueueSize ?? 1024,
      reconnectBaseDelayMs: options.reconnectBaseDelayMs ?? 1000,
      reconnectMaxDelayMs: options.reconnectMaxDelayMs ?? 30_000,
      random: options.random ?? Math.random,
    };
  }

  get state(): WireConnectionState {
    return this.stateValue;
  }

  get initializeResult(): InitializeResult | null {
    return this.initializeResultValue;
  }

  onStateChanged(handler: (state: WireConnectionState, error?: unknown) => void): Unsubscribe {
    this.stateHandlers.add(handler);
    return () => this.stateHandlers.delete(handler);
  }

  private setState(state: WireConnectionState, error?: unknown): void {
    this.stateValue = state;
    for (const handler of this.stateHandlers) handler(state, error);
  }

  async connect(): Promise<void> {
    this.setState("connecting");
    if (this.transport instanceof WebSocketTransport) {
      await this.transport.connect();
    }
    this.setState("ready");
  }

  async start(): Promise<void> {
    if (this.readerPromise) return;
    if (this.stateValue === "disconnected") this.setState("ready");
    this.readerPromise = this.readerLoop();
  }

  async stop(): Promise<void> {
    this.explicitlyClosed = true;
    this.reconnecting = false;
    await this.transport.close();
    this.rejectAllPending(new TransportClosed("Wire client closed"));
    this.rejectQueuedWrites(new TransportClosed("Wire client closed"));
    this.readerPromise = null;
    this.setState("closed");
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
    channelTools?: ChannelToolDescriptor[] | null;
    extraCapabilities?: Record<string, unknown> | null;
  }): Promise<InitializeResult> {
    this.initializeOptions = { ...opts };
    this.setState("initializing");
    if (!this.readerPromise) await this.start();

    const capabilities: Record<string, unknown> = {
      approvalSupport: opts.approvalSupport ?? false,
      requestUserInputSupport: opts.requestUserInputSupport ?? false,
      streamingSupport: opts.streamingSupport ?? true,
      configChange: opts.configChange ?? false,
      appBindingVersion: 1,
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

    const clientInfo: { name: string; version: string; title?: string } = {
      name: opts.clientName,
      version: opts.clientVersion,
    };
    if (opts.clientTitle) clientInfo.title = opts.clientTitle;

    const result = await this.requestRawInternal<ClientRequestMethods["initialize"]["result"]>("initialize", {
      clientInfo,
      capabilities,
    }, this.options.initializeTimeoutMs, true);
    await this.notifyRaw("initialized", {}, true);
    this.initialized = true;
    this.setState("ready");
    this.initializeResultValue = result;
    return this.initializeResultValue;
  }

  on<M extends keyof ServerNotificationMethods>(
    method: M,
    fn: (params: ServerNotificationMethods[M]["params"]) => void | Promise<void>,
  ): Unsubscribe {
    return this.addNotificationHandler(method, fn as NotificationHandler);
  }

  onRaw(method: string, fn: NotificationHandler): Unsubscribe {
    return this.addNotificationHandler(method, fn);
  }

  onAnyNotificationRaw(
    fn: (method: string, params: Record<string, unknown>) => void | Promise<void>,
  ): Unsubscribe {
    this.rawNotificationHandlers.add(fn);
    return () => this.rawNotificationHandlers.delete(fn);
  }

  private addNotificationHandler(method: string, fn: NotificationHandler): Unsubscribe {
    const list = this.handlers.get(method) ?? [];
    list.push(fn);
    this.handlers.set(method, list);
    return () => this.unregisterHandler(method, fn);
  }

  private unregisterHandler(method: string, fn: NotificationHandler): void {
    const list = this.handlers.get(method);
    if (!list) return;
    const i = list.indexOf(fn);
    if (i >= 0) list.splice(i, 1);
  }

  registerServerRequestHandler<M extends keyof ServerRequestMethods>(
    method: M,
    fn: (
      requestId: string | number,
      params: ServerRequestMethods[M]["params"],
    ) => ServerRequestMethods[M]["result"] | Promise<ServerRequestMethods[M]["result"]>,
  ): void {
    this.requestHandlers.set(method, fn as ServerRequestHandler);
  }

  registerServerRequestHandlerRaw<P extends Record<string, unknown>, R>(
    method: string,
    fn: (requestId: string | number, params: P) => R | Promise<R>,
  ): void {
    this.requestHandlers.set(method, fn as ServerRequestHandler);
  }

  protected hasServerRequestHandler(method: string): boolean {
    return this.requestHandlers.has(method);
  }

  registerServerRequestFallbackRaw(
    fn: (
      method: string,
      requestId: string | number,
      params: Record<string, unknown>,
    ) => unknown | Promise<unknown>,
  ): Unsubscribe {
    this.rawServerRequestFallback = fn;
    return () => {
      if (this.rawServerRequestFallback === fn) this.rawServerRequestFallback = null;
    };
  }

  private nextRequestId(): number {
    return this.nextId++;
  }

  async request<M extends keyof ClientRequestMethods>(
    method: M,
    params: ClientRequestMethods[M]["params"],
    timeoutMs?: number | null,
  ): Promise<ClientRequestMethods[M]["result"]> {
    return await this.requestRaw<ClientRequestMethods[M]["result"]>(method, params, timeoutMs);
  }

  async requestRaw<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T> {
    return await this.requestRawInternal<T>(method, params, timeoutMs, false);
  }

  private async requestRawInternal<T>(
    method: string,
    params: unknown,
    timeoutMs: number | null | undefined,
    bypassReconnectQueue: boolean,
  ): Promise<T> {
    const id = this.nextRequestId();
    return new Promise<T>((resolve, reject) => {
      const effectiveTimeout = timeoutMs === undefined ? this.options.defaultTimeoutMs : timeoutMs;
      const timer = effectiveTimeout == null
        ? null
        : setTimeout(() => {
            this.pending.delete(id);
            reject(new RequestTimeoutError(method, effectiveTimeout));
          }, effectiveTimeout);
      this.pending.set(id, { resolve: resolve as (v: unknown) => void, reject, timer, written: false });
      const msg = new JsonRpcMessage({
        method,
        id,
        params: params as Record<string, unknown> | undefined,
      });
      void this.writeMessage(msg.toDict(), id, bypassReconnectQueue).catch((error) => this.rejectPending(id, error));
    });
  }

  async notify<M extends keyof ClientNotificationMethods>(
    method: M,
    params: ClientNotificationMethods[M]["params"],
  ): Promise<void> {
    await this.notifyRaw(method, params);
  }

  async notifyRaw(
    method: string,
    params: Record<string, unknown> = {},
    bypassReconnectQueue = false,
  ): Promise<void> {
    const msg = new JsonRpcMessage({ method, params });
    await this.writeMessage(msg.toDict(), undefined, bypassReconnectQueue);
  }

  private async writeMessage(
    message: Record<string, unknown>,
    requestId?: string | number,
    bypassReconnectQueue = false,
  ): Promise<void> {
    if (this.explicitlyClosed) throw new TransportClosed("Wire client closed");
    if (this.reconnecting && !bypassReconnectQueue) {
      if (this.queuedWrites.length >= this.options.maxReconnectQueueSize) {
        throw new ReconnectQueueFullError(this.options.maxReconnectQueueSize);
      }
      await new Promise<void>((resolve, reject) => {
        this.queuedWrites.push({ message, requestId, resolve, reject });
      });
      return;
    }

    await this.transport.writeMessage(message);
    if (requestId !== undefined) {
      const pending = this.pending.get(requestId);
      if (pending) pending.written = true;
    }
  }

  private rejectPending(requestId: string | number, error: unknown): void {
    const pending = this.pending.get(requestId);
    if (!pending) return;
    this.pending.delete(requestId);
    if (pending.timer) clearTimeout(pending.timer);
    pending.reject(error);
  }

  private rejectAllPending(error: unknown, writtenOnly = false): void {
    for (const [id, pending] of this.pending) {
      if (writtenOnly && !pending.written) continue;
      this.pending.delete(id);
      if (pending.timer) clearTimeout(pending.timer);
      pending.reject(error);
    }
  }

  private rejectQueuedWrites(error: unknown): void {
    for (const queued of this.queuedWrites.splice(0)) queued.reject(error);
  }

  private async flushQueuedWrites(): Promise<void> {
    const queued = this.queuedWrites.splice(0);
    for (const item of queued) {
      if (item.requestId !== undefined && !this.pending.has(item.requestId)) {
        item.resolve();
        continue;
      }
      try {
        await this.transport.writeMessage(item.message);
        if (item.requestId !== undefined) {
          const pending = this.pending.get(item.requestId);
          if (pending) pending.written = true;
        }
        item.resolve();
      } catch (error) {
        item.reject(error);
        if (item.requestId !== undefined) this.rejectPending(item.requestId, error);
      }
    }
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
            this.setState("disconnected", e);
            this.rejectAllPending(e, this.options.autoReconnect);
            if (await this.tryReconnect()) continue;
            this.rejectAllPending(e);
            this.rejectQueuedWrites(e);
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

  private async tryReconnect(): Promise<boolean> {
    if (
      this.explicitlyClosed ||
      !this.options.autoReconnect ||
      !(this.transport instanceof WebSocketTransport) ||
      !this.initializeOptions
    ) {
      return false;
    }

    this.reconnecting = true;
    while (!this.explicitlyClosed) {
      this.setState("reconnecting");
      const delay = Math.min(
        this.options.reconnectBaseDelayMs * 2 ** this.reconnectAttempt,
        this.options.reconnectMaxDelayMs,
      );
      const jittered = Math.round(delay * (0.8 + this.options.random() * 0.4));
      await new Promise((resolve) => setTimeout(resolve, jittered));
      try {
        await this.transport.connect();
        await this.performReconnectHandshake(this.initializeOptions);
        this.reconnectAttempt = 0;
        this.reconnecting = false;
        await this.flushQueuedWrites();
        this.setState("ready");
        return true;
      } catch (error) {
        this.reconnectAttempt += 1;
        this.setState("reconnectError", error);
      }
    }
    return false;
  }

  private async performReconnectHandshake(
    options: NonNullable<DotCraftWireClient["initializeOptions"]>,
  ): Promise<void> {
    let settled = false;
    let failure: unknown;
    const handshake = this.initialize(options).then(
      () => { settled = true; },
      (error) => { failure = error; settled = true; },
    );
    while (!settled) {
      const raw = await this.transport.readMessage();
      await this.dispatch(JsonRpcMessage.fromDict(raw));
      await Promise.race([
        handshake,
        new Promise<void>((resolve) => setTimeout(resolve, 1)),
      ]);
    }
    await handshake;
    if (failure !== undefined) throw failure;
  }

  private async dispatch(msg: JsonRpcMessage): Promise<void> {
    if (msg.isResponse) {
      const fut = this.pending.get(msg.id as string | number);
      if (!fut) return;
      this.pending.delete(msg.id as string | number);
      if (fut.timer) clearTimeout(fut.timer);
      if (msg.error) {
        const code = (msg.error.code ?? -1) as number;
        const m = String(msg.error.message ?? "Unknown error");
        fut.reject(toJsonRpcError(code, m, msg.error.data));
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
    for (const handler of [...this.rawNotificationHandlers]) {
      void Promise.resolve(handler(method, params)).catch(() => {
        /* host listener failures are isolated from the read loop */
      });
    }
  }

  private async dispatchServerRequest(msg: JsonRpcMessage): Promise<void> {
    const method = msg.method ?? "";
    const params = (msg.params as Record<string, unknown>) ?? {};
    const requestId = msg.id as string | number;

    const handler = this.requestHandlers.get(method);
    if (!handler) {
      if (this.rawServerRequestFallback) {
        try {
          await this.sendResponse(
            requestId,
            (await this.rawServerRequestFallback(method, requestId, params)) ?? {},
          );
        } catch {
          await this.sendErrorResponse(requestId, -32603, "Internal error");
        }
        return;
      }
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
