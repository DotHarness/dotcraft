import { mkdir, rm, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { randomUUID } from "node:crypto";

import {
  DECISION_CANCEL,
  localImagePart,
  textPart,
  type InputPart,
  type SocialChannelTarget,
} from "@dotcraft/channel";
import {
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
  splitUserInputRequestByQuestion,
  userInputResponseFromText,
  type ChannelToolDescriptor,
  type ChannelAdapterMessageOpts,
  type UserInputResponse,
  type WorkspaceContext,
} from "@dotcraft/channel";

import { parseWeComApprovalDecision } from "./approval.js";
import { WeComMediaError, WeComMediaTools } from "./wecom-media-tools.js";
import { WeComPermissionService } from "./permission.js";
import { WeComBotRegistry, WeComBotServer } from "./wecom-server.js";
import { WeComPusher } from "./wecom-pusher.js";
import {
  WeComChatType,
  WeComEventType,
  WeComMsgType,
  type WeComFrom,
  type WeComMessage,
} from "./wecom-types.js";
import type { WeComConfig } from "./wecom-config.js";

type PendingApproval = {
  channelContext: string;
  userId: string;
  resolve: (decision: string) => void;
  timer: ReturnType<typeof setTimeout>;
};

type PendingUserInput = {
  channelContext: string;
  userId: string;
  request: Record<string, unknown>;
  promptTitle?: string;
  resolve: (response: UserInputResponse) => void;
};

export function validateWeComConfig(rawConfig: unknown): asserts rawConfig is WeComConfig {
  if (!rawConfig || typeof rawConfig !== "object") {
    throw new ConfigValidationError("WeCom config must be an object.", ["config"]);
  }
  const config = rawConfig as Record<string, unknown>;
  const dotcraft = asRecord(config.dotcraft);
  const wecom = asRecord(config.wecom);

  const fields: string[] = [];
  const wsUrl = String(dotcraft.wsUrl ?? "").trim();
  if (!wsUrl) {
    fields.push("dotcraft.wsUrl");
  } else if (!/^wss?:\/\//i.test(wsUrl)) {
    throw new ConfigValidationError("dotcraft.wsUrl must use ws:// or wss://.", ["dotcraft.wsUrl"]);
  }

  const port = wecom.port === undefined ? 9000 : Number(wecom.port);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new ConfigValidationError("wecom.port must be an integer between 1 and 65535.", ["wecom.port"]);
  }

  const scheme = String(wecom.scheme ?? "http").trim().toLowerCase();
  if (scheme !== "http" && scheme !== "https") {
    throw new ConfigValidationError("wecom.scheme must be 'http' or 'https'.", ["wecom.scheme"]);
  }

  const robots = Array.isArray(wecom.robots) ? wecom.robots : [];
  if (robots.length === 0) fields.push("wecom.robots");
  for (const [index, item] of robots.entries()) {
    const robot = asRecord(item);
    if (!String(robot.path ?? "").trim()) fields.push(`wecom.robots[${index}].path`);
    if (!String(robot.token ?? "").trim()) fields.push(`wecom.robots[${index}].token`);
    if (!String(robot.aesKey ?? "").trim()) fields.push(`wecom.robots[${index}].aesKey`);
  }

  for (const key of ["adminUsers", "whitelistedUsers", "whitelistedChats"]) {
    if (wecom[key] !== undefined && !Array.isArray(wecom[key])) {
      throw new ConfigValidationError(`wecom.${key} must be an array.`, [`wecom.${key}`]);
    }
  }

  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export class WeComAdapter extends ModuleChannelAdapter<WeComConfig> {
  private server: WeComBotServer | undefined;
  private registry: WeComBotRegistry | undefined;
  private permission = new WeComPermissionService({});
  private readonly mediaTools = new WeComMediaTools();
  private readonly threadContextMap = new Map<string, string>();
  private readonly lastSenderByContext = new Map<string, string>();
  private readonly pendingApprovals = new Map<string, PendingApproval>();
  private readonly pendingUserInputs = new Map<string, PendingUserInput>();
  private approvalTimeoutMs = 60_000;
  private tempDir = "";

  constructor() {
    super("wecom", "dotcraft-wecom", "0.1.0", [
      "item/reasoning/delta",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ]);
  }

  protected override getConfigFileName(_context: WorkspaceContext): string {
    return "wecom.json";
  }

  protected override validateConfig(rawConfig: unknown): asserts rawConfig is WeComConfig {
    validateWeComConfig(rawConfig);
  }

  protected override buildTransportFromConfig(config: WeComConfig): Transport {
    return new WebSocketTransport({
      url: config.dotcraft.wsUrl,
      token: config.dotcraft.token ?? "",
    });
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
    const conversationId = parseWeComConversationId(channelContext);
    if (!conversationId) return null;
    const platformUserId = String(sender.senderId ?? opts.userId ?? "");
    const displayName = typeof sender.senderName === "string" && sender.senderName.trim()
      ? sender.senderName.trim()
      : null;

    return {
      channelName: "wecom",
      conversationKind: "chat",
      conversationId,
      deliveryTarget: channelContext,
      displayName: `WeCom chat ${conversationId}`,
      boundBy: platformUserId
        ? {
          platformUserId,
          displayName,
        }
        : null,
    };
  }

  override async startWithContext(context: WorkspaceContext): Promise<void> {
    this.defaultWorkspacePath = context.workspaceRoot;
    this.tempDir = join(context.craftPath, "tmp", "wecom-standard");
    await mkdir(this.tempDir, { recursive: true });
    await super.startWithContext(context);
    if (this.getStatus() !== "ready" || !this.loadedConfig) return;

    const config = this.loadedConfig;
    this.approvalTimeoutMs = config.wecom.approvalTimeoutMs ?? 60_000;
    this.permission = new WeComPermissionService(config.wecom);

    try {
      const registry = new WeComBotRegistry();
      for (const robot of config.wecom.robots ?? []) {
        registry.register(robot.path, robot.token, robot.aesKey);
      }
      for (const path of registry.getAllPaths()) {
        registry.setHandlers(
          path,
          (parameters, from, pusher) => this.handleTextMessage(parameters, from, pusher),
          (message, pusher) => this.handleCommonMessage(message, pusher),
          (eventType, chatType, from, pusher) => this.handleEventMessage(eventType, chatType, from, pusher),
        );
      }

      const server = new WeComBotServer(registry, {
        host: config.wecom.host ?? "0.0.0.0",
        port: config.wecom.port ?? 9000,
        scheme: config.wecom.scheme ?? "http",
        tls: config.wecom.tls,
      });
      await server.start();
      this.registry = registry;
      this.server = server;
      console.info(
        `[wecom] callback server listening on ${config.wecom.scheme ?? "http"}://${config.wecom.host ?? "0.0.0.0"}:${config.wecom.port ?? 9000}`,
      );
    } catch (error) {
      this.setStatus("stopped", {
        code: "startupFailed",
        message: error instanceof Error ? error.message : String(error),
        timestamp: new Date().toISOString(),
      });
    }
  }

  override async stop(): Promise<void> {
    this.resolveAllPendingApprovals(DECISION_CANCEL);
    this.resolveAllPendingUserInputs(emptyUserInputResponse());
    const server = this.server;
    this.server = undefined;
    this.registry = undefined;
    await server?.stop();
    await this.cleanupTempFiles();
    await super.stop();
  }

  protected override onThreadContextBound(threadId: string, channelContext: string): void {
    if (channelContext) this.threadContextMap.set(threadId, channelContext);
  }

  protected override onThreadsArchived(_identityKey: string, archivedThreadIds: string[]): void {
    for (const threadId of archivedThreadIds) {
      this.threadContextMap.delete(threadId);
    }
  }

  override async onDeliver(target: string, content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    try {
      const pusher = this.createPusher(target);
      await pusher.pushText(content);
      return true;
    } catch (error) {
      console.error("[wecom] delivery failed:", error instanceof Error ? error.message : String(error));
      return false;
    }
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    _metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    try {
      const result = await this.mediaTools.sendStructuredMessage(this.createPusher(target), message);
      return result;
    } catch (error) {
      if (error instanceof WeComMediaError) {
        return { delivered: false, errorCode: error.code, errorMessage: error.message };
      }
      return {
        delivered: false,
        errorCode: "AdapterDeliveryFailed",
        errorMessage: error instanceof Error ? error.message : String(error),
      };
    }
  }

  protected override async onToolCall(request: Record<string, unknown>): Promise<Record<string, unknown>> {
    try {
      const context = asRecord(request.context);
      const target = String(context.channelContext ?? "");
      if (!target) {
        return {
          success: false,
          errorCode: "MissingChatContext",
          errorMessage: "WeCom tool execution requires a current chat context.",
        };
      }
      return await this.mediaTools.executeToolCall(
        this.createPusher(target),
        String(request.tool ?? ""),
        asRecord(request.arguments),
      );
    } catch (error) {
      if (error instanceof WeComMediaError) {
        return { success: false, errorCode: error.code, errorMessage: error.message };
      }
      return {
        success: false,
        errorCode: "AdapterToolCallFailed",
        errorMessage: error instanceof Error ? error.message : String(error),
      };
    }
  }

  override async onApprovalRequest(request: Record<string, unknown>): Promise<string> {
    const threadId = String(request.threadId ?? "");
    const requestId = String(request.requestId ?? "");
    const approvalType = String(request.approvalType ?? "");
    const operation = String(request.operation ?? "");
    const target = String(request.target ?? "");
    const channelContext = this.threadContextMap.get(threadId);
    if (!channelContext) {
      console.warn(`[wecom] cannot find chat for thread ${threadId}; auto-cancelling approval`);
      return DECISION_CANCEL;
    }

    const approvalUserId = this.lastSenderByContext.get(channelContext);
    if (!approvalUserId) {
      console.warn(`[wecom] cannot find approver for '${channelContext}'; auto-cancelling approval`);
      return DECISION_CANCEL;
    }

    const timeoutSeconds = Math.round(this.approvalTimeoutMs / 1000);
    const prompt = approvalType === "shell"
      ? `⚠️ 需要执行命令权限：\`${operation}\`\n回复 同意/yes 批准，同意全部/yes all 本会话放行同类操作，拒绝/no 拒绝（${timeoutSeconds}秒超时自动拒绝）`
      : `⚠️ 需要 ${operation} 文件权限：\`${target}\`\n回复 同意/yes 批准，同意全部/yes all 本会话放行同类操作，拒绝/no 拒绝（${timeoutSeconds}秒超时自动拒绝）`;

    await this.createPusher(channelContext).pushText(prompt);

    return await new Promise<string>((resolveDecision) => {
      const timer = setTimeout(() => {
        this.pendingApprovals.delete(requestId);
        resolveDecision(DECISION_CANCEL);
      }, this.approvalTimeoutMs);
      this.pendingApprovals.set(requestId, {
        channelContext,
        userId: approvalUserId,
        resolve: (decision) => {
          clearTimeout(timer);
          this.pendingApprovals.delete(requestId);
          resolveDecision(decision);
        },
        timer,
      });
    });
  }

  protected override async onUserInputRequest(request: Record<string, unknown>): Promise<UserInputResponse> {
    const threadId = String(request.threadId ?? "");
    const requestId = String(request.requestId ?? "");
    const channelContext = this.threadContextMap.get(threadId);
    if (!channelContext || !requestId) {
      console.warn(`[wecom] cannot find chat for thread ${threadId}; resolving user input empty`);
      return emptyUserInputResponse();
    }

    const userId = this.lastSenderByContext.get(channelContext);
    if (!userId) {
      console.warn(`[wecom] cannot find user for '${channelContext}'; resolving user input empty`);
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
        channelContext,
        userId,
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
    channelContext: string;
    userId: string;
    promptTitle?: string;
  }): Promise<UserInputResponse> {
    const requestId = String(params.request.requestId ?? "");
    if (!requestId) {
      return emptyUserInputResponse();
    }

    this.cancelPendingUserInputsFor(params.channelContext, params.userId);
    await this.createPusher(params.channelContext).pushText(
      `❓ 需要你回答 DotCraft 问题\n${buildUserInputPrompt(params.request, { title: params.promptTitle })}`,
    );

    return await new Promise<UserInputResponse>((resolve) => {
      this.pendingUserInputs.set(requestId, {
        channelContext: params.channelContext,
        userId: params.userId,
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
  ): Promise<void> {
    if (!segmentText.trim()) return;
    await this.createPusher(channelContext).pushMarkdown(segmentText);
  }

  protected override async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (!segmentsWereDelivered && replyText) {
      await this.createPusher(channelContext).pushMarkdown(replyText);
    }
    await this.cleanupTempFiles();
  }

  protected override async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    console.error(`[wecom] turn ${turnId} failed on thread ${threadId}: ${error}`);
    await this.cleanupTempFiles();
  }

  private async handleTextMessage(parameters: string[], from: WeComFrom, pusher: WeComPusher): Promise<void> {
    const plainText = parameters.join(" ").trim();
    if (!plainText) {
      await pusher.pushText("请输入消息内容");
      return;
    }

    const channelContext = `chat:${pusher.getChatId()}`;
    const approvalDecision = parseWeComApprovalDecision(plainText);
    if (approvalDecision && this.resolvePendingApproval(approvalDecision, from.userId, channelContext)) {
      return;
    }
    if (await this.tryResolvePendingUserInput(plainText, from.userId, channelContext)) {
      return;
    }

    await this.runInboundMessage(plainText, from, pusher, [textPart(plainText)]);
  }

  private async handleCommonMessage(message: WeComMessage, pusher: WeComPusher): Promise<void> {
    const from = message.from ?? { userId: "", name: "", alias: "" };
    const inputParts = await this.buildInputParts(message);
    if (inputParts) {
      const text = message.msgType === WeComMsgType.Voice ? message.voice?.content ?? "[voice]" : `[${message.msgType}]`;
      await this.runInboundMessage(text, from, pusher, inputParts);
      return;
    }

    let info = `收到 ${message.msgType} 类型消息`;
    if (message.msgType === WeComMsgType.Attachment) {
      info += `\nCallbackId: ${message.attachment?.callbackId ?? ""}`;
    } else if (message.msgType === WeComMsgType.File) {
      info += `\n文件URL: ${message.file?.url ?? ""}`;
    }
    await pusher.pushText(info);
  }

  private async handleEventMessage(eventType: string, chatType: string, from: WeComFrom, pusher: WeComPusher): Promise<string | null> {
    const message = eventType === WeComEventType.AddToChat
      ? `欢迎 ${from.name} 将我添加到${chatType === WeComChatType.Group ? "群聊" : "会话"}！输入 /help 查看可用命令。`
      : eventType === WeComEventType.EnterChat
        ? `你好，${from.name}！我是 DotCraft，随时为您服务。输入 /help 查看可用命令。`
        : eventType === WeComEventType.DeleteFromChat
          ? "再见！"
          : null;
    if (message) await pusher.pushText(message);
    return null;
  }

  private async runInboundMessage(text: string, from: WeComFrom, pusher: WeComPusher, inputParts: InputPart[]): Promise<void> {
    const chatId = pusher.getChatId();
    const channelContext = `chat:${chatId}`;
    const senderName = from.name || from.alias || from.userId;
    this.lastSenderByContext.set(channelContext, from.userId);
    const role = this.permission.getUserRole(from.userId, chatId);
    await this.handleMessage({
      userId: channelContext,
      userName: senderName,
      text,
      channelContext,
      sender: {
        senderId: from.userId,
        senderName,
        senderRole: role,
        groupId: channelContext,
      },
      inputParts,
    });
  }

  private async buildInputParts(message: WeComMessage): Promise<InputPart[] | null> {
    if (message.msgType === WeComMsgType.Image && message.image?.imageUrl) {
      const image = await this.downloadImageAsLocalPart(message.image.imageUrl);
      return image ? [image] : null;
    }
    if (message.msgType === WeComMsgType.Mixed && message.mixedMessage?.msgItems.length) {
      const parts: InputPart[] = [];
      for (const item of message.mixedMessage.msgItems) {
        if (item.msgType === WeComMsgType.Text && item.text?.content) parts.push(textPart(item.text.content));
        if (item.msgType === WeComMsgType.Image && item.image?.imageUrl) {
          const image = await this.downloadImageAsLocalPart(item.image.imageUrl);
          if (image) parts.push(image);
        }
      }
      return parts.length > 0 ? parts : null;
    }
    if (message.msgType === WeComMsgType.Voice && message.voice?.content) {
      return [textPart(message.voice.content)];
    }
    return null;
  }

  private async downloadImageAsLocalPart(url: string): Promise<InputPart | null> {
    try {
      const response = await fetch(url);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const bytes = Buffer.from(await response.arrayBuffer());
      const mediaType = response.headers.get("content-type")?.split(";")[0]?.trim() || "image/jpeg";
      const fileName = `${randomUUID()}${mediaTypeToExt(mediaType)}`;
      const path = join(this.tempDir, fileName);
      await writeFile(path, bytes);
      return { ...localImagePart(path), mimeType: mediaType, fileName };
    } catch (error) {
      console.warn("[wecom] failed to download image:", error instanceof Error ? error.message : String(error));
      return null;
    }
  }

  private createPusher(target: string): WeComPusher {
    const chatId = target.replace(/^chat:/, "");
    const webhookUrl = this.registry?.getWebhookUrl(chatId);
    if (!webhookUrl) throw new Error(`No WeCom webhook is available for target '${target}'.`);
    return new WeComPusher(chatId, webhookUrl);
  }

  private resolvePendingApproval(decision: string, userId: string, channelContext: string): boolean {
    for (const [requestId, pending] of this.pendingApprovals) {
      if (pending.userId !== userId || pending.channelContext !== channelContext) {
        continue;
      }

      this.pendingApprovals.delete(requestId);
      pending.resolve(decision);
      return true;
    }

    return false;
  }

  private async tryResolvePendingUserInput(text: string, userId: string, channelContext: string): Promise<boolean> {
    for (const [requestId, pending] of this.pendingUserInputs) {
      if (pending.userId !== userId || pending.channelContext !== channelContext) {
        continue;
      }

      const response = userInputResponseFromText(pending.request, text);
      if (!response) {
        await this.createPusher(channelContext).pushText(
          buildUserInputPrompt(pending.request, { title: pending.promptTitle }),
        );
        return true;
      }

      this.pendingUserInputs.delete(requestId);
      pending.resolve(response);
      return true;
    }

    return false;
  }

  private resolveAllPendingApprovals(decision: string): void {
    for (const [requestId, pending] of this.pendingApprovals) {
      clearTimeout(pending.timer);
      pending.resolve(decision);
      this.pendingApprovals.delete(requestId);
    }
  }

  private resolveAllPendingUserInputs(response: UserInputResponse): void {
    for (const [requestId, pending] of this.pendingUserInputs) {
      pending.resolve(response);
      this.pendingUserInputs.delete(requestId);
    }
  }

  private cancelPendingUserInputsFor(channelContext: string, userId: string): void {
    for (const [requestId, pending] of this.pendingUserInputs) {
      if (pending.channelContext !== channelContext || pending.userId !== userId) {
        continue;
      }
      pending.resolve(emptyUserInputResponse());
      this.pendingUserInputs.delete(requestId);
    }
  }

  private async cleanupTempFiles(): Promise<void> {
    if (!this.tempDir) return;
    await rm(this.tempDir, { recursive: true, force: true }).catch(() => undefined);
    await mkdir(this.tempDir, { recursive: true }).catch(() => undefined);
  }
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function mediaTypeToExt(mediaType: string): string {
  switch (mediaType.toLowerCase()) {
    case "image/png":
      return ".png";
    case "image/gif":
      return ".gif";
    case "image/webp":
      return ".webp";
    case "image/jpeg":
    default:
      return ".jpg";
  }
}

function parseWeComConversationId(channelContext: string): string | null {
  const match = /^chat:(.+)$/.exec(channelContext.trim());
  return match?.[1]?.trim() || null;
}
