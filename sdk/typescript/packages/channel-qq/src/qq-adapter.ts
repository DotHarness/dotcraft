import { mkdir, rm, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { randomUUID } from "node:crypto";

import {
  localImagePart,
  textPart,
} from "@dotcraft/sdk";
import {
  WebSocketTransport,
  type Transport,
} from "@dotcraft/sdk/wire";
import {
  ConfigValidationError,
  ModuleChannelAdapter,
  type WorkspaceContext,
} from "@dotcraft/sdk/channel";

import { parseQQApprovalDecision } from "./approval.js";
import {
  OneBotReverseWsServer,
  atSegment,
  getAtQQ,
  getImageUrl,
  getPlainText,
  getSenderName,
  normalizeMessageSegments,
  textSegment,
  type OneBotMessageEvent,
} from "./onebot.js";
import { QQMediaError, QQMediaTools } from "./qq-media-tools.js";
import type { QQConfig } from "./qq-config.js";
import { QQPermissionService, normalizeIds } from "./permission.js";
import { channelContextForQQEvent, parseQQTarget } from "./target.js";

type PendingApproval = {
  channelContext: string;
  userId: string;
  resolve: (decision: string) => void;
  timer: ReturnType<typeof setTimeout>;
};

export function validateQQConfig(rawConfig: unknown): asserts rawConfig is QQConfig {
  if (!rawConfig || typeof rawConfig !== "object") {
    throw new ConfigValidationError("QQ config must be an object.", ["config"]);
  }
  const config = rawConfig as Record<string, unknown>;
  const dotcraft = asRecord(config.dotcraft);
  const qq = asRecord(config.qq);

  const fields: string[] = [];
  const wsUrl = String(dotcraft.wsUrl ?? "").trim();
  if (!wsUrl) {
    fields.push("dotcraft.wsUrl");
  } else if (!/^wss?:\/\//i.test(wsUrl)) {
    throw new ConfigValidationError("dotcraft.wsUrl must use ws:// or wss://.", ["dotcraft.wsUrl"]);
  }

  const port = qq.port === undefined ? 6700 : Number(qq.port);
  if (!Number.isInteger(port) || port < 1 || port > 65535) {
    throw new ConfigValidationError("qq.port must be an integer between 1 and 65535.", ["qq.port"]);
  }

  try {
    normalizeIds(asIdArray(qq.adminUsers));
    normalizeIds(asIdArray(qq.whitelistedUsers));
    normalizeIds(asIdArray(qq.whitelistedGroups));
  } catch (error) {
    throw new ConfigValidationError(error instanceof Error ? error.message : String(error), [
      "qq.adminUsers",
      "qq.whitelistedUsers",
      "qq.whitelistedGroups",
    ]);
  }

  if (fields.length > 0) {
    throw new ConfigValidationError(`Missing required fields: ${fields.join(", ")}`, fields);
  }
}

export class QQAdapter extends ModuleChannelAdapter<QQConfig> {
  private oneBot: OneBotReverseWsServer | null = null;
  private permission = new QQPermissionService({});
  private readonly mediaTools = new QQMediaTools();
  private readonly threadContextMap = new Map<string, string>();
  private readonly lastSenderByContext = new Map<string, string>();
  private readonly pendingApprovals = new Map<string, PendingApproval>();
  private requireMentionInGroups = true;
  private approvalTimeoutMs = 60_000;
  private tempDir = "";

  constructor() {
    super("qq", "dotcraft-qq", "0.1.0", [
      "item/reasoning/delta",
      "subagent/progress",
      "item/usage/delta",
      "system/event",
      "plan/updated",
    ]);
  }

  protected override getConfigFileName(_context: WorkspaceContext): string {
    return "qq.json";
  }

  protected override validateConfig(rawConfig: unknown): asserts rawConfig is QQConfig {
    validateQQConfig(rawConfig);
  }

  protected override buildTransportFromConfig(config: QQConfig): Transport {
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
    this.tempDir = join(context.craftPath, "tmp", "qq-standard");
    await mkdir(this.tempDir, { recursive: true });
    await super.startWithContext(context);
    if (this.getStatus() !== "ready" || !this.loadedConfig) {
      return;
    }

    const config = this.loadedConfig;
    this.permission = new QQPermissionService(config.qq);
    this.requireMentionInGroups = config.qq.requireMentionInGroups ?? true;
    this.approvalTimeoutMs = config.qq.approvalTimeoutMs ?? 60_000;

    try {
      const server = new OneBotReverseWsServer(
        config.qq.host ?? "127.0.0.1",
        config.qq.port ?? 6700,
        config.qq.accessToken ?? "",
      );
      server.onMessage((evt) => this.handleOneBotMessage(evt));
      await server.start();
      this.oneBot = server;
      console.info(`[qq] OneBot reverse WebSocket listening on ws://${config.qq.host ?? "127.0.0.1"}:${config.qq.port ?? 6700}/`);
    } catch (error) {
      this.setStatus("stopped", {
        code: "startupFailed",
        message: error instanceof Error ? error.message : String(error),
        timestamp: new Date().toISOString(),
      });
    }
  }

  override async stop(): Promise<void> {
    this.resolveAllPendingApprovals("cancel");
    const server = this.oneBot;
    this.oneBot = null;
    await server?.stop();
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
    const result = await this.mediaTools.sendStructuredMessage(this.requireOneBot(), target, {
      kind: "text",
      text: content,
    });
    return Boolean(result.delivered);
  }

  protected override async onSend(
    target: string,
    message: Record<string, unknown>,
    _metadata: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    try {
      return await this.mediaTools.sendStructuredMessage(this.requireOneBot(), target, message);
    } catch (error) {
      if (error instanceof QQMediaError) {
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
      return await this.mediaTools.executeToolCall(
        this.requireOneBot(),
        String(request.tool ?? ""),
        asRecord(request.arguments),
      );
    } catch (error) {
      if (error instanceof QQMediaError) {
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
    const reason = String(request.reason ?? "");
    const channelContext = this.threadContextMap.get(threadId);
    if (!channelContext) {
      console.warn(`[qq] cannot find chat for thread ${threadId}; auto-cancelling approval`);
      return "cancel";
    }

    const target = parseQQTarget(channelContext);
    if (!target) {
      console.warn(`[qq] invalid approval target '${channelContext}'; auto-cancelling approval`);
      return "cancel";
    }

    const approvalUserId = this.lastSenderByContext.get(channelContext) ?? (target.kind === "user" ? target.id : undefined);
    if (!approvalUserId) {
      console.warn(`[qq] cannot find approver for '${channelContext}'; auto-cancelling approval`);
      return "cancel";
    }

    const prompt =
      "\u26a0\ufe0f \u9700\u8981\u64cd\u4f5c\u5ba1\u6279\n" +
      `Type: ${approvalType}\n` +
      `Operation: ${operation}\n` +
      (reason ? `Reason: ${reason}\n` : "") +
      "\n\u56de\u590d \u540c\u610f/yes \u6279\u51c6\uff0c\u540c\u610f\u5168\u90e8/yes all \u672c\u4f1a\u8bdd\u653e\u884c\u540c\u7c7b\u64cd\u4f5c\uff0c\u62d2\u7edd/no \u62d2\u7edd\u3002";
    await this.sendApprovalPrompt(target, prompt, approvalUserId);

    return await new Promise<string>((resolveDecision) => {
      const timer = setTimeout(() => {
        this.pendingApprovals.delete(requestId);
        resolveDecision("cancel");
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

  protected override async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    if (!segmentText.trim()) return;
    await this.onDeliver(channelContext, segmentText, {});
    if (isFinal) {
      await this.cleanupTempImages();
    }
  }

  protected override async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    if (!segmentsWereDelivered && replyText) {
      await this.onDeliver(channelContext, replyText, {});
    }
    await this.cleanupTempImages();
  }

  protected override async onTurnFailed(threadId: string, turnId: string, error: string): Promise<void> {
    console.error(`[qq] turn ${turnId} failed on thread ${threadId}: ${error}`);
    await this.cleanupTempImages();
  }

  private async handleOneBotMessage(evt: OneBotMessageEvent): Promise<void> {
    const isGroup = evt.message_type === "group";
    const groupId = evt.group_id;
    if (isGroup && groupId === undefined) {
      console.warn("[qq] group message missing group_id; ignored");
      return;
    }
    const senderId = String(evt.user_id);
    const channelContext = channelContextForQQEvent(isGroup, groupId, evt.user_id);
    const threadUserId = isGroup ? channelContext : senderId;
    const senderName = getSenderName(evt);
    const rawText = getPlainText(evt.message).trim();

    const approvalDecision = parseQQApprovalDecision(rawText);
    if (approvalDecision && this.resolvePendingApproval(approvalDecision, senderId, channelContext)) {
      return;
    }

    const segments = normalizeMessageSegments(evt.message);
    if (isGroup && this.requireMentionInGroups && !this.isAtSelf(evt, segments)) {
      return;
    }

    const role = this.permission.getUserRole(senderId, isGroup ? groupId : undefined);
    if (role === "unauthorized") {
      console.info(`[qq] unauthorized user ${senderId} ignored`);
      return;
    }

    const inputParts = await this.buildInputParts(segments, rawText);
    if (!rawText && inputParts.length === 0) {
      return;
    }

    this.lastSenderByContext.set(channelContext, senderId);

    await this.handleMessage({
      userId: threadUserId,
      userName: senderName,
      text: rawText || "[image]",
      channelContext,
      sender: {
        senderId,
        senderName,
        senderRole: role,
        ...(isGroup ? { groupId: channelContext } : {}),
      },
      omitSenderGroupId: !isGroup,
      inputParts: inputParts.length > 0 ? inputParts : undefined,
    });
  }

  private async buildInputParts(segments: ReturnType<typeof normalizeMessageSegments>, fallbackText: string): Promise<Record<string, unknown>[]> {
    const parts: Record<string, unknown>[] = [];
    for (const segment of segments) {
      if (segment.type === "text") {
        const text = String(segment.data?.text ?? "");
        if (text) parts.push(textPart(text));
        continue;
      }
      if (segment.type === "image") {
        const url = getImageUrl(segment);
        if (!url) continue;
        const image = await this.downloadImageAsLocalPart(url);
        if (image) parts.push(image);
      }
    }
    if (parts.length === 0 && fallbackText) {
      parts.push(textPart(fallbackText));
    }
    return parts;
  }

  private async downloadImageAsLocalPart(url: string): Promise<Record<string, unknown> | null> {
    try {
      const response = await fetch(url);
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      const bytes = Buffer.from(await response.arrayBuffer());
      const mediaType = response.headers.get("content-type")?.split(";")[0]?.trim() || "image/jpeg";
      const ext = mediaTypeToExt(mediaType);
      const fileName = `${randomUUID()}${ext}`;
      const path = join(this.tempDir, fileName);
      await writeFile(path, bytes);
      return { ...localImagePart(path), mimeType: mediaType, fileName };
    } catch (error) {
      console.warn("[qq] failed to download image:", error instanceof Error ? error.message : String(error));
      return null;
    }
  }

  private isAtSelf(evt: OneBotMessageEvent, segments: ReturnType<typeof normalizeMessageSegments>): boolean {
    const selfId = evt.self_id === undefined ? "" : String(evt.self_id);
    if (!selfId) return false;
    return segments.some((segment) => getAtQQ(segment) === selfId);
  }

  private async sendApprovalPrompt(
    target: NonNullable<ReturnType<typeof parseQQTarget>>,
    prompt: string,
    userId?: string,
  ): Promise<void> {
    const server = this.requireOneBot();
    const message = target.kind === "group"
      ? userId ? [atSegment(userId), textSegment(` ${prompt}`)] : [textSegment(prompt)]
      : [textSegment(prompt)];
    const action = target.kind === "group"
      ? { action: "send_group_msg", params: { group_id: Number(target.id), message } }
      : { action: "send_private_msg", params: { user_id: Number(target.id), message } };
    await server.sendAction(action);
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

  private resolveAllPendingApprovals(decision: string): void {
    for (const [requestId, pending] of this.pendingApprovals) {
      clearTimeout(pending.timer);
      pending.resolve(decision);
      this.pendingApprovals.delete(requestId);
    }
  }

  private async cleanupTempImages(): Promise<void> {
    if (!this.tempDir) return;
    await rm(this.tempDir, { recursive: true, force: true }).catch(() => undefined);
    await mkdir(this.tempDir, { recursive: true }).catch(() => undefined);
  }

  private requireOneBot(): OneBotReverseWsServer {
    if (!this.oneBot) throw new Error("OneBot server is not running.");
    return this.oneBot;
  }
}

function asRecord(value: unknown): Record<string, unknown> {
  return value && typeof value === "object" ? (value as Record<string, unknown>) : {};
}

function asIdArray(value: unknown): Array<number | string> | undefined {
  if (value === undefined) return undefined;
  if (!Array.isArray(value)) throw new Error("QQ id lists must be arrays.");
  return value.map((item) => {
    if (typeof item !== "number" && typeof item !== "string") {
      throw new Error("QQ id list values must be numbers or strings.");
    }
    return item;
  });
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
