/**
 * Weixin external channel: iLink HTTP + DotCraft WebSocket wire.
 */

import { mkdirSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { randomUUID } from "node:crypto";
import QRCode from "qrcode";
import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
  type SocialChannelTarget,
} from "@dotcraft/channel";
import {
  ChannelAppServerClient,
  ModuleConfigLoader,
  WebSocketTransport,
  type Transport,
} from "@dotcraft/channel/runtime";
import {
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
  type ChannelToolDescriptor,
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
import {
  WEIXIN_SEND_FILE_TOOL,
  WEIXIN_SEND_IMAGE_TOOL,
  WeixinMediaError,
  WeixinMediaTools,
} from "./weixin-media-tools.js";

/** Match QQ/WeCom keyword-style approval (plain text). */
function parseApprovalDecision(text: string): string | null {
  const t = text.trim().toLowerCase();
  const raw = text.trim();

  if (
    ["同意全部", "允许全部"].some((x) => raw.includes(x)) ||
    ["yes all", "approve all", "y all"].includes(t)
  ) {
    return DECISION_ACCEPT_FOR_SESSION;
  }
  if (
    ["同意", "允许", "yes", "y", "approve"].includes(t) ||
    ["同意", "允许"].some((x) => raw === x)
  ) {
    return DECISION_ACCEPT;
  }
  if (["拒绝", "不同意", "no", "n", "reject", "deny"].includes(t) || raw === "拒绝") {
    return DECISION_DECLINE;
  }
  return null;
}

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

interface CaptionDeliveryResult {
  attempted: boolean;
  delivered?: boolean;
  errorMessage?: string;
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
  private readonly mediaTools = new WeixinMediaTools();

  /** userId -> waiter for active approval */
  private readonly approvalWaiters = new Map<
    string,
    { resolve: (v: string) => void; timer: ReturnType<typeof setTimeout> }
  >();
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

  private async sendCaptionIfPresent(toUserId: string, caption: string): Promise<CaptionDeliveryResult> {
    if (!caption.trim()) return { attempted: false };
    try {
      await this.sendWeixinText(toUserId, caption);
      return { attempted: true, delivered: true };
    } catch (error) {
      console.error("caption send failed:", error);
      return { attempted: true, delivered: false, errorMessage: this.errText(error) };
    }
  }

  private attachCaptionInfo(
    result: Record<string, unknown>,
    caption: CaptionDeliveryResult,
  ): Record<string, unknown> {
    if (!caption.attempted) return result;

    const updated: Record<string, unknown> = {
      ...result,
      captionDelivered: caption.delivered === true,
    };
    if (caption.errorMessage) {
      updated.captionError = caption.errorMessage;
    }

    const structured = result.structuredContent;
    if (structured && typeof structured === "object" && !Array.isArray(structured)) {
      updated.structuredContent = {
        ...(structured as Record<string, unknown>),
        captionDelivered: caption.delivered === true,
        captionError: caption.errorMessage ?? null,
      };
    }

    return updated;
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

  /**
   * If message is an approval reply, consume it and return true (do not forward to agent).
   */
  tryHandleApprovalReply(fromUserId: string, text: string): boolean {
    const pending = this.approvalWaiters.get(fromUserId);
    if (!pending) return false;
    const decision = parseApprovalDecision(text);
    if (!decision) return false;
    clearTimeout(pending.timer);
    pending.resolve(decision);
    return true;
  }

  /** Resolve pending approval with cancel (e.g. user sent /new while a prompt was open). */
  private cancelPendingApprovalIfAny(userId: string): void {
    const pending = this.approvalWaiters.get(userId);
    if (!pending) return;
    clearTimeout(pending.timer);
    pending.resolve(DECISION_CANCEL);
  }

  private cancelPendingUserInputIfAny(userId: string): void {
    const pending = this.userInputWaiters.get(userId);
    if (!pending) return;
    this.userInputWaiters.delete(userId);
    pending.resolve(emptyUserInputResponse());
  }

  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    const userId = target.replace(/^user:/, "");
    try {
      await this.sendWeixinText(userId, content);
      return true;
    } catch (e) {
      console.error("onDeliver failed:", e);
      return false;
    }
  }

  protected override getDeliveryCapabilities(): Record<string, unknown> | null {
    return this.mediaTools.getDeliveryCapabilities();
  }

  protected override getChannelTools(): ChannelToolDescriptor[] | null {
    return this.mediaTools.getChannelTools();
  }

  protected override buildSocialTarget(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
  ): SocialChannelTarget | null {
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
    metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const kind = String(message.kind ?? "");
    if (kind === "text") {
      return await super.onSend(target, message, metadata);
    }

    if (kind !== "file" && kind !== "image") {
      return {
        delivered: false,
        errorCode: "UnsupportedDeliveryKind",
        errorMessage: `Weixin channel does not support structured '${kind}' delivery.`,
      };
    }

    let captionResult: CaptionDeliveryResult = { attempted: false };
    try {
      const userId = target.replace(/^user:/, "");
      const caption = String(message.caption ?? "");
      captionResult = await this.sendCaptionIfPresent(userId, caption);
      const result = await this.mediaTools.sendStructuredMessage({
        baseUrl: this.apiBaseUrl,
        token: this.getBotToken(),
        toUserId: userId,
        contextToken: this.contextTokens[userId],
        clientId: `dotcraft-weixin-${randomUUID()}`,
        message,
      });
      return this.attachCaptionInfo(result, captionResult);
    } catch (error) {
      if (error instanceof WeixinMediaError) {
        return this.attachCaptionInfo(
          { delivered: false, errorCode: error.code, errorMessage: error.message },
          captionResult,
        );
      }
      return this.attachCaptionInfo(
        {
          delivered: false,
          errorCode: "AdapterDeliveryFailed",
          errorMessage: error instanceof Error ? error.message : String(error),
        },
        captionResult,
      );
    }
  }

  protected override async onToolCall(
    request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const tool = String(request.tool ?? "");
    if (tool !== WEIXIN_SEND_FILE_TOOL && tool !== WEIXIN_SEND_IMAGE_TOOL) {
      return {
        success: false,
        errorCode: "UnsupportedTool",
        errorMessage: `Unknown tool '${tool}'.`,
      };
    }

    const args = (request.arguments as Record<string, unknown>) ?? {};
    const context = (request.context as Record<string, unknown>) ?? {};
    const target = String(context.channelContext ?? context.groupId ?? "");
    if (!target) {
      return {
        success: false,
        errorCode: "MissingChatContext",
        errorMessage: "Current tool call does not contain a Weixin chat target.",
      };
    }

    let captionResult: CaptionDeliveryResult = { attempted: false };
    try {
      const caption = String(args.caption ?? "");
      captionResult = await this.sendCaptionIfPresent(target, caption);
      const result = await this.mediaTools.executeToolCall({
        baseUrl: this.apiBaseUrl,
        token: this.getBotToken(),
        toUserId: target,
        contextToken: this.contextTokens[target],
        clientId: `dotcraft-weixin-${randomUUID()}`,
        toolName: tool,
        args,
      });
      return this.attachCaptionInfo(result, captionResult);
    } catch (error) {
      if (error instanceof WeixinMediaError) {
        return this.attachCaptionInfo(
          {
            success: false,
            errorCode: error.code,
            errorMessage: error.message,
            contentItems: [{ type: "text", text: error.message }],
            structuredContent: {
              delivered: false,
              errorCode: error.code,
              errorMessage: error.message,
            },
          },
          captionResult,
        );
      }
      return this.attachCaptionInfo(
        {
          success: false,
          errorCode: "AdapterToolCallFailed",
          errorMessage: error instanceof Error ? error.message : String(error),
          contentItems: [{ type: "text", text: error instanceof Error ? error.message : String(error) }],
          structuredContent: {
            delivered: false,
            errorCode: "AdapterToolCallFailed",
            errorMessage: error instanceof Error ? error.message : String(error),
          },
        },
        captionResult,
      );
    }
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

    return new Promise<string>((resolve) => {
      const timer = setTimeout(() => {
        this.approvalWaiters.delete(userId);
        resolve(DECISION_CANCEL);
      }, this.approvalTimeoutMs);
      this.approvalWaiters.set(userId, {
        resolve: (v: string) => {
          clearTimeout(timer);
          this.approvalWaiters.delete(userId);
          resolve(v);
        },
        timer,
      });
    });
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

  protected override async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    segmentText: string,
    _isFinal: boolean,
    channelContext: string,
  ): Promise<boolean | void> {
    if (!segmentText.trim()) return true;
    try {
      await this.sendWeixinText(channelContext, segmentText);
      return true;
    } catch (e) {
      console.error("onSegmentCompleted send failed:", e);
      return false;
    }
  }

  protected override async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (segmentsWereDelivered) return;
    if (!replyText) return;
    try {
      await this.sendWeixinText(channelContext, replyText);
    } catch (e) {
      console.error("onTurnCompleted send failed:", e);
    }
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

    if (isNewCommand(text)) {
      this.cancelPendingApprovalIfAny(from);
      this.cancelPendingUserInputIfAny(from);
      await this.newThread(from, from);
      await this.sendWeixinText(from, "已开启新对话，请直接输入消息。");
      return;
    }

    if (this.tryHandleApprovalReply(from, text)) return;
    if (await this.tryHandleUserInputReply(from, text)) return;

    const name = from;
    // Fire-and-forget so the monitor loop can continue polling for approval replies
    // while the turn is in progress. Per-identity ordering is preserved by the
    // threadQueues/runWorker queue inside handleMessage.
    void this.handleMessage({
      userId: from,
      userName: name,
      text,
      channelContext: from,
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
