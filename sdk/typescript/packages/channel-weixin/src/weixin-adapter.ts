/**
 * Weixin external channel: iLink HTTP + DotCraft WebSocket wire.
 */

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import QRCode from "qrcode";
import {
  DECISION_CANCEL,
  DECISION_DECLINE,
  type ChannelTarget,
} from "@dotcraft/channel";
import {
  ChannelAppServerClient,
  ModuleConfigLoader,
  WebSocketTransport,
  type Transport,
} from "@dotcraft/channel/runtime";
import {
  ApprovalReplyTracker,
  ConfigValidationError,
  ModuleChannelAdapter,
  buildUserInputPrompt,
  emptyUserInputResponse,
  hasUserInputAnswer,
  mergeUserInputResponses,
  resolveModuleStatePath,
  resolveModuleTempPath,
  splitUserInputRequestByQuestion,
  userInputResponseFromText,
  type ChannelChatDescriptor,
  type ChannelDeliveryCapabilities,
  type ChannelAdapterMessageOpts,
  type ModuleError,
  type UserInputResponse,
  type WorkspaceContext,
} from "@dotcraft/channel";

import { waitForQrLogin, DEFAULT_BOT_TYPE } from "./auth.js";
import { markdownToPlainText } from "./formatting.js";
import { runMonitorLoop } from "./monitor.js";
import { WeixinState, type WeixinCredentials } from "./state.js";
import type { WeixinConfig } from "./weixin-config.js";
import { buildTextMessageReq, sendMessage } from "./weixin-api.js";
import { deliverWeixinMessage, weixinDeliveryCapabilities } from "./weixin-delivery.js";
import { WeixinMediaSender } from "./weixin-media.js";

/** Telegram-style /new: start a fresh thread (strict match, case-insensitive). */
function isNewCommand(text: string): boolean {
  return /^\s*\/new\s*$/i.test(text.trim());
}

function hasUsableCredentials(credentials: WeixinCredentials | null | undefined): credentials is WeixinCredentials {
  if (!credentials) return false;
  return Boolean(credentials.botToken?.trim() && credentials.ilinkBotId?.trim());
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function shouldRetrySendMessage(error: unknown): boolean {
  const name = error instanceof Error ? error.name : "";
  const message = error instanceof Error ? error.message : String(error);
  return name === "AbortError" || message.includes("fetch failed") || /\b5\d\d:/.test(message);
}

export function validateWeixinConfig(rawConfig: unknown): asserts rawConfig is WeixinConfig {
  if (!rawConfig || typeof rawConfig !== "object") {
    throw new ConfigValidationError("Weixin config must be an object.", ["config"]);
  }
  const config = rawConfig as Record<string, unknown>;
  const dotcraft = (config.dotcraft as Record<string, unknown> | undefined) ?? {};
  const weixin = (config.weixin as Record<string, unknown> | undefined) ?? {};

  const fields: string[] = [];
  const wsUrl = String(dotcraft.wsUrl ?? "").trim();
  const apiBaseUrl = String(weixin.apiBaseUrl ?? "").trim();

  if (!wsUrl) {
    fields.push("dotcraft.wsUrl");
  } else if (!/^wss?:\/\//i.test(wsUrl)) {
    throw new ConfigValidationError("dotcraft.wsUrl must use ws:// or wss://.", ["dotcraft.wsUrl"]);
  }

  if (!apiBaseUrl) {
    fields.push("weixin.apiBaseUrl");
  }

  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export class WeixinAdapter extends ModuleChannelAdapter<WeixinConfig> {
  private apiBaseUrl = "";
  private botToken = "";
  private approvalTimeoutMs = 120_000;
  private state: WeixinState | undefined;
  private tempDir = "";
  private contextTokens: Record<string, string> = {};
  private dotcraftStarted = false;
  private monitorAbortController: AbortController | undefined;
  private authAbortController: AbortController | undefined;
  private authFlowInProgress = false;
  private readonly media = new WeixinMediaSender();

  private readonly approvalReplies = new ApprovalReplyTracker({
    notify: async ({ userId }, text) => {
      await this.sendWeixinText(userId, text);
    },
  });
  private readonly userInputWaiters = new Map<
    string,
    { request: Record<string, unknown>; promptTitle?: string; resolve: (response: UserInputResponse) => void }
  >();

  constructor() {
    super("weixin", "dotcraft-weixin", "0.1.0", [
      "item/reasoning/delta",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ]);
  }

  protected override getConfigFileName(_context: WorkspaceContext): string {
    return "weixin.json";
  }

  protected override validateConfig(rawConfig: unknown): asserts rawConfig is WeixinConfig {
    validateWeixinConfig(rawConfig);
  }

  protected override buildTransportFromConfig(config: WeixinConfig): Transport {
    return new WebSocketTransport({
      url: config.dotcraft.wsUrl,
      token: config.dotcraft.token ?? "",
    });
  }

  override async startWithContext(context: WorkspaceContext): Promise<void> {
    this.context = context;
    this.setStatus("starting");

    const configLoader = new ModuleConfigLoader<WeixinConfig>({
      getConfigFileName: (ctx) => this.getConfigFileName(ctx),
      validateConfig: (rawConfig): asserts rawConfig is WeixinConfig => this.validateConfig(rawConfig),
    });
    const loaded = await configLoader.load(context);
    if (loaded.status !== "loaded") {
      this.setStatus(loaded.status, loaded.error);
      return;
    }
    this.loadedConfig = loaded.config;

    const config = this.loadedConfig;
    this.approvalTimeoutMs = config.weixin.approvalTimeoutMs ?? 120_000;
    this.tempDir = resolveModuleTempPath(context);
    mkdirSync(this.tempDir, { recursive: true });
    this.state = new WeixinState(resolveModuleStatePath(context));
    this.contextTokens = this.state.loadContextTokens();

    const existing = this.state.loadCredentials();
    if (hasUsableCredentials(existing)) {
      try {
        await this.ensureDotCraftReady(config);
        this.activateCredentials(existing, config);
        this.startMonitor(config);
      } catch (error) {
        this.setStatus("stopped", this.buildError("startupFailed", this.errText(error)));
      }
      return;
    }

    this.signalAuthRequired(
      this.buildError("authRequired", "Weixin QR login is required before startup.", {
        statePath: resolveModuleStatePath(context),
      }),
    );
    this.beginAuthFlow("startup");
  }

  override async stop(): Promise<void> {
    this.authAbortController?.abort();
    this.authAbortController = undefined;
    this.monitorAbortController?.abort();
    this.monitorAbortController = undefined;
    this.authFlowInProgress = false;
    this.approvalReplies.resolveAll(DECISION_CANCEL);
    this.resolveAllPendingUserInputs(emptyUserInputResponse());

    if (this.dotcraftStarted) {
      await super.stop();
      this.dotcraftStarted = false;
      return;
    }
    this.setStatus("stopped", this.getError());
  }

  private beginAuthFlow(reason: "startup" | "expired"): void {
    if (this.authFlowInProgress) return;
    if (!this.loadedConfig) return;
    if (!this.state) return;

    const config = this.loadedConfig;
    const state = this.state;
    const authController = new AbortController();
    this.authAbortController = authController;
    this.authFlowInProgress = true;

    void (async () => {
      try {
        const botType = config.weixin.botType ?? DEFAULT_BOT_TYPE;
        const creds = await waitForQrLogin({
          apiBaseUrl: config.weixin.apiBaseUrl,
          botType,
          abortSignal: authController.signal,
          onQrUrl: (url) => {
            this.persistQrArtifact(url);
            this.setStatus(
              "authRequired",
              this.buildError("authRequired", "Scan the Weixin QR code to continue.", {
                reason,
                qrUrl: url,
                qrPath: join(this.tempDir, "qr.png"),
              }),
            );
          },
        });

        if (authController.signal.aborted) return;

        state.saveCredentials(creds);
        this.activateCredentials(creds, config);
        await this.ensureDotCraftReady(config);
        this.startMonitor(config);
        this.setStatus("ready");
      } catch (error) {
        if (authController.signal.aborted) return;
        this.setStatus("authRequired", this.buildError("authRequired", this.errText(error)));
      } finally {
        if (this.authAbortController === authController) {
          this.authAbortController = undefined;
        }
        this.authFlowInProgress = false;
      }
    })();
  }

  private startMonitor(config: WeixinConfig): void {
    const state = this.getState();
    const token = this.getBotToken();
    this.monitorAbortController?.abort();
    const controller = new AbortController();
    this.monitorAbortController = controller;

    void runMonitorLoop({
      baseUrl: this.apiBaseUrl,
      token,
      getInitialBuf: () => state.loadSyncBuf(),
      saveBuf: (buf) => state.saveSyncBuf(buf),
      longPollMs: config.weixin.pollTimeoutMs,
      abortSignal: controller.signal,
      callbacks: {
        onMessage: async (msg) => {
          await this.handleInboundUserMessage(msg);
        },
        onSessionExpired: async () => {
          controller.abort();
          this.signalAuthExpired(this.buildError("authExpired", "Weixin session expired."));
          this.signalAuthRequired(
            this.buildError("authRequired", "Weixin login is required again after expiry."),
          );
          this.beginAuthFlow("expired");
        },
      },
    }).catch((error) => {
      if (controller.signal.aborted) return;
      this.setStatus("stopped", this.buildError("unexpectedRuntimeFailure", this.errText(error)));
    });
  }

  private activateCredentials(creds: WeixinCredentials, config: WeixinConfig): void {
    this.apiBaseUrl = creds.baseUrl || config.weixin.apiBaseUrl;
    this.botToken = creds.botToken;
  }

  private async ensureDotCraftReady(config: WeixinConfig): Promise<void> {
    if (this.dotcraftStarted) return;
    this.client = new ChannelAppServerClient(this.buildTransportFromConfig(config), { autoReconnect: true });
    await super.start();
    this.dotcraftStarted = true;
  }

  private persistQrArtifact(url: string): void {
    if (!this.tempDir) return;
    mkdirSync(this.tempDir, { recursive: true });
    writeFileSync(join(this.tempDir, "qr-url.txt"), `${url}\n`, "utf-8");
    void (async () => {
      try {
        const data = await QRCode.toBuffer(url, { type: "png", margin: 1, width: 360 });
        writeFileSync(join(this.tempDir, "qr.png"), data);
      } catch {
        // Ignore QR artifact persistence failures.
      }
    })();
  }

  private getState(): WeixinState {
    if (!this.state) {
      throw new Error("Weixin state is not initialized.");
    }
    return this.state;
  }

  private getBotToken(): string {
    if (!this.botToken) {
      throw new Error("Weixin bot token is not available.");
    }
    return this.botToken;
  }

  private buildError(code: ModuleError["code"], message: string, detail?: Record<string, unknown>): ModuleError {
    return {
      code,
      message,
      detail,
      timestamp: new Date().toISOString(),
    };
  }

  private errText(error: unknown): string {
    return error instanceof Error ? error.message : String(error);
  }

  private findUserIdForThread(threadId: string): string | undefined {
    for (const [identityKey, tid] of this.threadMap) {
      if (tid === threadId) {
        const userId = identityKey.split(":")[0];
        return userId;
      }
    }
    return undefined;
  }

  private async sendWeixinText(toUserId: string, text: string): Promise<void> {
    const ctx = this.contextTokens[toUserId];
    const clientId = `dotcraft-weixin-${randomUUID()}`;
    const req = buildTextMessageReq({
      toUserId,
      text: markdownToPlainText(text),
      contextToken: ctx,
      clientId,
    });
    let lastError: unknown = null;
    for (let attempt = 1; attempt <= 3; attempt += 1) {
      try {
        await sendMessage({
          baseUrl: this.apiBaseUrl,
          token: this.getBotToken(),
          body: req,
        });
        return;
      } catch (error) {
        lastError = error;
        if (attempt === 3 || !shouldRetrySendMessage(error)) break;
        await delay(attempt === 1 ? 250 : 750);
      }
    }
    throw lastError instanceof Error ? lastError : new Error(String(lastError));
  }

  private cancelPendingUserInputIfAny(userId: string): void {
    const pending = this.userInputWaiters.get(userId);
    if (!pending) return;
    this.userInputWaiters.delete(userId);
    pending.resolve(emptyUserInputResponse());
  }

  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    try {
      await this.sendWeixinText(target, content);
      return true;
    } catch (e) {
      console.error("onDeliver failed:", e);
      return false;
    }
  }

  protected override getDisplayName(): string {
    return "WeChat";
  }

  protected override getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return weixinDeliveryCapabilities();
  }

  protected override getChatDescriptor(
    _opts: ChannelAdapterMessageOpts,
    _channelContext: string,
  ): ChannelChatDescriptor {
    return { kind: "direct", membership: "shared" };
  }

  protected override buildChannelTarget(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
  ): ChannelTarget | null {
    const conversationId = channelContext.trim();
    if (!conversationId) return null;
    const platformUserId = String(sender.senderId ?? opts.userId ?? "");
    const displayName = typeof sender.senderName === "string" && sender.senderName.trim()
      ? sender.senderName.trim()
      : null;
    return {
      channelName: "weixin",
      conversationKind: "user",
      conversationId,
      deliveryTarget: conversationId,
      displayName: displayName ?? `Weixin user ${conversationId}`,
      boundBy: platformUserId
        ? {
          platformUserId,
          displayName,
        }
        : null,
    };
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    _metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    return await deliverWeixinMessage(
      {
        getContextToken: (userId) => this.contextTokens[userId],
        sendText: (userId, text) => this.sendWeixinText(userId, text),
        sendMedia: (userId, media) =>
          this.media.sendStructuredMessage({
            baseUrl: this.apiBaseUrl,
            token: this.getBotToken(),
            toUserId: userId,
            contextToken: this.contextTokens[userId],
            clientId: `dotcraft-weixin-${randomUUID()}`,
            message: media,
          }),
      },
      target,
      message,
    );
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const threadId = String(request.threadId ?? "");
    const approvalType = String(request.approvalType ?? "");
    const operation = String(request.operation ?? "");
    const target = String(request.target ?? "");

    const userId = this.findUserIdForThread(threadId);
    if (!userId) {
      console.warn("No user for thread; declining approval");
      return DECISION_DECLINE;
    }

    // Chinese prompt aligned with QQ/WeCom session-protocol wording (WeChat is Chinese-first).
    const timeoutSeconds = Math.round(this.approvalTimeoutMs / 1000);
    const prompt =
      approvalType === "shell"
        ? `⚠️ 需要执行命令权限：\`${operation}\`\n回复 同意/yes 批准，同意全部/yes all 本会话放行同类操作，拒绝/no 拒绝（${timeoutSeconds}秒超时自动拒绝）`
        : `⚠️ 需要 ${operation} 文件权限：\`${target}\`\n回复 同意/yes 批准，同意全部/yes all 本会话放行同类操作，拒绝/no 拒绝（${timeoutSeconds}秒超时自动拒绝）`;

    await this.sendWeixinText(userId, prompt);

    return await this.approvalReplies.wait(
      String(request.requestId ?? "") || randomUUID(),
      { channelContext: userId, userId },
      this.approvalTimeoutMs,
    );
  }

  protected override async onUserInputRequest(request: Record<string, unknown>): Promise<UserInputResponse> {
    const threadId = String(request.threadId ?? "");
    const userId = this.findUserIdForThread(threadId);
    if (!userId) {
      console.warn("No user for thread; resolving user input empty");
      return emptyUserInputResponse();
    }

    const steps = splitUserInputRequestByQuestion(request);
    if (steps.length === 0) {
      return emptyUserInputResponse();
    }

    const responses: UserInputResponse[] = [];
    for (const step of steps) {
      const response = await this.requestUserInputStep({
        userId,
        request: step.request,
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
    userId: string;
    request: Record<string, unknown>;
    promptTitle?: string;
  }): Promise<UserInputResponse> {
    await this.sendWeixinText(
      params.userId,
      `❓ 需要你回答 DotCraft 问题\n${buildUserInputPrompt(params.request, { title: params.promptTitle })}`,
    );

    return await new Promise<UserInputResponse>((resolve) => {
      const previous = this.userInputWaiters.get(params.userId);
      previous?.resolve(emptyUserInputResponse());
      this.userInputWaiters.set(params.userId, {
        request: params.request,
        promptTitle: params.promptTitle,
        resolve,
      });
    });
  }

  private userInputPromptTitle(questionIndex: number, questionCount: number): string {
    const base = "DotCraft needs your input";
    return questionCount > 1 ? `${base} (${questionIndex + 1}/${questionCount})` : base;
  }

  updateContextToken(userId: string, token: string | undefined): void {
    if (!token) return;
    this.contextTokens[userId] = token;
    this.getState().saveContextTokens(this.contextTokens);
  }

  async handleInboundUserMessage(msg: {
    from_user_id?: string;
    item_list?: { type?: number; text_item?: { text?: string } }[];
    context_token?: string;
  }): Promise<void> {
    const from = msg.from_user_id ?? "";
    if (!from) return;

    this.updateContextToken(from, msg.context_token);

    const text =
      msg.item_list
        ?.map((i) => (i.type === 1 ? i.text_item?.text ?? "" : ""))
        .join("")
        .trim() ?? "";

    const approver = { channelContext: from, userId: from };
    if (isNewCommand(text)) {
      this.approvalReplies.cancel(approver);
      this.cancelPendingUserInputIfAny(from);
      await this.newThread(from, from);
      await this.sendWeixinText(from, "已开启新对话，请直接输入消息。");
      return;
    }

    if (this.approvalReplies.tryResolve(approver, text)) return;
    if (await this.tryHandleUserInputReply(from, text)) return;
    if (await this.approvalReplies.remindIfPending(approver)) return;

    const name = from;
    // Fire-and-forget so the monitor loop can continue polling for approval replies
    // while the turn is in progress. Per-identity ordering is preserved by the
    // threadQueues/runWorker queue inside handleMessage.
    void this.handleMessage({
      userId: from,
      userName: name,
      text,
      channelContext: from,
      directTarget: from,
      senderExtra: { senderRole: "admin" },
    }).catch((e) => console.error("handleMessage error:", e));
  }

  private async tryHandleUserInputReply(userId: string, text: string): Promise<boolean> {
    const pending = this.userInputWaiters.get(userId);
    if (!pending) return false;
    const response = userInputResponseFromText(pending.request, text);
    if (!response) {
      await this.sendWeixinText(userId, buildUserInputPrompt(pending.request, { title: pending.promptTitle }));
      return true;
    }

    this.userInputWaiters.delete(userId);
    pending.resolve(response);
    return true;
  }

  private resolveAllPendingUserInputs(response: UserInputResponse): void {
    for (const [userId, pending] of this.userInputWaiters) {
      pending.resolve(response);
      this.userInputWaiters.delete(userId);
    }
  }
}
