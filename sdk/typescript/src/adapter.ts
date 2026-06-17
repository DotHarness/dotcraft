/**
 * ChannelAdapter: high-level base class for external channel adapters.
 */

import { DotCraftWireClient } from "./client.js";
import { DotCraftError } from "./errors.js";
import {
  ERR_THREAD_NOT_ACTIVE,
  ERR_TURN_IN_PROGRESS,
  JsonRpcMessage,
  Thread,
  Turn,
  textPart,
} from "./models.js";
import { Transport } from "./transport.js";
import {
  ApprovalDispatcher,
  ChannelMessageQueue,
  ChannelToolDispatcher,
  CommandRouter,
  DeliveryDispatcher,
  ModuleLifecycleState,
  ThreadResolver,
  TurnStreamReducer,
  UserInputDispatcher,
  buildChannelSender,
  type ChannelAdapterMessageOptions,
  type ThreadResolveEvent,
} from "./channelRuntime.js";
import type { LifecycleStatus, ModuleError } from "./lifecycle.js";

/** Queued inbound message; skipCommand skips slash handling for expanded prompts. */
export type ChannelAdapterMessageOpts = ChannelAdapterMessageOptions;

/** Optional construction-time settings for {@link ChannelAdapter}. */
export type ChannelAdapterOptions = {
  /** When `true`, enables stderr trace logs for `consumeTurnEventStream`. Omitted or `false` disables. */
  debugStream?: boolean;
};

export abstract class ChannelAdapter {
  protected client: DotCraftWireClient;
  protected readonly channelName: string;
  private readonly clientName: string;
  private readonly clientVersion: string;
  private readonly optOutNotifications: string[];
  /** See {@link ChannelAdapterOptions.debugStream}. */
  private readonly adapterStreamDebug: boolean | undefined;

  protected readonly threadMap = new Map<string, string>();
  protected readonly threadResolver: ThreadResolver;
  private readonly messageQueue: ChannelMessageQueue;
  private readonly commandRouter: CommandRouter;
  private readonly streamReducer: TurnStreamReducer;
  private readonly approvalDispatcher: ApprovalDispatcher;
  private readonly userInputDispatcher: UserInputDispatcher;
  private readonly deliveryDispatcher: DeliveryDispatcher;
  private readonly channelToolDispatcher: ChannelToolDispatcher;
  private readonly lifecycle = new ModuleLifecycleState();
  private running = false;

  /** Default workspace path; override per instance or in handleMessage. */
  protected defaultWorkspacePath = "";

  constructor(
    transport: Transport,
    channelName: string,
    clientName: string,
    clientVersion: string,
    optOutNotifications: string[] = [],
    options?: ChannelAdapterOptions,
  ) {
    this.client = new DotCraftWireClient(transport);
    this.channelName = channelName;
    this.clientName = clientName;
    this.clientVersion = clientVersion;
    this.optOutNotifications = optOutNotifications;
    this.adapterStreamDebug = options?.debugStream;
    this.threadResolver = new ThreadResolver({
      client: () => this.client,
      channelName: this.channelName,
      threadMap: this.threadMap,
      onEvent: (event) => this.onThreadResolveEvent(event),
    });
    this.messageQueue = new ChannelMessageQueue({
      isRunning: () => this.running,
      onError: (identityKey, error) => {
        console.error(`Error processing message for ${identityKey}:`, error);
      },
    });
    this.commandRouter = new CommandRouter({
      client: () => this.client,
      threadResolver: this.threadResolver,
      identityKey: (userId, channelContext) => this.identityKey(userId, channelContext),
      getDefaultWorkspacePath: () => this.defaultWorkspacePath,
      deliver: (target, content, metadata) => this.onDeliver(target, content, metadata),
      enqueueMessage: (opts) => this.enqueueMessage(opts),
      onThreadContextBound: (threadId, channelContext) => this.onThreadContextBound(threadId, channelContext),
      onThreadsArchived: (identityKey, archivedThreadIds) => this.onThreadsArchived(identityKey, archivedThreadIds),
    });
    this.streamReducer = new TurnStreamReducer({
      debug: (message, data) => this.debugAdapterStreamLog(message, data),
    });
    this.approvalDispatcher = new ApprovalDispatcher({
      client: () => this.client,
      onApprovalRequest: (params) => this.onApprovalRequest(params),
    });
    this.userInputDispatcher = new UserInputDispatcher({
      client: () => this.client,
      onUserInputRequest: (params) => this.onUserInputRequest(params),
    });
    this.deliveryDispatcher = new DeliveryDispatcher({
      client: () => this.client,
      onDeliver: (target, content, metadata) => this.onDeliver(target, content, metadata),
      onSend: (target, message, metadata) => this.onSend(target, message, metadata),
    });
    this.channelToolDispatcher = new ChannelToolDispatcher({
      client: () => this.client,
      onToolCall: (params) => this.onToolCall(params),
    });
  }

  /** Stream debug is enabled only when `ChannelAdapterOptions.debugStream` is `true`. */
  protected isAdapterStreamDebugEnabled(): boolean {
    return this.adapterStreamDebug === true;
  }

  protected debugAdapterStreamLog(
    message: string,
    data: Record<string, unknown> | (() => Record<string, unknown>),
  ): void {
    if (!this.isAdapterStreamDebugEnabled()) return;
    const payload = typeof data === "function" ? data() : data;
    console.error(`[dotcraft-sdk:adapter-stream] ${message}`, payload);
  }

  abstract onDeliver(target: string, content: string, metadata: Record<string, unknown>): Promise<boolean>;

  abstract onApprovalRequest(request: Record<string, unknown>): Promise<string>;

  protected async onUserInputRequest(
    _request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    return { answers: {} };
  }

  protected async onSend(
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      const ok = await this.onDeliver(target, String(message.text ?? ""), metadata);
      return { delivered: ok };
    }

    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `Adapter does not implement structured '${kind}' delivery.`,
    };
  }

  protected getDeliveryCapabilities(): Record<string, unknown> | null {
    return null;
  }

  protected getChannelTools(): Record<string, unknown>[] | null {
    return null;
  }

  protected async onToolCall(
    _request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    return {
      success: false,
      errorCode: "UnsupportedTool",
      errorMessage: "Adapter does not implement channel tool calls.",
    };
  }

  /**
   * @param segmentsWereDelivered When true, progressive segments were already shown (e.g. tool
   *   boundaries); default implementation skips sending the full reply again to avoid duplicates.
   */
  protected async onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (segmentsWereDelivered) return;
    if (replyText) await this.onDeliver(channelContext, replyText, {});
  }

  protected async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    console.error(`Turn ${turnId} failed on thread ${threadId}: ${error}`);
  }

  protected async onTurnCancelled(threadId: string, turnId: string): Promise<void> {
    console.info(`Turn ${turnId} cancelled on thread ${threadId}`);
  }

  protected async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    _segmentText: string,
    _isFinal: boolean,
    _channelContext: string,
  ): Promise<boolean | void> {
    // Default no-op; adapters can override for progressive delivery.
  }

  /** Called after the thread is resolved for an inbound message (e.g. map threadId → chat target). */
  protected onThreadContextBound(_threadId: string, _channelContext: string): void {}

  protected onThreadResolveEvent(_event: ThreadResolveEvent): void {}

  getStatus(): LifecycleStatus {
    return this.lifecycle.getStatus();
  }

  getError(): ModuleError | undefined {
    return this.lifecycle.getError();
  }

  onStatusChange(handler: (status: LifecycleStatus, error?: ModuleError) => void): void {
    this.lifecycle.onStatusChange(handler);
  }

  protected setStatus(status: LifecycleStatus, error?: ModuleError): void {
    this.lifecycle.setStatus(status, error);
  }

  async start(): Promise<void> {
    this.setStatus("starting");
    await this.client.connect();
    await this.client.start();
    this.approvalDispatcher.register();
    this.userInputDispatcher.register();
    this.deliveryDispatcher.register();
    this.channelToolDispatcher.register();
    await this.client.initialize({
      clientName: this.clientName,
      clientVersion: this.clientVersion,
      approvalSupport: true,
      requestUserInputSupport: true,
      streamingSupport: true,
      optOutNotifications: this.optOutNotifications,
      channelName: this.channelName,
      deliverySupport: true,
      deliveryCapabilities: this.getDeliveryCapabilities(),
      channelTools: this.getChannelTools(),
    });
    this.running = true;
    this.setStatus("ready");

    console.info(`ChannelAdapter '${this.channelName}' started (client: ${this.clientName} ${this.clientVersion})`);
  }

  async stop(): Promise<void> {
    this.running = false;
    await this.client.stop();
    this.setStatus("stopped", this.lifecycle.getError());
    console.info(`ChannelAdapter '${this.channelName}' stopped`);
  }

  async handleMessage(opts: ChannelAdapterMessageOpts): Promise<void> {
    const result = await this.commandRouter.routeBeforeQueue(opts);
    if (result === "enqueue") this.enqueueMessage(opts);
  }

  /**
   * Schedule a message for serial processing (one turn at a time per identity).
   * Does not wait for the turn to complete (matches Python asyncio.Queue.put).
   */
  protected enqueueMessage(opts: ChannelAdapterMessageOpts): void {
    const channelContext = opts.channelContext ?? "";
    const identityKey = this.identityKey(opts.userId, channelContext);
    this.messageQueue.enqueue(identityKey, async () => {
      await this.processMessage(identityKey, opts);
    });
  }

  protected identityKey(userId: string, channelContext: string): string {
    return `${userId}:${channelContext}`;
  }

  protected async resetIdentityThreads(userId: string, channelContext = ""): Promise<string[]> {
    const identityKey = this.identityKey(userId, channelContext);
    return await this.threadResolver.resetIdentityThreads({
      identityKey,
      userId,
      channelContext,
      workspacePath: this.defaultWorkspacePath,
    });
  }

  protected onThreadsArchived(_identityKey: string, _archivedThreadIds: string[]): void {
    // Default no-op for adapters without per-thread local state.
  }

  protected async applyCommandResetResult(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
    commandName: string,
    commandResult: Record<string, unknown>,
  ): Promise<void> {
    await this.commandRouter.applyCommandResetResult({
      identityKey,
      userId,
      channelContext,
      workspacePath,
      commandName,
      commandResult,
    });
  }

  protected async recoverThreadAfterNotActive(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
    staleThreadId: string,
  ): Promise<Thread> {
    return await this.threadResolver.recoverThreadAfterNotActive({
      identityKey,
      userId,
      channelContext,
      workspacePath,
    }, staleThreadId);
  }

  protected async processMessage(
    identityKey: string,
    opts: ChannelAdapterMessageOpts,
  ): Promise<void> {
    const channelContext = opts.channelContext ?? "";
    const workspacePath = opts.workspacePath ?? this.defaultWorkspacePath;

    const thread = await this.getOrCreateThread(
      identityKey,
      opts.userId,
      channelContext,
      workspacePath,
    );
    this.onThreadContextBound(thread.id, channelContext);

    const sender = buildChannelSender(opts, channelContext);
    const commandRoute = await this.commandRouter.routeForTurn({
      identityKey,
      opts,
      threadId: thread.id,
      sender,
      workspacePath,
    });
    if (commandRoute.kind === "handled") return;

    const turnOpts = commandRoute.opts;
    const input = turnOpts.inputParts?.length ? turnOpts.inputParts : [textPart(turnOpts.text)];

    const eventStream = this.client.streamEvents(thread.id);
    let turn: Turn;
    try {
      turn = await this.client.turnStart(thread.id, input, sender);
    } catch (e) {
      await eventStream.return?.();
      if (e instanceof DotCraftError && e.rpcCode === ERR_TURN_IN_PROGRESS) {
        await new Promise((r) => setTimeout(r, 1000));
        this.enqueueMessage(turnOpts);
        return;
      }
      if (e instanceof DotCraftError && e.rpcCode === ERR_THREAD_NOT_ACTIVE) {
        const recovered = await this.recoverThreadAfterNotActive(
          identityKey,
          turnOpts.userId,
          channelContext,
          workspacePath,
          thread.id,
        );
        this.onThreadContextBound(recovered.id, channelContext);
        const stream2 = this.client.streamEvents(recovered.id);
        try {
          turn = await this.client.turnStart(recovered.id, input, sender);
        } catch (err) {
          await stream2.return?.();
          throw err;
        }
        await this.consumeTurnEventStream(stream2, recovered.id, turn.id, channelContext);
        return;
      }
      throw e;
    }

    await this.consumeTurnEventStream(eventStream, thread.id, turn.id, channelContext);
  }

  /**
   * Runs the streaming loop for an already-started turn. Separated so callers can subscribe
   * to events before {@link DotCraftWireClient.turnStart}.
   */
  protected async consumeTurnEventStream(
    eventStream: AsyncIterableIterator<JsonRpcMessage>,
    threadId: string,
    turnId: string,
    channelContext: string,
  ): Promise<void> {
    await this.streamReducer.consume(
      eventStream,
      { threadId, turnId, channelContext },
      {
        onSegmentCompleted: (...args) => this.onSegmentCompleted(...args),
        onTurnCompleted: (...args) => this.onTurnCompleted(...args),
        onTurnFailed: (...args) => this.onTurnFailed(...args),
        onTurnCancelled: (...args) => this.onTurnCancelled(...args),
      },
    );
  }

  protected async getOrCreateThread(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
  ): Promise<Thread> {
    return await this.threadResolver.getOrCreateThread({
      identityKey,
      userId,
      channelContext,
      workspacePath,
    });
  }

  async newThread(userId: string, channelContext = ""): Promise<void> {
    await this.resetIdentityThreads(userId, channelContext);
  }
}
