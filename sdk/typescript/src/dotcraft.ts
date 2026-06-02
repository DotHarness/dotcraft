import { userInfo } from "node:os";

import { DotCraftWireClient, type NotificationHandler, type Unsubscribe } from "./client.js";
import {
  JsonRpcMessage,
  ServerCapabilities,
  ServerInfo,
  Thread,
  Turn,
  textPart,
} from "./models.js";
import { WebSocketTransport } from "./transport.js";
import { HubClient } from "./hubClient.js";
import {
  InitializationError,
  TurnCancelledError,
  TurnFailedError,
  TurnInProgressError,
} from "./errors.js";
import {
  extractAgentReplyTextFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";

export type InputPart = Record<string, unknown>;
export type SenderContext = Record<string, unknown>;
export type ApprovalDecision =
  | "accept"
  | "acceptForSession"
  | "acceptAlways"
  | "decline"
  | "cancel";

export type ApprovalHandler = (request: Record<string, unknown>) => Promise<ApprovalDecision> | ApprovalDecision;
export type UserInputHandler =
  (request: Record<string, unknown>) => Promise<Record<string, unknown>> | Record<string, unknown>;

export interface DotCraftCapabilityOptions {
  [key: string]: unknown;
}

export interface DotCraftLocalOptions {
  workspacePath: string;
  clientName?: string;
  clientVersion?: string;
  clientTitle?: string;
  dotcraftBin?: string;
  hubStartupTimeoutMs?: number;
  approvalHandler?: ApprovalHandler;
  userInputHandler?: UserInputHandler;
  capabilities?: DotCraftCapabilityOptions;
}

export interface DotCraftRemoteOptions {
  url: string;
  token?: string | null;
  clientName?: string;
  clientVersion?: string;
  clientTitle?: string;
  approvalHandler?: ApprovalHandler;
  userInputHandler?: UserInputHandler;
  capabilities?: DotCraftCapabilityOptions;
}

export interface SessionIdentity {
  channelName: string;
  userId: string;
  workspacePath?: string;
  channelContext?: string;
}

export interface ThreadIdentityOptions {
  channelName?: string;
  userId?: string;
  workspacePath?: string;
  channelContext?: string;
}

export interface DynamicToolSpec {
  namespace?: string | null;
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  deferLoading?: boolean;
  approval?: Record<string, unknown>;
}

export interface DynamicToolCallRequest {
  threadId: string;
  turnId: string;
  callId: string;
  namespace?: string | null;
  tool: string;
  arguments: Record<string, unknown>;
}

export interface DynamicToolCallResult {
  success: boolean;
  contentItems?: Record<string, unknown>[];
  structuredResult?: unknown;
  errorCode?: string;
  errorMessage?: string;
}

export type DynamicToolHandler =
  (request: DynamicToolCallRequest) => Promise<DynamicToolCallResult> | DynamicToolCallResult;

export interface DynamicToolBinding extends DynamicToolSpec {
  handler: DynamicToolHandler;
}

export interface AppScopeDescriptor {
  id: string;
  displayName: string;
  description: string;
  risk: "read" | "mutate" | "externalWrite" | string;
  defaultSelected?: boolean | null;
}

export interface AppToolCatalogEntry {
  name: string;
  scope: string;
  risk: "read" | "mutate" | "externalWrite" | string;
  defaultExposure: "direct" | "deferred" | string;
  description?: string | null;
}

export interface AppDynamicToolCatalog {
  enabled: boolean;
  description?: string | null;
}

export interface AppHandoff {
  mode: "url" | "customProtocol" | "localCommand" | string;
  uri?: string | null;
  command?: string | null;
  args?: string[] | null;
  trustedRoot?: string | null;
}

export interface AppInfo {
  appId: string;
  toolNamespace: string;
  displayName: string;
  developerName: string;
  description: string;
  category?: string | null;
  icon?: string | null;
  pluginId: string;
  installed: boolean;
  enabled: boolean;
  catalogVisible: boolean;
  localCatalog?: boolean;
  registeredRoot?: string | null;
  releasePage?: string | null;
  downloadUrl?: string | null;
  connectionState: string;
  accountLabel?: string | null;
  handoffModes: AppHandoff[];
  scopes: AppScopeDescriptor[];
  toolCatalog: AppToolCatalogEntry[];
  dynamicToolCatalog?: AppDynamicToolCatalog | null;
  bindingSummary?: ThreadAppBindingSummary | null;
  diagnostics?: Record<string, unknown>[];
}

export interface ThreadAppBindingSummary {
  bindingRequestId?: string | null;
  threadId: string;
  bindingId: string;
  appId: string;
  displayName?: string | null;
  toolNamespace?: string | null;
  state: string;
  connectionState: string;
  grantedScopes: string[];
  expiresAt?: string | null;
}

export interface ThreadAppBinding {
  bindingRequestId?: string | null;
  bindingId: string;
  threadId: string;
  appId: string;
  displayName?: string | null;
  toolNamespace?: string | null;
  state: string;
  connectionState: string;
  grantedScopes: string[];
  attachedToolCount: number;
  expiresAt?: string | null;
  lastChangedAt: string;
  approvalMode?: string | null;
  auditRef?: string | null;
  diagnostic?: string | null;
}

export interface AppConnectionStartResult {
  connectionRequestId: string;
  appId: string;
  state: string;
  expiresAt: string;
  handoff: AppHandoff;
}

export interface AppConnectionStatus {
  appId: string;
  state: string;
  connectedAt?: string | null;
  expiresAt?: string | null;
  accountLabel?: string | null;
  diagnostic?: string | null;
}

export interface AppBindingRequestCreateResult {
  bindingRequestId: string;
  threadId: string;
  appId: string;
  requestedScopes: string[];
  state: string;
  tokenExpiresAt: string;
  handoff: AppHandoff;
  confirmation?: Record<string, unknown>;
}

export interface AppBindingAcceptResult {
  binding: ThreadAppBinding;
}

export interface AppBindingAttachToolsResult {
  binding: ThreadAppBinding;
  acceptedToolCount: number;
  warnings: string[];
}

export interface AppBindingManager {
  listApps(params?: { threadId?: string; includeDisabled?: boolean; includeCatalog?: boolean; forceRefresh?: boolean }): Promise<AppInfo[]>;
  viewApp(appId: string, params?: { threadId?: string }): Promise<AppInfo>;
  registerLocalApp(appId: string, rootPath: string): Promise<AppInfo>;
  startConnection(appId: string, params?: { handoffMode?: string; returnTo?: string }): Promise<AppConnectionStartResult>;
  connect(params: {
    connectionRequestId: string;
    requestToken: string;
    appId: string;
    accountLabel?: string;
    expiresAt?: string;
    connectionProof?: Record<string, unknown>;
  }): Promise<AppConnectionStatus>;
  connectionStatus(appId: string): Promise<AppConnectionStatus>;
  revokeConnection(appId: string, reason?: string): Promise<AppConnectionStatus>;
  createBindingRequest(params: {
    threadId: string;
    appId: string;
    requestedScopes: string[];
    requestedTools?: string[];
    reason?: string;
    source: "pluginDetail" | "threadMenu" | "welcome" | "agentSuggestion" | "sdk";
  }): Promise<AppBindingRequestCreateResult>;
  cancelBindingRequest(bindingRequestId: string, reason?: string): Promise<Record<string, unknown>>;
  acceptBinding(params: {
    bindingRequestId: string;
    requestToken: string;
    grantId: string;
    grantedScopes: string[];
    expiresAt?: string;
    approvalMode: "interactive" | "policyAutoApproved" | "adminApproved" | string;
    approvedBy?: string;
    auditRef?: string;
  }): Promise<AppBindingAcceptResult>;
  attachTools(params: {
    bindingId: string;
    threadId: string;
    appId: string;
    grantId: string;
    tools: DynamicToolBinding[];
    directToolNames?: string[];
    deferredToolNames?: string[];
    grantProof?: Record<string, unknown>;
  }): Promise<AppBindingAttachToolsResult>;
  listThreadBindings(threadId: string, includeRevoked?: boolean): Promise<ThreadAppBinding[]>;
  revokeThreadBinding(threadId: string, bindingId: string, reason?: string): Promise<Record<string, unknown>>;
  refreshThreadBindings(threadId: string, bindingId?: string): Promise<Record<string, unknown>[]>;
}

export const APP_BINDING_ERROR_CODES = {
  offline: "AppBindingOffline",
  expired: "AppBindingExpired",
  revoked: "AppBindingRevoked",
  scopeDenied: "AppBindingScopeDenied",
  toolUnavailable: "AppBindingToolUnavailable",
  protocolViolation: "AppBindingProtocolViolation",
} as const;

export type AppBindingErrorCode =
  typeof APP_BINDING_ERROR_CODES[keyof typeof APP_BINDING_ERROR_CODES];

export function appBindingToolError(
  errorCode: AppBindingErrorCode,
  errorMessage: string,
  structuredResult?: unknown,
): DynamicToolCallResult {
  return {
    success: false,
    errorCode,
    errorMessage,
    structuredResult,
    contentItems: [{ type: "text", text: `${errorCode}: ${errorMessage}` }],
  };
}

export function appBindingUnavailableError(
  state: "offline" | "expired" | "revoked" | string,
  message?: string,
): DynamicToolCallResult {
  if (state === "expired") {
    return appBindingToolError(
      APP_BINDING_ERROR_CODES.expired,
      message ?? "The app binding has expired.",
    );
  }
  if (state === "revoked") {
    return appBindingToolError(
      APP_BINDING_ERROR_CODES.revoked,
      message ?? "The app binding was revoked.",
    );
  }
  return appBindingToolError(
    APP_BINDING_ERROR_CODES.offline,
    message ?? "The app binding is offline. Reconnect the app or refresh the binding.",
  );
}

export interface StartThreadOptions extends ThreadIdentityOptions {
  displayName?: string | null;
  historyMode?: string;
  config?: Record<string, unknown> | null;
  dynamicTools?: DynamicToolBinding[];
  additionalContext?: Record<string, RuntimeAdditionalContextEntry>;
}

export interface ResumeThreadOptions {
  dynamicTools?: DynamicToolBinding[];
  additionalContext?: Record<string, RuntimeAdditionalContextEntry>;
}

export interface RuntimeAdditionalContextEntry {
  kind: "application";
  value: string;
}

export interface GetOrCreateThreadOptions extends StartThreadOptions {
  includeArchived?: boolean;
}

export interface ListThreadOptions extends ThreadIdentityOptions {
  includeArchived?: boolean;
  query?: string;
  limit?: number;
  cursor?: string;
}

export interface ReadThreadOptions {
  includeTurns?: boolean;
  turnLimit?: number;
  cursor?: string;
}

export interface ThreadListPage {
  threads: Thread[];
  nextCursor?: string | null;
  totalMatched?: number | null;
  raw: Record<string, unknown>;
}

export interface SubscribeOptions {
  replayRecent?: boolean;
}

export interface ThreadSubscription {
  threadId: string;
  unsubscribe(): Promise<void>;
}

export type RunInput =
  | string
  | InputPart[]
  | {
      input: InputPart[];
      sender?: SenderContext;
    };

export interface RunOptions {
  sender?: SenderContext;
  collectRawEvents?: boolean;
  abortSignal?: AbortSignal;
  enqueueIfBusy?: boolean;
}

export interface EnqueueOptions {
  sender?: SenderContext;
}

export interface QueuedInputResult {
  queuedInput?: unknown;
  queuedInputs?: unknown[];
  raw: Record<string, unknown>;
}

export interface DotCraftRunResult {
  thread: Thread;
  turn: Turn | null;
  text: string;
  items: unknown[];
  usage?: Record<string, unknown> | null;
  rawEvents?: JsonRpcMessage[];
  queuedInput?: unknown;
}

export interface DotCraftRunEventBase {
  type: string;
  threadId: string;
  turnId?: string;
  raw: JsonRpcMessage;
}

export type DotCraftRunEvent = DotCraftRunEventBase & {
  delta?: string;
  item?: unknown;
  turn?: Turn;
  result?: DotCraftRunResult;
  error?: string;
  queuedInput?: unknown;
};

export interface ThreadManager {
  getOrCreate(options?: GetOrCreateThreadOptions): Promise<DotCraftThread>;
  start(options?: StartThreadOptions): Promise<DotCraftThread>;
  resume(threadId: string, options?: ResumeThreadOptions): Promise<DotCraftThread>;
  list(options?: ListThreadOptions): Promise<Thread[]>;
  listPage(options?: ListThreadOptions): Promise<ThreadListPage>;
  read(threadId: string, options?: ReadThreadOptions): Promise<Thread>;
}

function defaultUserId(): string {
  try {
    return userInfo().username || "local-user";
  } catch {
    return "local-user";
  }
}

function normalizeIdentity(options: ThreadIdentityOptions = {}): SessionIdentity {
  return {
    channelName: options.channelName ?? "sdk",
    userId: options.userId ?? defaultUserId(),
    ...(options.workspacePath ? { workspacePath: options.workspacePath } : {}),
    ...(options.channelContext ? { channelContext: options.channelContext } : {}),
  };
}

function normalizeRunInput(input: RunInput, sender?: SenderContext): { input: InputPart[]; sender?: SenderContext } {
  if (typeof input === "string") return { input: [textPart(input)], sender };
  if (Array.isArray(input)) return { input, sender };
  return { input: input.input, sender: input.sender ?? sender };
}

function stripDynamicToolHandlers(tools: DynamicToolBinding[] | undefined): Record<string, unknown>[] | null {
  if (!tools?.length) return null;
  return tools.map(({ handler: _handler, ...tool }) => tool as Record<string, unknown>);
}

function toolKey(threadId: string, namespace: string | null | undefined, name: string): string {
  return `${threadId}\u0000${namespace ?? ""}\u0000${name}`;
}

function methodToRunEventType(method: string | null | undefined): string {
  switch (method) {
    case "thread/started": return "thread_started";
    case "thread/resumed": return "thread_resumed";
    case "thread/statusChanged": return "thread_status_changed";
    case "thread/runtimeChanged": return "thread_runtime_changed";
    case "thread/queue/updated": return "queue_updated";
    case "turn/started": return "turn_started";
    case "item/started": return "item_started";
    case "item/completed": return "item_completed";
    case "item/agentMessage/delta": return "agent_message_delta";
    case "item/reasoning/delta": return "reasoning_delta";
    case "item/toolCall/argumentsDelta": return "tool_arguments_delta";
    case "item/approval/resolved": return "approval_resolved";
    case "item/usage/delta": return "usage_delta";
    case "subagent/progress": return "subagent_progress";
    case "plan/updated": return "plan_updated";
    case "system/event": return "system_event";
    case "turn/completed": return "completed";
    case "turn/failed": return "failed";
    case "turn/cancelled": return "cancelled";
    default: return "raw";
  }
}

function paramsRecord(raw: unknown): Record<string, unknown> {
  return raw && typeof raw === "object" ? raw as Record<string, unknown> : {};
}

function eventTurn(params: Record<string, unknown>): Turn | null {
  const raw = params.turn;
  return raw && typeof raw === "object" ? Turn.fromWire(raw as Record<string, unknown>) : null;
}

function resolveThreadId(fallbackThreadId: string, params: Record<string, unknown>): string {
  if (typeof params.threadId === "string") return params.threadId;
  const turn = params.turn;
  if (turn && typeof turn === "object" && typeof (turn as { threadId?: unknown }).threadId === "string") {
    return (turn as { threadId: string }).threadId;
  }
  const thread = params.thread;
  if (thread && typeof thread === "object" && typeof (thread as { id?: unknown }).id === "string") {
    return (thread as { id: string }).id === fallbackThreadId ? fallbackThreadId : fallbackThreadId;
  }
  return fallbackThreadId;
}

function abortError(): Error {
  const error = new Error("Operation was aborted.");
  error.name = "AbortError";
  return error;
}

class RunReducer {
  private readonly itemOrder: string[] = [];
  private readonly itemSeen = new Set<string>();
  private readonly perItemDelta = new Map<string, string>();
  private activeAgentItemId: string | null = null;
  private lastDeltaAgentItemId: string | null = null;
  private orphanDelta = "";

  apply(event: JsonRpcMessage): void {
    const params = paramsRecord(event.params);
    if (event.method === "item/started") {
      const item = paramsRecord(params.item);
      if (item.type === "agentMessage" && typeof item.id === "string" && item.id) {
        this.activeAgentItemId = item.id;
        this.lastDeltaAgentItemId = item.id;
        this.pushOrder(item.id);
      }
      return;
    }
    if (event.method === "item/agentMessage/delta") {
      const delta = typeof params.delta === "string" ? params.delta : "";
      const explicit = typeof params.itemId === "string" && params.itemId ? params.itemId : null;
      const itemId = explicit ?? this.activeAgentItemId ?? this.lastDeltaAgentItemId;
      if (itemId) {
        this.pushOrder(itemId);
        this.perItemDelta.set(itemId, (this.perItemDelta.get(itemId) ?? "") + delta);
        this.lastDeltaAgentItemId = itemId;
        if (!this.activeAgentItemId) this.activeAgentItemId = itemId;
      } else {
        this.orphanDelta += delta;
      }
      return;
    }
    if (event.method === "item/completed") {
      const item = paramsRecord(params.item);
      if (item.type !== "agentMessage") return;
      const itemId = typeof item.id === "string" ? item.id : "";
      if (!itemId) return;
      this.pushOrder(itemId);
      const payload = paramsRecord(item.payload);
      const snapshot = typeof payload.text === "string" ? payload.text : "";
      const delta = this.perItemDelta.get(itemId) ?? "";
      this.perItemDelta.set(itemId, mergeReplyTextFromDeltaAndSnapshot(delta, snapshot));
      if (this.activeAgentItemId === itemId) this.activeAgentItemId = null;
      this.lastDeltaAgentItemId = itemId;
    }
  }

  textFromCompleted(params: Record<string, unknown>): string {
    const snapshot = extractAgentReplyTextFromTurnCompletedParams(params);
    const delta = this.itemOrder.map((id) => this.perItemDelta.get(id) ?? "").join("") + this.orphanDelta;
    return mergeReplyTextFromDeltaAndSnapshot(delta, snapshot);
  }

  private pushOrder(itemId: string): void {
    if (!itemId || this.itemSeen.has(itemId)) return;
    this.itemSeen.add(itemId);
    this.itemOrder.push(itemId);
  }
}

class ThreadManagerImpl implements ThreadManager {
  constructor(private readonly sdk: DotCraft) {}

  async getOrCreate(options: GetOrCreateThreadOptions = {}): Promise<DotCraftThread> {
    const identity = normalizeIdentity(options);
    const threads = await this.sdk.wire.threadList({
      ...identity,
      includeArchived: options.includeArchived ?? false,
    });
    const reusable = threads.find((thread) => thread.status === "active" || thread.status === "paused");
    if (reusable) {
      if (reusable.status === "paused") {
        return await this.resume(reusable.id, {
          dynamicTools: options.dynamicTools,
          additionalContext: options.additionalContext,
        });
      }
      const snapshot = await this.sdk.wire.threadRead(reusable.id);
      const thread = new DotCraftThread(this.sdk, snapshot, identity);
      thread.bindDynamicTools(options.dynamicTools);
      return thread;
    }
    return await this.start(options);
  }

  async start(options: StartThreadOptions = {}): Promise<DotCraftThread> {
    const identity = normalizeIdentity(options);
    const snapshot = await this.sdk.wire.threadStart({
      ...identity,
      displayName: options.displayName,
      historyMode: options.historyMode,
      config: options.config,
      dynamicTools: stripDynamicToolHandlers(options.dynamicTools),
      additionalContext: options.additionalContext,
    });
    const thread = new DotCraftThread(this.sdk, snapshot, identity);
    thread.bindDynamicTools(options.dynamicTools);
    return thread;
  }

  async resume(threadId: string, options: ResumeThreadOptions = {}): Promise<DotCraftThread> {
    const snapshot = await this.sdk.wire.threadResume(threadId, {
      dynamicTools: stripDynamicToolHandlers(options.dynamicTools),
      additionalContext: options.additionalContext,
    });
    const thread = new DotCraftThread(this.sdk, snapshot, {
      channelName: snapshot.originChannel || "sdk",
      userId: snapshot.userId || defaultUserId(),
      workspacePath: snapshot.workspacePath || undefined,
    });
    thread.bindDynamicTools(options.dynamicTools);
    return thread;
  }

  async list(options: ListThreadOptions = {}): Promise<Thread[]> {
    const page = await this.listPage(options);
    return page.threads;
  }

  async listPage(options: ListThreadOptions = {}): Promise<ThreadListPage> {
    const identity = normalizeIdentity(options);
    return await this.sdk.wire.threadListPage({
      ...identity,
      includeArchived: options.includeArchived ?? false,
      query: options.query,
      limit: options.limit,
      cursor: options.cursor,
    });
  }

  async read(threadId: string, options: ReadThreadOptions = {}): Promise<Thread> {
    return await this.sdk.wire.threadRead(threadId, options.includeTurns ?? false, {
      turnLimit: options.turnLimit,
      cursor: options.cursor,
    });
  }
}

class AppBindingManagerImpl implements AppBindingManager {
  constructor(private readonly sdk: DotCraft) {}

  async listApps(params: {
    threadId?: string;
    includeDisabled?: boolean;
    includeCatalog?: boolean;
    forceRefresh?: boolean;
  } = {}): Promise<AppInfo[]> {
    const result = await this.sdk.request<{ apps?: AppInfo[] }>("app/list", {
      includeCatalog: params.includeCatalog ?? true,
      includeDisabled: params.includeDisabled ?? true,
      threadId: params.threadId,
      forceRefresh: params.forceRefresh ?? false,
    });
    return result.apps ?? [];
  }

  async viewApp(appId: string, params: { threadId?: string } = {}): Promise<AppInfo> {
    const result = await this.sdk.request<{ app?: AppInfo }>("app/view", {
      appId,
      threadId: params.threadId,
    });
    if (!result.app) throw new Error(`App '${appId}' was not returned by app/view.`);
    return result.app;
  }

  async registerLocalApp(appId: string, rootPath: string): Promise<AppInfo> {
    const result = await this.sdk.request<{ app?: AppInfo }>("app/local/register", {
      appId,
      rootPath,
    });
    if (!result.app) throw new Error(`App '${appId}' was not returned by app/local/register.`);
    return result.app;
  }

  async startConnection(
    appId: string,
    params: { handoffMode?: string; returnTo?: string } = {},
  ): Promise<AppConnectionStartResult> {
    return await this.sdk.request<AppConnectionStartResult>("app/connection/start", {
      appId,
      handoffMode: params.handoffMode,
      returnTo: params.returnTo,
    });
  }

  async connect(params: {
    connectionRequestId: string;
    requestToken: string;
    appId: string;
    accountLabel?: string;
    expiresAt?: string;
    connectionProof?: Record<string, unknown>;
  }): Promise<AppConnectionStatus> {
    return await this.sdk.request<AppConnectionStatus>("app/connection/connect", params);
  }

  async connectionStatus(appId: string): Promise<AppConnectionStatus> {
    return await this.sdk.request<AppConnectionStatus>("app/connection/status", { appId });
  }

  async revokeConnection(appId: string, reason?: string): Promise<AppConnectionStatus> {
    return await this.sdk.request<AppConnectionStatus>("app/connection/revoke", { appId, reason });
  }

  async createBindingRequest(params: {
    threadId: string;
    appId: string;
    requestedScopes: string[];
    requestedTools?: string[];
    reason?: string;
    source: "pluginDetail" | "threadMenu" | "welcome" | "agentSuggestion" | "sdk";
  }): Promise<AppBindingRequestCreateResult> {
    return await this.sdk.request<AppBindingRequestCreateResult>("app/binding/request/create", params);
  }

  async cancelBindingRequest(bindingRequestId: string, reason?: string): Promise<Record<string, unknown>> {
    return await this.sdk.request<Record<string, unknown>>("app/binding/request/cancel", {
      bindingRequestId,
      reason,
    });
  }

  async acceptBinding(params: {
    bindingRequestId: string;
    requestToken: string;
    grantId: string;
    grantedScopes: string[];
    expiresAt?: string;
    approvalMode: "interactive" | "policyAutoApproved" | "adminApproved" | string;
    approvedBy?: string;
    auditRef?: string;
  }): Promise<AppBindingAcceptResult> {
    return await this.sdk.request<AppBindingAcceptResult>("app/binding/accept", params);
  }

  async attachTools(params: {
    bindingId: string;
    threadId: string;
    appId: string;
    grantId: string;
    tools: DynamicToolBinding[];
    directToolNames?: string[];
    deferredToolNames?: string[];
    grantProof?: Record<string, unknown>;
  }): Promise<AppBindingAttachToolsResult> {
    const result = await this.sdk.request<AppBindingAttachToolsResult>("app/binding/attachTools", {
      ...params,
      tools: stripDynamicToolHandlers(params.tools) ?? [],
    });
    for (const tool of params.tools) {
      this.sdk.registerDynamicToolHandler(params.threadId, tool.namespace ?? null, tool.name, tool.handler);
    }
    return result;
  }

  async listThreadBindings(threadId: string, includeRevoked = false): Promise<ThreadAppBinding[]> {
    const result = await this.sdk.request<{ bindings?: ThreadAppBinding[] }>("thread/appBindings/list", {
      threadId,
      includeRevoked,
    });
    return result.bindings ?? [];
  }

  async revokeThreadBinding(threadId: string, bindingId: string, reason?: string): Promise<Record<string, unknown>> {
    return await this.sdk.request<Record<string, unknown>>("thread/appBindings/revoke", {
      threadId,
      bindingId,
      reason,
    });
  }

  async refreshThreadBindings(threadId: string, bindingId?: string): Promise<Record<string, unknown>[]> {
    const result = await this.sdk.request<{ bindings?: Record<string, unknown>[] }>("thread/appBindings/refresh", {
      threadId,
      bindingId,
    });
    return result.bindings ?? [];
  }
}

export class DotCraft {
  readonly threads: ThreadManager;
  readonly appBindings: AppBindingManager;

  private constructor(
    readonly wire: DotCraftWireClient,
    readonly serverInfo: ServerInfo,
    readonly capabilities: ServerCapabilities,
    private readonly approvalHandler?: ApprovalHandler,
    private readonly userInputHandler?: UserInputHandler,
  ) {
    this.threads = new ThreadManagerImpl(this);
    this.appBindings = new AppBindingManagerImpl(this);
    this.installServerRequestHandlers();
  }

  static async local(options: DotCraftLocalOptions): Promise<DotCraft> {
    if (!options.workspacePath?.trim()) {
      throw new InitializationError("workspacePath is required for DotCraft.local().");
    }
    const hub = new HubClient({
      dotcraftBin: options.dotcraftBin,
      hubStartupTimeoutMs: options.hubStartupTimeoutMs,
    });
    const ensured = await hub.ensureAppServer(options.workspacePath, {
      clientName: options.clientName ?? "dotcraft-sdk",
      clientVersion: options.clientVersion ?? "0.1.0",
    });
    const wsUrl = ensured.endpoints.appServerWebSocket;
    if (!wsUrl) throw new InitializationError("Hub response did not include endpoints.appServerWebSocket.");
    return await DotCraft.connect(new WebSocketTransport({ url: wsUrl }), options);
  }

  static async remote(options: DotCraftRemoteOptions): Promise<DotCraft> {
    return await DotCraft.connect(new WebSocketTransport({ url: options.url, token: options.token }), options);
  }

  request<T>(method: string, params?: unknown): Promise<T> {
    return this.wire.request<T>(method, params);
  }

  on(event: string, handler: NotificationHandler): Unsubscribe {
    return this.wire.on(event, handler);
  }

  async close(): Promise<void> {
    await this.wire.stop();
  }

  registerDynamicToolHandler(
    threadId: string,
    namespace: string | null | undefined,
    name: string,
    handler: DynamicToolHandler,
  ): Unsubscribe {
    const key = toolKey(threadId, namespace, name);
    this.dynamicToolHandlers.set(key, handler);
    return () => this.dynamicToolHandlers.delete(key);
  }

  private readonly dynamicToolHandlers = new Map<string, DynamicToolHandler>();

  private static async connect(
    transport: WebSocketTransport,
    options: DotCraftLocalOptions | DotCraftRemoteOptions,
  ): Promise<DotCraft> {
    const wire = new DotCraftWireClient(transport);
    await wire.connect();
    const initialized = await wire.initialize({
      clientName: options.clientName ?? "dotcraft-sdk",
      clientVersion: options.clientVersion ?? "0.1.0",
      clientTitle: options.clientTitle,
      approvalSupport: true,
      requestUserInputSupport: true,
      streamingSupport: true,
      configChange: true,
      extraCapabilities: options.capabilities,
    });
    return new DotCraft(
      wire,
      initialized.serverInfo,
      initialized.capabilities,
      options.approvalHandler,
      options.userInputHandler,
    );
  }

  private installServerRequestHandlers(): void {
    this.wire.setApprovalHandler(async (_id, params) => {
      if (!this.approvalHandler) return "accept";
      return await this.approvalHandler(params);
    });
    this.wire.registerServerRequestHandler("item/tool/requestUserInput", async (_id, params) => {
      if (!this.userInputHandler) return { answers: {} };
      return await this.userInputHandler(params);
    });
    this.wire.registerServerRequestHandler("item/tool/call", async (_id, params) => {
      const request = params as unknown as DynamicToolCallRequest;
      const handler = this.dynamicToolHandlers.get(toolKey(request.threadId, request.namespace, request.tool));
      if (!handler) {
        return {
          success: false,
          errorCode: "UnsupportedTool",
          errorMessage: "No handler registered for this dynamic tool.",
        };
      }
      try {
        return await handler(request);
      } catch (error) {
        return {
          success: false,
          errorCode: "AdapterToolCallFailed",
          errorMessage: error instanceof Error ? error.message : String(error),
        };
      }
    });
  }
}

export class DotCraftThread {
  readonly id: string;
  private cached: Thread;
  private readonly dynamicToolUnsubscribes: Unsubscribe[] = [];

  constructor(
    private readonly sdk: DotCraft,
    snapshot: Thread,
    readonly identity: SessionIdentity,
  ) {
    this.cached = snapshot;
    this.id = snapshot.id;
  }

  snapshot(): Thread {
    return this.cached;
  }

  async refresh(options: ReadThreadOptions = {}): Promise<Thread> {
    this.cached = await this.sdk.wire.threadRead(this.id, options.includeTurns ?? false);
    return this.cached;
  }

  async subscribe(options: SubscribeOptions = {}): Promise<ThreadSubscription> {
    await this.sdk.wire.threadSubscribe(this.id, options.replayRecent ?? false);
    return {
      threadId: this.id,
      unsubscribe: async () => {
        await this.unsubscribe();
      },
    };
  }

  async unsubscribe(): Promise<void> {
    await this.sdk.wire.threadUnsubscribe(this.id);
  }

  async run(input: RunInput, options: RunOptions = {}): Promise<DotCraftRunResult> {
    let queued: DotCraftRunResult | null = null;
    for await (const event of this.runStreamed(input, options)) {
      if (event.type === "completed" && event.result) return event.result;
      if (event.type === "queue_updated") {
        queued = {
          thread: this.cached,
          turn: null,
          text: "",
          items: [],
          rawEvents: options.collectRawEvents ? [event.raw] : undefined,
          queuedInput: event.queuedInput,
        };
      }
      if (event.type === "failed") {
        throw new TurnFailedError(event.error ?? "Turn failed.", event.turn);
      }
      if (event.type === "cancelled") {
        throw new TurnCancelledError("Turn was cancelled.", event.turn);
      }
    }
    if (queued) return queued;
    throw new TurnFailedError("Run finished without a terminal event.");
  }

  async *runStreamed(input: RunInput, options: RunOptions = {}): AsyncIterable<DotCraftRunEvent> {
    if (options.abortSignal?.aborted) throw abortError();

    const normalized = normalizeRunInput(input, options.sender);
    const reducer = new RunReducer();
    const rawEvents: JsonRpcMessage[] = [];
    const eventStream = this.sdk.wire.streamEvents(this.id);
    let turn: Turn;
    let abortListener: (() => void) | null = null;

    try {
      try {
        turn = await this.sdk.wire.turnStart(this.id, normalized.input, normalized.sender);
      } catch (error) {
        await eventStream.return?.();
        if (error instanceof TurnInProgressError && options.enqueueIfBusy) {
          const queued = await this.enqueue({ input: normalized.input, sender: normalized.sender });
          const raw = JsonRpcMessage.fromDict({ method: "thread/queue/updated", params: queued.raw });
          yield {
            type: "queue_updated",
            threadId: this.id,
            raw,
            queuedInput: queued.queuedInput,
          };
          return;
        }
        throw error;
      }

      if (options.abortSignal) {
        abortListener = () => {
          void this.interrupt(turn.id).catch(() => {});
        };
        options.abortSignal.addEventListener("abort", abortListener, { once: true });
      }

      for await (const raw of eventStream) {
        rawEvents.push(raw);
        reducer.apply(raw);
        const params = paramsRecord(raw.params);
        const resolvedTurn = eventTurn(params);
        const type = methodToRunEventType(raw.method);
        const base: DotCraftRunEvent = {
          type,
          threadId: resolveThreadId(this.id, params),
          turnId: typeof params.turnId === "string" ? params.turnId : resolvedTurn?.id,
          raw,
        };

        if (type === "agent_message_delta" || type === "reasoning_delta" || type === "tool_arguments_delta") {
          base.delta = typeof params.delta === "string" ? params.delta : "";
        }
        if (type === "item_started" || type === "item_completed" || type === "approval_resolved") {
          base.item = params.item;
        }
        if (resolvedTurn) base.turn = resolvedTurn;

        if (type === "completed" && resolvedTurn) {
          this.cached = await this.sdk.wire.threadRead(this.id);
          const text = reducer.textFromCompleted(params);
          base.result = {
            thread: this.cached,
            turn: resolvedTurn,
            text,
            items: resolvedTurn.items,
            usage: resolvedTurn.tokenUsage,
            rawEvents: options.collectRawEvents ? [...rawEvents] : undefined,
          };
          yield base;
          return;
        }
        if (type === "failed") {
          base.error = typeof params.error === "string" ? params.error : resolvedTurn?.error ?? "Turn failed.";
          yield base;
          return;
        }
        if (type === "cancelled") {
          yield base;
          return;
        }

        yield base;
      }
    } finally {
      if (abortListener && options.abortSignal) {
        options.abortSignal.removeEventListener("abort", abortListener);
      }
      await eventStream.return?.();
    }
  }

  async enqueue(input: RunInput, options: EnqueueOptions = {}): Promise<QueuedInputResult> {
    const normalized = normalizeRunInput(input, options.sender);
    const raw = await this.sdk.wire.turnEnqueue(this.id, normalized.input, normalized.sender);
    return {
      queuedInput: raw.queuedInput,
      queuedInputs: raw.queuedInputs as unknown[] | undefined,
      raw,
    };
  }

  async interrupt(turnId: string): Promise<void> {
    await this.sdk.wire.turnInterrupt(this.id, turnId);
  }

  async setMode(mode: string): Promise<void> {
    await this.sdk.wire.threadSetMode(this.id, mode);
  }

  async archive(): Promise<void> {
    await this.sdk.wire.threadArchive(this.id);
  }

  async delete(): Promise<void> {
    await this.sdk.wire.threadDelete(this.id);
  }

  onToolCall(namespace: string | null, name: string, handler: DynamicToolHandler): Unsubscribe {
    return this.sdk.registerDynamicToolHandler(this.id, namespace, name, handler);
  }

  bindDynamicTools(tools: DynamicToolBinding[] | undefined): void {
    if (!tools?.length) return;
    for (const unsubscribe of this.dynamicToolUnsubscribes.splice(0)) unsubscribe();
    for (const tool of tools) {
      this.dynamicToolUnsubscribes.push(this.onToolCall(tool.namespace ?? null, tool.name, tool.handler));
    }
  }
}
