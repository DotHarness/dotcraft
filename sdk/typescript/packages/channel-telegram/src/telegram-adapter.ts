import { HttpsProxyAgent } from "https-proxy-agent";
import {
  Bot,
  GrammyError,
  HttpError,
  InlineKeyboard,
  type Context,
} from "grammy";
import {
  WebSocketTransport,
  type Transport,
} from "@dotcraft/sdk/wire";
import {
  ConfigValidationError,
  ModuleChannelAdapter,
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
  type UserInputResponse,
  type WorkspaceContext,
} from "@dotcraft/sdk/channel";

import { markdownToTelegramHtml, splitTelegramMessage } from "./formatting.js";
import {
  TelegramMediaError,
  TelegramMediaTools,
  type TelegramApiLike,
} from "./telegram-media-tools.js";
import type { TelegramConfig } from "./telegram-config.js";

const APPROVAL_BUTTONS = [
  { decision: "accept", label: "\u2705 Approve" },
  { decision: "acceptForSession", label: "\u2705 Approve (this session)" },
  { decision: "decline", label: "\u274C Decline" },
  { decision: "cancel", label: "\u{1F6D1} Cancel turn" },
] as const;

export const DEFAULT_BOT_COMMANDS = [
  { command: "new", description: "Start a new conversation" },
  { command: "help", description: "Show available commands" },
] as const;

type PendingApproval = {
  resolve: (decision: string) => void;
  timer: ReturnType<typeof setTimeout>;
};

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

  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export async function buildTelegramBotCommands(
  commandList: () => Promise<Record<string, unknown>[]>,
): Promise<Array<{ command: string; description: string }>> {
  const merged = new Map<string, string>();
  for (const item of DEFAULT_BOT_COMMANDS) {
    merged.set(item.command, item.description);
  }

  const commands = await commandList();
  for (const item of commands) {
    const name = String(item.name ?? "").trim().replace(/^\//, "").toLowerCase();
    if (!name || name.length > 32 || !/^[a-z0-9_]+$/.test(name)) {
      continue;
    }

    const description = String(item.description ?? "DotCraft command").trim() || "DotCraft command";
    if (!merged.has(name)) {
      merged.set(name, description.slice(0, 256));
    }
  }

  return [...merged.entries()].map(([command, description]) => ({ command, description }));
}

export function parseTargetChatId(target: string): number | null {
  const raw = String(target).trim();
  if (!raw) return null;
  const normalized = raw.startsWith("group:")
    ? raw.slice("group:".length)
    : raw.startsWith("user:")
      ? raw.slice("user:".length)
      : raw;
  const parsed = Number(normalized);
  return Number.isInteger(parsed) ? parsed : null;
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
  private readonly mediaTools = new TelegramMediaTools();
  private readonly threadContextMap = new Map<string, string>();
  private readonly typingTasks = new Map<string, Promise<void>>();
  private readonly typingAbortControllers = new Map<string, AbortController>();
  private readonly pendingApprovals = new Map<string, PendingApproval>();
  private readonly pendingUserInputs = new Map<string, PendingUserInput>();
  private readonly pendingUserInputByChat = new Map<string, string>();
  private nextUserInputToken = 1;
  private pollingPromise: Promise<void> | null = null;
  private approvalTimeoutMs = 120_000;
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

  protected override getDeliveryCapabilities(): Record<string, unknown> | null {
    return this.mediaTools.getDeliveryCapabilities();
  }

  protected override getChannelTools(): Record<string, unknown>[] | null {
    return this.mediaTools.getChannelTools();
  }

  override async startWithContext(context: WorkspaceContext): Promise<void> {
    this.defaultWorkspacePath = context.workspaceRoot;
    await super.startWithContext(context);
    if (this.getStatus() !== "ready" || !this.loadedConfig) {
      return;
    }

    const config = this.loadedConfig;
    this.approvalTimeoutMs = config.telegram.approvalTimeoutMs ?? 120_000;
    this.pollTimeoutSeconds = Math.max(1, Math.ceil((config.telegram.pollTimeoutMs ?? 30_000) / 1000));

    try {
      const bot = this.createBot(config);
      this.bot = bot;
      this.installBotHandlers(bot);

      const me = await bot.api.getMe();
      console.info(`[telegram] bot @${me.username ?? me.first_name} connected`);

      const commands = await buildTelegramBotCommands(() => this.client.commandList());
      await bot.api.setMyCommands(commands);
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
    this.resolveAllPendingApprovals("cancel");
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

  protected override onThreadsArchived(_identityKey: string, archivedThreadIds: string[]): void {
    for (const threadId of archivedThreadIds) {
      this.threadContextMap.delete(threadId);
    }
  }

  override async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    const bot = this.requireBot();
    const chatId = parseTargetChatId(target);
    if (chatId === null) {
      console.error(`[telegram] invalid delivery target: ${target}`);
      return false;
    }

    this.stopTyping(String(chatId));
    await this.sendText(bot.api as TelegramApiLike, chatId, content);
    return true;
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      return await super.onSend(target, message, metadata);
    }

    const chatId = parseTargetChatId(target);
    if (chatId === null) {
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: `Invalid Telegram target '${target}'.`,
      };
    }

    try {
      return await this.mediaTools.sendStructuredMessage(this.requireBot().api as TelegramApiLike, chatId, message, metadata);
    } catch (error) {
      if (error instanceof TelegramMediaError) {
        return {
          delivered: false,
          errorCode: error.code,
          errorMessage: error.message,
        };
      }
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: describeTelegramError(error),
      };
    }
  }

  protected override async onToolCall(request: Record<string, unknown>): Promise<Record<string, unknown>> {
    const tool = String(request.tool ?? "");
    const args = asRecord(request.arguments);
    const context = asRecord(request.context);
    const target = String(context.channelContext ?? context.groupId ?? "");
    const chatId = parseTargetChatId(target);
    if (chatId === null) {
      return {
        success: false,
        errorCode: "MissingChatContext",
        errorMessage: "Current tool call does not contain a Telegram chat target.",
      };
    }

    try {
      return await this.mediaTools.executeToolCall(this.requireBot().api as TelegramApiLike, tool, chatId, args);
    } catch (error) {
      if (error instanceof TelegramMediaError) {
        return {
          success: false,
          errorCode: error.code,
          errorMessage: error.message,
        };
      }
      return {
        success: false,
        errorCode: "AdapterToolCallFailed",
        errorMessage: describeTelegramError(error),
      };
    }
  }

  override async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const threadId = String(request.threadId ?? "");
    const approvalType = String(request.approvalType ?? "");
    const operation = String(request.operation ?? "");
    const reason = String(request.reason ?? "");
    const requestId = String(request.requestId ?? "");

    const chatTarget = this.threadContextMap.get(threadId);
    const chatId = chatTarget ? parseTargetChatId(chatTarget) : null;
    if (chatId === null) {
      console.warn(`[telegram] cannot find chat for thread ${threadId}; auto-declining approval`);
      return "decline";
    }

    const keyboard = new InlineKeyboard();
    for (const button of APPROVAL_BUTTONS) {
      keyboard.text(button.label, `approval:${requestId}:${button.decision}`).row();
    }

    const prompt =
      "<b>Agent approval required</b>\n" +
      `Type: <code>${escapeHtml(approvalType)}</code>\n` +
      `Operation: <code>${escapeHtml(operation)}</code>\n` +
      `Reason: ${escapeHtml(reason)}`;

    await this.requireBot().api.sendMessage(chatId, prompt, {
      parse_mode: "HTML",
      reply_markup: keyboard,
    });

    const decision = await new Promise<string>((resolveDecision) => {
      const prefix = `approval:${requestId}`;
      const timer = setTimeout(() => {
        this.pendingApprovals.delete(prefix);
        resolveDecision("cancel");
      }, this.approvalTimeoutMs);

      this.pendingApprovals.set(prefix, {
        resolve: (value) => {
          clearTimeout(timer);
          this.pendingApprovals.delete(prefix);
          resolveDecision(value);
        },
        timer,
      });
    });

    return decision;
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

  protected override async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    const chatId = parseTargetChatId(channelContext);
    if (chatId === null) {
      return;
    }

    await this.sendText(this.requireBot().api as TelegramApiLike, chatId, segmentText);
    if (isFinal) {
      this.stopTyping(String(chatId));
    }
  }

  protected override async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    const chatId = parseTargetChatId(channelContext);
    if (chatId === null) {
      return;
    }

    this.stopTyping(String(chatId));
    if (!segmentsWereDelivered && replyText) {
      await this.sendText(this.requireBot().api as TelegramApiLike, chatId, replyText);
    }
  }

  protected override async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    const channelContext = this.threadContextMap.get(threadId);
    if (channelContext) {
      this.stopTyping(channelContext);
    }
    console.error(`[telegram] turn ${turnId} failed: ${error}`);
  }

  protected override async onTurnCancelled(threadId: string, turnId: string): Promise<void> {
    const channelContext = this.threadContextMap.get(threadId);
    if (channelContext) {
      this.stopTyping(channelContext);
    }
    console.info(`[telegram] turn ${turnId} cancelled`);
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
    const text = String(ctx.msg.text ?? "");
    if (await this.tryHandlePendingUserInputText(String(chatId), text)) {
      return;
    }
    this.startTyping(String(chatId));
    await this.handleMessage({
      userId: String(ctx.from.id),
      userName: ctx.from.first_name || ctx.from.username || String(ctx.from.id),
      text,
      channelContext: String(chatId),
      senderExtra: { senderRole: "admin" },
    });
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

    if (parts.length !== 3 || parts[0] !== "approval") {
      return;
    }

    const prefix = `${parts[0]}:${parts[1]}`;
    const decision = parts[2] ?? "cancel";
    const pending = this.pendingApprovals.get(prefix);
    if (pending) {
      pending.resolve(decision);
    }

    const message = ctx.callbackQuery.message;
    if (!message || !("message_id" in message) || !("chat" in message)) {
      return;
    }

    const chosenLabel = APPROVAL_BUTTONS.find((button) => button.decision === decision)?.label ?? decision;
    try {
      await this.requireBot().api.editMessageReplyMarkup(message.chat.id, message.message_id, {
        reply_markup: undefined,
      });
      const text = "text" in message ? escapeHtml(String(message.text ?? "")) : "";
      await this.requireBot().api.editMessageText(
        message.chat.id,
        message.message_id,
        `${text}\n\n<i>Decision: ${escapeHtml(chosenLabel)}</i>`,
        { parse_mode: "HTML" },
      );
    } catch {
      // Ignore edit failures for stale/deleted messages.
    }
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
    this.resolveAllPendingApprovals("cancel");
    this.bot = null;
    const message = describeTelegramError(error);
    await super.stop();
    this.setStatus("stopped", this.runtimeError("startupFailed", message));
  }

  private async handlePollingFailure(error: unknown): Promise<void> {
    this.stopAllTyping();
    this.resolveAllPendingApprovals("cancel");
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

  private async sendText(api: TelegramApiLike, chatId: number, content: string): Promise<void> {
    if (!content) {
      return;
    }

    for (const chunk of splitTelegramMessage(content)) {
      try {
        const html = markdownToTelegramHtml(chunk);
        await api.sendMessage(chatId, html, { parse_mode: "HTML" });
      } catch (error) {
        console.warn(`[telegram] html send failed, retrying as plain text: ${describeTelegramError(error)}`);
        await api.sendMessage(chatId, chunk);
      }
    }
  }

  private startTyping(chatId: string): void {
    this.stopTyping(chatId);
    const controller = new AbortController();
    this.typingAbortControllers.set(chatId, controller);
    const task = this.typingLoop(chatId, controller.signal).finally(() => {
      const current = this.typingAbortControllers.get(chatId);
      if (current === controller) {
        this.typingAbortControllers.delete(chatId);
      }
      this.typingTasks.delete(chatId);
    });
    this.typingTasks.set(chatId, task);
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

  private resolveAllPendingApprovals(decision: string): void {
    for (const [key, pending] of this.pendingApprovals) {
      clearTimeout(pending.timer);
      pending.resolve(decision);
      this.pendingApprovals.delete(key);
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

function escapeHtml(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}

function describeTelegramError(error: unknown): string {
  if (error instanceof GrammyError) {
    const code =
      typeof (error as unknown as { error_code?: unknown }).error_code === "number"
        ? Number((error as unknown as { error_code?: unknown }).error_code)
        : undefined;
    return code ? `[${code}] ${error.description}` : error.description;
  }
  if (error instanceof HttpError) {
    return error.message;
  }
  if (error instanceof Error) {
    return error.message;
  }
  return String(error);
}
