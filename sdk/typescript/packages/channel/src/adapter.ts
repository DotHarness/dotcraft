/**
 * ChannelAdapter: high-level base class for external channel adapters.
 */

import { ChannelAppServerClient } from "./channelAppServerClient.js";
import {
  ERR_THREAD_NOT_ACTIVE,
  ERR_TURN_IN_PROGRESS,
  JsonRpcMessage,
  JsonRpcError,
  type Transport,
} from "@dotcraft/sdk/wire";
import { textPart } from "@dotcraft/sdk";
import type {
  ChannelChatDescriptor,
  ChannelDeliveryCapabilities,
  ChannelToolDescriptor,
} from "./capability.js";
import type {
  AppBinding,
  CommandChoice,
  CommandInfo,
  InputPart,
  RuntimeAdditionalContextEntry,
  SenderContext,
  SessionThread,
  SessionTurn,
  ChannelTarget,
  ThreadRuntimeState,
} from "@dotcraft/sdk/contracts";
import {
  ApprovalDispatcher,
  ChannelMessageQueue,
  ChannelToolDispatcher,
  ModuleLifecycleState,
  ThreadResolver,
  UserInputDispatcher,
  buildChannelSender,
  type ChannelAdapterMessageOptions,
  type ThreadResolveEvent,
} from "./channelRuntime.js";
import { CommandRouter, type CommandChoiceContext, type CommandRunRequest } from "./commandRouter.js";
import { DeliveryDispatcher } from "./deliveryDispatcher.js";
import type { LifecycleStatus, ModuleError } from "./lifecycle.js";
import {
  consumeTurnStream,
  type ChannelTurn,
  type ChannelTurnOutcome,
  type TurnItemActivity,
} from "./turnStream.js";

export type ChannelAdapterMessageOpts = ChannelAdapterMessageOptions;

const CHANNEL_REQUEST_START_FAILED_CODE = "channelRequestStartFailed";
const CHANNEL_REQUEST_START_FAILED_MESSAGE =
  "DotCraft couldn't start this request. Please try again or check the channel logs.";
const CHANNEL_TURN_FAILED_CODE = "channelTurnFailed";
const CHANNEL_TURN_FAILED_MESSAGE = "DotCraft couldn't finish this request. Please try again.";

export abstract class ChannelAdapter {
  protected client: ChannelAppServerClient;
  protected readonly channelName: string;
  private readonly clientName: string;
  private readonly clientVersion: string;
  private readonly optOutNotifications: string[];
  private clientStateUnsubscribe: (() => void) | undefined;

  protected readonly threadMap = new Map<string, string>();
  protected readonly threadResolver: ThreadResolver;
  private readonly messageQueue: ChannelMessageQueue;
  private readonly commandRouter: CommandRouter;
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
  ) {
    this.client = new ChannelAppServerClient(transport, { autoReconnect: true });
    this.channelName = channelName;
    this.clientName = clientName;
    this.clientVersion = clientVersion;
    this.optOutNotifications = optOutNotifications;
    this.threadResolver = new ThreadResolver({
      client: () => this.client,
      channelName: this.channelName,
      threadMap: this.threadMap,
      getRuntimeAdditionalContext: () => this.getRuntimeAdditionalContext(),
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
      deliver: (target, content, metadata) => this.onDeliver(target, content, metadata),
      enqueueMessage: (opts) => this.enqueueMessage(opts),
      switchThread: (thread, request) => this.switchCurrentThread(thread, request),
      renderChoices: (target, choices, context) => this.renderCommandChoices(target, choices, context),
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
      onSend: (target, message, metadata) => this.onSend(target, message, metadata),
    });
    this.channelToolDispatcher = new ChannelToolDispatcher({
      client: () => this.client,
      onToolCall: (params) => this.onToolCall(params),
    });
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

  protected abstract getDisplayName(): string;

  protected abstract getDeliveryCapabilities(): ChannelDeliveryCapabilities;

  protected abstract getChatDescriptor(
    opts: ChannelAdapterMessageOpts,
    channelContext: string,
  ): ChannelChatDescriptor;

  protected getChannelTools(): ChannelToolDescriptor[] | null {
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

  protected async onTurnStarted(_turn: ChannelTurn): Promise<void> {}

  protected async onActivity(_turn: ChannelTurn, _activity: TurnItemActivity): Promise<void> {}

  protected async onTurnRetrying(_turn: ChannelTurn): Promise<void> {}

  protected async onTurnEnded(turn: ChannelTurn, outcome: ChannelTurnOutcome): Promise<void> {
    if (outcome.status !== "failed") {
      console.info(`Turn ${turn.turnId} ${outcome.status} on thread ${turn.threadId}`);
      return;
    }
    console.error(`Turn ${turn.turnId} failed on thread ${turn.threadId}: ${outcome.error}`);
    await this.sendGenericNotice(turn.channelContext, CHANNEL_TURN_FAILED_MESSAGE, CHANNEL_TURN_FAILED_CODE);
  }

  protected onThreadContextBound(_threadId: string, _channelContext: string): void {}

  protected onCurrentThreadChanged(
    _previousThreadId: string | undefined,
    _threadId: string,
    _channelContext: string,
  ): void {}

  protected async renderCommandChoices(
    target: string,
    choices: CommandChoice[],
    _context: CommandChoiceContext,
  ): Promise<void> {
    const lines = choices.map((choice, index) => `${index + 1}. ${choice.label || choice.command}: ${choice.command}`);
    await this.onDeliver(target, lines.join("\n"), {});
  }

  /** Returns compact application context bound to Threads owned by this adapter. */
  protected getRuntimeAdditionalContext(): Record<string, RuntimeAdditionalContextEntry> | undefined {
    return undefined;
  }

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
    this.threadResolver.invalidateConnectionBoundRuntime();
    this.clientStateUnsubscribe?.();
    this.clientStateUnsubscribe = this.client.onStateChanged((state) => {
      if (state !== "reconnecting") return;
      this.threadResolver.invalidateConnectionBoundRuntime();
      this.commandRouter.invalidate();
    });
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
      streamingSupport: false,
      optOutNotifications: this.optOutNotifications,
      channelAdapter: {
        channelName: this.channelName,
        displayName: this.getDisplayName(),
        deliveryCapabilities: this.getDeliveryCapabilities(),
        channelTools: (this.getChannelTools() ?? []).map((tool) => ({
          ...tool,
          requiresChatContext: tool.requiresChatContext ?? false,
        })),
      },
    });
    this.running = true;
    this.commandRouter.invalidate();
    void this.commandRouter.refresh();
    this.setStatus("ready");

    console.info(`ChannelAdapter '${this.channelName}' started (client: ${this.clientName} ${this.clientVersion})`);
  }

  async stop(): Promise<void> {
    this.running = false;
    this.clientStateUnsubscribe?.();
    this.clientStateUnsubscribe = undefined;
    await this.client.stop();
    this.setStatus("stopped", this.lifecycle.getError());
    console.info(`ChannelAdapter '${this.channelName}' stopped`);
  }

  async handleMessage(opts: ChannelAdapterMessageOpts): Promise<void> {
    const channelContext = opts.channelContext ?? "";
    const bindCode = this.parseChannelBindCode(opts.text);
    if (bindCode) {
      const sender = buildChannelSender(opts, channelContext);
      if (await this.tryAcceptChannelBinding(opts, sender, channelContext, bindCode)) return;
    }

    if (!opts.skipCommand && await this.commandRouter.isCommand(opts.text)) {
      await this.runCommand(opts);
      return;
    }
    this.enqueueMessage(opts);
  }

  protected async isCommand(text: string): Promise<boolean> {
    return await this.commandRouter.isCommand(text);
  }

  protected async listCommands(): Promise<CommandInfo[]> {
    return await this.commandRouter.listCommands();
  }

  protected async executeCommandChoice(
    command: string,
    opts: Omit<ChannelAdapterMessageOpts, "text" | "inputParts" | "skipCommand">,
  ): Promise<void> {
    await this.runCommand({ ...opts, text: command });
  }

  private async runCommand(opts: ChannelAdapterMessageOpts): Promise<void> {
    const channelContext = opts.channelContext ?? "";
    const sender = buildChannelSender(opts, channelContext);
    try {
      const { threadId } = await this.resolveConversationThread(opts, sender);
      await this.commandRouter.run({ opts, threadId, sender });
    } catch (error) {
      console.error(`[${this.channelName}] command failed:`, error);
      await this.sendGenericNotice(channelContext, CHANNEL_REQUEST_START_FAILED_MESSAGE, CHANNEL_REQUEST_START_FAILED_CODE);
    }
  }

  private async switchCurrentThread(thread: SessionThread, request: CommandRunRequest): Promise<void> {
    const channelContext = request.opts.channelContext ?? "";
    const identityKey = this.identityKey(request.opts.userId, channelContext);
    const previousThreadId = this.threadResolver.getCachedThreadId(identityKey);
    await this.client.threadResume(thread.id, {
      additionalContext: this.getRuntimeAdditionalContext(),
      chat: this.getChatDescriptor(request.opts, channelContext),
    });
    this.threadResolver.setCachedThread(identityKey, thread.id);
    this.onThreadContextBound(thread.id, channelContext);
    if (previousThreadId !== thread.id) this.onCurrentThreadChanged(previousThreadId, thread.id, channelContext);
  }

  protected getChannelBindingAppId(): string {
    return `com.dotharness.channel.${this.channelName}`;
  }

  protected parseChannelBindCode(text: string): string | null {
    const match = /^\/bind\s+([1-9][0-9]{5})\s*$/i.exec(text.trim());
    return match?.[1] ?? null;
  }

  protected buildChannelTarget(
    _opts: ChannelAdapterMessageOpts,
    _sender: Record<string, unknown>,
    _channelContext: string,
  ): ChannelTarget | null {
    return null;
  }

  protected async onChannelBindingAccepted(
    binding: AppBinding,
    target: ChannelTarget,
    _opts: ChannelAdapterMessageOpts,
  ): Promise<void> {
    if (!target.deliveryTarget || !binding.threadId) {
      throw new Error("Accepted channel binding omitted its delivery target or thread id.");
    }
    await this.onDeliver(
      target.deliveryTarget,
      `Bound this conversation to thread ${binding.threadId}.`,
      {
        appId: binding.appId,
        bindingId: binding.bindingId,
        authorityRevision: binding.authorityRevision,
      },
    );
  }

  protected async onChannelBindingFailed(
    error: unknown,
    target: ChannelTarget,
    _opts: ChannelAdapterMessageOpts,
  ): Promise<void> {
    const message = error instanceof Error ? error.message : String(error);
    if (!target.deliveryTarget) throw new Error("Channel binding target omitted its delivery target.");
    await this.onDeliver(target.deliveryTarget, `Binding failed: ${message}`, {
      error: message,
    });
  }

  private async tryAcceptChannelBinding(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
    bindCode: string,
  ): Promise<boolean> {
    const target = this.buildChannelTarget(opts, sender, channelContext);
    if (!target) return false;

    try {
      await this.client.request(
        "app/channelBinding/request/get",
        { code: bindCode },
      );
      const binding = await this.client.request(
        "app/channelBinding/accept",
        { code: bindCode, target },
      );
      if (!binding.threadId) throw new Error("Accepted channel binding omitted its thread id.");
      const identityKey = this.identityKey(opts.userId, channelContext);
      this.threadResolver.setCachedThread(identityKey, binding.threadId);
      this.onThreadContextBound(binding.threadId, channelContext);
      await this.onChannelBindingAccepted(binding, target, opts);
    } catch (error) {
      await this.onChannelBindingFailed(error, target, opts);
    }

    return true;
  }

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

  protected async recoverThreadAfterNotActive(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
    staleThreadId: string,
    chat?: ChannelChatDescriptor,
  ): Promise<SessionThread> {
    return await this.threadResolver.recoverThreadAfterNotActive({
      identityKey,
      userId,
      channelContext,
      workspacePath,
      chat,
    }, staleThreadId);
  }

  protected async processMessage(
    identityKey: string,
    opts: ChannelAdapterMessageOpts,
  ): Promise<void> {
    const channelContext = opts.channelContext ?? "";
    const workspacePath = opts.workspacePath ?? this.defaultWorkspacePath;
    const sender = buildChannelSender(opts, channelContext);
    let accepted = false;

    try {
      const { thread, threadId, binding, chat } = await this.resolveConversationThread(opts, sender);
      const input = opts.inputParts?.length ? opts.inputParts : [textPart(opts.text)];

      if (binding) {
        await this.enqueueChannelBoundInput(binding, input, opts, sender);
        return;
      }

      await this.submitInput(thread!, input, sender, channelContext, async () => {
        const recovered = await this.recoverThreadAfterNotActive(
          identityKey,
          opts.userId,
          channelContext,
          workspacePath,
          threadId,
          chat,
        );
        this.onThreadContextBound(recovered.id, channelContext);
        return recovered;
      });
      accepted = true;
    } catch (error) {
      if (!accepted) await this.sendGenericNotice(
        channelContext,
        CHANNEL_REQUEST_START_FAILED_MESSAGE,
        CHANNEL_REQUEST_START_FAILED_CODE,
      );
      throw error;
    }
  }

  private async resolveConversationThread(
    opts: ChannelAdapterMessageOpts,
    sender: SenderContext,
  ): Promise<{ thread: SessionThread | null; threadId: string; binding: AppBinding | null; chat: ChannelChatDescriptor }> {
    const channelContext = opts.channelContext ?? "";
    const identityKey = this.identityKey(opts.userId, channelContext);
    const channelTarget = this.buildChannelTarget(opts, sender, channelContext);
    const binding = channelTarget ? await this.resolveChannelBindingForMessage(channelTarget) : null;
    const chat = this.getChatDescriptor(opts, channelContext);
    let thread: SessionThread | null = null;
    let threadId: string;
    if (binding) {
      if (!binding.threadId) throw new Error("Resolved channel binding omitted its thread id.");
      threadId = binding.threadId;
      this.threadMap.set(identityKey, threadId);
    } else {
      thread = await this.getOrCreateThread(
        identityKey,
        opts.userId,
        channelContext,
        opts.workspacePath ?? this.defaultWorkspacePath,
        chat,
      );
      threadId = thread.id;
    }
    this.onThreadContextBound(threadId, channelContext);
    return { thread, threadId, binding, chat };
  }

  private async submitInput(
    thread: SessionThread,
    input: InputPart[],
    sender: SenderContext,
    channelContext: string,
    recover?: () => Promise<SessionThread>,
  ): Promise<void> {
    if (thread.runtime?.activeTurnId || thread.runtime?.maintenanceKind) {
      if (await this.addToActiveTurn(thread.id, thread.runtime, input, sender)) return;
      const latest = await this.client.threadRead(thread.id);
      if (latest.runtime?.activeTurnId || latest.runtime?.maintenanceKind) {
        await this.client.turnEnqueue(thread.id, input, sender);
        return;
      }
      thread = latest;
    }

    const eventStream = this.client.streamEvents(thread.id);
    let turn: SessionTurn;
    try {
      turn = await this.client.turnStart(thread.id, input, sender);
    } catch (error) {
      await eventStream.return?.();
      if (error instanceof JsonRpcError && error.rpcCode === ERR_TURN_IN_PROGRESS) {
        const latest = await this.client.threadRead(thread.id);
        if (!(await this.addToActiveTurn(thread.id, latest.runtime, input, sender)))
          await this.client.turnEnqueue(thread.id, input, sender);
        return;
      }
      if (recover && error instanceof JsonRpcError && error.rpcCode === ERR_THREAD_NOT_ACTIVE) {
        await this.submitInput(await recover(), input, sender, channelContext);
        return;
      }
      throw error;
    }

    void this.runTurn(eventStream, { threadId: thread.id, turnId: turn.id, channelContext });
  }

  private async addToActiveTurn(
    threadId: string,
    runtime: ThreadRuntimeState | undefined,
    input: InputPart[],
    sender: SenderContext,
  ): Promise<boolean> {
    if (runtime?.maintenanceKind) {
      await this.client.turnEnqueue(threadId, input, sender);
      return true;
    }
    const activeTurnId = runtime?.activeTurnId;
    if (!activeTurnId) return false;
    try {
      await this.client.turnSteer(threadId, activeTurnId, input, sender);
      return true;
    } catch (error) {
      if (!(error instanceof JsonRpcError)) throw error;
      return false;
    }
  }

  private async runTurn(eventStream: AsyncIterableIterator<JsonRpcMessage>, turn: ChannelTurn): Promise<void> {
    const reportHookError = (hook: string, error: unknown): void => {
      console.error(`[${this.channelName}] ${hook} failed for turn ${turn.turnId}:`, error);
    };
    let outcome: ChannelTurnOutcome = { status: "disconnected" };
    try {
      await this.onTurnStarted(turn).catch((error) => reportHookError("onTurnStarted", error));
      outcome = await consumeTurnStream(eventStream, turn, {
        onActivity: (current, activity) => this.onActivity(current, activity),
        onTurnRetrying: (current) => this.onTurnRetrying(current),
        onHandlerError: reportHookError,
      });
    } catch (error) {
      console.error(`[${this.channelName}] event stream for turn ${turn.turnId} failed:`, error);
    } finally {
      await eventStream.return?.();
    }
    await this.onTurnEnded(turn, outcome).catch((error) => reportHookError("onTurnEnded", error));
  }

  private async sendGenericNotice(channelContext: string, message: string, failureCode: string): Promise<void> {
    if (!channelContext) {
      console.error(`[${this.channelName}] cannot deliver ${failureCode}: channel context is empty`);
      return;
    }

    try {
      const delivered = await this.onDeliver(channelContext, message, { failureCode });
      if (!delivered) {
        console.error(`[${this.channelName}] ${failureCode} notice was not delivered`);
      }
    } catch (deliveryError) {
      console.error(`[${this.channelName}] failed to deliver ${failureCode} notice:`, deliveryError);
    }
  }

  private async enqueueChannelBoundInput(
    binding: AppBinding,
    input: InputPart[],
    opts: ChannelAdapterMessageOpts,
    sender: SenderContext,
  ): Promise<void> {
    await this.client.request("app/threadInput/enqueue", {
      bindingId: binding.bindingId,
      input,
      displayText: opts.text,
      triggerLabel: `${this.channelName} message`,
      triggerRefId: this.channelBindingTriggerRef(binding),
      startPolicy: "runWhenIdle",
      sender,
    });
  }

  private async resolveChannelBindingForMessage(
    target: ChannelTarget,
  ): Promise<AppBinding | null> {
    try {
      const result = await this.client.request(
        "app/channelBinding/resolve",
        {
          channelName: target.channelName,
          accountId: target.accountId,
          conversationKind: target.conversationKind,
          conversationId: target.conversationId,
        },
      );
      const binding = result.binding ?? null;
      return binding?.state === "active" ? binding : null;
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.warn(
        `[${this.channelName}] channel binding resolve failed; falling back to normal channel routing: ${message}`,
      );
      return null;
    }
  }

  private channelBindingTriggerRef(binding: AppBinding): string | undefined {
    const target = binding.channelTarget;
    if (!target) return undefined;
    return [
      target.channelName,
      target.accountId ?? "",
      target.conversationKind,
      target.conversationId,
    ].join(":");
  }

  protected async getOrCreateThread(
    identityKey: string,
    userId: string,
    channelContext: string,
    workspacePath: string,
    chat?: ChannelChatDescriptor,
  ): Promise<SessionThread> {
    return await this.threadResolver.getOrCreateThread({
      identityKey,
      userId,
      channelContext,
      workspacePath,
      chat,
    });
  }
}
