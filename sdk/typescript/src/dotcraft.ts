import { userInfo } from "node:os";

import { type NotificationHandler, type Unsubscribe } from "./client.js";
import { DotCraftAppServerClient } from "./appServerClient.js";
import {
  JsonRpcMessage,
  ServerCapabilities,
  ServerInfo,
  Thread,
  Turn,
  textPart,
} from "./models.js";
import { WebSocketTransport } from "./transport.js";
import { HubClient, type HubBinaryMatchPolicy } from "./hubClient.js";
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
import type {
  ClientRequestMethods,
  ServerNotificationMethods,
} from "./generated/appserver/index.js";

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
  executable?: string;
  expectedExecutable?: string;
  binaryMatchPolicy?: HubBinaryMatchPolicy;
  hubStartupTimeoutMs?: number;
  homeDir?: string;
  approvalHandler?: ApprovalHandler;
  userInputHandler?: UserInputHandler;
  capabilities?: DotCraftCapabilityOptions;
}

export type DotCraftLocalChatOptions = Omit<DotCraftLocalOptions, "workspacePath">;

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

export interface DynamicToolFunctionSpec {
  type: "function";
  name: string;
  description: string;
  inputSchema: Record<string, unknown>;
  deferLoading?: boolean;
  approval?: Record<string, unknown>;
}

export interface DynamicToolNamespaceSpec {
  type: "namespace";
  name: string;
  description: string;
  tools: DynamicToolFunctionSpec[];
}

export type DynamicToolSpec = DynamicToolFunctionSpec | DynamicToolNamespaceSpec;

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
  contentItems?: DynamicToolContentItem[];
  structuredContent?: unknown;
  errorCode?: string;
  errorMessage?: string;
}

export type DynamicToolContentItem =
  | { type: "text"; text: string }
  | { type: "image"; mediaType: string; url: string; dataBase64?: never }
  | { type: "image"; mediaType: string; dataBase64: string; url?: never };

export type DynamicToolHandler =
  (request: DynamicToolCallRequest) => Promise<DynamicToolCallResult> | DynamicToolCallResult;

export type DynamicToolFunctionBinding = DynamicToolFunctionSpec & { handler: DynamicToolHandler };
export type DynamicToolNamespaceBinding = Omit<DynamicToolNamespaceSpec, "tools"> & {
  tools: DynamicToolFunctionBinding[];
};
export type DynamicToolBinding = DynamicToolFunctionBinding | DynamicToolNamespaceBinding;

export type McpServerOriginKind = "workspace" | "plugin" | "thread" | "binding" | (string & {});

export interface McpServerOrigin {
  kind: McpServerOriginKind;
  pluginId?: string | null;
  pluginDisplayName?: string | null;
  declaredName?: string | null;
  threadId?: string | null;
  bindingId?: string | null;
}

export interface McpServerRuntimeStatus {
  name: string;
  serverInfo?: unknown;
  tools: Record<string, unknown>;
  resources: unknown[];
  resourceTemplates: unknown[];
  authStatus: "unsupported" | "notLoggedIn" | "bearerToken" | "oAuth" | string;
  declaredName?: string | null;
  runtimeName?: string | null;
  origin?: McpServerOrigin | null;
}

export interface McpServerStatusListParams {
  threadId?: string | null;
  cursor?: string | null;
  limit?: number | null;
  detail?: "full" | "toolsAndAuthOnly" | null;
}

export interface McpServerStatusListResult {
  data: McpServerRuntimeStatus[];
  nextCursor?: string | null;
}

export interface McpServerResourceReadParams {
  threadId?: string | null;
  server: string;
  uri: string;
}

export interface McpServerResourceReadResult { contents: unknown; }

export interface McpServerToolCallParams {
  threadId: string;
  server: string;
  tool: string;
  arguments?: Record<string, unknown> | null;
  _meta?: unknown;
}

export interface McpServerToolCallResult {
  content?: unknown;
  structuredContent?: unknown;
  isError?: boolean;
  _meta?: unknown;
}

export interface McpServerOAuthLoginParams {
  name: string;
  threadId?: string | null;
  scopes?: string[] | null;
  timeoutSecs?: number | null;
}

export interface McpServerOAuthLoginResult { authorizationUrl: string; }
export type McpServerReloadResult = Record<string, never>;

export interface McpServerStartupStatusUpdatedNotification {
  threadId?: string | null;
  name: string;
  status: "starting" | "ready" | "failed" | "cancelled";
  error?: string | null;
  failureReason?: "reauthenticationRequired" | string | null;
}

export interface McpServerOAuthLoginCompletedNotification {
  name: string;
  threadId?: string | null;
  success: boolean;
  error?: string | null;
}

export interface McpServerElicitationRequest {
  threadId?: string | null;
  turnId?: string | null;
  serverName: string;
  mode: "form" | "url";
  elicitationId?: string | null;
  message?: string | null;
  url?: string | null;
  requestedSchema?: Record<string, unknown> | null;
  _meta?: unknown;
}

export interface McpServerElicitationResponse {
  action: "accept" | "decline" | "cancel";
  content?: Record<string, unknown> | null;
  _meta?: unknown;
}

export interface McpRuntimeManager {
  listStatus(params?: McpServerStatusListParams): Promise<McpServerStatusListResult>;
  readResource(params: McpServerResourceReadParams): Promise<McpServerResourceReadResult>;
  callTool(params: McpServerToolCallParams): Promise<McpServerToolCallResult>;
  loginOAuth(params: McpServerOAuthLoginParams): Promise<McpServerOAuthLoginResult>;
  reload(): Promise<McpServerReloadResult>;
}

export interface AppHandoff {
  mode: "url" | "customProtocol" | "localCommand" | "bindCode" | string;
  uri?: string | null;
  bindCode?: string | null;
  instructions?: string | null;
  command?: string | null;
  args?: string[] | null;
  trustedRoot?: string | null;
}

export type AppBindingKind = "app" | "socialChannel" | "managedApp" | (string & {});

export type SocialBindingTargetSelection =
  | "confirmInChannel"
  | "currentConversation"
  | (string & {});

export interface SocialBindingIntent {
  channelName: string;
  targetSelection?: SocialBindingTargetSelection;
  displayHint?: string | null;
}

export interface SocialChannelBoundBy {
  platformUserId: string;
  displayName?: string | null;
}

export interface SocialChannelTarget {
  channelName: string;
  accountId?: string | null;
  conversationKind: string;
  conversationId: string;
  deliveryTarget: string;
  displayName?: string | null;
  boundBy?: SocialChannelBoundBy | null;
}

export interface AppInfo {
  appId: string;
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
  bindingSummary?: ThreadAppBindingSummary | null;
  diagnostics?: Record<string, unknown>[];
}

export interface ThreadAppBindingSummary {
  threadId: string;
  bindingId: string;
  appId: string;
  displayName?: string | null;
  state: string;
  authorityRevision: number;
  approvedCapabilityRevision: number;
  candidateCapabilityRevision?: number | null;
  socialTarget?: SocialChannelTarget | null;
  failureReason?: string | null;
}

export interface ThreadAppBinding {
  bindingId: string;
  threadId: string;
  appId: string;
  displayName?: string | null;
  state: string;
  authorityRevision: number;
  approvedCapabilityRevision: number;
  candidateCapabilityRevision?: number | null;
  approvedTools?: Record<string, unknown>[];
  pendingChanges?: Array<{ kind: string; tool: string; detail: string }>;
  socialTarget?: SocialChannelTarget | null;
  failureReason?: string | null;
  updatedAt?: string;
}

export interface AppConnectionStartResult {
  connectionRequestId: string;
  requestToken: string;
  expiresAt: string;
  handoff?: AppHandoff | null;
}

export interface AppPrincipal {
  principalId: string;
  appId: string;
  userId: string;
  expiresAt: string;
}

export interface AppConnectionConnectResult {
  principal: AppPrincipal;
  credential: string;
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
  bindingId: string;
  state: string;
  expiresAt: string;
  handoff?: AppHandoff | null;
}

export interface AppBindingRequestGetResult {
  bindingRequestId: string;
  bindingId: string;
  threadId: string;
  appId: string;
  state: string;
  expiresAt: string;
}

export interface AppSocialBindingResolveParams {
  channelName: string;
  accountId?: string | null;
  conversationKind: string;
  conversationId: string;
}

export interface AppSocialBindingResolveResult {
  binding?: ThreadAppBinding | null;
}

export interface AppSurface {
  appId: string;
  surfaceId: string;
  endpoint: string;
  bearer: string;
  expiresAt: string;
}

export interface AppSurfacePublishParams {
  surfaceId: string;
  endpoint: string;
  bearer: string;
}

export interface AppSurfaceResolveParams {
  appId: string;
  surfaceId: string;
}

export interface AppThreadInputEnqueueResult {
  queuedInput?: unknown;
  queuedInputs?: unknown[];
}

export interface AppBindingManager {
  listApps(params?: { threadId?: string; includeDisabled?: boolean; includeCatalog?: boolean; forceRefresh?: boolean }): Promise<AppInfo[]>;
  viewApp(appId: string, params?: { threadId?: string }): Promise<AppInfo>;
  registerLocalApp(appId: string, rootPath: string): Promise<AppInfo>;
  startConnection(appId: string, params?: { handoffMode?: string; returnTo?: string }): Promise<AppConnectionStartResult>;
  connect(params: {
    connectionRequestId: string;
    requestToken: string;
    accountLabel?: string;
  }): Promise<AppConnectionConnectResult>;
  connectionStatus(appId: string): Promise<AppConnectionStatus>;
  revokeConnection(appId: string, reason?: string): Promise<AppConnectionStatus>;
  publishSurface(params: AppSurfacePublishParams): Promise<AppSurface>;
  resolveSurface(params: AppSurfaceResolveParams): Promise<AppSurface>;
  authenticate(appId: string, credential: string): Promise<Record<string, unknown>>;
  refreshCredential(): Promise<Record<string, unknown>>;
  activate(params: { bindingRequestId: string; endpoint: string; bearer: string; bearerExpiresAt?: string }): Promise<Record<string, unknown>>;
  rebind(params: { bindingId: string; authorityRevision: number; endpoint: string; bearer: string; bearerExpiresAt?: string }): Promise<Record<string, unknown>>;
  confirmCapabilities(threadId: string, bindingId: string, candidateRevision: number, decision: "accept" | "reject"): Promise<Record<string, unknown>>;
  enable(threadId: string, appId: string): Promise<AppBindingRequestCreateResult>;
  createSocialBindingRequest(params: {
    threadId: string;
    channelName: string;
  }): Promise<AppBindingRequestCreateResult>;
  getBindingRequest(params: {
    bindingRequestId?: string;
    requestToken?: string;
    bindCode?: string;
  }): Promise<AppBindingRequestGetResult>;
  acceptSocialBinding(params: {
    requestToken: string;
    socialTarget: SocialChannelTarget;
  }): Promise<ThreadAppBinding>;
  resolveSocialBinding(params: AppSocialBindingResolveParams): Promise<AppSocialBindingResolveResult>;
  enqueueThreadInput(params: {
    bindingId: string;
    input: InputPart[];
    displayText?: string;
    triggerLabel?: string;
    triggerRefId?: string;
    startPolicy?: string;
    sender?: SenderContext;
  }): Promise<AppThreadInputEnqueueResult>;
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
  structuredContent?: unknown,
): DynamicToolCallResult {
  return {
    success: false,
    errorCode,
    errorMessage,
    structuredContent,
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

/** A parsed App Binding deep-link handoff. Parallel to the .NET and Python SDK helpers. */
export interface ParsedAppBindingHandoff {
  scheme: string;
  operation: string;
  appId: string;
  requestId: string;
  requestToken: string;
  appServerUrl?: string;
}

/**
 * Parse an App Binding handoff URL such as
 * `app://dotcraft/connect?app=...&request=...&token=...&endpoint=...`.
 *
 * Deep links are activation hints, not authorization: always inspect the request
 * over AppServer before rendering confirmation or accepting.
 */
export function parseAppBindingHandoff(
  url: string,
  options?: { expectedScheme?: string; expectedAppId?: string },
): ParsedAppBindingHandoff {
  const parsed = new URL(url);
  const scheme = parsed.protocol.replace(/:$/, "");
  if (options?.expectedScheme && scheme !== options.expectedScheme) {
    throw new Error(`Unexpected handoff scheme '${scheme}', expected '${options.expectedScheme}'.`);
  }

  const operation = parsed.pathname.replace(/^\//, "");
  const query = parsed.searchParams;
  const appId = query.get("app") ?? "";
  const requestId = query.get("request") ?? "";
  const requestToken = query.get("token") ?? "";
  if (!appId || !requestId || !requestToken) {
    throw new Error("The handoff URL must contain app, request, and token query parameters.");
  }
  if (options?.expectedAppId && appId !== options.expectedAppId) {
    throw new Error(`Unexpected handoff appId '${appId}', expected '${options.expectedAppId}'.`);
  }

  return {
    scheme,
    operation,
    appId,
    requestId,
    requestToken,
    appServerUrl: query.get("endpoint") ?? undefined,
  };
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
  scope?: "identity" | "workspace";
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

function stripRuntimeDynamicToolHandlers(tools: DynamicToolBinding[] | undefined): DynamicToolSpec[] | undefined {
  if (tools === undefined) return undefined;
  return tools.map((declaration) => {
    if (declaration.type === "namespace") {
      return {
        ...declaration,
        tools: declaration.tools.map(({ handler: _handler, ...tool }) => tool),
      };
    }
    const { handler: _handler, ...tool } = declaration;
    return tool;
  });
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
      dynamicTools: stripRuntimeDynamicToolHandlers(options.dynamicTools),
      additionalContext: options.additionalContext,
    });
    const thread = new DotCraftThread(this.sdk, snapshot, identity);
    thread.bindDynamicTools(options.dynamicTools);
    return thread;
  }

  async resume(threadId: string, options: ResumeThreadOptions = {}): Promise<DotCraftThread> {
    const snapshot = await this.sdk.wire.threadResume(threadId, {
      dynamicTools: stripRuntimeDynamicToolHandlers(options.dynamicTools),
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
      scope: options.scope,
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
    const result = await this.sdk.requestRaw<{ apps?: AppInfo[] }>("app/list", {
      includeCatalog: params.includeCatalog ?? true,
      includeDisabled: params.includeDisabled ?? true,
      threadId: params.threadId,
      forceRefresh: params.forceRefresh ?? false,
    });
    return result.apps ?? [];
  }

  async viewApp(appId: string, params: { threadId?: string } = {}): Promise<AppInfo> {
    const result = await this.sdk.requestRaw<{ app?: AppInfo }>("app/view", {
      appId,
      threadId: params.threadId,
    });
    if (!result.app) throw new Error(`App '${appId}' was not returned by app/view.`);
    return result.app;
  }

  async registerLocalApp(appId: string, rootPath: string): Promise<AppInfo> {
    const result = await this.sdk.requestRaw<{ app?: AppInfo }>("app/local/register", {
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
    return await this.sdk.requestRaw<AppConnectionStartResult>("app/connection/start", {
      appId,
      handoffMode: params.handoffMode,
      returnTo: params.returnTo,
    });
  }

  async connect(params: {
    connectionRequestId: string;
    requestToken: string;
    accountLabel?: string;
  }): Promise<AppConnectionConnectResult> {
    return await this.sdk.requestRaw<AppConnectionConnectResult>("app/connection/connect", {
      connectionRequestId: params.connectionRequestId,
      requestToken: params.requestToken,
      accountLabel: params.accountLabel,
    });
  }

  async connectionStatus(appId: string): Promise<AppConnectionStatus> {
    return await this.sdk.requestRaw<AppConnectionStatus>("app/connection/status", { appId });
  }

  async revokeConnection(appId: string, reason?: string): Promise<AppConnectionStatus> {
    return await this.sdk.requestRaw<AppConnectionStatus>("app/connection/revoke", { appId, reason });
  }

  async publishSurface(params: AppSurfacePublishParams): Promise<AppSurface> {
    return await this.sdk.requestRaw<AppSurface>("app/surface/publish", {
      surfaceId: params.surfaceId,
      endpoint: params.endpoint,
      bearer: params.bearer,
    });
  }

  async resolveSurface(params: AppSurfaceResolveParams): Promise<AppSurface> {
    return await this.sdk.requestRaw<AppSurface>("app/surface/resolve", {
      appId: params.appId,
      surfaceId: params.surfaceId,
    });
  }

  async authenticate(appId: string, credential: string): Promise<Record<string, unknown>> {
    return await this.sdk.requestRaw("app/connection/authenticate", { appId, credential });
  }

  async refreshCredential(): Promise<Record<string, unknown>> {
    return await this.sdk.requestRaw("app/connection/refresh", {});
  }

  async activate(params: { bindingRequestId: string; endpoint: string; bearer: string; bearerExpiresAt?: string }): Promise<Record<string, unknown>> {
    return await this.sdk.requestRaw("app/binding/activate", params);
  }

  async rebind(params: { bindingId: string; authorityRevision: number; endpoint: string; bearer: string; bearerExpiresAt?: string }): Promise<Record<string, unknown>> {
    return await this.sdk.requestRaw("app/binding/rebind", params);
  }

  async confirmCapabilities(threadId: string, bindingId: string, candidateRevision: number, decision: "accept" | "reject"): Promise<Record<string, unknown>> {
    return await this.sdk.requestRaw("thread/appBindings/confirmCapabilities", { threadId, bindingId, candidateRevision, decision });
  }

  async enable(threadId: string, appId: string): Promise<AppBindingRequestCreateResult> {
    return await this.sdk.requestRaw<AppBindingRequestCreateResult>("thread/appBindings/enable", {
      threadId,
      appId,
    });
  }

  async createSocialBindingRequest(params: {
    threadId: string;
    channelName: string;
  }): Promise<AppBindingRequestCreateResult> {
    return await this.sdk.requestRaw<AppBindingRequestCreateResult>("thread/socialBindings/request/create", {
      threadId: params.threadId,
      channelName: params.channelName,
    });
  }

  async getBindingRequest(params: {
    bindingRequestId?: string;
    requestToken?: string;
    bindCode?: string;
  }): Promise<AppBindingRequestGetResult> {
    return await this.sdk.requestRaw<AppBindingRequestGetResult>(
      params.bindCode ? "app/socialBinding/request/get" : "app/binding/request/get",
      params.bindCode ? { code: params.bindCode } : {
        bindingRequestId: params.bindingRequestId,
        requestToken: params.requestToken,
      },
    );
  }

  async acceptSocialBinding(params: {
    requestToken: string;
    socialTarget: SocialChannelTarget;
  }): Promise<ThreadAppBinding> {
    const target = params.socialTarget;
    return await this.sdk.requestRaw<ThreadAppBinding>("app/socialBinding/accept", {
      code: params.requestToken,
      target,
    });
  }

  async resolveSocialBinding(params: AppSocialBindingResolveParams): Promise<AppSocialBindingResolveResult> {
    return await this.sdk.requestRaw<AppSocialBindingResolveResult>("app/socialBinding/resolve", {
      channelName: params.channelName,
      accountId: params.accountId,
      conversationKind: params.conversationKind,
      conversationId: params.conversationId,
    });
  }

  async enqueueThreadInput(params: {
    bindingId: string;
    input: InputPart[];
    displayText?: string;
    triggerLabel?: string;
    triggerRefId?: string;
    startPolicy?: string;
    sender?: SenderContext;
  }): Promise<AppThreadInputEnqueueResult> {
    return await this.sdk.requestRaw<AppThreadInputEnqueueResult>("app/threadInput/enqueue", params);
  }

  async listThreadBindings(threadId: string, includeRevoked = false): Promise<ThreadAppBinding[]> {
    const result = await this.sdk.requestRaw<{ bindings?: ThreadAppBinding[] }>("thread/appBindings/list", {
      threadId,
      includeRevoked,
    });
    return result.bindings ?? [];
  }

  async revokeThreadBinding(threadId: string, bindingId: string, reason?: string): Promise<Record<string, unknown>> {
    return await this.sdk.requestRaw<Record<string, unknown>>("thread/appBindings/revoke", {
      threadId,
      bindingId,
      reason,
    });
  }

  async refreshThreadBindings(threadId: string, bindingId?: string): Promise<Record<string, unknown>[]> {
    const result = await this.sdk.requestRaw<{ bindings?: Record<string, unknown>[] }>("thread/appBindings/list", { threadId });
    return result.bindings ?? [];
  }
}

export interface ModelInfo {
  id: string;
  displayName: string;
  provider?: string | null;
}

export interface ModelManager {
  list(): Promise<ModelInfo[]>;
}

class ModelManagerImpl implements ModelManager {
  constructor(private readonly sdk: DotCraft) {}

  async list(): Promise<ModelInfo[]> {
    const result = await this.sdk.requestRaw<{ models?: unknown[]; items?: unknown[] }>("model/list", {});
    const items = (result.models ?? result.items ?? []) as unknown[];
    return items
      .filter((m): m is Record<string, unknown> => typeof m === "object" && m !== null)
      .map((m) => ({
        id: String(m.id ?? m.modelId ?? m.name ?? ""),
        displayName: String(m.displayName ?? m.name ?? m.id ?? ""),
        provider: (m.provider as string | undefined) ?? null,
      }))
      .filter((m) => m.id.length > 0);
  }
}

class McpRuntimeManagerImpl implements McpRuntimeManager {
  constructor(private readonly sdk: DotCraft) {}

  listStatus(params: McpServerStatusListParams = {}): Promise<McpServerStatusListResult> {
    return this.sdk.requestRaw<McpServerStatusListResult>("mcpServerStatus/list", params);
  }

  readResource(params: McpServerResourceReadParams): Promise<McpServerResourceReadResult> {
    return this.sdk.requestRaw<McpServerResourceReadResult>("mcpServer/resource/read", params);
  }

  callTool(params: McpServerToolCallParams): Promise<McpServerToolCallResult> {
    return this.sdk.requestRaw<McpServerToolCallResult>("mcpServer/tool/call", params);
  }

  loginOAuth(params: McpServerOAuthLoginParams): Promise<McpServerOAuthLoginResult> {
    return this.sdk.requestRaw<McpServerOAuthLoginResult>("mcpServer/oauth/login", params);
  }

  reload(): Promise<McpServerReloadResult> {
    return this.sdk.requestRaw<McpServerReloadResult>("config/mcpServer/reload");
  }
}

export class DotCraft {
  readonly threads: ThreadManager;
  readonly appBindings: AppBindingManager;
  readonly models: ModelManager;
  readonly mcpRuntime: McpRuntimeManager;

  private constructor(
    readonly wire: DotCraftAppServerClient,
    readonly serverInfo: ServerInfo,
    readonly capabilities: ServerCapabilities,
    private readonly approvalHandler?: ApprovalHandler,
    private readonly userInputHandler?: UserInputHandler,
  ) {
    this.threads = new ThreadManagerImpl(this);
    this.appBindings = new AppBindingManagerImpl(this);
    this.models = new ModelManagerImpl(this);
    this.mcpRuntime = new McpRuntimeManagerImpl(this);
    this.installServerRequestHandlers();
  }

  static async local(options: DotCraftLocalOptions): Promise<DotCraft> {
    if (!options.workspacePath?.trim()) {
      throw new InitializationError("workspacePath is required for DotCraft.local().");
    }
    const hub = new HubClient({
      executable: options.executable,
      expectedExecutable: options.expectedExecutable,
      binaryMatchPolicy: options.binaryMatchPolicy,
      hubStartupTimeoutMs: options.hubStartupTimeoutMs,
      homeDir: options.homeDir,
    });
    const ensured = await hub.ensureAppServer(options.workspacePath, {
      clientName: options.clientName ?? "dotcraft-sdk",
      clientVersion: options.clientVersion ?? "0.1.0",
    });
    const wsUrl = ensured.endpoints.appServerWebSocket;
    if (!wsUrl) throw new InitializationError("Hub response did not include endpoints.appServerWebSocket.");
    return await DotCraft.connect(new WebSocketTransport({ url: wsUrl }), options);
  }

  static async localChat(options: DotCraftLocalChatOptions = {}): Promise<DotCraft> {
    const hub = new HubClient({
      executable: options.executable,
      expectedExecutable: options.expectedExecutable,
      binaryMatchPolicy: options.binaryMatchPolicy,
      hubStartupTimeoutMs: options.hubStartupTimeoutMs,
      homeDir: options.homeDir,
    });
    const ensured = await hub.ensureDefaultChatAppServer({
      clientName: options.clientName ?? "dotcraft-sdk",
      clientVersion: options.clientVersion ?? "0.1.0",
    });
    const wsUrl = ensured.endpoints.appServerWebSocket;
    if (!wsUrl) throw new InitializationError("Hub response did not include endpoints.appServerWebSocket.");
    return await DotCraft.connect(new WebSocketTransport({ url: wsUrl }), {
      ...options,
      workspacePath: ensured.workspacePath,
    });
  }

  static async remote(options: DotCraftRemoteOptions): Promise<DotCraft> {
    return await DotCraft.connect(new WebSocketTransport({ url: options.url, token: options.token }), options);
  }

  request<M extends keyof ClientRequestMethods>(
    method: M,
    params: ClientRequestMethods[M]["params"],
  ): Promise<ClientRequestMethods[M]["result"]> {
    return this.wire.request(method, params);
  }

  requestRaw<T = unknown>(method: string, params?: unknown): Promise<T> {
    return this.wire.requestRaw<T>(method, params);
  }

  on<M extends keyof ServerNotificationMethods>(
    event: M,
    handler: (params: ServerNotificationMethods[M]["params"]) => void | Promise<void>,
  ): Unsubscribe {
    return this.wire.on(event, handler);
  }

  onRaw(event: string, handler: NotificationHandler): Unsubscribe {
    return this.wire.onRaw(event, handler);
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
    if (options.capabilities?.approvalSupport === true && !options.approvalHandler) {
      throw new InitializationError("approvalSupport requires an approvalHandler.");
    }
    if (options.capabilities?.requestUserInputSupport === true && !options.userInputHandler) {
      throw new InitializationError("requestUserInputSupport requires a userInputHandler.");
    }
    const wire = new DotCraftAppServerClient(transport, { autoReconnect: true });
    let sdk: DotCraft | undefined;
    if (options.approvalHandler) {
      wire.registerServerRequestHandler("item/approval/request", async (_id, params) => ({
        decision: await options.approvalHandler!(params),
      }));
    }
    if (options.userInputHandler) {
      wire.registerServerRequestHandler("item/tool/requestUserInput", async (_id, params) => (
        await options.userInputHandler!(params)
      ) as never);
    }
    wire.registerServerRequestHandler("item/tool/call", async (_id, params) => {
      const request = params as unknown as DynamicToolCallRequest;
      const handler = sdk?.dynamicToolHandlers.get(toolKey(request.threadId, request.namespace, request.tool));
      if (!handler) {
        return { success: false, errorCode: "UnsupportedTool", errorMessage: "No handler registered for this dynamic tool." };
      }
      try {
        return await handler(request) as never;
      } catch (error) {
        return {
          success: false,
          errorCode: "AdapterToolCallFailed",
          errorMessage: error instanceof Error ? error.message : String(error),
        };
      }
    });
    await wire.connect();
    const initialized = await wire.initialize({
      clientName: options.clientName ?? "dotcraft-sdk",
      clientVersion: options.clientVersion ?? "0.1.0",
      clientTitle: options.clientTitle,
      approvalSupport: Boolean(options.approvalHandler),
      requestUserInputSupport: Boolean(options.userInputHandler),
      streamingSupport: true,
      configChange: true,
      extraCapabilities: options.capabilities,
    });
    sdk = new DotCraft(
      wire,
      initialized.serverInfo,
      initialized.capabilities,
      options.approvalHandler,
      options.userInputHandler,
    );
    return sdk;
  }

  private installServerRequestHandlers(): void {
    if (this.approvalHandler) {
      this.wire.registerServerRequestHandler("item/approval/request", async (_id, params) => ({
        decision: await this.approvalHandler!(params),
      }));
    }
    if (this.userInputHandler) {
      this.wire.registerServerRequestHandler("item/tool/requestUserInput", async (_id, params) => (
        await this.userInputHandler!(params)
      ) as never);
    }
    this.wire.registerServerRequestHandler("item/tool/call", async (_id, params) => {
      const request = params as unknown as DynamicToolCallRequest;
      const handler = this.dynamicToolHandlers.get(toolKey(request.threadId, request.namespace, request.tool));
      if (!handler) {
        return { success: false, errorCode: "UnsupportedTool", errorMessage: "No handler registered for this dynamic tool." };
      }
      try {
        return await handler(request) as never;
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
    for (const unsubscribe of this.dynamicToolUnsubscribes.splice(0)) unsubscribe();
    if (!tools) return;
    for (const declaration of tools) {
      if (declaration.type === "namespace") {
        for (const tool of declaration.tools) {
          this.dynamicToolUnsubscribes.push(this.onToolCall(declaration.name, tool.name, tool.handler));
        }
      } else {
        this.dynamicToolUnsubscribes.push(this.onToolCall(null, declaration.name, declaration.handler));
      }
    }
  }
}
