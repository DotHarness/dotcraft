import * as fs from "node:fs";
import * as os from "node:os";
import * as path from "node:path";
import { readFile } from "node:fs/promises";

import * as Lark from "@larksuiteoapi/node-sdk";

import type {
  FeishuApiErrorKind,
  FeishuBotDiagnosticTag,
  FeishuChatMessageItem,
  FeishuChatMessagePage,
  FeishuConfig,
  FeishuBotInfo,
  FeishuCardActionEvent,
  FeishuDocxBlockCreateResult,
  FeishuDocxBlockDeleteResult,
  FeishuDocxBlockBatchUpdateResult,
  FeishuDocxBlockInfo,
  FeishuDocxBlockListPage,
  FeishuDocxComment,
  FeishuDocxCommentBatchQueryResult,
  FeishuDocxCommentContent,
  FeishuDocxCommentContentElement,
  FeishuDocxCommentCreateResult,
  FeishuDocxCommentReply,
  FeishuDocxCommentReplyCreateResult,
  FeishuDocxCommentReplyListPage,
  FeishuDocxCommentListPage,
  FeishuDocxDocumentInfo,
  FeishuDriveMediaUploadResult,
  FeishuDocxRawContent,
  FeishuListChatMessagesOptions,
  FeishuMoveDocxToWikiResult,
  FeishuMessageEvent,
  FeishuReplyOptions,
  FeishuSendResult,
  FeishuWikiMoveTaskStatus,
  FeishuWikiNodeInfo,
  FeishuWikiNodeListPage,
  FeishuWikiObjType,
  FeishuWikiSpaceInfo,
  FeishuWikiSpaceListPage,
} from "./feishu-types.js";
import { FeishuApiError } from "./feishu-types.js";
import { errorMessage, logError, logInfo, logWarn } from "./logging.js";

type FeishuEventHandlers = {
  onMessage: (event: FeishuMessageEvent) => Promise<void>;
  onCardAction: (event: FeishuCardActionEvent) => Promise<void>;
};

type FeishuApiPayload = {
  code?: number;
  msg?: string;
  data?: Record<string, unknown>;
  raw: unknown;
};

function resolveBrand(brand?: FeishuConfig["feishu"]["brand"]): Lark.Domain {
  if (!brand || brand === "feishu") return Lark.Domain.Feishu;
  return Lark.Domain.Lark;
}

async function extractBufferFromResponse(
  response: unknown,
): Promise<{ buffer: Buffer; contentType?: string }> {
  if (Buffer.isBuffer(response)) {
    return { buffer: response };
  }
  if (response instanceof ArrayBuffer) {
    return { buffer: Buffer.from(response) };
  }
  if (!response || typeof response !== "object") {
    throw new Error("Unexpected Feishu binary response");
  }

  const resp = response as Record<string, unknown>;
  const headers = (resp.headers as Record<string, string> | undefined) ?? {};
  const contentType = headers["content-type"] ?? headers["Content-Type"];

  const data = resp.data;
  if (Buffer.isBuffer(data)) {
    return { buffer: data, contentType };
  }
  if (data instanceof ArrayBuffer) {
    return { buffer: Buffer.from(data), contentType };
  }
  if (data && typeof data === "object" && typeof (data as NodeJS.ReadableStream).pipe === "function") {
    return {
      buffer: await streamToBuffer(data as NodeJS.ReadableStream),
      contentType,
    };
  }
  if (typeof (resp as { getReadableStream?: () => Promise<NodeJS.ReadableStream> }).getReadableStream === "function") {
    const stream = await (resp as { getReadableStream: () => Promise<NodeJS.ReadableStream> }).getReadableStream();
    return { buffer: await streamToBuffer(stream), contentType };
  }

  throw new Error("Unable to read binary resource from Feishu response");
}

function streamToBuffer(stream: NodeJS.ReadableStream): Promise<Buffer> {
  return new Promise<Buffer>((resolve, reject) => {
    const chunks: Buffer[] = [];
    stream.on("data", (chunk: Buffer | Uint8Array) => chunks.push(Buffer.from(chunk)));
    stream.on("end", () => resolve(Buffer.concat(chunks)));
    stream.on("error", reject);
  });
}

const AUTH_CODES = new Set([99991661, 99991663, 99991668, 99991671]);
const PERMISSION_CODES = new Set([99991400, 99991401, 99991403, 230006, 230027]);
const INVALID_ARGUMENT_CODES = new Set([10002, 10013, 230001, 230025, 230099, 99991672]);
const RATE_LIMIT_CODES = new Set([11232, 230020, 99991429]);
const UPSTREAM_CODES = new Set([100500, 99991450, 99991499]);

export class FeishuClient {
  readonly sdk: Lark.Client;
  private wsClient: Lark.WSClient | null = null;
  private readonly appId: string;
  private readonly appSecret: string;
  private readonly domain: string | Lark.Domain;
  private readonly apiBaseUrl: string;
  private tenantAccessToken: string | null = null;
  private tenantAccessTokenExpiresAt = 0;

  constructor(private readonly config: FeishuConfig["feishu"]) {
    this.appId = config.appId;
    this.appSecret = config.appSecret;
    this.domain = resolveBrand(config.brand);
    this.apiBaseUrl = resolveApiBaseUrl(config.brand);
    this.sdk = new Lark.Client({
      appId: this.appId,
      appSecret: this.appSecret,
      appType: Lark.AppType.SelfBuild,
      domain: this.domain,
    });
  }

  async probeBot(): Promise<FeishuBotInfo> {
    logInfo("startup.bot_probe_request", { method: "GET", path: "/open-apis/bot/v3/info" });
    const token = await this.getTenantAccessToken();
    const response = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/bot/v3/info`, {
          method: "GET",
          headers: {
            Authorization: `Bearer ${token}`,
          },
        }),
      "Failed to query Feishu bot info",
    );

    const data = (response.data as Record<string, unknown>) ?? {};
    const bot = (response.bot as Record<string, unknown> | undefined) ?? {};

    const responseKeys = Object.keys(response).map((key) => `response.${key}`);
    const dataKeys = Object.keys(data).map((key) => `data.${key}`);
    const botKeys = Object.keys(bot).map((key) => `bot.${key}`);
    const rawFieldKeys = [...responseKeys, ...dataKeys, ...botKeys].sort();

    const openIdCandidates = [
      bot.open_id,
      bot.bot_open_id,
      bot.id,
      data.open_id,
      data.bot_open_id,
      data.bot_id,
      (data.bot as Record<string, unknown> | undefined)?.open_id,
      (data.bot as Record<string, unknown> | undefined)?.id,
      (data.pingBotInfo as Record<string, unknown> | undefined)?.botID,
      (response.open_id as string | undefined),
      (response.bot_open_id as string | undefined),
    ]
      .map((value) => (value == null ? "" : String(value)))
      .filter((value) => value.length > 0);

    const appName = String(bot.app_name ?? data.app_name ?? data.bot_name ?? "");
    const botName = appName;
    const openId = openIdCandidates[0] ?? "";
    const hasBotIdentity = openId.length > 0;
    const diagnosticTag = hasBotIdentity ? undefined : classifyBotDiagnosticTag(response, data, bot);
    const diagnosticMessage = hasBotIdentity
      ? undefined
      : diagnosticTag === "botCapabilityDisabled"
        ? "Feishu bot info returned no bot identity field and suggests bot capability is disabled or unpublished. " +
          `Available fields: [${rawFieldKeys.join(", ")}]`
        : "Feishu bot info returned no bot identity field. " +
          "SDK response shape may differ from the expected bot info contract. " +
          `Available fields: [${rawFieldKeys.join(", ")}]`;

    return {
      appName,
      botName,
      openId,
      hasBotIdentity,
      tenantKey: data.tenant_key ? String(data.tenant_key) : undefined,
      activateStatus: data.activate_status != null ? Number(data.activate_status) : undefined,
      rawFieldKeys,
      diagnosticMessage,
      diagnosticTag,
    };
  }

  async sendTextMessage(target: string, text: string): Promise<FeishuSendResult> {
    const normalizedText = text.trim();
    if (!normalizedText) {
      throw new TypeError("Feishu text delivery requires non-empty text.");
    }

    const { receiveId, receiveIdType } = this.resolveTarget(target);
    const response = await this.sendMessage(receiveId, receiveIdType, "text", {
      text: normalizedText,
    });
    const responseData = (response.data as Record<string, unknown> | undefined) ?? {};
    return {
      messageId: String(responseData.message_id ?? ""),
      chatId: String(responseData.chat_id ?? ""),
    };
  }

  async replyToMessage(
    messageId: string,
    text: string,
    opts?: FeishuReplyOptions,
  ): Promise<FeishuSendResult> {
    const normalizedMessageId = messageId.trim();
    if (!normalizedMessageId) {
      throw new TypeError("Feishu reply requires a messageId.");
    }
    const normalizedText = text.trim();
    if (!normalizedText) {
      throw new TypeError("Feishu reply requires non-empty text.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/im/v1/messages/${encodeURIComponent(normalizedMessageId)}/reply`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            content: JSON.stringify({ text: normalizedText }),
            msg_type: "text",
            ...(opts?.replyInThread !== undefined ? { reply_in_thread: opts.replyInThread } : {}),
            ...(opts?.uuid?.trim() ? { uuid: opts.uuid.trim() } : {}),
          }),
        }),
      "Failed to reply to Feishu message.",
    );
    const responseData = (payload.data as Record<string, unknown> | undefined) ?? {};
    return {
      messageId: String(responseData.message_id ?? ""),
      chatId: String(responseData.chat_id ?? ""),
    };
  }

  async listChatMessages(
    chatId: string,
    options: FeishuListChatMessagesOptions,
  ): Promise<FeishuChatMessagePage> {
    const normalizedChatId = chatId.trim();
    if (!normalizedChatId) {
      throw new TypeError("Feishu history lookup requires a chatId.");
    }

    const normalizedStartTime = options.startTime.trim();
    if (!normalizedStartTime) {
      throw new TypeError("Feishu history lookup requires a startTime.");
    }
    if (options.pageSize !== undefined && (!Number.isInteger(options.pageSize) || options.pageSize <= 0)) {
      throw new TypeError("Feishu history lookup requires pageSize to be a positive integer.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams({
      container_id_type: "chat",
      container_id: normalizedChatId,
      start_time: normalizedStartTime,
    });
    if (options.endTime?.trim()) {
      params.set("end_time", options.endTime.trim());
    }
    if (options.pageSize !== undefined) {
      params.set("page_size", String(options.pageSize));
    }
    if (options.pageToken?.trim()) {
      params.set("page_token", options.pageToken.trim());
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/im/v1/messages?${params.toString()}`, {
          method: "GET",
          headers: {
            Authorization: `Bearer ${token}`,
          },
        }),
      "Failed to list Feishu chat messages.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const items = Array.isArray(data.items) ? data.items.map(mapChatMessageItem) : [];
    return {
      items,
      nextPageToken: data.page_token ? String(data.page_token) : undefined,
      hasMore: data.has_more == null ? undefined : Boolean(data.has_more),
    };
  }

  async createDocxDocument(options: {
    title?: string;
    folderToken?: string;
  }): Promise<FeishuDocxDocumentInfo> {
    const token = await this.getTenantAccessToken();
    const body: Record<string, unknown> = {};
    if (options.title?.trim()) body.title = options.title.trim();
    if (options.folderToken?.trim()) body.folder_token = options.folderToken.trim();

    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/docx/v1/documents`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify(body),
        }),
      "Failed to create Feishu docx document.",
    );

    return this.parseDocxDocumentInfo(payload);
  }

  async createWikiNode(options: {
    spaceId: string;
    parentNodeToken?: string;
    objType?: FeishuWikiObjType;
    nodeType?: "origin" | "shortcut";
    originNodeToken?: string;
    title?: string;
  }): Promise<FeishuWikiNodeInfo> {
    const normalizedSpaceId = options.spaceId.trim();
    if (!normalizedSpaceId) {
      throw new TypeError("Feishu wiki node creation requires a spaceId.");
    }

    const token = await this.getTenantAccessToken();
    const body: Record<string, unknown> = {
      obj_type: options.objType ?? "docx",
      node_type: options.nodeType ?? "origin",
    };
    if (options.parentNodeToken?.trim()) {
      body.parent_node_token = options.parentNodeToken.trim();
    }
    if (options.originNodeToken?.trim()) {
      body.origin_node_token = options.originNodeToken.trim();
    }
    if (options.title?.trim()) {
      body.title = options.title.trim();
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/wiki/v2/spaces/${encodeURIComponent(normalizedSpaceId)}/nodes`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify(body),
        }),
      "Failed to create Feishu wiki node.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const node = (data.node as Record<string, unknown> | undefined) ?? data;
    return mapWikiNodeInfo(node);
  }

  async updateWikiNodeTitle(spaceId: string, nodeToken: string, title: string): Promise<void> {
    const normalizedSpaceId = spaceId.trim();
    const normalizedNodeToken = nodeToken.trim();
    const normalizedTitle = title.trim();
    if (!normalizedSpaceId) {
      throw new TypeError("Feishu wiki title update requires a spaceId.");
    }
    if (!normalizedNodeToken) {
      throw new TypeError("Feishu wiki title update requires a nodeToken.");
    }
    if (!normalizedTitle) {
      throw new TypeError("Feishu wiki title update requires a non-empty title.");
    }

    const token = await this.getTenantAccessToken();
    await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/spaces/${encodeURIComponent(normalizedSpaceId)}/nodes/${encodeURIComponent(normalizedNodeToken)}/update_title`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              title: normalizedTitle,
            }),
          },
        ),
      "Failed to update Feishu wiki node title.",
    );
  }

  async getWikiNode(
    nodeToken: string,
    objType: FeishuWikiObjType = "wiki",
  ): Promise<FeishuWikiNodeInfo> {
    const normalizedNodeToken = nodeToken.trim();
    if (!normalizedNodeToken) {
      throw new TypeError("Feishu wiki node lookup requires a nodeToken.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    params.set("token", normalizedNodeToken);
    params.set("obj_type", objType);
    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/wiki/v2/spaces/get_node?${params.toString()}`, {
          method: "GET",
          headers: {
            Authorization: `Bearer ${token}`,
          },
        }),
      "Failed to get Feishu wiki node info.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const node = (data.node as Record<string, unknown> | undefined) ?? data;
    return mapWikiNodeInfo(node);
  }

  async listWikiSpaces(options: {
    pageSize?: number;
    pageToken?: string;
  } = {}): Promise<FeishuWikiSpaceListPage> {
    if (options.pageSize !== undefined && (!Number.isInteger(options.pageSize) || options.pageSize <= 0)) {
      throw new TypeError("Feishu wiki space listing requires pageSize to be a positive integer.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    if (options.pageSize !== undefined) {
      params.set("page_size", String(options.pageSize));
    }
    if (options.pageToken?.trim()) {
      params.set("page_token", options.pageToken.trim());
    }
    const query = params.toString();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/spaces${query ? `?${query}` : ""}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to list Feishu wiki spaces.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const rawItems = Array.isArray(data.items) ? (data.items as unknown[]) : [];
    return {
      items: rawItems.map((item) => mapWikiSpaceInfo(item)),
      nextPageToken: data.page_token ? String(data.page_token) : undefined,
      hasMore: data.has_more == null ? undefined : Boolean(data.has_more),
    };
  }

  async getWikiSpace(spaceId: string): Promise<FeishuWikiSpaceInfo> {
    const normalizedSpaceId = spaceId.trim();
    if (!normalizedSpaceId) {
      throw new TypeError("Feishu wiki space lookup requires a spaceId.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/spaces/${encodeURIComponent(normalizedSpaceId)}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to get Feishu wiki space info.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const space = (data.space as Record<string, unknown> | undefined) ?? data;
    return mapWikiSpaceInfo(space);
  }

  async listWikiNodes(options: {
    spaceId: string;
    parentNodeToken?: string;
    pageSize?: number;
    pageToken?: string;
  }): Promise<FeishuWikiNodeListPage> {
    const normalizedSpaceId = options.spaceId.trim();
    if (!normalizedSpaceId) {
      throw new TypeError("Feishu wiki node listing requires a spaceId.");
    }
    if (options.pageSize !== undefined && (!Number.isInteger(options.pageSize) || options.pageSize <= 0)) {
      throw new TypeError("Feishu wiki node listing requires pageSize to be a positive integer.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    if (options.parentNodeToken?.trim()) {
      params.set("parent_node_token", options.parentNodeToken.trim());
    }
    if (options.pageSize !== undefined) {
      params.set("page_size", String(options.pageSize));
    }
    if (options.pageToken?.trim()) {
      params.set("page_token", options.pageToken.trim());
    }
    const query = params.toString();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/spaces/${encodeURIComponent(normalizedSpaceId)}/nodes${query ? `?${query}` : ""}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to list Feishu wiki nodes.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const items = Array.isArray(data.items) ? data.items.map(mapWikiNodeInfo) : [];
    return {
      items,
      nextPageToken: data.page_token ? String(data.page_token) : undefined,
      hasMore: data.has_more == null ? undefined : Boolean(data.has_more),
    };
  }

  async moveDocxToWiki(options: {
    spaceId: string;
    objToken: string;
    objType?: "docx";
    parentWikiToken?: string;
    apply?: boolean;
  }): Promise<FeishuMoveDocxToWikiResult> {
    const normalizedSpaceId = options.spaceId.trim();
    if (!normalizedSpaceId) {
      throw new TypeError("Feishu move-to-wiki requires a spaceId.");
    }
    const normalizedObjToken = options.objToken.trim();
    if (!normalizedObjToken) {
      throw new TypeError("Feishu move-to-wiki requires an objToken.");
    }

    const token = await this.getTenantAccessToken();
    const body: Record<string, unknown> = {
      obj_type: options.objType ?? "docx",
      obj_token: normalizedObjToken,
    };
    if (options.parentWikiToken?.trim()) {
      body.parent_wiki_token = options.parentWikiToken.trim();
    }
    if (typeof options.apply === "boolean") {
      body.apply = options.apply;
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/spaces/${encodeURIComponent(normalizedSpaceId)}/nodes/move_docs_to_wiki`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify(body),
          },
        ),
      "Failed to move Feishu docx into wiki.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    return {
      wikiToken: data.wiki_token ? String(data.wiki_token) : undefined,
      taskId: data.task_id ? String(data.task_id) : undefined,
      applied: data.applied == null ? undefined : Boolean(data.applied),
    };
  }

  async getWikiMoveTask(taskId: string): Promise<FeishuWikiMoveTaskStatus> {
    const normalizedTaskId = taskId.trim();
    if (!normalizedTaskId) {
      throw new TypeError("Feishu wiki move task lookup requires a taskId.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    params.set("task_type", "move");
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/tasks/${encodeURIComponent(normalizedTaskId)}?${params.toString()}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to query Feishu wiki move task status.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const task = (data.task as Record<string, unknown> | undefined) ?? {};
    const moveResult = (task.move_result as Record<string, unknown> | undefined) ?? {};
    return {
      taskId: task.task_id ? String(task.task_id) : normalizedTaskId,
      status: Number(moveResult.status ?? task.status ?? 0),
      statusMessage: moveResult.status_msg ? String(moveResult.status_msg) : undefined,
      wikiToken: moveResult.wiki_token ? String(moveResult.wiki_token) : undefined,
      objToken: moveResult.obj_token ? String(moveResult.obj_token) : undefined,
      objType: moveResult.obj_type ? String(moveResult.obj_type) : undefined,
    };
  }

  async moveWikiNode(options: {
    spaceId: string;
    nodeToken: string;
    targetParentToken?: string;
    targetSpaceId?: string;
  }): Promise<FeishuWikiNodeInfo> {
    const normalizedSpaceId = options.spaceId.trim();
    if (!normalizedSpaceId) {
      throw new TypeError("Feishu wiki node move requires a spaceId.");
    }
    const normalizedNodeToken = options.nodeToken.trim();
    if (!normalizedNodeToken) {
      throw new TypeError("Feishu wiki node move requires a nodeToken.");
    }

    const token = await this.getTenantAccessToken();
    const body: Record<string, unknown> = {};
    if (options.targetParentToken?.trim()) {
      body.target_parent_token = options.targetParentToken.trim();
    }
    if (options.targetSpaceId?.trim()) {
      body.target_space_id = options.targetSpaceId.trim();
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/wiki/v2/spaces/${encodeURIComponent(normalizedSpaceId)}/nodes/${encodeURIComponent(normalizedNodeToken)}/move`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify(body),
          },
        ),
      "Failed to move Feishu wiki node.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    return mapWikiNodeInfo(data.node);
  }

  async getDocxDocument(documentId: string): Promise<FeishuDocxDocumentInfo> {
    const normalizedDocumentId = documentId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx lookup requires a documentId.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}`, {
          method: "GET",
          headers: {
            Authorization: `Bearer ${token}`,
          },
        }),
      "Failed to get Feishu docx document info.",
    );

    return this.parseDocxDocumentInfo(payload);
  }

  async getDocxRawContent(documentId: string): Promise<FeishuDocxRawContent> {
    const normalizedDocumentId = documentId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx raw content lookup requires a documentId.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}/raw_content`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to get Feishu docx raw content.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    return {
      documentId: normalizedDocumentId,
      content: String(data.content ?? ""),
    };
  }

  async listDocxComments(options: {
    fileToken: string;
    pageSize?: number;
    pageToken?: string;
    isSolved?: boolean;
    isWhole?: boolean;
  }): Promise<FeishuDocxCommentListPage> {
    const normalizedFileToken = options.fileToken.trim();
    if (!normalizedFileToken) {
      throw new TypeError("Feishu docx comment listing requires a fileToken.");
    }
    if (options.pageSize !== undefined && (!Number.isInteger(options.pageSize) || options.pageSize <= 0)) {
      throw new TypeError("Feishu docx comment listing requires pageSize to be a positive integer.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    params.set("file_type", "docx");
    if (options.pageSize !== undefined) {
      params.set("page_size", String(options.pageSize));
    }
    if (options.pageToken?.trim()) {
      params.set("page_token", options.pageToken.trim());
    }
    if (options.isSolved !== undefined) {
      params.set("is_solved", String(options.isSolved));
    }
    if (options.isWhole !== undefined) {
      params.set("is_whole", String(options.isWhole));
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/drive/v1/files/${encodeURIComponent(normalizedFileToken)}/comments?${params.toString()}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to list Feishu docx comments.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const items = Array.isArray(data.items)
      ? data.items
          .map((item) => mapDocxComment(item))
          .filter((item): item is FeishuDocxComment => item !== null)
      : [];
    return {
      fileToken: normalizedFileToken,
      items,
      nextPageToken: data.page_token ? String(data.page_token) : undefined,
      hasMore: data.has_more == null ? undefined : Boolean(data.has_more),
    };
  }

  async batchQueryDocxComments(options: {
    fileToken: string;
    commentIds: string[];
  }): Promise<FeishuDocxCommentBatchQueryResult> {
    const normalizedFileToken = options.fileToken.trim();
    if (!normalizedFileToken) {
      throw new TypeError("Feishu docx comment batch query requires a fileToken.");
    }
    if (!Array.isArray(options.commentIds) || options.commentIds.length === 0) {
      throw new TypeError("Feishu docx comment batch query requires at least one commentId.");
    }
    const normalizedCommentIds = options.commentIds
      .map((commentId) => commentId.trim())
      .filter((commentId) => commentId.length > 0);
    if (normalizedCommentIds.length === 0) {
      throw new TypeError("Feishu docx comment batch query requires non-empty commentIds.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/drive/v1/files/${encodeURIComponent(normalizedFileToken)}/comments/batch_query?file_type=docx`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              comment_ids: normalizedCommentIds,
            }),
          },
        ),
      "Failed to batch query Feishu docx comments.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const items = Array.isArray(data.items)
      ? data.items
          .map((item) => mapDocxComment(item))
          .filter((item): item is FeishuDocxComment => item !== null)
      : [];
    return {
      fileToken: normalizedFileToken,
      items,
    };
  }

  async listDocxCommentReplies(options: {
    fileToken: string;
    commentId: string;
    pageSize?: number;
    pageToken?: string;
  }): Promise<FeishuDocxCommentReplyListPage> {
    const normalizedFileToken = options.fileToken.trim();
    if (!normalizedFileToken) {
      throw new TypeError("Feishu docx comment reply listing requires a fileToken.");
    }
    const normalizedCommentId = options.commentId.trim();
    if (!normalizedCommentId) {
      throw new TypeError("Feishu docx comment reply listing requires a commentId.");
    }
    if (options.pageSize !== undefined && (!Number.isInteger(options.pageSize) || options.pageSize <= 0)) {
      throw new TypeError("Feishu docx comment reply listing requires pageSize to be a positive integer.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    params.set("file_type", "docx");
    if (options.pageSize !== undefined) {
      params.set("page_size", String(options.pageSize));
    }
    if (options.pageToken?.trim()) {
      params.set("page_token", options.pageToken.trim());
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/drive/v1/files/${encodeURIComponent(normalizedFileToken)}/comments/${encodeURIComponent(normalizedCommentId)}/replies?${params.toString()}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to list Feishu docx comment replies.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const itemsRaw = (Array.isArray(data.items) ? data.items : undefined) ?? (Array.isArray(data.replies) ? data.replies : undefined) ?? [];
    return {
      fileToken: normalizedFileToken,
      commentId: normalizedCommentId,
      items: itemsRaw
        .map((item) => mapDocxCommentReply(item))
        .filter((item): item is FeishuDocxCommentReply => item !== null),
      nextPageToken: data.page_token ? String(data.page_token) : undefined,
      hasMore: data.has_more == null ? undefined : Boolean(data.has_more),
    };
  }

  async createDocxComment(options: {
    fileToken: string;
    replyElements: Record<string, unknown>[];
    anchorBlockId?: string;
  }): Promise<FeishuDocxCommentCreateResult> {
    const normalizedFileToken = options.fileToken.trim();
    if (!normalizedFileToken) {
      throw new TypeError("Feishu docx comment creation requires a fileToken.");
    }
    if (!Array.isArray(options.replyElements) || options.replyElements.length === 0) {
      throw new TypeError("Feishu docx comment creation requires at least one reply element.");
    }
    const normalizedAnchorBlockId = options.anchorBlockId?.trim();

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/drive/v1/files/${encodeURIComponent(normalizedFileToken)}/new_comments`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            file_type: "docx",
            reply_elements: options.replyElements,
            ...(normalizedAnchorBlockId
              ? {
                  anchor: {
                    block_id: normalizedAnchorBlockId,
                  },
                }
              : {}),
          }),
        }),
      "Failed to create Feishu docx comment.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const commentId = String(data.comment_id ?? "");
    if (!commentId) {
      throw new Error("Feishu docx comment creation response did not include comment_id.");
    }
    return {
      fileToken: normalizedFileToken,
      commentId,
      createTime: data.created_at ? String(data.created_at) : data.create_time ? String(data.create_time) : undefined,
    };
  }

  async createDocxCommentReply(options: {
    fileToken: string;
    commentId: string;
    replyElements: Record<string, unknown>[];
  }): Promise<FeishuDocxCommentReplyCreateResult> {
    const normalizedFileToken = options.fileToken.trim();
    if (!normalizedFileToken) {
      throw new TypeError("Feishu docx comment reply creation requires a fileToken.");
    }
    const normalizedCommentId = options.commentId.trim();
    if (!normalizedCommentId) {
      throw new TypeError("Feishu docx comment reply creation requires a commentId.");
    }
    if (!Array.isArray(options.replyElements) || options.replyElements.length === 0) {
      throw new TypeError("Feishu docx comment reply creation requires at least one reply element.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/drive/v1/files/${encodeURIComponent(normalizedFileToken)}/comments/${encodeURIComponent(normalizedCommentId)}/replies?file_type=docx`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              reply_elements: options.replyElements,
            }),
          },
        ),
      "Failed to create Feishu docx comment reply.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const replyId = String(data.reply_id ?? "");
    if (!replyId) {
      throw new Error("Feishu docx comment reply creation response did not include reply_id.");
    }
    return {
      fileToken: normalizedFileToken,
      commentId: normalizedCommentId,
      replyId,
      createTime: data.created_at ? String(data.created_at) : data.create_time ? String(data.create_time) : undefined,
    };
  }

  async patchDocxCommentSolved(options: {
    fileToken: string;
    commentId: string;
    isSolved: boolean;
  }): Promise<void> {
    const normalizedFileToken = options.fileToken.trim();
    if (!normalizedFileToken) {
      throw new TypeError("Feishu docx comment patch requires a fileToken.");
    }
    const normalizedCommentId = options.commentId.trim();
    if (!normalizedCommentId) {
      throw new TypeError("Feishu docx comment patch requires a commentId.");
    }

    const token = await this.getTenantAccessToken();
    await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/drive/v1/files/${encodeURIComponent(normalizedFileToken)}/comments/${encodeURIComponent(normalizedCommentId)}?file_type=docx`,
          {
            method: "PATCH",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              is_solved: options.isSolved,
            }),
          },
        ),
      "Failed to patch Feishu docx comment solved state.",
    );
  }

  async listDocxBlocks(options: {
    documentId: string;
    pageSize?: number;
    pageToken?: string;
  }): Promise<FeishuDocxBlockListPage> {
    const normalizedDocumentId = options.documentId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx block listing requires a documentId.");
    }
    if (options.pageSize !== undefined && (!Number.isInteger(options.pageSize) || options.pageSize <= 0)) {
      throw new TypeError("Feishu docx block listing requires pageSize to be a positive integer.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    if (options.pageSize !== undefined) {
      params.set("page_size", String(options.pageSize));
    }
    if (options.pageToken?.trim()) {
      params.set("page_token", options.pageToken.trim());
    }
    const query = params.toString();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}/blocks${query ? `?${query}` : ""}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to list Feishu docx blocks.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const items = Array.isArray(data.items)
      ? data.items
          .map((item) => mapDocxBlockInfo(item))
          .filter((item): item is FeishuDocxBlockInfo => item !== null)
      : [];
    return {
      documentId: normalizedDocumentId,
      items,
      nextPageToken: data.page_token ? String(data.page_token) : undefined,
      hasMore: data.has_more == null ? undefined : Boolean(data.has_more),
    };
  }

  async getDocxBlock(documentId: string, blockId: string): Promise<FeishuDocxBlockInfo> {
    const normalizedDocumentId = documentId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx block lookup requires a documentId.");
    }
    const normalizedBlockId = blockId.trim();
    if (!normalizedBlockId) {
      throw new TypeError("Feishu docx block lookup requires a blockId.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}/blocks/${encodeURIComponent(normalizedBlockId)}`,
          {
            method: "GET",
            headers: {
              Authorization: `Bearer ${token}`,
            },
          },
        ),
      "Failed to get Feishu docx block.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const blockRaw = (data.block as Record<string, unknown> | undefined) ?? data;
    const mapped = mapDocxBlockInfo(blockRaw);
    if (!mapped) {
      throw new Error("Feishu docx block response did not include block_id/block_type.");
    }
    return mapped;
  }

  async updateDocxBlocks(
    documentId: string,
    requests: Record<string, unknown>[],
  ): Promise<FeishuDocxBlockBatchUpdateResult> {
    const normalizedDocumentId = documentId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx block update requires a documentId.");
    }
    if (!Array.isArray(requests) || requests.length === 0) {
      throw new TypeError("Feishu docx block update requires at least one request.");
    }

    const token = await this.getTenantAccessToken();
    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}/blocks/batch_update`,
          {
            method: "PATCH",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({ requests }),
          },
        ),
      "Failed to batch update Feishu docx blocks.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const updatedRaw =
      (Array.isArray(data.items) ? data.items : undefined) ??
      (Array.isArray(data.updated_blocks) ? data.updated_blocks : undefined) ??
      [];
    return {
      documentId: normalizedDocumentId,
      revisionId: parseOptionalNumber(data.document_revision_id ?? data.revision_id),
      updatedBlocks: updatedRaw
        .map((item) => mapDocxBlockInfo(item))
        .filter((item): item is FeishuDocxBlockInfo => item !== null),
    };
  }

  async deleteDocxBlockChildren(
    documentId: string,
    parentBlockId: string,
    startIndex: number,
    endIndex: number,
  ): Promise<FeishuDocxBlockDeleteResult> {
    const normalizedDocumentId = documentId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx child deletion requires a documentId.");
    }
    const normalizedParentBlockId = parentBlockId.trim();
    if (!normalizedParentBlockId) {
      throw new TypeError("Feishu docx child deletion requires a parentBlockId.");
    }
    if (!Number.isInteger(startIndex) || !Number.isInteger(endIndex) || startIndex < 0 || endIndex <= startIndex) {
      throw new TypeError("Feishu docx child deletion requires valid [startIndex, endIndex) range.");
    }

    const token = await this.getTenantAccessToken();
    await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}/blocks/${encodeURIComponent(normalizedParentBlockId)}/children/batch_delete`,
          {
            method: "DELETE",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              start_index: startIndex,
              end_index: endIndex,
            }),
          },
        ),
      "Failed to delete Feishu docx child blocks.",
    );
    return {
      documentId: normalizedDocumentId,
      parentBlockId: normalizedParentBlockId,
      startIndex,
      endIndex,
    };
  }

  async uploadDocxMedia(params: {
    filePath: string;
    parentType: "docx_image" | "docx_file";
    parentNode: string;
    documentId?: string;
  }): Promise<FeishuDriveMediaUploadResult> {
    const normalizedPath = params.filePath.trim();
    if (!normalizedPath) {
      throw new TypeError("Feishu docx media upload requires filePath.");
    }
    const normalizedParentNode = params.parentNode.trim();
    if (!normalizedParentNode) {
      throw new TypeError("Feishu docx media upload requires parentNode.");
    }

    const fileBuffer = await readFile(normalizedPath);
    const fileName = path.basename(normalizedPath);
    const token = await this.getTenantAccessToken();
    const formData = new FormData();
    formData.set("file_name", fileName);
    formData.set("parent_type", params.parentType);
    formData.set("parent_node", normalizedParentNode);
    formData.set("size", String(fileBuffer.length));
    if (params.documentId?.trim()) {
      formData.set("extra", JSON.stringify({ drive_route_token: params.documentId.trim() }));
    }
    formData.set("file", new Blob([fileBuffer], { type: inferMediaType(fileName) }), fileName);

    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/drive/v1/medias/upload_all`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
          },
          body: formData,
        }),
      "Failed to upload media into Feishu docx.",
    );
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const fileToken = String(data.file_token ?? "");
    if (!fileToken) {
      throw new Error("Feishu drive media upload response did not include file_token.");
    }
    return {
      fileToken,
      fileName: data.file_name ? String(data.file_name) : fileName,
      size: parseOptionalNumber(data.size) ?? fileBuffer.length,
    };
  }

  async createDocxBlocks(
    documentId: string,
    blockId: string,
    options: {
      children: Record<string, unknown>[];
      documentRevisionId?: number;
      index?: number;
      clientToken?: string;
    },
  ): Promise<FeishuDocxBlockCreateResult> {
    const normalizedDocumentId = documentId.trim();
    const normalizedBlockId = blockId.trim();
    if (!normalizedDocumentId) {
      throw new TypeError("Feishu docx block creation requires a documentId.");
    }
    if (!normalizedBlockId) {
      throw new TypeError("Feishu docx block creation requires a blockId.");
    }
    if (!Array.isArray(options.children) || options.children.length === 0) {
      throw new TypeError("Feishu docx block creation requires at least one child block.");
    }

    const token = await this.getTenantAccessToken();
    const params = new URLSearchParams();
    params.set("document_revision_id", String(options.documentRevisionId ?? -1));
    if (options.clientToken?.trim()) {
      params.set("client_token", options.clientToken.trim());
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(
          `${this.apiBaseUrl}/open-apis/docx/v1/documents/${encodeURIComponent(normalizedDocumentId)}/blocks/${encodeURIComponent(normalizedBlockId)}/children?${params.toString()}`,
          {
            method: "POST",
            headers: {
              Authorization: `Bearer ${token}`,
              "Content-Type": "application/json",
            },
            body: JSON.stringify({
              children: options.children,
              index: options.index ?? -1,
            }),
          },
        ),
      "Failed to append Feishu docx blocks.",
    );

    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const blocksRaw =
      (Array.isArray(data.children) ? data.children : undefined) ??
      (Array.isArray(data.items) ? data.items : undefined) ??
      [];

    return {
      documentId: normalizedDocumentId,
      revisionId: parseOptionalNumber(data.document_revision_id ?? data.revision_id),
      blocks: blocksRaw
        .map((item) => mapDocxBlockInfo(item))
        .filter((item): item is FeishuDocxBlockCreateResult["blocks"][number] => item !== null),
    };
  }

  async startEventStream(handlers: FeishuEventHandlers, abortSignal?: AbortSignal): Promise<void> {
    logInfo("feishu.ws.starting");
    const dispatcher = new Lark.EventDispatcher({
      encryptKey: this.config.encryptKey ?? "",
      verificationToken: this.config.verificationToken ?? "",
    });
    dispatcher.register({
      "im.message.receive_v1": (data: unknown) => handlers.onMessage(data as FeishuMessageEvent),
      "card.action.trigger": (data: unknown) => handlers.onCardAction(data as FeishuCardActionEvent),
    } as never);

    if (this.wsClient) {
      try {
        logWarn("feishu.ws.replacing_existing_client");
        this.wsClient.close({ force: true });
      } catch {
      }
    }

    this.wsClient = new Lark.WSClient({
      appId: this.appId,
      appSecret: this.appSecret,
      domain: this.domain,
      loggerLevel: Lark.LoggerLevel.info,
    });

    const wsClientAny = this.wsClient as unknown as {
      handleEventData: (data: Record<string, unknown>) => unknown;
    };
    const originalHandleEventData = wsClientAny.handleEventData.bind(wsClientAny);
    let loggedCardPatch = false;
    wsClientAny.handleEventData = (data: Record<string, unknown>) => {
      const headers = Array.isArray(data.headers) ? (data.headers as Array<Record<string, unknown>>) : [];
      const messageType = headers.find((header) => header.key === "type")?.value;
      if (messageType === "card") {
        if (!loggedCardPatch) {
          loggedCardPatch = true;
          logInfo("feishu.ws.card_event_patch_enabled");
        }
        const patchedHeaders = headers.map((header) =>
          header.key === "type" ? { ...header, value: "event" } : header,
        );
        return originalHandleEventData({ ...data, headers: patchedHeaders });
      }
      return originalHandleEventData(data);
    };

    const startPromise = this.wsClient.start({ eventDispatcher: dispatcher });
    if (!abortSignal) {
      await startPromise;
      logInfo("feishu.ws.started");
      return;
    }

    await new Promise<void>((resolve, reject) => {
      if (abortSignal.aborted) {
        logInfo("feishu.ws.abort_requested", { reason: "pre_aborted" });
        this.stopEventStream();
        resolve();
        return;
      }

      abortSignal.addEventListener(
        "abort",
        () => {
          logInfo("feishu.ws.abort_requested", { reason: "signal" });
          this.stopEventStream();
          resolve();
        },
        { once: true },
      );

      void startPromise.catch((error) => {
        logError("feishu.ws.start_failed", { message: errorMessage(error) });
        this.stopEventStream();
        reject(error);
      });
      void startPromise.then(() => {
        logInfo("feishu.ws.started");
      });
    });
  }

  stopEventStream(): void {
    if (!this.wsClient) return;
    try {
      this.wsClient.close({ force: true });
    } catch {
    }
    this.wsClient = null;
    logInfo("feishu.ws.stopped");
  }

  async sendInteractiveCard(
    target: string,
    card: Record<string, unknown>,
  ): Promise<FeishuSendResult> {
    assertCardPayloadShape(card);
    const { receiveId, receiveIdType } = this.resolveTarget(target);
    const response = await this.callSdk(
      () =>
        this.sdk.im.message.create({
          params: {
            receive_id_type: receiveIdType as never,
          },
          data: {
            receive_id: receiveId,
            msg_type: "interactive",
            content: JSON.stringify(card),
          },
        }),
      "Failed to send Feishu interactive message",
    );

    return {
      messageId: String(response.data?.message_id ?? ""),
      chatId: String(response.data?.chat_id ?? ""),
    };
  }

  async addMessageReaction(messageId: string, emojiType: string): Promise<void> {
    const normalizedMessageId = messageId.trim();
    const normalizedEmojiType = emojiType.trim();
    if (!normalizedMessageId) {
      throw new TypeError("Feishu message reaction requires a messageId.");
    }
    if (!normalizedEmojiType) {
      throw new TypeError("Feishu message reaction requires an emojiType.");
    }

    await this.callSdk(
      () =>
        this.sdk.im.messageReaction.create({
          path: {
            message_id: normalizedMessageId,
          },
          data: {
            reaction_type: {
              emoji_type: normalizedEmojiType,
            },
          },
        }),
      "Failed to add Feishu message reaction",
    );
  }

  async updateInteractiveCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    assertCardPayloadShape(card);
    await this.callSdk(
      () =>
        (this.sdk as unknown as {
          request: (request: Record<string, unknown>) => Promise<unknown>;
        }).request({
          method: "PATCH",
          url: `/open-apis/im/v1/messages/${messageId}`,
          data: {
            content: JSON.stringify(card),
          },
        }),
      "Failed to update Feishu interactive card",
    );
  }

  async sendFile(
    target: string,
    file: {
      fileName: string;
      data: Buffer;
      mediaType?: string;
    },
  ): Promise<FeishuSendResult & { fileKey: string }> {
    if (!file.fileName.trim()) {
      throw new TypeError("Feishu file delivery requires a fileName.");
    }
    if (file.data.length === 0) {
      throw new TypeError("Feishu file delivery does not support empty files.");
    }
    if (file.data.length > 30 * 1024 * 1024) {
      throw new TypeError("Feishu file delivery only supports files up to 30 MB.");
    }

    const fileKey = await this.uploadFile(file.fileName, file.data, file.mediaType);
    const { receiveId, receiveIdType } = this.resolveTarget(target);
    const response = await this.sendMessage(receiveId, receiveIdType, "file", {
      file_key: fileKey,
    });
    const responseData = (response.data as Record<string, unknown> | undefined) ?? {};

    return {
      messageId: String(responseData.message_id ?? ""),
      chatId: String(responseData.chat_id ?? ""),
      fileKey,
    };
  }

  async downloadMessageImage(messageId: string, imageKey: string, downloadDir?: string): Promise<string> {
    const response = await this.callSdk(
      () =>
        this.sdk.im.messageResource.get({
          path: {
            message_id: messageId,
            file_key: imageKey,
          },
          params: {
            type: "image",
          },
        }),
      "Failed to download Feishu image",
    );

    const { buffer, contentType } = await extractBufferFromResponse(response);
    const extension = extensionFromContentType(contentType);
    const dir = downloadDir ? path.resolve(downloadDir) : path.join(os.tmpdir(), "dotcraft-feishu");
    fs.mkdirSync(dir, { recursive: true });
    const filePath = path.join(dir, `feishu-${messageId}-${Date.now()}${extension}`);
    fs.writeFileSync(filePath, buffer);
    return filePath;
  }

  private resolveTarget(target: string): { receiveId: string; receiveIdType: "chat_id" | "open_id" } {
    if (target.startsWith("group:")) {
      return {
        receiveId: target.slice("group:".length),
        receiveIdType: "chat_id",
      };
    }
    if (target.startsWith("dm:")) {
      return {
        receiveId: target.slice("dm:".length),
        receiveIdType: "open_id",
      };
    }
    return {
      receiveId: target,
      receiveIdType: "chat_id",
    };
  }

  private async uploadFile(fileName: string, data: Buffer, mediaType?: string): Promise<string> {
    const token = await this.getTenantAccessToken();
    const formData = new FormData();
    formData.set("file_type", toFeishuFileType(fileName, mediaType));
    formData.set("file_name", fileName);
    formData.set("file", new Blob([data], { type: mediaType ?? inferMediaType(fileName) }), fileName);

    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/im/v1/files`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
          },
          body: formData,
        }),
      `Failed to upload file '${fileName}' to Feishu.`,
    );

    const dataNode = (payload.data as Record<string, unknown> | undefined) ?? {};
    const fileKey = String(dataNode.file_key ?? "");
    if (!fileKey) {
      throw new Error("Feishu upload response did not include file_key.");
    }
    return fileKey;
  }

  private async sendMessage(
    receiveId: string,
    receiveIdType: "chat_id" | "open_id",
    msgType: string,
    content: Record<string, unknown>,
  ): Promise<Record<string, unknown>> {
    const token = await this.getTenantAccessToken();
    return await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/im/v1/messages?receive_id_type=${encodeURIComponent(receiveIdType)}`, {
          method: "POST",
          headers: {
            Authorization: `Bearer ${token}`,
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            receive_id: receiveId,
            msg_type: msgType,
            content: JSON.stringify(content),
          }),
        }),
      `Failed to send Feishu '${msgType}' message.`,
    );
  }

  private async getTenantAccessToken(): Promise<string> {
    const now = Date.now();
    if (this.tenantAccessToken && now < this.tenantAccessTokenExpiresAt) {
      return this.tenantAccessToken;
    }

    const payload = await this.callJsonApi(
      () =>
        fetch(`${this.apiBaseUrl}/open-apis/auth/v3/tenant_access_token/internal`, {
          method: "POST",
          headers: {
            "Content-Type": "application/json",
          },
          body: JSON.stringify({
            app_id: this.appId,
            app_secret: this.appSecret,
          }),
        }),
      "Failed to obtain Feishu tenant access token.",
    );

    const token = String(payload.tenant_access_token ?? "");
    if (!token) {
      throw new Error("Feishu auth response did not include tenant_access_token.");
    }

    const expiresInSeconds = Math.max(60, Number(payload.expire ?? payload.expires_in ?? 7200));
    this.tenantAccessToken = token;
    this.tenantAccessTokenExpiresAt = now + (expiresInSeconds - 60) * 1000;
    return token;
  }

  private async callSdk<T>(run: () => Promise<T>, defaultMessage: string): Promise<T> {
    try {
      const response = await run();
      const payload = toFeishuApiPayload(response);
      if (payload.code !== undefined && payload.code !== 0) {
        throw createFeishuApiError({
          defaultMessage,
          code: payload.code,
          msg: payload.msg,
          raw: payload.raw,
        });
      }
      return response;
    } catch (error) {
      throw normalizeFeishuError(error, defaultMessage);
    }
  }

  private async callJsonApi(
    run: () => Promise<Response>,
    defaultMessage: string,
  ): Promise<Record<string, unknown>> {
    let response: Response;
    try {
      response = await run();
    } catch (error) {
      throw normalizeFeishuError(error, defaultMessage);
    }

    let payload: unknown;
    try {
      payload = await response.json();
    } catch (error) {
      throw normalizeFeishuError(error, defaultMessage);
    }

    const apiPayload = toFeishuApiPayload(payload);
    if (!response.ok || (apiPayload.code !== undefined && apiPayload.code !== 0)) {
      throw createFeishuApiError({
        defaultMessage,
        code: apiPayload.code,
        msg: apiPayload.msg,
        httpStatus: response.status,
        raw: apiPayload.raw,
      });
    }

    return apiPayload.raw as Record<string, unknown>;
  }

  private parseDocxDocumentInfo(payload: Record<string, unknown>): FeishuDocxDocumentInfo {
    const data = (payload.data as Record<string, unknown> | undefined) ?? {};
    const document = (data.document as Record<string, unknown> | undefined) ?? data;
    const documentId = String(document.document_id ?? data.document_id ?? "");
    if (!documentId) {
      throw new Error("Feishu docx response did not include document_id.");
    }

    return {
      documentId,
      revisionId: parseOptionalNumber(document.revision_id ?? data.revision_id) ?? 0,
      title: String(document.title ?? data.title ?? ""),
      url: String(document.url ?? data.url ?? "") || this.buildDocxUrl(documentId),
    };
  }

  private buildDocxUrl(documentId: string): string {
    const host = this.config.brand === "lark" ? "https://larksuite.com" : "https://feishu.cn";
    return `${host}/docx/${documentId}`;
  }
}

function normalizeFeishuError(error: unknown, defaultMessage: string): Error {
  if (error instanceof FeishuApiError || error instanceof TypeError) {
    return error;
  }

  const payload = toFeishuApiPayload(error);
  if (payload.code !== undefined || payload.msg !== undefined) {
    return createFeishuApiError({
      defaultMessage,
      code: payload.code,
      msg: payload.msg,
      raw: payload.raw,
    });
  }

  if (error instanceof Error) {
    return createFeishuApiError({
      defaultMessage,
      msg: error.message,
      raw: error,
      cause: error,
    });
  }

  return createFeishuApiError({
    defaultMessage,
    raw: error,
  });
}

function createFeishuApiError(params: {
  defaultMessage: string;
  code?: number;
  msg?: string;
  httpStatus?: number;
  raw?: unknown;
  cause?: unknown;
}): FeishuApiError {
  const kind = classifyFeishuApiError(params.code, params.httpStatus);
  return new FeishuApiError({
    kind,
    retryable: isRetryableFeishuError(kind, params.httpStatus),
    code: params.code,
    msg: params.msg,
    httpStatus: params.httpStatus,
    raw: params.raw,
    cause: params.cause,
    message: formatFeishuErrorMessage(params.defaultMessage, params.msg, params.code, params.httpStatus),
  });
}

function toFeishuApiPayload(input: unknown): FeishuApiPayload {
  if (!input || typeof input !== "object") {
    return { raw: input };
  }
  const record = input as Record<string, unknown>;
  const code = parseOptionalNumber(record.code);
  const msg = typeof record.msg === "string" ? record.msg : undefined;
  const data = record.data && typeof record.data === "object" ? (record.data as Record<string, unknown>) : undefined;
  return { code, msg, data, raw: input };
}

function parseOptionalNumber(value: unknown): number | undefined {
  if (typeof value === "number" && Number.isFinite(value)) return value;
  if (typeof value === "string" && value.trim()) {
    const parsed = Number(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return undefined;
}

function classifyFeishuApiError(code?: number, httpStatus?: number): FeishuApiErrorKind {
  if (httpStatus === 401) return "auth";
  if (httpStatus === 403) return "permission";
  if (httpStatus === 400 || httpStatus === 404 || httpStatus === 422) return "invalidArgument";
  if (httpStatus === 429) return "rateLimited";
  if (httpStatus !== undefined && httpStatus >= 500) return "upstream";

  if (code === undefined) return "unknown";
  if (AUTH_CODES.has(code)) return "auth";
  if (PERMISSION_CODES.has(code)) return "permission";
  if (INVALID_ARGUMENT_CODES.has(code)) return "invalidArgument";
  if (RATE_LIMIT_CODES.has(code)) return "rateLimited";
  if (UPSTREAM_CODES.has(code) || code >= 90000) return "upstream";
  return "unknown";
}

function isRetryableFeishuError(kind: FeishuApiErrorKind, httpStatus?: number): boolean {
  return kind === "rateLimited" || kind === "upstream" || httpStatus === 408;
}

function formatFeishuErrorMessage(
  defaultMessage: string,
  msg?: string,
  code?: number,
  httpStatus?: number,
): string {
  const details: string[] = [];
  if (msg?.trim()) details.push(msg.trim());
  if (code !== undefined) details.push(`code=${code}`);
  if (httpStatus !== undefined) details.push(`httpStatus=${httpStatus}`);
  return details.length ? `${defaultMessage} ${details.join(" ")}` : defaultMessage;
}

function classifyBotDiagnosticTag(
  response: Record<string, unknown>,
  data: Record<string, unknown>,
  bot: Record<string, unknown>,
): FeishuBotDiagnosticTag {
  const capabilityHints = [
    data.activate_status,
    response.activate_status,
    bot.activate_status,
    data.status,
    response.status,
    bot.status,
    data.bot_status,
    response.bot_status,
  ]
    .map((value) => (value == null ? "" : String(value).toLowerCase()))
    .filter((value) => value.length > 0);
  const hasCapabilityDisabledHint =
    capabilityHints.some((value) => value === "0" || value.includes("disabled") || value.includes("unpublished")) ||
    capabilityHints.some((value) => value.includes("inactive"));

  return hasCapabilityDisabledHint ? "botCapabilityDisabled" : "identityFieldsMissing";
}

function mapChatMessageItem(input: unknown): FeishuChatMessageItem {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : {};
  const senderRecord =
    record.sender && typeof record.sender === "object" ? (record.sender as Record<string, unknown>) : {};
  const senderIdRecord =
    senderRecord.id && typeof senderRecord.id === "object"
      ? (senderRecord.id as Record<string, unknown>)
      : senderRecord.sender_id && typeof senderRecord.sender_id === "object"
        ? (senderRecord.sender_id as Record<string, unknown>)
        : {};
  const mentions = Array.isArray(record.mentions)
    ? record.mentions
        .map(mapMention)
        .filter((mention): mention is FeishuChatMessageItem["mentions"][number] => mention !== null)
    : [];
  const bodyRecord = record.body && typeof record.body === "object" ? (record.body as Record<string, unknown>) : {};

  return {
    messageId: String(record.message_id ?? ""),
    chatId: String(record.chat_id ?? ""),
    chatType:
      record.chat_type === "p2p" || record.chat_type === "group"
        ? (record.chat_type as "p2p" | "group")
        : undefined,
    messageType: String(record.msg_type ?? record.message_type ?? ""),
    createTime: record.create_time ? String(record.create_time) : undefined,
    parentId: record.parent_id ? String(record.parent_id) : undefined,
    rootId: record.root_id ? String(record.root_id) : undefined,
    sender: {
      openId: senderIdRecord.open_id ? String(senderIdRecord.open_id) : undefined,
      userId: senderIdRecord.user_id ? String(senderIdRecord.user_id) : undefined,
      unionId: senderIdRecord.union_id ? String(senderIdRecord.union_id) : undefined,
      senderType: senderRecord.sender_type ? String(senderRecord.sender_type) : undefined,
      tenantKey: senderRecord.tenant_key ? String(senderRecord.tenant_key) : undefined,
    },
    mentions,
    rawContent: bodyRecord.content == null ? "" : String(bodyRecord.content),
  };
}

function mapDocxBlockInfo(input: unknown): FeishuDocxBlockInfo | null {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : null;
  if (!record) return null;
  const blockId = String(record.block_id ?? "");
  const blockType = parseOptionalNumber(record.block_type);
  if (!blockId || blockType === undefined) return null;
  const children = Array.isArray(record.children)
    ? record.children.map((child) => String(child ?? "")).filter((child) => child.length > 0)
    : undefined;
  const parentId = record.parent_id ? String(record.parent_id) : undefined;
  const textContent = pickDocxBlockText(record);
  return {
    blockId,
    blockType,
    ...(parentId ? { parentId } : {}),
    ...(children && children.length > 0 ? { children } : {}),
    ...(textContent ? { textContent } : {}),
  };
}

function mapDocxCommentContentElement(input: unknown): FeishuDocxCommentContentElement | null {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : null;
  if (!record) return null;
  const type = String(record.type ?? "").trim();
  if (!type) return null;
  let text = record.text ? String(record.text) : undefined;
  let mentionUser = record.mention_user ? String(record.mention_user) : undefined;
  let link = record.link ? String(record.link) : undefined;

  if (type === "text_run") {
    const textRun = record.text_run && typeof record.text_run === "object"
      ? (record.text_run as Record<string, unknown>)
      : undefined;
    text = text ?? optionalString(textRun?.text) ?? optionalString(textRun?.content);
    link = link ?? optionalString(textRun?.link) ?? optionalString(textRun?.url);
  } else if (type === "docs_link") {
    const docsLink = record.docs_link && typeof record.docs_link === "object"
      ? (record.docs_link as Record<string, unknown>)
      : undefined;
    link = link ?? optionalString(docsLink?.url) ?? optionalString(docsLink?.link);
  } else if (type === "person") {
    const person = record.person && typeof record.person === "object"
      ? (record.person as Record<string, unknown>)
      : undefined;
    mentionUser =
      mentionUser ??
      optionalString(person?.user_id) ??
      optionalString(person?.open_id) ??
      optionalString(person?.openid);
  }

  return {
    type,
    ...(text ? { text } : {}),
    ...(mentionUser ? { mentionUser } : {}),
    ...(link ? { link } : {}),
  };
}

function mapDocxCommentContent(input: unknown): FeishuDocxCommentContent | undefined {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : null;
  if (!record) return undefined;
  const elementsRaw =
    (Array.isArray(record.elements) ? record.elements : undefined) ??
    (Array.isArray(record.reply_elements) ? record.reply_elements : undefined) ??
    [];
  const elements = elementsRaw
    .map((item) => mapDocxCommentContentElement(item))
    .filter((item): item is NonNullable<typeof item> => item !== null);
  if (elements.length === 0) return undefined;
  return { elements };
}

function mapDocxCommentReply(input: unknown): FeishuDocxCommentReply | null {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : null;
  if (!record) return null;
  const replyId = String(record.reply_id ?? "");
  if (!replyId) return null;
  const contentRecord =
    (record.content && typeof record.content === "object" ? (record.content as Record<string, unknown>) : undefined) ?? record;
  const content = mapDocxCommentContent(contentRecord);
  const text = pickDocxCommentReplyText(content);
  return {
    replyId,
    userId: record.user_id ? String(record.user_id) : undefined,
    createTime: record.create_time ? String(record.create_time) : undefined,
    updateTime: record.update_time ? String(record.update_time) : undefined,
    isSolved: record.is_solved == null ? undefined : Boolean(record.is_solved),
    ...(text ? { text } : {}),
    ...(content ? { content } : {}),
  };
}

function pickDocxCommentReplyText(content: FeishuDocxCommentContent | undefined): string | undefined {
  if (!content) return undefined;
  const joined = content.elements
    .map((element) => element.text ?? element.link ?? element.mentionUser ?? "")
    .join("");
  const normalized = joined.trim();
  return normalized ? normalized : undefined;
}

function optionalString(value: unknown): string | undefined {
  if (value == null) return undefined;
  const normalized = String(value).trim();
  return normalized ? normalized : undefined;
}

function mapDocxComment(input: unknown): FeishuDocxComment | null {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : null;
  if (!record) return null;
  const commentId = String(record.comment_id ?? "");
  if (!commentId) return null;
  const quoteRecord = record.quote && typeof record.quote === "object" ? (record.quote as Record<string, unknown>) : undefined;
  const quoteContent = mapDocxCommentContent(quoteRecord?.content);
  const replyList = record.reply_list && typeof record.reply_list === "object"
    ? (record.reply_list as Record<string, unknown>)
    : undefined;
  const repliesRaw: unknown[] =
    (Array.isArray(replyList?.replies) ? (replyList?.replies as unknown[]) : undefined) ??
    (Array.isArray(record.replies) ? (record.replies as unknown[]) : undefined) ??
    [];
  const replies = repliesRaw
    .map((item) => mapDocxCommentReply(item))
    .filter((item): item is FeishuDocxCommentReply => item !== null);

  return {
    commentId,
    userId: record.user_id ? String(record.user_id) : undefined,
    createTime: record.create_time ? String(record.create_time) : undefined,
    updateTime: record.update_time ? String(record.update_time) : undefined,
    isSolved: record.is_solved == null ? undefined : Boolean(record.is_solved),
    isWhole: record.is_whole == null ? undefined : Boolean(record.is_whole),
    ...(quoteRecord
      ? {
          quote: {
            blockId: quoteRecord.block_id ? String(quoteRecord.block_id) : undefined,
            ...(quoteContent ? { content: quoteContent } : {}),
          },
        }
      : {}),
    replyList: {
      replies,
      hasMore: replyList?.has_more == null ? undefined : Boolean(replyList.has_more),
    },
  };
}

function pickDocxBlockText(record: Record<string, unknown>): string | undefined {
  const blockFields = [
    "text",
    "heading1",
    "heading2",
    "heading3",
    "heading4",
    "heading5",
    "heading6",
    "heading7",
    "heading8",
    "heading9",
    "quote",
    "code",
    "bullet",
    "ordered",
    "todo",
  ];
  for (const field of blockFields) {
    const block = record[field];
    if (!block || typeof block !== "object") continue;
    const elements = (block as Record<string, unknown>).elements;
    if (!Array.isArray(elements)) continue;
    const text = elements
      .map((element) => {
        if (!element || typeof element !== "object") return "";
        const textRun = (element as Record<string, unknown>).text_run;
        if (!textRun || typeof textRun !== "object") return "";
        return String((textRun as Record<string, unknown>).content ?? "");
      })
      .join("");
    if (text) return text;
  }
  return undefined;
}

function mapWikiNodeInfo(input: unknown): FeishuWikiNodeInfo {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : {};
  return {
    spaceId: String(record.space_id ?? ""),
    nodeToken: String(record.node_token ?? ""),
    objToken: String(record.obj_token ?? ""),
    objType: String(record.obj_type ?? ""),
    nodeType: String(record.node_type ?? ""),
    parentNodeToken: record.parent_node_token ? String(record.parent_node_token) : undefined,
    originNodeToken: record.origin_node_token ? String(record.origin_node_token) : undefined,
    originSpaceId: record.origin_space_id ? String(record.origin_space_id) : undefined,
    hasChild: record.has_child == null ? undefined : Boolean(record.has_child),
    title: record.title ? String(record.title) : undefined,
    objCreateTime: record.obj_create_time ? String(record.obj_create_time) : undefined,
    objEditTime: record.obj_edit_time ? String(record.obj_edit_time) : undefined,
    nodeCreateTime: record.node_create_time ? String(record.node_create_time) : undefined,
  };
}

function mapWikiSpaceInfo(input: unknown): FeishuWikiSpaceInfo {
  const record = input && typeof input === "object" ? (input as Record<string, unknown>) : {};
  return {
    spaceId: String(record.space_id ?? ""),
    name: record.name ? String(record.name) : undefined,
    description: record.description ? String(record.description) : undefined,
    visibility: record.visibility ? String(record.visibility) : undefined,
    spaceType: record.space_type ? String(record.space_type) : undefined,
    openSharing: record.open_sharing ? String(record.open_sharing) : undefined,
  };
}

function mapMention(input: unknown): FeishuChatMessageItem["mentions"][number] | null {
  if (!input || typeof input !== "object") return null;
  const record = input as Record<string, unknown>;
  const id = record.id && typeof record.id === "object" ? (record.id as Record<string, unknown>) : {};
  return {
    key: String(record.key ?? ""),
    id: {
      open_id: id.open_id ? String(id.open_id) : undefined,
      user_id: id.user_id ? String(id.user_id) : undefined,
      union_id: id.union_id ? String(id.union_id) : undefined,
    },
    name: String(record.name ?? ""),
    tenant_key: record.tenant_key ? String(record.tenant_key) : undefined,
  };
}

function assertCardPayloadShape(card: Record<string, unknown>): void {
  const schema = String(card.schema ?? "");
  const body = card.body as Record<string, unknown> | undefined;
  const elements = body?.elements;
  if (schema === "2.0" && Array.isArray(elements)) {
    const forbiddenTagPath = findForbiddenV2TagPath(elements);
    if (!forbiddenTagPath) return;

    throw new Error(
      `Invalid Feishu card payload: schema 2.0 does not support 'action' tags (found at ${forbiddenTagPath}).`,
    );
  }

  logWarn("feishu.card.payload.shape_unexpected", {
    schema,
    hasBody: Boolean(body),
    hasElements: Array.isArray(elements),
  });
}

function findForbiddenV2TagPath(
  elements: unknown[],
  pathValue = "body.elements",
): string | null {
  for (const [index, element] of elements.entries()) {
    if (!element || typeof element !== "object") continue;

    const record = element as Record<string, unknown>;
    const currentPath = `${pathValue}[${index}]`;
    if (record.tag === "action") return currentPath;

    const nestedElements = record.elements;
    if (Array.isArray(nestedElements)) {
      const nestedPath = findForbiddenV2TagPath(nestedElements, `${currentPath}.elements`);
      if (nestedPath) return nestedPath;
    }

    const nestedColumns = record.columns;
    if (Array.isArray(nestedColumns)) {
      const columnPath = findForbiddenV2ColumnsPath(nestedColumns, `${currentPath}.columns`);
      if (columnPath) return columnPath;
    }
  }

  return null;
}

function findForbiddenV2ColumnsPath(
  columns: unknown[],
  pathValue: string,
): string | null {
  for (const [index, column] of columns.entries()) {
    if (!column || typeof column !== "object") continue;

    const record = column as Record<string, unknown>;
    const currentPath = `${pathValue}[${index}]`;
    const nestedElements = record.elements;
    if (!Array.isArray(nestedElements)) continue;

    const nestedPath = findForbiddenV2TagPath(nestedElements, `${currentPath}.elements`);
    if (nestedPath) return nestedPath;
  }

  return null;
}

function extensionFromContentType(contentType?: string): string {
  switch (contentType) {
    case "image/png":
      return ".png";
    case "image/gif":
      return ".gif";
    case "image/webp":
      return ".webp";
    case "image/jpeg":
    case "image/jpg":
    default:
      return ".jpg";
  }
}

function resolveApiBaseUrl(brand?: FeishuConfig["feishu"]["brand"]): string {
  if (!brand || brand === "feishu") return "https://open.feishu.cn";
  return "https://open.larksuite.com";
}

function toFeishuFileType(fileName: string, mediaType?: string): string {
  const extension = path.extname(fileName).toLowerCase();
  if (mediaType === "application/pdf" || extension === ".pdf") return "pdf";
  if (extension === ".doc" || extension === ".docx") return "doc";
  if (extension === ".xls" || extension === ".xlsx" || extension === ".csv") return "xls";
  if (extension === ".ppt" || extension === ".pptx") return "ppt";
  if (extension === ".opus") return "opus";
  if (mediaType === "video/mp4" || extension === ".mp4") return "mp4";
  return "stream";
}

function inferMediaType(fileName: string): string {
  const extension = path.extname(fileName).toLowerCase();
  return extension === ".pdf"
    ? "application/pdf"
    : extension === ".json"
      ? "application/json"
      : extension === ".xml"
        ? "application/xml"
        : extension === ".txt"
          ? "text/plain"
          : extension === ".csv"
            ? "text/csv"
            : extension === ".md"
              ? "text/markdown"
              : "application/octet-stream";
}
