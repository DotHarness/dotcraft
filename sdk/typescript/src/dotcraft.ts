import { ThreadManagerImpl } from "./threadManager.js";

import { type NotificationHandler, type Unsubscribe } from "./client.js";
import { InternalAppServerClient } from "./appServerClient.js";
import { JsonRpcMessage } from "./models.js";
import { DotCraftThread } from "./thread.js";
import { toContractSender } from "./threadInput.js";
export { DotCraftThread } from "./thread.js";
import { WebSocketTransport } from "./transport.js";
import { HubClient, type HubBinaryMatchPolicy } from "./hubClient.js";
import { InitializationError } from "./errors.js";
import type {
  ClientRequestMethods,
  DynamicToolCallResult,
  DynamicToolContentItem,
  InputPart,
  JsonValue,
  McpServerElicitationResponse,
  McpServerOAuthLoginCompletedNotification,
  McpServerOAuthLoginParams,
  McpServerOAuthLoginResult,
  McpServerOrigin,
  McpServerReloadResult,
  McpServerResourceReadParams,
  McpServerResourceReadResult,
  McpServerRuntimeStatus,
  McpServerStartupStatusUpdatedNotification,
  McpServerStatusListParams,
  McpServerStatusListResult,
  McpServerToolCallParams,
  McpServerToolCallResult,
  RuntimeAdditionalContextEntry,
  SenderContext as ContractSenderContext,
  ServerNotificationMethods,
  ServerCapabilities,
  ServerInfo,
  SessionThread,
  SessionTurn,
  ThreadListResult,
  ThreadResumeParams,
  ThreadForkParams,
  ThreadItemsListResult,
  ThreadSummary,
  ThreadTurnsListResult,
  ModelCatalogItem,
} from "./generated/appserver/index.js";
export type ApprovalDecision =
  | "accept"
  | "acceptForSession"
  | "acceptAlways"
  | "decline"
  | "cancel";

export type ApprovalHandler = (request: Record<string, unknown>) => Promise<ApprovalDecision> | ApprovalDecision;
export type UserInputHandler =
  (request: Record<string, unknown>) => Promise<Record<string, unknown>> | Record<string, unknown>;

export type SenderContext = Partial<ContractSenderContext> & Pick<ContractSenderContext, "senderId">;

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

export type DynamicToolHandler =
  (request: DynamicToolCallRequest) => Promise<DynamicToolCallResult> | DynamicToolCallResult;

export type DynamicToolFunctionBinding = DynamicToolFunctionSpec & { handler: DynamicToolHandler };
export type DynamicToolNamespaceBinding = Omit<DynamicToolNamespaceSpec, "tools"> & {
  tools: DynamicToolFunctionBinding[];
};
export type DynamicToolBinding = DynamicToolFunctionBinding | DynamicToolNamespaceBinding;

export type McpServerOriginKind = "workspace" | "plugin" | "thread" | "binding" | (string & {});

export interface McpServerElicitationRequest {
  threadId: string;
  turnId?: string | null;
  serverName: string;
  mode: "form" | "url";
  elicitationId?: string | null;
  message?: string | null;
  url?: string | null;
  requestedSchema?: Record<string, unknown> | null;
  _meta?: unknown;
}

export interface McpRuntimeManager {
  listStatus(params?: McpServerStatusListParams): Promise<McpServerStatusListResult>;
  readResource(params: McpServerResourceReadParams): Promise<McpServerResourceReadResult>;
  callTool(params: McpServerToolCallParams): Promise<McpServerToolCallResult>;
  loginOAuth(params: McpServerOAuthLoginParams): Promise<McpServerOAuthLoginResult>;
  reload(): Promise<McpServerReloadResult>;
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
  initialTurnsPage?: ThreadResumeParams["initialTurnsPage"];
}

export type ForkThreadOptions = Omit<ThreadForkParams, "threadId" | "dynamicTools"> & {
  dynamicTools?: DynamicToolBinding[];
};

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

export interface ThreadHistoryPageOptions {
  cursor?: string;
  limit?: number;
  sortDirection?: "ascending" | "descending";
}

export interface ThreadItemPageOptions extends ThreadHistoryPageOptions {
  turnId?: string;
}

export type RunInput =
  | string
  | InputPart[]
  | {
      input: InputPart[];
      sender?: SenderContext;
    };

export interface RunOptions extends Pick<ClientRequestMethods["turn/start"]["params"], "providerId" | "model" | "reasoning" | "speed" | "approvalPolicy"> {
  sender?: SenderContext;
  collectRawEvents?: boolean;
  abortSignal?: AbortSignal;
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
  thread: SessionThread;
  turn: SessionTurn | null;
  text: string;
  items: unknown[];
  usage?: Record<string, unknown> | null;
  rawEvents?: JsonRpcMessage[];
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
  turn?: SessionTurn;
  result?: DotCraftRunResult;
  error?: string;
};

export interface ThreadManager {
  getOrCreate(options?: GetOrCreateThreadOptions): Promise<DotCraftThread>;
  start(options?: StartThreadOptions): Promise<DotCraftThread>;
  resume(threadId: string, options?: ResumeThreadOptions): Promise<DotCraftThread>;
  fork(threadId: string, options?: ForkThreadOptions): Promise<DotCraftThread>;
  list(options?: ListThreadOptions): Promise<ThreadSummary[]>;
  listPage(options?: ListThreadOptions): Promise<ThreadListResult>;
  read(threadId: string): Promise<SessionThread>;
  listTurns(threadId: string, options?: ThreadHistoryPageOptions): Promise<ThreadTurnsListResult>;
  listItems(threadId: string, options?: ThreadItemPageOptions): Promise<ThreadItemsListResult>;
}

function toolKey(threadId: string, namespace: string | null | undefined, name: string): string {
  return `${threadId}\u0000${namespace ?? ""}\u0000${name}`;
}


export interface ModelManager {
  list(): Promise<ModelCatalogItem[]>;
}

class ModelManagerImpl implements ModelManager {
  constructor(private readonly sdk: DotCraft) {}

  async list(): Promise<ModelCatalogItem[]> {
    const result = await this.sdk.request("model/list", {});
    return result.models ?? [];
  }
}

class McpRuntimeManagerImpl implements McpRuntimeManager {
  constructor(private readonly sdk: DotCraft) {}

  listStatus(params: McpServerStatusListParams = {}): Promise<McpServerStatusListResult> {
    return this.sdk.request("mcpServerStatus/list", params);
  }

  readResource(params: McpServerResourceReadParams): Promise<McpServerResourceReadResult> {
    return this.sdk.request("mcpServer/resource/read", params);
  }

  callTool(params: McpServerToolCallParams): Promise<McpServerToolCallResult> {
    return this.sdk.request("mcpServer/tool/call", params);
  }

  loginOAuth(params: McpServerOAuthLoginParams): Promise<McpServerOAuthLoginResult> {
    return this.sdk.request("mcpServer/oauth/login", params);
  }

  reload(): Promise<McpServerReloadResult> {
    return this.sdk.request("config/mcpServer/reload", {});
  }
}

export class DotCraft {
  readonly threads: ThreadManager;
  readonly models: ModelManager;
  readonly mcpRuntime: McpRuntimeManager;

  private constructor(
    readonly wire: InternalAppServerClient,
    readonly serverInfo: ServerInfo,
    readonly capabilities: ServerCapabilities,
    private readonly approvalHandler?: ApprovalHandler,
    private readonly userInputHandler?: UserInputHandler,
  ) {
    this.threads = new ThreadManagerImpl(this);
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
    const wire = new InternalAppServerClient(transport, { autoReconnect: true });
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
