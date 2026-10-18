import {
  DECISION_DECLINE,
  parseApprovalReplyDecision,
  type ChannelTarget,
  type CommandChoice,
  type CommandChoiceContext,
} from "@dotcraft/channel";
import {
  WebSocketTransport,
  type ThreadResolveEvent,
} from "@dotcraft/channel/runtime";
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
  type ChannelChatDescriptor,
  type ChannelDeliveryCapabilities,
  type ChannelToolDescriptor,
  type ChannelAdapterMessageOpts,
  type ChannelTurn,
  type ChannelTurnOutcome,
  type TurnItemActivity,
  type ModuleError,
  type UserInputResponse,
  type WorkspaceContext,
} from "@dotcraft/channel";
import { FeishuApprovalCards } from "./approval-cards.js";
import {
  buildCommandChoicesCard,
  buildUserAuthCard,
  buildUserAuthNoticeCard,
  buildUserInputCard,
  buildUserInputResolvedCard,
  type FeishuUserAuthNotice,
  type QuestionPosition,
} from "./card-builder.js";
import { updateCard } from "./card-sender.js";
import {
  commandChoiceMessageOptions,
  feishuMessageOptions,
  parseCommandChoiceSelection,
} from "./command-choices.js";
import { FEISHU_DELIVERY_CAPABILITIES, FeishuChannelSender } from "./channel-send.js";
import { FeishuChatInfoCache } from "./chat-info-cache.js";
import {
  conversationTargetBase,
  describeConversation,
  formatConversationTarget,
  parseConversationTarget,
} from "./conversation-target.js";
import { createFeishuEventHandlers } from "./event-handler.js";
import {
  type FeishuCardActionEvent,
  type FeishuConfig,
  type FeishuSendResult,
  type ParsedInboundMessage,
} from "./feishu-types.js";
import { FeishuClient } from "./feishu-client.js";
import {
  FeishuUserIdentity,
  FeishuUserIdentityError,
  type FeishuDeviceAuthorization,
} from "./feishu-user-identity.js";
import { FeishuLoadingIcon } from "./loading-icon.js";
import { FeishuMediaDelivery } from "./media-delivery.js";
import { FeishuOutboundRouter } from "./outbound-router.js";
import { FeishuStatusCards } from "./status-card.js";
import {
  FeishuCliTool,
  getFeishuCliRuntimeAdditionalContext,
  getFeishuCliToolDescriptors,
} from "./feishu-cli-tool.js";
import { errorMessage, logInfo, logWarn, shortId } from "./logging.js";

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
  if (feishu.cli !== undefined
      && (typeof feishu.cli !== "object" || feishu.cli === null || Array.isArray(feishu.cli))) {
    throw new ConfigValidationError("feishu.cli must be an object.", ["feishu.cli"]);
  }
  const cli = feishu.cli as Record<string, unknown> | undefined;
  if (cli?.enabled !== undefined && typeof cli.enabled !== "boolean") {
    throw new ConfigValidationError("feishu.cli.enabled must be a boolean.", ["feishu.cli.enabled"]);
  }
  if (feishu.adminUsers !== undefined
      && (!Array.isArray(feishu.adminUsers) || feishu.adminUsers.some((id) => typeof id !== "string"))) {
    throw new ConfigValidationError("feishu.adminUsers must be a string array.", ["feishu.adminUsers"]);
  }
  if (cli?.userScopes !== undefined
      && (!Array.isArray(cli.userScopes) || cli.userScopes.some((scope) => typeof scope !== "string"))) {
    throw new ConfigValidationError("feishu.cli.userScopes must be a string array.", ["feishu.cli.userScopes"]);
  }
  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export class FeishuAdapter extends ModuleChannelAdapter<FeishuConfig> {
  private feishu: FeishuClient | undefined;
  private cardTitle = "DotCraft";
  private adminUsers = new Set<string>();
  private cliTool: FeishuCliTool | undefined;
  private userIdentity: FeishuUserIdentity | undefined;
  private eventAbortController: AbortController | undefined;
  private loadingIcon: FeishuLoadingIcon | undefined;
  private readonly router = new FeishuOutboundRouter(() => this.getFeishuClient());
  private readonly media = new FeishuMediaDelivery(this.router);
  private readonly sender = new FeishuChannelSender({
    router: this.router,
    media: this.media,
    cardTitle: () => this.cardTitle,
  });
  private readonly threadContextMap = new Map<string, string>();
  private readonly statusCards = new FeishuStatusCards({
    sendCard: (target, card) => this.router.sendCard(target, card),
    updateCard: (messageId, card) => updateCard(this.getFeishuClient(), messageId, card),
    loadingIconImgKey: () => this.loadingIcon?.imgKey() ?? Promise.resolve(undefined),
  });
  private readonly approvals = new FeishuApprovalCards({
    sendCard: (target, card) => this.router.sendCard(target, card),
    updateCard: (messageId, card) => updateCard(this.getFeishuClient(), messageId, card),
    cardTitle: () => this.cardTitle,
  });
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
      ["item/reasoning/delta", "subagent/progress", "item/usage/delta", "plan/updated"],
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
    this.approvals.timeoutMs = config.feishu.approvalTimeoutMs ?? 120000;
    this.adminUsers = new Set((config.feishu.adminUsers ?? []).map((id) => id.trim()));
    this.feishu = new FeishuClient(config.feishu);
    const stateDir = resolveModuleStatePath(context);
    this.loadingIcon = new FeishuLoadingIcon(this.feishu, { stateDir });
    const userIdentity = new FeishuUserIdentity({
      appId: config.feishu.appId,
      appSecret: config.feishu.appSecret,
      brand: config.feishu.brand ?? "feishu",
      scopes: config.feishu.cli?.userScopes ?? [],
      stateDir,
    });
    this.userIdentity = userIdentity;
    if (config.feishu.cli?.enabled === true) {
      this.cliTool = await FeishuCliTool.create(
        context.workspaceRoot,
        config.feishu,
        () => this.getFeishuClient().getTenantAccessToken(),
        () => userIdentity.getAccessToken(),
      );
    }

    try {
      const botInfo = await this.feishu.probeBot();
      const handlers = createFeishuEventHandlers({
        adapter: this,
        client: this.feishu,
        bot: botInfo,
        config: config.feishu,
        craftPath: context.craftPath,
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
    await this.statusCards.stopAll();
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

  protected override buildChannelTarget(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
  ): ChannelTarget | null {
    const target = parseFeishuChannelTarget(conversationTargetBase(channelContext));
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
    return (await this.sender.sendText(target, content)).delivered === true;
  }

  protected getDisplayName(): string {
    return "Feishu";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return FEISHU_DELIVERY_CAPABILITIES;
  }

  protected getChatDescriptor(_opts: ChannelAdapterMessageOpts, channelContext: string): ChannelChatDescriptor {
    return describeConversation(channelContext);
  }

  protected override getChannelTools(): ChannelToolDescriptor[] | null {
    const tools: ChannelToolDescriptor[] = getFeishuCliToolDescriptors(this.loadedConfig?.feishu.cli?.enabled === true);
    if (this.userIdentity?.isConfigured() === true) {
      tools.push({
        name: "FeishuAuthorizeUser",
        description:
          "Start Feishu user-identity authorization when a call fails with FeishuCliUserAuthorizationRequired. "
          + "The link is delivered privately to whoever asked, never to the current group. "
          + "Does not help with FeishuCliUserIdentityUnavailable, which needs an administrator to configure scopes.",
        requiresChatContext: true,
        inputSchema: {
          type: "object",
          properties: {},
          additionalProperties: false,
        },
      });
    }
    return tools;
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    _metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    return await this.sender.send(target, message);
  }

  protected override async onToolCall(
    request: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const tool = String(request.tool ?? "");
    const args = (request.arguments as Record<string, unknown>) ?? {};
    const context = (request.context as Record<string, unknown>) ?? {};
    if (tool === "FeishuCli") {
      return this.cliTool
        ? await this.cliTool.invoke(args)
        : {
          success: false,
          errorCode: "FeishuCliDisabled",
          errorMessage: "The official Feishu CLI is disabled for this Channel.",
        };
    }
    if (tool === "FeishuAuthorizeUser") {
      return await this.authorizeUserFromTool(String(context.senderId ?? ""));
    }
    return {
      success: false,
      errorCode: "UnsupportedTool",
      errorMessage: `Unknown tool '${tool}'.`,
    };
  }

  async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const requestId = String(request.requestId ?? "");
    const threadId = String(request.threadId ?? "");
    const channelTarget = this.threadContextMap.get(threadId);
    if (!channelTarget || !requestId) {
      logWarn("approval.request.invalid_context", {
        requestId: shortId(requestId),
        threadId: shortId(threadId),
      });
      return DECISION_DECLINE;
    }
    return await this.approvals.request(channelTarget, request);
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

  protected override async onTurnStarted(turn: ChannelTurn): Promise<void> {
    this.statusCards.start(turn);
  }

  protected override async onActivity(turn: ChannelTurn, _activity: TurnItemActivity): Promise<void> {
    this.statusCards.working(turn);
  }

  protected override async onTurnRetrying(turn: ChannelTurn): Promise<void> {
    this.statusCards.retrying(turn);
  }

  protected override async onTurnEnded(turn: ChannelTurn, outcome: ChannelTurnOutcome): Promise<void> {
    (outcome.status === "failed" ? logWarn : logInfo)("turn.ended", {
      threadId: shortId(turn.threadId),
      turnId: shortId(turn.turnId),
      status: outcome.status,
      ...(outcome.status === "failed" ? { error: outcome.error } : {}),
    });
    await this.statusCards.end(turn, outcome);
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
      event.action === "listed_active" ||
      event.action === "created"
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
    const approvalDecision = parseApprovalReplyDecision(message.text, { slashAliasesOnly: true });
    if (approvalDecision && this.approvals.resolveInChat(message.channelContext, approvalDecision)) {
      return;
    }
    if (isUserAuthCommand(message.text)) {
      await this.handleUserAuthCommand(message);
      return;
    }

    await this.handleMessage({
      ...feishuMessageOptions(
        { senderId: message.userId, senderName: message.userName, senderRole: this.senderRole(message.userId) },
        { threadUserId: message.threadUserId, channelContext: message.channelContext, isGroup: message.chatType === "group" },
      ),
      text: message.text,
      workspacePath: this.defaultWorkspacePath,
      inputParts: message.parts.length ? message.parts : undefined,
    });
  }

  protected override async renderCommandChoices(
    target: string,
    choices: CommandChoice[],
    _context: CommandChoiceContext,
  ): Promise<void> {
    await this.router.sendCard(target, buildCommandChoicesCard(choices, target));
  }

  private senderRole(openId: string): "admin" | "user" {
    return this.adminUsers.has(openId) ? "admin" : "user";
  }

  private handleCommandChoiceAction(event: FeishuCardActionEvent, value: Record<string, unknown>): boolean {
    const selection = parseCommandChoiceSelection(event, value);
    if (!selection) {
      logWarn("command_choice.action_invalid", {
        openMessageId: shortId(String(event.context?.open_message_id ?? "")),
      });
      return false;
    }
    void this.executeCommandChoice(
      selection.command,
      commandChoiceMessageOptions(selection, this.senderRole(selection.operatorId)),
    );
    return true;
  }

  /** Authorization binds one operator account, so it only runs where the sender is unambiguous. */
  private async handleUserAuthCommand(message: ParsedInboundMessage): Promise<void> {
    const identity = this.userIdentity;
    const target = message.channelContext;
    const notice = async (
      kind: FeishuUserAuthNotice,
      params: Record<string, string> = {},
    ): Promise<void> => {
      await this.router.sendCard(target, buildUserAuthNoticeCard(kind, params));
    };
    if (!identity?.isConfigured()) {
      await notice("authDisabledBody");
      return;
    }
    if (message.chatType !== "p2p") {
      await notice("authDirectOnlyBody");
      return;
    }

    const action = userAuthAction(message.text);
    if (action === "revoke") {
      identity.clearBinding();
      logInfo("user_identity.binding.cleared", {});
      await notice("authRevokedBody");
      return;
    }
    if (action === "status") {
      const binding = identity.getBinding();
      await (binding
        ? notice("authStatusBound", { name: binding.name || binding.openId })
        : notice("authStatusUnbound"));
      return;
    }

    if (!(await this.startUserAuthorization(identity, target))) {
      await notice("authFailedBody");
    }
  }

  private async startUserAuthorization(identity: FeishuUserIdentity, target: string): Promise<boolean> {
    let authorization: FeishuDeviceAuthorization;
    try {
      authorization = await identity.requestAuthorization();
    } catch (error) {
      logWarn("user_identity.authorization.request_failed", {
        code: error instanceof FeishuUserIdentityError ? error.code : "unknown",
      });
      return false;
    }
    await this.router.sendCard(
      target,
      buildUserAuthCard(authorization.verificationUriComplete, authorization.userCode),
    );
    const signal = this.eventAbortController?.signal;
    void this.awaitUserAuthorization(identity, authorization, target, signal).catch(() => {});
    return true;
  }

  /** The link binds whoever opens it, so it is only ever delivered to the requester's own chat. */
  private async authorizeUserFromTool(senderId: string): Promise<Record<string, unknown>> {
    const identity = this.userIdentity;
    if (!identity?.isConfigured()) {
      return {
        success: false,
        errorCode: "FeishuCliUserIdentityUnavailable",
        errorMessage: "User identity is not enabled for this Channel. An administrator configures its scopes in the channel settings.",
      };
    }
    const binding = identity.getBinding();
    if (binding) {
      return {
        success: true,
        contentItems: [{
          type: "text",
          text: `Feishu user identity is already authorized as ${binding.name || binding.openId}.`,
        }],
      };
    }
    if (!/^ou_[A-Za-z0-9]+$/.test(senderId)) {
      return {
        success: false,
        errorCode: "FeishuAuthorizeUserSenderUnknown",
        errorMessage: "Could not tell who is asking. Ask them to send /feishu-auth to this bot in a direct message.",
      };
    }
    if (!(await this.startUserAuthorization(identity, `dm:${senderId}`))) {
      return {
        success: false,
        errorCode: "FeishuAuthorizeUserFailed",
        errorMessage: "Feishu did not return an authorization link. Try again shortly.",
      };
    }
    logInfo("user_identity.authorization.tool_started", {});
    return {
      success: true,
      contentItems: [{
        type: "text",
        text: "Sent the authorization link to the requester in a direct message. It is not shown here because whoever opens it becomes the authorized account.",
      }],
    };
  }

  private async awaitUserAuthorization(
    identity: FeishuUserIdentity,
    authorization: FeishuDeviceAuthorization,
    target: string,
    signal: AbortSignal | undefined,
  ): Promise<void> {
    try {
      const record = await identity.waitForAuthorization(authorization, signal);
      logInfo("user_identity.binding.created", { scopes: record.scopes.length });
      await this.router.sendCard(
        target,
        buildUserAuthNoticeCard("authSuccessBody", { name: record.name || record.openId }),
      );
    } catch (error) {
      if (signal?.aborted) return;
      logWarn("user_identity.authorization.failed", {
        code: error instanceof FeishuUserIdentityError ? error.code : "unknown",
      });
      await this.router.sendCard(target, buildUserAuthNoticeCard("authFailedBody"));
    }
  }

  handleCardAction(event: FeishuCardActionEvent): boolean {
    const value = parseActionValue(event.action?.value);
    if (value?.kind === "userInput") {
      return this.handleUserInputCardAction(event, value);
    }
    if (value?.kind === "command") {
      return this.handleCommandChoiceAction(event, value);
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
    return this.approvals.handleAction(event, value);
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

  protected override onCurrentThreadChanged(
    previousThreadId: string | undefined,
    _threadId: string,
    channelContext: string,
  ): void {
    if (previousThreadId) this.threadContextMap.delete(previousThreadId);
    this.router.forget(channelContext);
  }
}

function parseFeishuChannelTarget(channelContext: string): {
  conversationKind: "group" | "user";
  conversationId: string;
  deliveryTarget: string;
} | null {
  const target = parseConversationTarget(channelContext);
  if (!target) return null;
  return {
    conversationKind: target.kind === "dm" ? "user" : "group",
    conversationId: target.id,
    deliveryTarget: formatConversationTarget(target),
  };
}

function isUserAuthCommand(text: string): boolean {
  return /^\/feishu-auth(?:\s+\S+)?$/i.test(text.trim());
}

function userAuthAction(text: string): "start" | "status" | "revoke" {
  const argument = text.trim().split(/\s+/)[1]?.toLowerCase();
  if (argument === "revoke") return "revoke";
  if (argument === "status") return "status";
  return "start";
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
