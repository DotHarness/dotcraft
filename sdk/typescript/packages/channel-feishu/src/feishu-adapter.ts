import {
  DECISION_CANCEL,
  DECISION_DECLINE,
  textPart,
  type SocialChannelTarget,
} from "@dotcraft/channel";
import {
  WebSocketTransport,
  configureTextMergeDebug,
  type ThreadResolveEvent,
} from "@dotcraft/channel/runtime";
import { mediaSourceFromToolPath, prepareMediaBytes } from "@dotcraft/channel/media";
import {
  ConfigValidationError,
  emptyUserInputResponse,
  hasUserInputAnswer,
  mergeUserInputResponses,
  normalizeUserInputQuestions,
  ModuleChannelAdapter,
  resolveModuleStatePath,
  splitUserInputRequestByQuestion,
  userInputResponseForSingleChoice,
  userInputResponseFromText,
  type ChannelToolDescriptor,
  type ChannelAdapterMessageOpts,
  type TurnItemActivity,
  type ModuleError,
  type UserInputResponse,
  type WorkspaceContext,
} from "@dotcraft/channel";
import {
  buildApprovalCard,
  buildApprovalResolvedCard,
  buildApprovalTimeoutCard,
  buildFileCaptionCard,
  buildNewConversationCard,
  buildTranscriptCard,
  buildUserInputCard,
  buildUserInputResolvedCard,
  type QuestionPosition,
} from "./card-builder.js";
import { createOrUpdateCard, sendReplyCards, updateCard } from "./card-sender.js";
import { FeishuChatInfoCache } from "./chat-info-cache.js";
import { conversationTargetBase } from "./conversation-target.js";
import { createFeishuEventHandlers } from "./event-handler.js";
import {
  type FeishuCardActionEvent,
  type FeishuConfig,
  type FeishuSendResult,
  type ParsedInboundMessage,
} from "./feishu-types.js";
import { FeishuClient } from "./feishu-client.js";
import { FeishuLoadingIcon } from "./loading-icon.js";
import { FeishuOutboundRouter } from "./outbound-router.js";
import { TurnCardController } from "./turn-card-controller.js";
import {
  FeishuCliTool,
  getFeishuCliRuntimeAdditionalContext,
  getFeishuCliToolDescriptors,
} from "./feishu-cli-tool.js";
import { errorMessage, logError, logInfo, logWarn, shortId } from "./logging.js";
import { composeTranscriptMarkdown } from "./transcript.js";

export function validateFeishuConfig(rawConfig: unknown): asserts rawConfig is FeishuConfig {
  const fields: string[] = [];
  if (!rawConfig || typeof rawConfig !== "object") {
    throw new ConfigValidationError("Feishu config must be an object.", ["config"]);
  }

  const config = rawConfig as Record<string, unknown>;
  const dotcraft = (config.dotcraft as Record<string, unknown> | undefined) ?? {};
  const feishu = (config.feishu as Record<string, unknown> | undefined) ?? {};

  const wsUrl = String(dotcraft.wsUrl ?? "").trim();
  const appId = String(feishu.appId ?? "").trim();
  const appSecret = String(feishu.appSecret ?? "").trim();
  const brand = String(feishu.brand ?? "").trim();
  if (!wsUrl) {
    fields.push("dotcraft.wsUrl");
  } else if (!/^wss?:\/\//i.test(wsUrl)) {
    throw new ConfigValidationError("dotcraft.wsUrl must use ws:// or wss://.", ["dotcraft.wsUrl"]);
  }
  if (!appId) fields.push("feishu.appId");
  if (!appSecret) fields.push("feishu.appSecret");
  if (brand && brand !== "feishu" && brand !== "lark") {
    throw new ConfigValidationError("feishu.brand must be either 'feishu' or 'lark'.", ["feishu.brand"]);
  }
  const streaming = feishu.streaming as Record<string, unknown> | undefined;
  if (streaming?.enabled !== undefined && typeof streaming.enabled !== "boolean") {
    throw new ConfigValidationError("feishu.streaming.enabled must be a boolean.", ["feishu.streaming.enabled"]);
  }
  if (feishu.cli !== undefined
      && (typeof feishu.cli !== "object" || feishu.cli === null || Array.isArray(feishu.cli))) {
    throw new ConfigValidationError("feishu.cli must be an object.", ["feishu.cli"]);
  }
  const cli = feishu.cli as Record<string, unknown> | undefined;
  if (cli?.enabled !== undefined && typeof cli.enabled !== "boolean") {
    throw new ConfigValidationError("feishu.cli.enabled must be a boolean.", ["feishu.cli.enabled"]);
  }
  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export class FeishuAdapter extends ModuleChannelAdapter<FeishuConfig> {
  private feishu: FeishuClient | undefined;
  private cardTitle = "DotCraft";
  private approvalTimeoutMs = 120000;
  private streamingEnabled = true;
  private cliTool: FeishuCliTool | undefined;
  private eventAbortController: AbortController | undefined;
  private loadingIcon: FeishuLoadingIcon | undefined;
  private statusTimings: { textStallMs?: number; statusSettleMs?: number } | undefined;
  private readonly router = new FeishuOutboundRouter(() => this.getFeishuClient());
  private readonly threadContextMap = new Map<string, string>();
  private readonly turnCards = new TurnCardController({
    streamingEnabled: () => this.streamingEnabled,
    client: () => this.getFeishuClient(),
    cardTitle: () => this.cardTitle,
    deliverCard: (target, cardId) => this.router.sendCardKit(target, cardId),
    statusIconImgKey: () => this.loadingIcon?.imgKey() ?? Promise.resolve(undefined),
    statusTimings: () => this.statusTimings,
  });
  private readonly approvalWaiters = new Map<
    string,
    {
      resolve: (decision: string) => void;
      timer: ReturnType<typeof setTimeout>;
      threadId: string;
      channelTarget: string;
      messageId: string;
      timeoutSeconds: number;
    }
  >();
  private readonly userInputWaiters = new Map<
    string,
    {
      resolve: (response: UserInputResponse) => void;
      threadId: string;
      channelTarget: string;
      messageId: string;
      request: Record<string, unknown>;
      questionPosition?: QuestionPosition;
    }
  >();
  private readonly userInputRequestByChannelTarget = new Map<string, string>();

  constructor() {
    super(
      "feishu",
      "dotcraft-feishu",
      "0.1.0",
      ["item/reasoning/delta", "subagent/progress", "item/usage/delta", "system/event", "plan/updated"],
    );
  }

  protected override getConfigFileName(_context: WorkspaceContext): string {
    return "feishu.json";
  }

  protected override getRuntimeAdditionalContext() {
    return getFeishuCliRuntimeAdditionalContext(this.loadedConfig?.feishu.cli?.enabled === true);
  }

  protected override validateConfig(rawConfig: unknown): asserts rawConfig is FeishuConfig {
    validateFeishuConfig(rawConfig);
  }

  protected override buildTransportFromConfig(config: FeishuConfig): WebSocketTransport {
    return new WebSocketTransport({
      url: config.dotcraft.wsUrl,
      token: config.dotcraft.token ?? "",
    });
  }

  override async startWithContext(context: WorkspaceContext): Promise<void> {
    await super.startWithContext(context);
    if (this.getStatus() !== "ready" || !this.loadedConfig) {
      return;
    }

    const config = this.loadedConfig;
    this.cardTitle = config.feishu.cardTitle ?? "DotCraft";
    this.approvalTimeoutMs = config.feishu.approvalTimeoutMs ?? 120000;
    this.streamingEnabled = config.feishu.streaming?.enabled !== false;
    configureTextMergeDebug(config.feishu.debug?.textMerge);
    this.feishu = new FeishuClient(config.feishu);
    this.loadingIcon = new FeishuLoadingIcon(this.feishu, { stateDir: resolveModuleStatePath(context) });
    if (config.feishu.cli?.enabled === true) {
      this.cliTool = await FeishuCliTool.create(
        context.workspaceRoot,
        config.feishu,
        () => this.getFeishuClient().getTenantAccessToken(),
      );
    }

    try {
      const botInfo = await this.feishu.probeBot();
      const handlers = createFeishuEventHandlers({
        adapter: this,
        client: this.feishu,
        bot: botInfo,
        config: config.feishu,
        chatInfo: new FeishuChatInfoCache(this.feishu),
      });
      this.eventAbortController = new AbortController();
      void this.feishu.startEventStream(handlers, this.eventAbortController.signal).catch((error) => {
        this.setStatus("stopped", this.runtimeError("unexpectedRuntimeFailure", errorMessage(error)));
      });
    } catch (error) {
      this.setStatus("stopped", this.runtimeError("startupFailed", errorMessage(error)));
    }
  }

  override async stop(): Promise<void> {
    this.cliTool?.stop();
    this.cliTool = undefined;
    this.resolveAllPendingUserInputs(emptyUserInputResponse());
    this.eventAbortController?.abort();
    this.eventAbortController = undefined;
    this.feishu?.stopEventStream();
    await this.turnCards.stopAll();
    await super.stop();
  }

  private runtimeError(code: ModuleError["code"], message: string): ModuleError {
    return {
      code,
      message,
      timestamp: new Date().toISOString(),
    };
  }

  private getFeishuClient(): FeishuClient {
    if (!this.feishu) {
      throw new Error("Feishu client is not initialized. Call startWithContext() first.");
    }
    return this.feishu;
  }

  async sendCardToConversation(message: ParsedInboundMessage, card: Record<string, unknown>): Promise<FeishuSendResult> {
    this.router.noteInbound(message.channelContext, message.messageId);
    return await this.router.sendCard(message.channelContext, card);
  }

  protected override buildSocialTarget(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
  ): SocialChannelTarget | null {
    const target = parseFeishuSocialTarget(conversationTargetBase(channelContext));
    if (!target) return null;
    const platformUserId = String(sender.senderId ?? opts.userId ?? "");
    const displayName = typeof sender.senderName === "string" && sender.senderName.trim()
      ? sender.senderName.trim()
      : null;
    return {
      channelName: "feishu",
      conversationKind: target.conversationKind,
      conversationId: target.conversationId,
      deliveryTarget: target.deliveryTarget,
      displayName: target.conversationKind === "group"
        ? `Feishu group ${target.conversationId}`
        : displayName ?? `Feishu user ${target.conversationId}`,
      boundBy: platformUserId
        ? {
          platformUserId,
          displayName,
        }
        : null,
    };
  }

  async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    logInfo("outbound.deliver.start", {
      target: shortId(target),
      contentChars: content.length,
    });
    try {
      await sendReplyCards(this.router, target, content, this.cardTitle);
      logInfo("outbound.deliver.success", {
        target: shortId(target),
      });
      return true;
    } catch (error) {
      logError("outbound.deliver.failed", {
        target: shortId(target),
        message: errorMessage(error),
      });
      return false;
    }
  }

  protected getDeliveryCapabilities(): Record<string, unknown> | null {
    return {
      structuredDelivery: true,
      media: {
        file: {
          maxBytes: 30 * 1024 * 1024,
          supportsHostPath: false,
          supportsUrl: false,
          supportsBase64: true,
          supportsCaption: true,
        },
      },
    };
  }

  protected override getChannelTools(): ChannelToolDescriptor[] | null {
    const tools: ChannelToolDescriptor[] = [
      {
        name: "FeishuSendFileToCurrentChat",
        description: "Send a real file attachment to the current Feishu chat.",
        requiresChatContext: true,
        approval: {
          kind: "file",
          targetArgument: "filePath",
          operation: "read",
        },
        display: {
          icon: "\u{1F4CE}",
          title: "Send file to current Feishu chat",
        },
        inputSchema: {
          type: "object",
          properties: {
            filePath: { type: "string" },
            fileName: { type: "string" },
            caption: { type: "string" },
          },
          required: ["filePath"],
        },
      },
    ];
    tools.push(...getFeishuCliToolDescriptors(this.loadedConfig?.feishu.cli?.enabled === true));
    return tools;
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

    if (kind === "file") {
      const result = await this.deliverFileMessage(target, message, {
        source: "structured",
        metadata,
      });
      return result;
    }

    return {
      delivered: false,
      errorCode: "UnsupportedDeliveryKind",
      errorMessage: `Feishu example does not implement structured '${kind}' delivery yet.`,
    };
  }

  protected override async onToolCall(
    request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const tool = String(request.tool ?? "");
    const args = (request.arguments as Record<string, unknown>) ?? {};
    const context = (request.context as Record<string, unknown>) ?? {};
    const target = String(context.channelContext ?? context.groupId ?? "");
    if (tool === "FeishuCli") {
      return this.cliTool
        ? await this.cliTool.invoke(args)
        : {
          success: false,
          errorCode: "FeishuCliDisabled",
          errorMessage: "The official Feishu CLI is disabled for this Channel.",
        };
    }
    if (tool !== "FeishuSendFileToCurrentChat") {
      return {
        success: false,
        errorCode: "UnsupportedTool",
        errorMessage: `Unknown tool '${tool}'.`,
      };
    }

    if (!target) {
      return {
        success: false,
        errorCode: "MissingChatContext",
        errorMessage: "Current tool call does not contain a Feishu chat target.",
      };
    }

    const filePath = String(args.filePath ?? "");
    const fileName = String(args.fileName ?? "");
    const caption = String(args.caption ?? "");
    if (!filePath) {
      return {
        success: false,
        errorCode: "MissingFilePath",
        errorMessage: "Feishu file sending requires a filePath.",
      };
    }

    try {
      const prepared = await prepareMediaBytes(
        mediaSourceFromToolPath(filePath, { fieldName: "filePath" }),
        {
          fileName: fileName || undefined,
          maxBytes: 30 * 1024 * 1024,
        },
      );
      const effectiveFileName = prepared.fileName;
      const sendResult = await this.router.sendFile(target, {
        fileName: effectiveFileName,
        data: prepared.bytes,
        mediaType: prepared.mediaType,
      });
      if (caption) {
        await this.sendCaptionCard(target, caption, {
          target,
          fileName: effectiveFileName,
          source: "tool",
        });
      }

      return {
        success: true,
        contentItems: [{ type: "text", text: `Sent ${effectiveFileName} to the current chat.` }],
        structuredContent: {
          delivered: true,
          fileName: effectiveFileName,
          remoteMessageId: sendResult.messageId,
          fileKey: sendResult.fileKey,
        },
      };
    } catch (error) {
      return {
        success: false,
        errorCode: "AdapterToolCallFailed",
        errorMessage: errorMessage(error),
      };
    }
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const requestId = String(request.requestId ?? "");
    const threadId = String(request.threadId ?? "");
    const approvalType = String(request.approvalType ?? "");
    const operation = String(request.operation ?? "");
    const target = String(request.target ?? "");
    const reason = String(request.reason ?? "");
    const channelTarget = this.threadContextMap.get(threadId);
    if (!channelTarget || !requestId) {
      logWarn("approval.request.invalid_context", {
        requestId: shortId(requestId),
        threadId: shortId(threadId),
      });
      return DECISION_DECLINE;
    }

    const timeoutSeconds = Math.max(1, Math.round(this.approvalTimeoutMs / 1000));
    const card = buildApprovalCard({
      requestId,
      approvalType,
      operation,
      target,
      reason,
      timeoutSeconds,
      cardTitle: this.cardTitle,
    });
    const sent = await this.router.sendCard(channelTarget, card);
    logInfo("approval.card_sent", {
      requestId: shortId(requestId),
      threadId: shortId(threadId),
      timeoutSec: timeoutSeconds,
      channelTarget: shortId(channelTarget),
      messageId: shortId(sent.messageId),
    });

    return new Promise<string>((resolve) => {
      const timer = setTimeout(() => {
        const waiter = this.approvalWaiters.get(requestId);
        this.approvalWaiters.delete(requestId);
        if (waiter?.messageId) {
          void this.tryUpdateApprovalCard(waiter.messageId, buildApprovalTimeoutCard({ requestId, timeoutSeconds }));
        }
        logWarn("approval.timeout", {
          requestId: shortId(requestId),
          decision: DECISION_CANCEL,
        });
        resolve(DECISION_CANCEL);
      }, this.approvalTimeoutMs);
      this.approvalWaiters.set(requestId, {
        resolve: (decision: string) => {
          clearTimeout(timer);
          const waiter = this.approvalWaiters.get(requestId);
          this.approvalWaiters.delete(requestId);
          if (waiter?.messageId) {
            void this.tryUpdateApprovalCard(
              waiter.messageId,
              buildApprovalResolvedCard({
                requestId,
                decision,
              }),
            );
          }
          logInfo("approval.resolved", {
            requestId: shortId(requestId),
            decision,
          });
          resolve(decision);
        },
        timer,
        threadId,
        channelTarget,
        messageId: sent.messageId,
        timeoutSeconds,
      });
    });
  }

  protected override async onUserInputRequest(request: Record<string, unknown>): Promise<UserInputResponse> {
    const requestId = String(request.requestId ?? "");
    const threadId = String(request.threadId ?? "");
    const channelTarget = this.threadContextMap.get(threadId);
    if (!channelTarget || !requestId) {
      logWarn("user_input.request.invalid_context", {
        requestId: shortId(requestId),
        threadId: shortId(threadId),
      });
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
        threadId,
        channelTarget,
        questionPosition: { index: step.questionIndex, count: step.questionCount },
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
    threadId: string;
    channelTarget: string;
    questionPosition?: QuestionPosition;
  }): Promise<UserInputResponse> {
    const requestId = String(params.request.requestId ?? "");
    if (!requestId) {
      return emptyUserInputResponse();
    }

    const previousRequestId = this.userInputRequestByChannelTarget.get(params.channelTarget);
    if (previousRequestId) {
      this.resolvePendingUserInput(previousRequestId, emptyUserInputResponse());
    }

    const sent = await this.router.sendCard(
      params.channelTarget,
      buildUserInputCard({
        request: params.request,
        cardTitle: this.cardTitle,
        questionPosition: params.questionPosition,
      }),
    );
    logInfo("user_input.card_sent", {
      requestId: shortId(requestId),
      threadId: shortId(params.threadId),
      channelTarget: shortId(params.channelTarget),
      messageId: shortId(sent.messageId),
    });

    return await new Promise<UserInputResponse>((resolve) => {
      this.userInputWaiters.set(requestId, {
        resolve,
        threadId: params.threadId,
        channelTarget: params.channelTarget,
        messageId: sent.messageId,
        request: params.request,
        questionPosition: params.questionPosition,
      });
      this.userInputRequestByChannelTarget.set(params.channelTarget, requestId);
    });
  }

  private async tryUpdateApprovalCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    try {
      await updateCard(this.getFeishuClient(), messageId, card);
      logInfo("approval.card_updated", {
        messageId: shortId(messageId),
      });
    } catch (error) {
      logWarn("approval.card_update_failed", {
        messageId: shortId(messageId),
        message: errorMessage(error),
      });
    }
  }

  private async upsertTurnTranscriptCard(
    threadId: string,
    turnId: string,
    channelTarget: string,
    transcriptText: string,
    isFinal: boolean,
  ): Promise<void> {
    const state = this.turnCards.getOrInit(threadId, turnId, channelTarget);
    state.accumulatedText = transcriptText;
    state.mode = "fallback";
    this.turnCards.markActive(threadId, turnId, channelTarget);
    const card = buildTranscriptCard(state.accumulatedText, isFinal, this.cardTitle);
    const sent = await createOrUpdateCard(
      this.getFeishuClient(),
      this.router,
      channelTarget,
      card,
      state.messageId,
    );
    state.messageId = sent.messageId;
    if (isFinal) {
      this.turnCards.clear(threadId, turnId);
    }
  }

  private async sendCaptionCard(
    channelTarget: string,
    caption: string,
    logContext: { target: string; fileName: string; source: "tool" | "structured" },
  ): Promise<void> {
    const normalized = caption.trim();
    if (!normalized) return;
    const card = buildFileCaptionCard(normalized, logContext.fileName);
    await this.router.sendCard(channelTarget, card);
    logInfo("outbound.send.file.caption_card_sent", {
      source: logContext.source,
      target: shortId(logContext.target),
      fileName: logContext.fileName,
      captionChars: normalized.length,
    });
  }

  protected override async onSegmentCompleted(
    threadId: string,
    turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    if (!segmentText.trim()) return;
    logInfo(isFinal ? "turn.completed_segment" : "turn.progress", {
      threadId: shortId(threadId),
      turnId: shortId(turnId),
      replyChars: segmentText.length,
      isFinal,
    });
    const state = this.turnCards.getOrInit(threadId, turnId, channelContext);
    if (state.mode === "native" || state.mode === "nativeFinalized") return;
    const transcriptText = state.hasProgress
      ? state.accumulatedText
      : composeTranscriptMarkdown([state.accumulatedText, segmentText]);
    await this.upsertTurnTranscriptCard(threadId, turnId, channelContext, transcriptText, isFinal);
  }

  protected override async onReplyProgress(
    threadId: string,
    turnId: string,
    replyParts: readonly string[],
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    const state = this.turnCards.getOrInit(threadId, turnId, channelContext);
    state.accumulatedText = composeTranscriptMarkdown(replyParts);
    state.hasProgress = true;
    this.turnCards.markActive(threadId, turnId, channelContext);

    if (!this.streamingEnabled || state.mode === "fallback" || !state.accumulatedText.trim()) return;
    if (isFinal) {
      if (state.mode !== "native" || !state.streamer) return;
      const completed = await state.streamer.complete(state.accumulatedText);
      state.mode = completed ? "nativeFinalized" : "fallback";
      return;
    }

    const streamer = await this.turnCards.ensureStreamer(state, threadId, turnId);
    const updated = await streamer.update(state.accumulatedText);
    state.mode = updated || streamer.hasVisibleCard ? "native" : "fallback";
  }

  protected override async consumeTurnEventStream(
    eventStream: Parameters<ModuleChannelAdapter["consumeTurnEventStream"]>[0],
    threadId: string,
    turnId: string,
    channelContext: string,
  ): Promise<void> {
    await this.turnCards.beginTurn(threadId, turnId, channelContext);
    try {
      await super.consumeTurnEventStream(eventStream, threadId, turnId, channelContext);
    } finally {
      await this.turnCards.endTurn(threadId, turnId);
    }
  }

  protected override async onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (!replyText.trim() || segmentsWereDelivered) {
      await this.turnCards.endTurn(threadId, turnId);
      return;
    }
    const state = this.turnCards.get(threadId, turnId);
    const transcriptText = state?.accumulatedText.trim() ? state.accumulatedText : replyText;
    await this.upsertTurnTranscriptCard(threadId, turnId, channelContext, transcriptText, true);
  }

  protected override async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    await this.turnCards.endTurn(threadId, turnId);
    await super.onTurnFailed(threadId, turnId, error);
  }

  protected override async onTurnCancelled(threadId: string, turnId: string): Promise<void> {
    await this.turnCards.endTurn(threadId, turnId);
    await super.onTurnCancelled(threadId, turnId);
  }

  protected override async onActivity(
    threadId: string,
    turnId: string,
    activity: TurnItemActivity,
    _channelContext: string,
  ): Promise<void> {
    const state = this.turnCards.get(threadId, turnId);
    if (state?.mode !== "native") return;
    state.streamer?.noteActivity(activity);
  }

  protected override onThreadContextBound(threadId: string, channelContext: string): void {
    this.threadContextMap.set(threadId, channelContext);
  }

  protected override onThreadResolveEvent(event: ThreadResolveEvent): void {
    if (event.action === "cache_invalidated") {
      logWarn("thread.cache_invalidated", {
        identityKey: shortId(event.identityKey),
        threadId: shortId(event.threadId ?? ""),
      });
      return;
    }
    if (
      event.action === "cache_hit" ||
      event.action === "resumed_from_cache" ||
      event.action === "listed_active" ||
      event.action === "listed_resumed" ||
      event.action === "created" ||
      event.action === "force_fresh_created"
    ) {
      logInfo("thread.resolve_action", {
        action: event.action,
        threadId: shortId(event.threadId ?? ""),
        identityKey: shortId(event.identityKey),
      });
    }
  }

  async handleInboundMessage(message: ParsedInboundMessage): Promise<void> {
    logInfo("inbound.handle.start", {
      messageId: shortId(message.messageId),
      kind: message.kind,
      chatType: message.chatType,
      topic: message.threadKey ? shortId(message.threadKey) : "",
    });
    this.router.noteInbound(message.channelContext, message.messageId);
    if (await this.tryResolvePendingUserInputFromText(message.channelContext, message.text)) {
      return;
    }
    if (isNewCommand(message.text)) {
      await this.newThread(message.threadUserId, message.channelContext);
      await this.router.sendCard(
        message.channelContext,
        buildNewConversationCard(),
      );
      logInfo("inbound.command.new_thread", {
        messageId: shortId(message.messageId),
        channelContext: shortId(message.channelContext),
      });
      return;
    }

    await this.handleMessage({
      userId: message.threadUserId,
      userName: message.userName,
      text: message.text,
      channelContext: message.channelContext,
      workspacePath: this.defaultWorkspacePath,
      sender: {
        senderId: message.userId,
        senderName: message.userName,
        ...(message.chatType === "group" ? { groupId: message.channelContext } : {}),
      },
      inputParts: message.parts.length ? message.parts : undefined,
      omitSenderGroupId: message.chatType !== "group",
    });
  }

  handleCardAction(event: FeishuCardActionEvent): boolean {
    const value = parseActionValue(event.action?.value);
    if (value?.kind === "userInput") {
      return this.handleUserInputCardAction(event, value);
    }
    if (!value || value.kind !== "approval") {
      const kindStr =
        value && typeof value === "object" && "kind" in value
          ? String((value as Record<string, unknown>).kind ?? "")
          : "";
      logWarn("approval.action_not_approval_kind", {
        kind: kindStr || "missing",
      });
      return false;
    }
    const requestId = String(value.requestId ?? "");
    const decision = String(value.decision ?? "");
    const waiter = this.approvalWaiters.get(requestId);
    if (!waiter) {
      logWarn("approval.action_no_waiter", {
        requestId: shortId(requestId),
        openMessageId: shortId(String(event.context?.open_message_id ?? "")),
      });
      return false;
    }
    const openMessageId = String(event.context?.open_message_id ?? "");
    if (openMessageId && waiter.messageId && openMessageId !== waiter.messageId) {
      logWarn("approval.action_message_mismatch", {
        requestId: shortId(requestId),
        expectedMessageId: shortId(waiter.messageId),
        actualMessageId: shortId(openMessageId),
      });
      return false;
    }
    waiter.resolve(decision || DECISION_CANCEL);
    logInfo("approval.action_resolved", {
      requestId: shortId(requestId),
      decision: decision || DECISION_CANCEL,
      messageId: shortId(openMessageId || waiter.messageId),
    });
    return true;
  }

  hasPendingUserInput(channelContext: string): boolean {
    return this.userInputRequestByChannelTarget.has(channelContext);
  }

  async tryHandlePendingUserInputMessage(message: ParsedInboundMessage): Promise<boolean> {
    return await this.tryResolvePendingUserInputFromText(message.channelContext, message.text);
  }

  private handleUserInputCardAction(event: FeishuCardActionEvent, value: Record<string, unknown>): boolean {
    const requestId = String(value.requestId ?? "");
    const optionIndex = Number(value.optionIndex);
    const waiter = this.userInputWaiters.get(requestId);
    if (!waiter || !Number.isInteger(optionIndex)) {
      logWarn("user_input.action_no_waiter", {
        requestId: shortId(requestId),
        openMessageId: shortId(String(event.context?.open_message_id ?? "")),
      });
      return false;
    }
    const openMessageId = String(event.context?.open_message_id ?? "");
    if (openMessageId && waiter.messageId && openMessageId !== waiter.messageId) {
      logWarn("user_input.action_message_mismatch", {
        requestId: shortId(requestId),
        expectedMessageId: shortId(waiter.messageId),
        actualMessageId: shortId(openMessageId),
      });
      return false;
    }

    const response = userInputResponseForSingleChoice(waiter.request, optionIndex);
    const question = normalizeUserInputQuestions(waiter.request)[0];
    const answerSummary = question?.options[optionIndex]?.label ?? "";
    this.resolvePendingUserInput(requestId, response, answerSummary);
    return true;
  }

  private async tryResolvePendingUserInputFromText(channelTarget: string, text: string): Promise<boolean> {
    const requestId = this.userInputRequestByChannelTarget.get(channelTarget);
    if (!requestId) return false;
    const waiter = this.userInputWaiters.get(requestId);
    if (!waiter) {
      this.userInputRequestByChannelTarget.delete(channelTarget);
      return false;
    }

    const response = userInputResponseFromText(waiter.request, text);
    if (!response) {
      const sent = await this.router.sendCard(
        channelTarget,
        buildUserInputCard({
          request: waiter.request,
          cardTitle: this.cardTitle,
          questionPosition: waiter.questionPosition,
        }),
      ).catch((error) => {
        logWarn("user_input.reprompt_failed", {
          requestId: shortId(requestId),
          message: errorMessage(error),
        });
        return null;
      });
      if (sent?.messageId) {
        waiter.messageId = sent.messageId;
      }
      return true;
    }

    this.resolvePendingUserInput(requestId, response);
    return true;
  }

  private resolvePendingUserInput(
    requestId: string,
    response: UserInputResponse,
    answerSummary?: string,
  ): void {
    const waiter = this.userInputWaiters.get(requestId);
    if (!waiter) return;
    this.userInputWaiters.delete(requestId);
    if (this.userInputRequestByChannelTarget.get(waiter.channelTarget) === requestId) {
      this.userInputRequestByChannelTarget.delete(waiter.channelTarget);
    }
    if (waiter.messageId) {
      void this.tryUpdateUserInputCard(
        waiter.messageId,
        buildUserInputResolvedCard({ requestId, answerSummary }),
      );
    }
    waiter.resolve(response);
  }

  private resolveAllPendingUserInputs(response: UserInputResponse): void {
    for (const requestId of [...this.userInputWaiters.keys()]) {
      this.resolvePendingUserInput(requestId, response);
    }
  }

  private async tryUpdateUserInputCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    try {
      await updateCard(this.getFeishuClient(), messageId, card);
      logInfo("user_input.card_updated", {
        messageId: shortId(messageId),
      });
    } catch (error) {
      logWarn("user_input.card_update_failed", {
        messageId: shortId(messageId),
        message: errorMessage(error),
      });
    }
  }

  protected override onThreadsArchived(_identityKey: string, archivedThreadIds: string[]): void {
    for (const threadId of archivedThreadIds) {
      const channelContext = this.threadContextMap.get(threadId);
      if (channelContext) this.router.forget(channelContext);
      this.threadContextMap.delete(threadId);
      this.turnCards.clearThread(threadId);
    }
  }

  override async newThread(userId: string, channelContext = ""): Promise<void> {
    const identityKey = this.identityKey(userId, channelContext);
    const archivedIds = await this.resetIdentityThreads(userId, channelContext);
    this.onThreadsArchived(identityKey, archivedIds);
  }

  private async deliverFileMessage(
    target: string,
    message: Record<string, unknown>,
    context: {
      source: "structured" | "tool";
      metadata: Record<string, unknown>;
    },
  ): Promise<Record<string, unknown>> {
    const caption = String(message.caption ?? "");
    const fileName = String(message.fileName ?? "attachment");

    try {
      const file = await resolveOutboundFilePayload(message, fileName);
      logInfo("outbound.send.file", {
        source: context.source,
        target: shortId(target),
        fileName: file.fileName,
        bytes: file.data.length,
      });
      const sendResult = await this.router.sendFile(target, file);
      if (caption) {
        await this.sendCaptionCard(target, caption, {
          target,
          fileName: file.fileName,
          source: context.source,
        });
      }

      return {
        delivered: true,
        remoteMessageId: sendResult.messageId,
        remoteMediaId: sendResult.fileKey,
      };
    } catch (error) {
      logError("outbound.send.file.failed", {
        source: context.source,
        target: shortId(target),
        fileName,
        message: errorMessage(error),
      });
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: errorMessage(error),
      };
    }
  }
}

function parseFeishuSocialTarget(channelContext: string): {
  conversationKind: "group" | "user";
  conversationId: string;
  deliveryTarget: string;
} | null {
  const target = channelContext.trim();
  if (!target) return null;
  if (target.startsWith("group:")) {
    const id = target.slice("group:".length).trim();
    return id ? { conversationKind: "group", conversationId: id, deliveryTarget: target } : null;
  }
  if (target.startsWith("dm:")) {
    const id = target.slice("dm:".length).trim();
    return id ? { conversationKind: "user", conversationId: id, deliveryTarget: target } : null;
  }
  return { conversationKind: "group", conversationId: target, deliveryTarget: target };
}

function isNewCommand(text: string): boolean {
  return /^\s*\/new\s*$/i.test(text.trim());
}

function parseActionValue(value: Record<string, unknown> | string | undefined): Record<string, unknown> | null {
  if (!value) return null;
  if (typeof value === "object") return value;
  try {
    return JSON.parse(value) as Record<string, unknown>;
  } catch {
    return null;
  }
}

async function resolveOutboundFilePayload(
  message: Record<string, unknown>,
  fallbackFileName: string,
): Promise<{
  fileName: string;
  data: Buffer;
  mediaType?: string;
}> {
  const source = (message.source as Record<string, unknown> | undefined) ?? {};
  const fileName = String(message.fileName ?? fallbackFileName).trim() || "attachment";
  const mediaType = String(message.mediaType ?? "").trim() || undefined;
  const prepared = await prepareMediaBytes(source, {
    fileName,
    mediaType,
    maxBytes: 30 * 1024 * 1024,
  });
  return {
    fileName: prepared.fileName,
    data: prepared.bytes,
    mediaType: prepared.mediaType,
  };
}
