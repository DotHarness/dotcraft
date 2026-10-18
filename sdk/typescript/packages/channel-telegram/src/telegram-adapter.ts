import { HttpsProxyAgent } from "https-proxy-agent";
import {
  Bot,
  GrammyError,
  InlineKeyboard,
  type Context,
} from "grammy";
import {
  WebSocketTransport,
  type Transport,
} from "@dotcraft/channel/runtime";
import {
  type ChannelTarget,
} from "@dotcraft/channel";
import {
  ConfigValidationError,
  ModuleChannelAdapter,
  parseApprovalReplyDecision,
  buildUserInputPrompt,
  canUseNativeSingleChoiceUserInput,
  emptyUserInputResponse,
  hasUserInputAnswer,
  mergeUserInputResponses,
  normalizeUserInputQuestions,
  splitUserInputRequestByQuestion,
  userInputResponseForSingleChoice,
  userInputResponseFromText,
  type ModuleError,
  type ChannelChatDescriptor,
  type ChannelDeliveryCapabilities,
  type ChannelAdapterMessageOpts,
  type ChannelTurn,
  type ChannelTurnOutcome,
  type CommandChoice,
  type CommandChoiceContext,
  type UserInputResponse,
  type WorkspaceContext,
} from "@dotcraft/channel";

import {
  TELEGRAM_DELIVERY_CAPABILITIES,
  describeTelegramError,
  parseTargetChatId,
  sendTelegramMessage,
  sendTelegramText,
} from "./telegram-delivery.js";
import type { TelegramApiLike } from "./telegram-media.js";
import type { TelegramConfig } from "./telegram-config.js";
import { TelegramApprovals, escapeHtml } from "./telegram-approvals.js";
import { TelegramCommandChoices, buildTelegramCommandMenus, stripBotMention } from "./telegram-commands.js";

type TelegramUser = { id: number; first_name?: string; username?: string };

type PendingUserInput = {
  request: Record<string, unknown>;
  chatTarget: string;
  promptTitle?: string;
  resolve: (response: UserInputResponse) => void;
};

export function validateTelegramConfig(rawConfig: unknown): asserts rawConfig is TelegramConfig {
  const fields: string[] = [];
  if (!rawConfig || typeof rawConfig !== "object") {
    throw new ConfigValidationError("Telegram config must be an object.", ["config"]);
  }

  const config = rawConfig as Record<string, unknown>;
  const dotcraft = asRecord(config.dotcraft);
  const telegram = asRecord(config.telegram);

  const wsUrl = String(dotcraft.wsUrl ?? "").trim();
  const botToken = String(telegram.botToken ?? "").trim();

  if (!wsUrl) {
    fields.push("dotcraft.wsUrl");
  } else if (!/^wss?:\/\//i.test(wsUrl)) {
    throw new ConfigValidationError("dotcraft.wsUrl must use ws:// or wss://.", ["dotcraft.wsUrl"]);
  }

  if (!botToken) {
    fields.push("telegram.botToken");
  }

  if (telegram.adminUsers !== undefined
      && (!Array.isArray(telegram.adminUsers)
        || telegram.adminUsers.some((id) => !/^\d+$/.test(String(id).trim())))) {
    throw new ConfigValidationError("telegram.adminUsers must be a list of Telegram user IDs.", ["telegram.adminUsers"]);
  }

  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export function isTelegramConflictError(error: unknown): boolean {
  if (error instanceof GrammyError && (error as unknown as { error_code?: number }).error_code === 409) {
    return true;
  }
  const payloadCode =
    typeof error === "object" && error !== null && "error_code" in error
      ? Number((error as { error_code?: unknown }).error_code)
      : NaN;
  if (payloadCode === 409) {
    return true;
  }

  const message = error instanceof Error ? error.message : String(error);
  return message.includes("409") && message.toLowerCase().includes("conflict");
}

export class TelegramAdapter extends ModuleChannelAdapter<TelegramConfig> {
  private bot: Bot | null = null;
  private readonly threadContextMap = new Map<string, string>();
  private readonly chatTitles = new Map<string, string>();
  private readonly typingTurns = new Map<string, Set<string>>();
  private readonly typingAbortControllers = new Map<string, AbortController>();
  private readonly approvals = new TelegramApprovals(() => this.requireBot().api);
  private readonly pendingUserInputs = new Map<string, PendingUserInput>();
  private readonly pendingUserInputByChat = new Map<string, string>();
  private nextUserInputToken = 1;
  private readonly commandChoices = new TelegramCommandChoices();
  private adminUsers = new Set<string>();
  private botUsername: string | undefined;
  private pollingPromise: Promise<void> | null = null;
  private pollTimeoutSeconds = 30;

  constructor() {
    super("telegram", "dotcraft-telegram", "0.1.0", [
      "item/reasoning/delta",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ]);
  }

  protected override getConfigFileName(_context: WorkspaceContext): string {
    return "telegram.json";
  }

  protected override validateConfig(rawConfig: unknown): asserts rawConfig is TelegramConfig {
    validateTelegramConfig(rawConfig);
  }

  protected override buildTransportFromConfig(config: TelegramConfig): Transport {
    return new WebSocketTransport({
      url: config.dotcraft.wsUrl,
      token: config.dotcraft.token ?? "",
    });
  }

  protected override buildChannelTarget(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
  ): ChannelTarget | null {
    const chatId = parseTargetChatId(channelContext);
    if (chatId === null) return null;
    const platformUserId = String(sender.senderId ?? opts.userId ?? "");
    const displayName = typeof sender.senderName === "string" && sender.senderName.trim()
      ? sender.senderName.trim()
      : null;
    const conversationKind = chatId < 0 ? "group" : "user";
    return {
      channelName: "telegram",
      conversationKind,
      conversationId: String(chatId),
      deliveryTarget: channelContext || String(chatId),
      displayName: conversationKind === "group"
        ? `Telegram chat ${chatId}`
        : displayName ?? `Telegram user ${chatId}`,
      boundBy: platformUserId
        ? {
          platformUserId,
          displayName,
        }
        : null,
    };
  }

  protected override getDisplayName(): string {
    return "Telegram";
  }

  protected override getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return TELEGRAM_DELIVERY_CAPABILITIES;
  }

  protected override getChatDescriptor(opts: ChannelAdapterMessageOpts, channelContext: string): ChannelChatDescriptor {
    const chatId = parseTargetChatId(channelContext);
    if (chatId !== null && chatId < 0) {
      return { kind: "group", label: this.chatTitles.get(String(chatId)), membership: "sender" };
    }
    return { kind: "direct", label: opts.userName, membership: "shared" };
  }

  override async startWithContext(context: WorkspaceContext): Promise<void> {
    this.defaultWorkspacePath = context.workspaceRoot;
    await super.startWithContext(context);
    if (this.getStatus() !== "ready" || !this.loadedConfig) {
      return;
    }

    const config = this.loadedConfig;
    this.approvals.timeoutMs = config.telegram.approvalTimeoutMs ?? 120_000;
    this.pollTimeoutSeconds = Math.max(1, Math.ceil((config.telegram.pollTimeoutMs ?? 30_000) / 1000));
    this.adminUsers = new Set((config.telegram.adminUsers ?? []).map((id) => String(id).trim()));

    try {
      const bot = this.createBot(config);
      this.bot = bot;
      this.installBotHandlers(bot);

      const me = await bot.api.getMe();
      this.botUsername = me.username;
      console.info(`[telegram] bot @${me.username ?? me.first_name} connected`);

      await this.publishCommandMenus(bot);
      await this.waitForPollingSession(bot);

      this.pollingPromise = bot.start({
        allowed_updates: ["message", "callback_query"],
        drop_pending_updates: true,
        timeout: this.pollTimeoutSeconds,
      }).catch((error) => {
        if (this.bot !== bot) {
          return;
        }
        void this.handlePollingFailure(error);
      });
    } catch (error) {
      await this.handleStartupFailure(error);
    }
  }

  override async stop(): Promise<void> {
    this.approvals.resolveAll("cancel");
    this.resolveAllPendingUserInputs(emptyUserInputResponse());
    this.stopAllTyping();

    const bot = this.bot;
    this.bot = null;
    if (bot?.isRunning()) {
      await bot.stop();
    }

    const polling = this.pollingPromise;
    this.pollingPromise = null;
    await polling?.catch(() => undefined);
    await super.stop();
  }

  protected override onThreadContextBound(threadId: string, channelContext: string): void {
    if (channelContext) {
      this.threadContextMap.set(threadId, channelContext);
    }
  }

  protected override async renderCommandChoices(
    target: string,
    choices: CommandChoice[],
    _context: CommandChoiceContext,
  ): Promise<void> {
    const chatId = parseTargetChatId(target);
    if (chatId === null) {
      console.error(`[telegram] invalid choices target: ${target}`);
      return;
    }
    await this.requireBot().api.sendMessage(chatId, "Choose an option:", {
      reply_markup: this.commandChoices.keyboard(chatId, choices),
    });
  }

  private async publishCommandMenus(bot: Bot): Promise<void> {
    for (const menu of buildTelegramCommandMenus(await this.listCommands(), this.adminUsers)) {
      try {
        await bot.api.setMyCommands(menu.commands, { scope: menu.scope });
      } catch (error) {
        console.warn(`[telegram] setMyCommands failed for scope ${menu.scope.type}: ${describeTelegramError(error)}`);
      }
    }
  }

  override async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    const bot = this.requireBot();
    const chatId = parseTargetChatId(target);
    if (chatId === null) {
      console.error(`[telegram] invalid delivery target: ${target}`);
      return false;
    }

    await sendTelegramText(bot.api as TelegramApiLike, chatId, content);
    return true;
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    _metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    return await sendTelegramMessage((this.bot?.api ?? null) as TelegramApiLike | null, target, message);
  }

  override async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const threadId = String(request.threadId ?? "");
    const chatTarget = this.threadContextMap.get(threadId);
    const chatId = chatTarget ? parseTargetChatId(chatTarget) : null;
    if (chatId === null) {
      console.warn(`[telegram] cannot find chat for thread ${threadId}; auto-declining approval`);
      return "decline";
    }
    return await this.approvals.request(chatId, request);
  }

  protected override async onUserInputRequest(request: Record<string, unknown>): Promise<UserInputResponse> {
    const threadId = String(request.threadId ?? "");
    const chatTarget = this.threadContextMap.get(threadId);
    const chatId = chatTarget ? parseTargetChatId(chatTarget) : null;
    if (chatId === null) {
      console.warn(`[telegram] cannot find chat for thread ${threadId}; resolving user input empty`);
      return emptyUserInputResponse();
    }

    const steps = splitUserInputRequestByQuestion(request);
    if (steps.length === 0) {
      return emptyUserInputResponse();
    }

    const responses: UserInputResponse[] = [];
    for (const step of steps) {
      const response = await this.requestUserInputStep({
        request: step.request,
        chatId,
        promptTitle: this.userInputPromptTitle(step.questionIndex, step.questionCount),
      });
      if (!hasUserInputAnswer(response, step.question.id)) {
        return emptyUserInputResponse();
      }
      responses.push(response);
    }

    return mergeUserInputResponses(responses);
  }

  private async requestUserInputStep(params: {
    request: Record<string, unknown>;
    chatId: number;
    promptTitle?: string;
  }): Promise<UserInputResponse> {
    const token = `ui${this.nextUserInputToken++}`;
    const prompt = escapeHtml(buildUserInputPrompt(params.request, { title: params.promptTitle }));
    const messageOptions: Parameters<TelegramApiLike["sendMessage"]>[2] = { parse_mode: "HTML" };
    if (canUseNativeSingleChoiceUserInput(params.request)) {
      const keyboard = new InlineKeyboard();
      const question = normalizeUserInputQuestions(params.request)[0]!;
      question.options.forEach((option, index) => {
        keyboard.text(option.label.slice(0, 64), `userinput:${token}:${index}`).row();
      });
      messageOptions.reply_markup = keyboard;
    }

    await this.requireBot().api.sendMessage(params.chatId, prompt, messageOptions);

    return await new Promise<UserInputResponse>((resolve) => {
      const chatKey = String(params.chatId);
      const previousToken = this.pendingUserInputByChat.get(chatKey);
      if (previousToken) {
        this.resolvePendingUserInput(previousToken, emptyUserInputResponse());
      }
      this.pendingUserInputs.set(token, {
        request: params.request,
        chatTarget: chatKey,
        promptTitle: params.promptTitle,
        resolve,
      });
      this.pendingUserInputByChat.set(chatKey, token);
    });
  }

  private userInputPromptTitle(questionIndex: number, questionCount: number): string {
    const base = "DotCraft needs your input";
    return questionCount > 1 ? `${base} (${questionIndex + 1}/${questionCount})` : base;
  }

  protected override async onTurnStarted(turn: ChannelTurn): Promise<void> {
    const chatId = parseTargetChatId(turn.channelContext);
    if (chatId === null) return;
    const key = String(chatId);
    const turns = this.typingTurns.get(key) ?? new Set<string>();
    turns.add(turn.turnId);
    this.typingTurns.set(key, turns);
    if (turns.size === 1) this.startTyping(key);
  }

  protected override async onTurnEnded(turn: ChannelTurn, outcome: ChannelTurnOutcome): Promise<void> {
    const chatId = parseTargetChatId(turn.channelContext);
    if (chatId !== null) {
      const key = String(chatId);
      const turns = this.typingTurns.get(key);
      turns?.delete(turn.turnId);
      if (!turns?.size) {
        this.typingTurns.delete(key);
        this.stopTyping(key);
      }
    }
    await super.onTurnEnded(turn, outcome);
  }

  private createBot(config: TelegramConfig): Bot {
    const proxy = config.telegram.httpsProxy?.trim();
    const bot =
      proxy
        ? new Bot(config.telegram.botToken, {
            client: {
              baseFetchConfig: {
                agent: new HttpsProxyAgent(proxy),
                compress: true,
              },
            },
          })
        : new Bot(config.telegram.botToken);
    bot.catch((error) => {
      console.error("[telegram] polling error:", describeTelegramError(error.error));
    });
    return bot;
  }

  private installBotHandlers(bot: Bot): void {
    bot.on("message:text", async (ctx) => {
      await this.handleIncomingText(ctx);
    });

    bot.on("callback_query:data", async (ctx) => {
      await this.handleApprovalCallback(ctx);
    });
  }

  private async handleIncomingText(ctx: Context): Promise<void> {
    if (!ctx.from || !ctx.chatId || !ctx.msg || !("text" in ctx.msg)) {
      return;
    }

    const chatId = ctx.chatId;
    if (ctx.chat && "title" in ctx.chat && ctx.chat.title) {
      this.chatTitles.set(String(chatId), ctx.chat.title);
    }
    const text = stripBotMention(String(ctx.msg.text ?? ""), this.botUsername);
    const approvalDecision = parseApprovalReplyDecision(text, { slashAliasesOnly: true });
    if (approvalDecision && await this.approvals.resolveInChat(chatId, approvalDecision)) {
      return;
    }
    if (await this.tryHandlePendingUserInputText(String(chatId), text)) {
      return;
    }
    await this.handleMessage({ ...this.messageOptsFor(ctx.from, chatId), text });
  }

  private messageOptsFor(from: TelegramUser, chatId: number): Omit<ChannelAdapterMessageOpts, "text"> {
    return {
      userId: String(from.id),
      userName: from.first_name || from.username || String(from.id),
      channelContext: String(chatId),
      directTarget: String(from.id),
      senderExtra: { senderRole: this.adminUsers.has(String(from.id)) ? "admin" : "user" },
    };
  }

  private async handleApprovalCallback(ctx: Context): Promise<void> {
    if (!ctx.callbackQuery || !("data" in ctx.callbackQuery) || !ctx.callbackQuery.data) {
      return;
    }
    await ctx.answerCallbackQuery();

    const parts = String(ctx.callbackQuery.data).split(":", 3);
    if (parts.length === 3 && parts[0] === "userinput") {
      await this.handleUserInputCallback(ctx, parts[1] ?? "", Number(parts[2]));
      return;
    }
    if (parts.length === 3 && parts[0] === "cmd") {
      await this.handleCommandChoiceCallback(ctx, parts[1] ?? "", Number(parts[2]));
      return;
    }

    if (parts.length !== 3 || parts[0] !== "approval") {
      return;
    }

    const message = ctx.callbackQuery.message;
    await this.approvals.resolveFromButton(
      parts[1] ?? "",
      parts[2] ?? "cancel",
      message && "message_id" in message && "chat" in message
        ? { chatId: message.chat.id, messageId: message.message_id, text: "text" in message ? String(message.text ?? "") : "" }
        : undefined,
    );
  }

  private async handleCommandChoiceCallback(ctx: Context, token: string, index: number): Promise<void> {
    const message = ctx.callbackQuery?.message;
    if (!ctx.from || !message || !("chat" in message) || !Number.isInteger(index)) return;
    const chatId = message.chat.id;
    const choice = this.commandChoices.take(token, chatId, index);
    if (!choice) return;

    await this.requireBot().api.editMessageText(
      chatId,
      message.message_id,
      `<i>Selected: ${escapeHtml(choice.label || choice.command || "")}</i>`,
      { parse_mode: "HTML", reply_markup: undefined },
    ).catch(() => undefined);
    await this.executeCommandChoice(String(choice.command), this.messageOptsFor(ctx.from, chatId));
  }

  private async handleUserInputCallback(ctx: Context, token: string, optionIndex: number): Promise<void> {
    const pending = this.pendingUserInputs.get(token);
    if (!pending || !Number.isInteger(optionIndex)) {
      return;
    }

    const response = userInputResponseForSingleChoice(pending.request, optionIndex);
    this.resolvePendingUserInput(token, response);

    const message = ctx.callbackQuery?.message;
    if (!message || !("message_id" in message) || !("chat" in message)) {
      return;
    }

    const question = normalizeUserInputQuestions(pending.request)[0];
    const chosenLabel = question?.options[optionIndex]?.label ?? "selected";
    try {
      await this.requireBot().api.editMessageReplyMarkup(message.chat.id, message.message_id, {
        reply_markup: undefined,
      });
      const text = "text" in message ? escapeHtml(String(message.text ?? "")) : "";
      await this.requireBot().api.editMessageText(
        message.chat.id,
        message.message_id,
        `${text}\n\n<i>Answer: ${escapeHtml(chosenLabel)}</i>`,
        { parse_mode: "HTML" },
      );
    } catch {
      // Ignore edit failures for stale/deleted messages.
    }
  }

  private async tryHandlePendingUserInputText(chatTarget: string, text: string): Promise<boolean> {
    const token = this.pendingUserInputByChat.get(chatTarget);
    if (!token) return false;
    const pending = this.pendingUserInputs.get(token);
    if (!pending) {
      this.pendingUserInputByChat.delete(chatTarget);
      return false;
    }

    const response = userInputResponseFromText(pending.request, text);
    if (!response) {
      await this.requireBot().api.sendMessage(
        Number(chatTarget),
        escapeHtml(buildUserInputPrompt(pending.request, {
          title: pending.promptTitle ?? "Please answer the pending DotCraft question",
        })),
        { parse_mode: "HTML" },
      ).catch(() => undefined);
      return true;
    }

    this.resolvePendingUserInput(token, response);
    return true;
  }

  private async waitForPollingSession(bot: Bot): Promise<void> {
    await bot.api.deleteWebhook({ drop_pending_updates: true });
    const maxWaitSeconds = 65;
    const retryIntervalSeconds = 5;
    for (let attempt = 0; attempt <= Math.floor(maxWaitSeconds / retryIntervalSeconds); attempt += 1) {
      try {
        await bot.api.getUpdates({ offset: -1, timeout: 0 });
        return;
      } catch (error) {
        if (!isTelegramConflictError(error)) {
          throw error;
        }

        if (attempt * retryIntervalSeconds >= maxWaitSeconds) {
          console.error(
            `[telegram] previous polling session did not expire after ${maxWaitSeconds}s; proceeding anyway`,
          );
          return;
        }

        console.warn(
          `[telegram] another polling session is active; retrying in ${retryIntervalSeconds}s (${attempt + 1}/${Math.floor(maxWaitSeconds / retryIntervalSeconds)})`,
        );
        await sleep(retryIntervalSeconds * 1000);
      }
    }
  }

  private async handleStartupFailure(error: unknown): Promise<void> {
    this.stopAllTyping();
    this.approvals.resolveAll("cancel");
    this.bot = null;
    const message = describeTelegramError(error);
    await super.stop();
    this.setStatus("stopped", this.runtimeError("startupFailed", message));
  }

  private async handlePollingFailure(error: unknown): Promise<void> {
    this.stopAllTyping();
    this.approvals.resolveAll("cancel");
    this.bot = null;
    await super.stop();
    this.setStatus("stopped", this.runtimeError("unexpectedRuntimeFailure", describeTelegramError(error)));
  }

  private runtimeError(code: ModuleError["code"], message: string): ModuleError {
    return {
      code,
      message,
      timestamp: new Date().toISOString(),
    };
  }

  private startTyping(chatId: string): void {
    this.stopTyping(chatId);
    const controller = new AbortController();
    this.typingAbortControllers.set(chatId, controller);
    void this.typingLoop(chatId, controller.signal).finally(() => {
      if (this.typingAbortControllers.get(chatId) === controller) {
        this.typingAbortControllers.delete(chatId);
      }
    });
  }

  private stopTyping(chatId: string): void {
    const controller = this.typingAbortControllers.get(chatId);
    controller?.abort();
  }

  private stopAllTyping(): void {
    for (const controller of this.typingAbortControllers.values()) {
      controller.abort();
    }
    this.typingAbortControllers.clear();
    this.typingTurns.clear();
  }

  private async typingLoop(chatId: string, signal: AbortSignal): Promise<void> {
    try {
      while (!signal.aborted && this.bot) {
        const targetChatId = parseTargetChatId(chatId);
        if (targetChatId === null) {
          return;
        }
        await this.bot.api.sendChatAction(targetChatId, "typing");
        await sleep(4000, signal);
      }
    } catch (error) {
      if (!signal.aborted) {
        console.debug(`[telegram] typing indicator error for ${chatId}: ${describeTelegramError(error)}`);
      }
    }
  }

  private resolvePendingUserInput(token: string, response: UserInputResponse): void {
    const pending = this.pendingUserInputs.get(token);
    if (!pending) return;
    this.pendingUserInputs.delete(token);
    if (this.pendingUserInputByChat.get(pending.chatTarget) === token) {
      this.pendingUserInputByChat.delete(pending.chatTarget);
    }
    pending.resolve(response);
  }

  private resolveAllPendingUserInputs(response: UserInputResponse): void {
    for (const token of [...this.pendingUserInputs.keys()]) {
      this.resolvePendingUserInput(token, response);
    }
  }

  private requireBot(): Bot {
    if (!this.bot) {
      throw new Error("Telegram bot is not initialized.");
    }
    return this.bot;
  }
}

function asRecord(value: unknown): Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

function sleep(ms: number, signal?: AbortSignal): Promise<void> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      cleanup();
      resolve();
    }, ms);
    const cleanup = () => {
      clearTimeout(timer);
      signal?.removeEventListener("abort", onAbort);
    };
    const onAbort = () => {
      cleanup();
      reject(new Error("aborted"));
    };
    signal?.addEventListener("abort", onAbort, { once: true });
  });
}
