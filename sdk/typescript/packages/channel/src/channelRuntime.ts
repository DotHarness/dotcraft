/**
 * Reusable runtime components for external channel adapters.
 */

import { readFile } from "node:fs/promises";
import { join } from "node:path";

import { commandRefPart } from "@dotcraft/sdk";
import type {
  InputPart,
  RuntimeAdditionalContextEntry,
  SenderContext,
  SessionThread,
} from "@dotcraft/sdk/contracts";
import type { ChannelAppServerClient } from "./channelAppServerClient.js";
import type { ChannelChatDescriptor } from "./capability.js";
import type { LifecycleStatus, ModuleError, ModuleErrorCode } from "./lifecycle.js";
import type { WorkspaceContext } from "./module.js";
import { emptyUserInputResponse, type UserInputResponse } from "./userInput.js";

export type ClientProvider = ChannelAppServerClient | (() => ChannelAppServerClient);

export function resolveClient(provider: ClientProvider): ChannelAppServerClient {
  return typeof provider === "function" ? provider() : provider;
}

function isNodeErrno(error: unknown, code: string): boolean {
  return (
    typeof error === "object" &&
    error !== null &&
    "code" in error &&
    String((error as { code?: unknown }).code) === code
  );
}

export type ChannelSenderContext = Record<string, unknown> & {
  senderId?: string;
  senderName?: string;
  senderRole?: string;
  groupId?: string;
  directTarget?: string;
};

export type ChannelAdapterMessageOptions = {
  userId: string;
  userName: string;
  text: string;
  channelContext?: string;
  workspacePath?: string;
  sender?: ChannelSenderContext;
  senderExtra?: Record<string, unknown>;
  skipCommand?: boolean;
  inputParts?: InputPart[];
  omitSenderGroupId?: boolean;
  directTarget?: string;
};

export function buildChannelSender(
  opts: Pick<ChannelAdapterMessageOptions, "userId" | "userName" | "sender" | "senderExtra" | "omitSenderGroupId" | "directTarget">,
  channelContext: string,
): SenderContext {
  const sender: Record<string, unknown> = { ...(opts.sender ?? {}) };
  if (sender.senderId === undefined) sender.senderId = opts.userId;
  if (sender.senderName === undefined) sender.senderName = opts.userName;
  if (sender.senderRole === undefined) sender.senderRole = "user";
  Object.assign(sender, opts.senderExtra ?? {});
  if (opts.directTarget && sender.directTarget === undefined) sender.directTarget = opts.directTarget;
  if (channelContext && !opts.omitSenderGroupId && sender.groupId === undefined) {
    sender.groupId = channelContext;
  }
  return sender as SenderContext;
}

export function replaceLeadingSlashTextWithCommandRef(
  inputParts: InputPart[] | undefined,
  rawSlashText: string,
): InputPart[] {
  const next = inputParts ? [...inputParts] : [];
  const textIndex = next.findIndex(
    (part) => String((part as { type?: unknown }).type ?? "") === "text",
  );
  const refPart = commandRefPart(rawSlashText);
  if (textIndex >= 0) {
    next[textIndex] = refPart;
  } else {
    next.unshift(refPart);
  }
  return next;
}

export type ChannelMessageJob = () => Promise<void>;

export interface ChannelMessageQueueOptions {
  isRunning?: () => boolean;
  onError?: (identityKey: string, error: unknown) => void;
}

export class ChannelMessageQueue {
  private readonly queues = new Map<string, ChannelMessageJob[]>();
  private readonly runningWorkers = new Map<string, boolean>();
  private readonly isRunning: () => boolean;
  private readonly onError: (identityKey: string, error: unknown) => void;

  constructor(options: ChannelMessageQueueOptions = {}) {
    this.isRunning = options.isRunning ?? (() => true);
    this.onError = options.onError ?? ((identityKey, error) => {
      console.error(`Error processing message for ${identityKey}:`, error);
    });
  }

  enqueue(identityKey: string, job: ChannelMessageJob): void {
    let q = this.queues.get(identityKey);
    if (!q) {
      q = [];
      this.queues.set(identityKey, q);
    }

    q.push(job);
    void this.runWorker(identityKey, q);
  }

  clear(identityKey?: string): void {
    if (identityKey === undefined) {
      this.queues.clear();
      return;
    }
    this.queues.delete(identityKey);
  }

  activeKeys(): string[] {
    const keys = new Set<string>();
    for (const [key, q] of this.queues) {
      if (q.length > 0) keys.add(key);
    }
    for (const [key, running] of this.runningWorkers) {
      if (running) keys.add(key);
    }
    return [...keys];
  }

  private async runWorker(identityKey: string, q: ChannelMessageJob[]): Promise<void> {
    if (this.runningWorkers.get(identityKey)) return;
    this.runningWorkers.set(identityKey, true);
    try {
      while (this.isRunning() && q.length > 0) {
        const job = q.shift();
        if (!job) continue;
        try {
          await job();
        } catch (error) {
          this.onError(identityKey, error);
        }
      }
    } finally {
      this.runningWorkers.set(identityKey, false);
      if (q.length > 0 && this.isRunning()) void this.runWorker(identityKey, q);
    }
  }
}

export type ThreadResolveEventAction =
  | "cache_hit"
  | "cache_invalidated"
  | "listed_active"
  | "created"
  | "recovered_stale_active"
  | "recovered_listed_active"
  | "recovered_created";

export interface ThreadResolveEvent {
  action: ThreadResolveEventAction;
  identityKey: string;
  userId?: string;
  channelContext?: string;
  workspacePath?: string;
  threadId?: string;
  staleThreadId?: string;
  error?: unknown;
}

export interface ThreadResolverOptions {
  client: ClientProvider;
  channelName: string;
  threadMap?: Map<string, string>;
  getRuntimeAdditionalContext?: () => Record<string, RuntimeAdditionalContextEntry> | undefined;
  onEvent?: (event: ThreadResolveEvent) => void;
}

export interface ThreadIdentityLookup {
  identityKey: string;
  userId: string;
  channelContext: string;
  workspacePath: string;
  chat?: ChannelChatDescriptor;
}

export class ThreadResolver {
  readonly threadMap: Map<string, string>;
  private readonly client: ClientProvider;
  private readonly channelName: string;
  private readonly resolving = new Map<string, Promise<SessionThread>>();
  private readonly getRuntimeAdditionalContext?: () => Record<string, RuntimeAdditionalContextEntry> | undefined;
  private readonly onEvent?: (event: ThreadResolveEvent) => void;

  constructor(options: ThreadResolverOptions) {
    this.client = options.client;
    this.channelName = options.channelName;
    this.threadMap = options.threadMap ?? new Map<string, string>();
    this.getRuntimeAdditionalContext = options.getRuntimeAdditionalContext;
    this.onEvent = options.onEvent;
  }

  invalidateConnectionBoundRuntime(): void {
    this.threadMap.clear();
  }

  getCachedThreadId(identityKey: string): string | undefined {
    return this.threadMap.get(identityKey);
  }

  setCachedThread(identityKey: string, threadId: string): void {
    this.threadMap.set(identityKey, threadId);
  }

  deleteCachedThread(identityKey: string): void {
    this.threadMap.delete(identityKey);
  }

  async getOrCreateThread(lookup: ThreadIdentityLookup): Promise<SessionThread> {
    const pending = this.resolving.get(lookup.identityKey);
    if (pending) return await pending;
    const resolving = this.resolveThread(lookup);
    this.resolving.set(lookup.identityKey, resolving);
    try {
      return await resolving;
    } finally {
      if (this.resolving.get(lookup.identityKey) === resolving) this.resolving.delete(lookup.identityKey);
    }
  }

  private async resolveThread(lookup: ThreadIdentityLookup): Promise<SessionThread> {
    const client = resolveClient(this.client);
    const additionalContext = this.getRuntimeAdditionalContext?.();

    const threadId = this.threadMap.get(lookup.identityKey);
    if (threadId) {
      try {
        const thread = await client.threadRead(threadId);
        if (thread.status === "active") {
          this.emit({ action: "cache_hit", ...lookup, threadId: thread.id });
          return thread;
        }
      } catch (error) {
        this.threadMap.delete(lookup.identityKey);
        this.emit({ action: "cache_invalidated", ...lookup, threadId, error });
      }
      this.threadMap.delete(lookup.identityKey);
    }

    const threads = await client.threadList({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
    });
    const reusable = threads.find((t) => t.status === "active");
    if (reusable) {
      const thread = additionalContext || lookup.chat
        ? await client.threadResume(reusable.id, { additionalContext, chat: lookup.chat })
        : await client.threadRead(reusable.id);
      this.threadMap.set(lookup.identityKey, thread.id);
      this.emit({ action: "listed_active", ...lookup, threadId: thread.id });
      return thread;
    }

    const thread = await client.threadStart({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
      additionalContext,
      chat: lookup.chat,
    });
    this.threadMap.set(lookup.identityKey, thread.id);
    this.emit({ action: "created", ...lookup, threadId: thread.id });
    return thread;
  }

  async recoverThreadAfterNotActive(
    lookup: ThreadIdentityLookup,
    staleThreadId: string,
  ): Promise<SessionThread> {
    const client = resolveClient(this.client);
    const additionalContext = this.getRuntimeAdditionalContext?.();

    try {
      const latest = await client.threadRead(staleThreadId);
      if (latest.status === "active") {
        this.threadMap.set(lookup.identityKey, latest.id);
        this.emit({ action: "recovered_stale_active", ...lookup, staleThreadId, threadId: latest.id });
        return latest;
      }
    } catch {
      // Continue with identity-level lookup.
    }

    this.threadMap.delete(lookup.identityKey);
    const threads = await client.threadList({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
    });
    const reusable = threads.find((t) => t.status === "active");
    if (reusable) {
      const thread = additionalContext || lookup.chat
        ? await client.threadResume(reusable.id, { additionalContext, chat: lookup.chat })
        : await client.threadRead(reusable.id);
      this.threadMap.set(lookup.identityKey, thread.id);
      this.emit({ action: "recovered_listed_active", ...lookup, staleThreadId, threadId: thread.id });
      return thread;
    }

    const fresh = await client.threadStart({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
      additionalContext,
      chat: lookup.chat,
    });
    this.threadMap.set(lookup.identityKey, fresh.id);
    this.emit({ action: "recovered_created", ...lookup, staleThreadId, threadId: fresh.id });
    return fresh;
  }

  private emit(event: ThreadResolveEvent): void {
    this.onEvent?.(event);
  }
}

export interface ApprovalDispatcherOptions {
  client: ClientProvider;
  onApprovalRequest: (request: Record<string, unknown>) => Promise<string>;
  onError?: (message: string, error: unknown) => void;
}

export class ApprovalDispatcher {
  private readonly client: ClientProvider;
  private readonly onApprovalRequest: (request: Record<string, unknown>) => Promise<string>;
  private readonly onError: (message: string, error: unknown) => void;

  constructor(options: ApprovalDispatcherOptions) {
    this.client = options.client;
    this.onApprovalRequest = options.onApprovalRequest;
    this.onError = options.onError ?? ((message, error) => console.error(message, error));
  }

  register(): void {
    resolveClient(this.client).registerServerRequestHandler("item/approval/request", async (_id, params) => {
      try {
        return { decision: await this.onApprovalRequest(params) };
      } catch (error) {
        this.onError("onApprovalRequest raised:", error);
        return { decision: "cancel" };
      }
    });
  }
}

export interface UserInputDispatcherOptions {
  client: ClientProvider;
  onUserInputRequest: (request: Record<string, unknown>) => Promise<UserInputResponse | Record<string, unknown>>;
  onError?: (message: string, error: unknown) => void;
}

export class UserInputDispatcher {
  private readonly client: ClientProvider;
  private readonly onUserInputRequest: (request: Record<string, unknown>) => Promise<UserInputResponse | Record<string, unknown>>;
  private readonly onError: (message: string, error: unknown) => void;

  constructor(options: UserInputDispatcherOptions) {
    this.client = options.client;
    this.onUserInputRequest = options.onUserInputRequest;
    this.onError = options.onError ?? ((message, error) => console.error(message, error));
  }

  register(): void {
    resolveClient(this.client).registerServerRequestHandler("item/tool/requestUserInput", async (_id, params) => {
      try {
        return await this.onUserInputRequest(params) as never;
      } catch (error) {
        this.onError("onUserInputRequest raised:", error);
        return emptyUserInputResponse() as never;
      }
    });
  }
}

export interface ChannelToolDispatcherOptions {
  client: ClientProvider;
  onToolCall: (request: Record<string, unknown>) => Promise<Record<string, unknown>>;
  onError?: (message: string, error: unknown) => void;
}

export class ChannelToolDispatcher {
  private readonly client: ClientProvider;
  private readonly onToolCall: (request: Record<string, unknown>) => Promise<Record<string, unknown>>;
  private readonly onError: (message: string, error: unknown) => void;

  constructor(options: ChannelToolDispatcherOptions) {
    this.client = options.client;
    this.onToolCall = options.onToolCall;
    this.onError = options.onError ?? ((message, error) => console.error(message, error));
  }

  register(): void {
    const client = resolveClient(this.client);
    client.registerServerRequestHandler("ext/channel/toolCall", async (_id, params) => {
      try {
        return await this.onToolCall((params as Record<string, unknown>) ?? {});
      } catch (error) {
        this.onError("onToolCall raised:", error);
        return {
          success: false,
          errorCode: "AdapterToolCallFailed",
          errorMessage: String(error),
        };
      }
    });
    client.registerServerRequestHandler("ext/channel/heartbeat", async () => ({}));
  }
}

export class ModuleLifecycleState {
  private lifecycleStatus: LifecycleStatus;
  private lifecycleError: ModuleError | undefined;
  private readonly statusHandlers: Array<(status: LifecycleStatus, error?: ModuleError) => void> = [];

  constructor(initialStatus: LifecycleStatus = "stopped") {
    this.lifecycleStatus = initialStatus;
  }

  getStatus(): LifecycleStatus {
    return this.lifecycleStatus;
  }

  getError(): ModuleError | undefined {
    return this.lifecycleError;
  }

  onStatusChange(handler: (status: LifecycleStatus, error?: ModuleError) => void): void {
    this.statusHandlers.push(handler);
  }

  setStatus(status: LifecycleStatus, error?: ModuleError): void {
    const nextError = status === "starting" || status === "ready" ? undefined : error;
    if (this.lifecycleStatus === status && this.lifecycleError === nextError) {
      return;
    }

    this.lifecycleStatus = status;
    this.lifecycleError = nextError;

    for (const handler of this.statusHandlers) {
      handler(status, this.lifecycleError);
    }
  }

  buildStatusError(code: "authRequired" | "authExpired", error?: Partial<ModuleError>): ModuleError {
    return {
      code,
      message: error?.message ?? (code === "authRequired" ? "Interactive authentication is required." : "Authentication has expired."),
      detail: error?.detail,
      timestamp: error?.timestamp ?? new Date().toISOString(),
    };
  }

  buildModuleError(code: ModuleErrorCode, message: string): ModuleError {
    return {
      code,
      message,
      timestamp: new Date().toISOString(),
    };
  }
}

export type LoadJsonConfigResult = { found: true; data: unknown } | { found: false };

export function resolveConfigPath(context: WorkspaceContext, configFileName: string): string {
  if (context.configOverridePath) return context.configOverridePath;
  return join(context.craftPath, configFileName);
}

export async function loadJsonConfig(configPath: string): Promise<LoadJsonConfigResult> {
  try {
    const raw = await readFile(configPath, "utf-8");
    return { found: true, data: JSON.parse(raw) };
  } catch (error) {
    if (isNodeErrno(error, "ENOENT")) {
      return { found: false };
    }
    throw error;
  }
}

export function resolveModuleStatePath(context: WorkspaceContext): string {
  return join(context.craftPath, "state", context.moduleId);
}

export function resolveModuleTempPath(context: WorkspaceContext): string {
  return join(context.craftPath, "tmp", context.moduleId);
}

export class ConfigValidationError extends Error {
  readonly fields?: string[];

  constructor(message: string, fields?: string[]) {
    super(message);
    this.name = "ConfigValidationError";
    this.fields = fields;
  }
}

export type ModuleConfigLoadResult<TConfig> =
  | { status: "loaded"; config: TConfig; configPath: string; stdioRuntime: boolean }
  | { status: "configMissing"; configPath: string; error: ModuleError }
  | { status: "configInvalid"; configPath: string; error: ModuleError };

export interface ModuleConfigLoaderOptions<TConfig> {
  getConfigFileName: (context: WorkspaceContext) => string;
  validateConfig: (rawConfig: unknown) => asserts rawConfig is TConfig;
  lifecycle?: ModuleLifecycleState;
  isStdioRuntime?: () => boolean;
}

export class ModuleConfigLoader<TConfig = unknown> {
  private readonly getConfigFileName: (context: WorkspaceContext) => string;
  private readonly validateConfig: (rawConfig: unknown) => asserts rawConfig is TConfig;
  private readonly lifecycle: ModuleLifecycleState;
  private readonly isStdioRuntime: () => boolean;

  constructor(options: ModuleConfigLoaderOptions<TConfig>) {
    this.getConfigFileName = options.getConfigFileName;
    this.validateConfig = options.validateConfig;
    this.lifecycle = options.lifecycle ?? new ModuleLifecycleState();
    this.isStdioRuntime = options.isStdioRuntime ?? isStdioChannelRuntime;
  }

  async load(context: WorkspaceContext): Promise<ModuleConfigLoadResult<TConfig>> {
    const configPath = resolveConfigPath(context, this.getConfigFileName(context));
    const loaded = await loadJsonConfig(configPath);
    if (!loaded.found) {
      return {
        status: "configMissing",
        configPath,
        error: this.lifecycle.buildModuleError("configMissing", `Config file not found: ${configPath}`),
      };
    }

    try {
      const stdioRuntime = this.isStdioRuntime();
      const rawConfig = applyChannelRuntimeDefaults(loaded.data, stdioRuntime);
      this.validateConfig(rawConfig);
      return { status: "loaded", config: rawConfig, configPath, stdioRuntime };
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      return {
        status: "configInvalid",
        configPath,
        error: this.lifecycle.buildModuleError("configInvalid", message),
      };
    }
  }
}

export function isStdioChannelRuntime(): boolean {
  return process.env.DOTCRAFT_CHANNEL_TRANSPORT === "stdio";
}

export function isWebSocketChannelRuntime(): boolean {
  return process.env.DOTCRAFT_CHANNEL_TRANSPORT === "websocket";
}

export function applyChannelRuntimeDefaults(rawConfig: unknown, stdioRuntime = isStdioChannelRuntime()): unknown {
  if (stdioRuntime) return withStdioRuntimeDefaults(rawConfig);
  if (isWebSocketChannelRuntime()) return withWebSocketRuntimeDefaults(rawConfig);
  return rawConfig;
}

export function withStdioRuntimeDefaults(rawConfig: unknown): unknown {
  if (!rawConfig || typeof rawConfig !== "object" || Array.isArray(rawConfig)) {
    return rawConfig;
  }

  const config = { ...(rawConfig as Record<string, unknown>) };
  const dotcraftRaw = config.dotcraft;
  const dotcraft =
    dotcraftRaw && typeof dotcraftRaw === "object" && !Array.isArray(dotcraftRaw)
      ? { ...(dotcraftRaw as Record<string, unknown>) }
      : {};

  if (typeof dotcraft.wsUrl !== "string" || dotcraft.wsUrl.trim() === "") {
    dotcraft.wsUrl = "ws://127.0.0.1/stdio-placeholder";
  }

  config.dotcraft = dotcraft;
  return config;
}

export function withWebSocketRuntimeDefaults(rawConfig: unknown): unknown {
  if (!rawConfig || typeof rawConfig !== "object" || Array.isArray(rawConfig)) {
    return rawConfig;
  }

  const wsUrl = process.env.DOTCRAFT_CHANNEL_WS_URL?.trim();
  if (!wsUrl) return rawConfig;

  const config = { ...(rawConfig as Record<string, unknown>) };
  const dotcraftRaw = config.dotcraft;
  const dotcraft =
    dotcraftRaw && typeof dotcraftRaw === "object" && !Array.isArray(dotcraftRaw)
      ? { ...(dotcraftRaw as Record<string, unknown>) }
      : {};

  dotcraft.wsUrl = wsUrl;
  if (process.env.DOTCRAFT_CHANNEL_WS_TOKEN !== undefined) {
    dotcraft.token = process.env.DOTCRAFT_CHANNEL_WS_TOKEN.trim();
  }

  config.dotcraft = dotcraft;
  return config;
}
