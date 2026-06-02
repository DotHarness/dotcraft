/**
 * Reusable runtime components for external channel adapters.
 */

import { readFile } from "node:fs/promises";
import { join } from "node:path";

import type { DotCraftWireClient, NotificationHandler, ServerRequestHandler } from "./client.js";
import { DotCraftError } from "./errors.js";
import { JsonRpcMessage, Thread, commandRefPart } from "./models.js";
import type { LifecycleStatus, ModuleError, ModuleErrorCode } from "./lifecycle.js";
import type { WorkspaceContext } from "./module.js";
import { getDeliveredFrontier } from "./deliveredFrontier.js";
import {
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";
import { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";

type ClientProvider = DotCraftWireClient | (() => DotCraftWireClient);

function resolveClient(provider: ClientProvider): DotCraftWireClient {
  return typeof provider === "function" ? provider() : provider;
}

function previewWireText(text: string, maxChars = 160): string {
  const s = text.replace(/\r\n/g, "\n");
  if (s.length <= maxChars) return s;
  return `${s.slice(0, maxChars)}...(+${s.length - maxChars} chars)`;
}

function shortWireId(id: string, max = 12): string {
  if (id.length <= max) return id;
  return `${id.slice(0, max)}...`;
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
  inputParts?: Record<string, unknown>[];
  omitSenderGroupId?: boolean;
};

export function buildChannelSender(
  opts: Pick<ChannelAdapterMessageOptions, "userId" | "userName" | "sender" | "senderExtra" | "omitSenderGroupId">,
  channelContext: string,
): Record<string, unknown> {
  const sender: Record<string, unknown> = { ...(opts.sender ?? {}) };
  if (sender.senderId === undefined) sender.senderId = opts.userId;
  if (sender.senderName === undefined) sender.senderName = opts.userName;
  Object.assign(sender, opts.senderExtra ?? {});
  if (channelContext && !opts.omitSenderGroupId && sender.groupId === undefined) {
    sender.groupId = channelContext;
  }
  return sender;
}

export function replaceLeadingSlashTextWithCommandRef(
  inputParts: Record<string, unknown>[] | undefined,
  rawSlashText: string,
): Record<string, unknown>[] {
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
  | "force_fresh_created"
  | "cache_hit"
  | "resumed_from_cache"
  | "cache_invalidated"
  | "listed_active"
  | "listed_resumed"
  | "created"
  | "archived"
  | "archive_failed"
  | "recovered_stale_active"
  | "recovered_stale_resumed"
  | "recovered_listed_active"
  | "recovered_listed_resumed"
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
  onEvent?: (event: ThreadResolveEvent) => void;
}

export interface ThreadIdentityLookup {
  identityKey: string;
  userId: string;
  channelContext: string;
  workspacePath: string;
}

export class ThreadResolver {
  readonly threadMap: Map<string, string>;
  private readonly client: ClientProvider;
  private readonly channelName: string;
  private readonly forceFreshThreadIdentities = new Set<string>();
  private readonly onEvent?: (event: ThreadResolveEvent) => void;

  constructor(options: ThreadResolverOptions) {
    this.client = options.client;
    this.channelName = options.channelName;
    this.threadMap = options.threadMap ?? new Map<string, string>();
    this.onEvent = options.onEvent;
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

  markFreshThread(identityKey: string): void {
    this.forceFreshThreadIdentities.add(identityKey);
  }

  clearFreshThread(identityKey: string): void {
    this.forceFreshThreadIdentities.delete(identityKey);
  }

  async resetIdentityThreads(lookup: ThreadIdentityLookup): Promise<string[]> {
    const client = resolveClient(this.client);
    const archivedIds = new Set<string>();
    const cachedThreadId = this.threadMap.get(lookup.identityKey);
    if (cachedThreadId) archivedIds.add(cachedThreadId);

    const threads = await client.threadList({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
    });
    for (const thread of threads) {
      if (thread.status === "active" || thread.status === "paused") archivedIds.add(thread.id);
    }

    for (const threadId of archivedIds) {
      try {
        await client.threadArchive(threadId);
        this.emit({ action: "archived", ...lookup, threadId });
      } catch (error) {
        this.emit({ action: "archive_failed", ...lookup, threadId, error });
      }
    }

    this.threadMap.delete(lookup.identityKey);
    this.forceFreshThreadIdentities.add(lookup.identityKey);
    return [...archivedIds];
  }

  async getOrCreateThread(lookup: ThreadIdentityLookup): Promise<Thread> {
    const client = resolveClient(this.client);

    if (this.forceFreshThreadIdentities.has(lookup.identityKey)) {
      const thread = await client.threadStart({
        channelName: this.channelName,
        userId: lookup.userId,
        channelContext: lookup.channelContext,
        workspacePath: lookup.workspacePath,
      });
      this.threadMap.set(lookup.identityKey, thread.id);
      this.forceFreshThreadIdentities.delete(lookup.identityKey);
      this.emit({ action: "force_fresh_created", ...lookup, threadId: thread.id });
      return thread;
    }

    const threadId = this.threadMap.get(lookup.identityKey);
    if (threadId) {
      try {
        const thread = await client.threadRead(threadId);
        if (thread.status === "active") {
          this.emit({ action: "cache_hit", ...lookup, threadId: thread.id });
          return thread;
        }
        if (thread.status === "paused") {
          const resumed = await client.threadResume(threadId);
          this.threadMap.set(lookup.identityKey, resumed.id);
          this.emit({ action: "resumed_from_cache", ...lookup, threadId: resumed.id });
          return resumed;
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
    const reusable = threads.find((t) => t.status === "active" || t.status === "paused");
    if (reusable) {
      const thread =
        reusable.status === "paused"
          ? await client.threadResume(reusable.id)
          : await client.threadRead(reusable.id);
      this.threadMap.set(lookup.identityKey, thread.id);
      this.emit({
        action: reusable.status === "paused" ? "listed_resumed" : "listed_active",
        ...lookup,
        threadId: thread.id,
      });
      return thread;
    }

    const thread = await client.threadStart({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
    });
    this.threadMap.set(lookup.identityKey, thread.id);
    this.emit({ action: "created", ...lookup, threadId: thread.id });
    return thread;
  }

  async recoverThreadAfterNotActive(
    lookup: ThreadIdentityLookup,
    staleThreadId: string,
  ): Promise<Thread> {
    const client = resolveClient(this.client);

    try {
      const latest = await client.threadRead(staleThreadId);
      if (latest.status === "paused") {
        const resumed = await client.threadResume(staleThreadId);
        this.threadMap.set(lookup.identityKey, resumed.id);
        this.emit({ action: "recovered_stale_resumed", ...lookup, staleThreadId, threadId: resumed.id });
        return resumed;
      }
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
    const reusable = threads.find((t) => t.status === "active" || t.status === "paused");
    if (reusable) {
      const thread =
        reusable.status === "paused"
          ? await client.threadResume(reusable.id)
          : await client.threadRead(reusable.id);
      this.threadMap.set(lookup.identityKey, thread.id);
      this.emit({
        action: reusable.status === "paused" ? "recovered_listed_resumed" : "recovered_listed_active",
        ...lookup,
        staleThreadId,
        threadId: thread.id,
      });
      return thread;
    }

    const fresh = await client.threadStart({
      channelName: this.channelName,
      userId: lookup.userId,
      channelContext: lookup.channelContext,
      workspacePath: lookup.workspacePath,
    });
    this.threadMap.set(lookup.identityKey, fresh.id);
    this.emit({ action: "recovered_created", ...lookup, staleThreadId, threadId: fresh.id });
    return fresh;
  }

  private emit(event: ThreadResolveEvent): void {
    this.onEvent?.(event);
  }
}

export type CommandRouteBeforeQueueResult = "handled" | "enqueue";
export type CommandRouteForTurnResult =
  | { kind: "handled" }
  | { kind: "continue"; opts: ChannelAdapterMessageOptions };

export interface CommandRouterOptions {
  client: ClientProvider;
  threadResolver: ThreadResolver;
  identityKey: (userId: string, channelContext: string) => string;
  getDefaultWorkspacePath: () => string;
  deliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
  enqueueMessage: (opts: ChannelAdapterMessageOptions) => void;
  onThreadContextBound?: (threadId: string, channelContext: string) => void;
  onThreadsArchived?: (identityKey: string, archivedThreadIds: string[]) => void;
}

export class CommandRouter {
  private readonly client: ClientProvider;
  private readonly threadResolver: ThreadResolver;
  private readonly identityKey: (userId: string, channelContext: string) => string;
  private readonly getDefaultWorkspacePath: () => string;
  private readonly deliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
  private readonly enqueueMessage: (opts: ChannelAdapterMessageOptions) => void;
  private readonly onThreadContextBound?: (threadId: string, channelContext: string) => void;
  private readonly onThreadsArchived?: (identityKey: string, archivedThreadIds: string[]) => void;

  constructor(options: CommandRouterOptions) {
    this.client = options.client;
    this.threadResolver = options.threadResolver;
    this.identityKey = options.identityKey;
    this.getDefaultWorkspacePath = options.getDefaultWorkspacePath;
    this.deliver = options.deliver;
    this.enqueueMessage = options.enqueueMessage;
    this.onThreadContextBound = options.onThreadContextBound;
    this.onThreadsArchived = options.onThreadsArchived;
  }

  async routeBeforeQueue(opts: ChannelAdapterMessageOptions): Promise<CommandRouteBeforeQueueResult> {
    if (opts.skipCommand) return "enqueue";
    const trimmedText = opts.text.trim();
    if (!trimmedText.startsWith("/")) return "enqueue";

    const channelContext = opts.channelContext ?? "";
    const identityKey = this.identityKey(opts.userId, channelContext);
    const threadId = this.threadResolver.getCachedThreadId(identityKey);
    if (!threadId) return "enqueue";

    const parts = trimmedText.split(/\s+/);
    const sender = buildChannelSender(opts, channelContext);
    try {
      const commandResult = await resolveClient(this.client).commandExecute({
        threadId,
        command: parts[0] ?? "",
        arguments: parts.length > 1 ? parts.slice(1) : undefined,
        sender,
      });
      const expanded = commandResult.expandedPrompt as string | undefined;
      if (expanded) {
        this.enqueueMessage({
          ...opts,
          inputParts: replaceLeadingSlashTextWithCommandRef(opts.inputParts, trimmedText),
          skipCommand: true,
        });
        return "handled";
      }
      if (Boolean(commandResult.handled)) {
        await this.applyCommandResetResult({
          identityKey,
          userId: opts.userId,
          channelContext,
          workspacePath: opts.workspacePath ?? this.getDefaultWorkspacePath(),
          commandName: parts[0] ?? "",
          commandResult,
        });
        const commandMessage = commandResult.message as string | undefined;
        if (commandMessage) {
          await this.deliver(channelContext, commandMessage, {});
        }
        return "handled";
      }
      return "handled";
    } catch (error) {
      if (error instanceof DotCraftError) {
        await this.deliver(channelContext, error.message || String(error), {});
        return "handled";
      }
      throw error;
    }
  }

  async routeForTurn(args: {
    identityKey: string;
    opts: ChannelAdapterMessageOptions;
    threadId: string;
    sender: Record<string, unknown>;
    workspacePath: string;
  }): Promise<CommandRouteForTurnResult> {
    const trimmedText = args.opts.text.trim();
    if (!trimmedText.startsWith("/") || args.opts.skipCommand) {
      return { kind: "continue", opts: args.opts };
    }

    const commandParts = trimmedText.split(/\s+/);
    const commandName = commandParts[0] ?? "";
    const commandArguments = commandParts.length > 1 ? commandParts.slice(1) : undefined;
    try {
      const commandResult = await resolveClient(this.client).commandExecute({
        threadId: args.threadId,
        command: commandName,
        arguments: commandArguments,
        sender: args.sender,
      });
      const expandedPrompt = commandResult.expandedPrompt as string | undefined;
      if (expandedPrompt) {
        return {
          kind: "continue",
          opts: {
            ...args.opts,
            inputParts: replaceLeadingSlashTextWithCommandRef(args.opts.inputParts, trimmedText),
          },
        };
      }
      if (Boolean(commandResult.handled)) {
        await this.applyCommandResetResult({
          identityKey: args.identityKey,
          userId: args.opts.userId,
          channelContext: args.opts.channelContext ?? "",
          workspacePath: args.workspacePath,
          commandName,
          commandResult,
        });
        const commandMessage = commandResult.message as string | undefined;
        if (commandMessage) {
          await this.deliver(args.opts.channelContext ?? "", commandMessage, {});
        }
        return { kind: "handled" };
      }
      return { kind: "continue", opts: args.opts };
    } catch (error) {
      if (error instanceof DotCraftError) {
        await this.deliver(args.opts.channelContext ?? "", error.message || String(error), {});
        return { kind: "handled" };
      }
      throw error;
    }
  }

  async applyCommandResetResult(args: {
    identityKey: string;
    userId: string;
    channelContext: string;
    workspacePath: string;
    commandName: string;
    commandResult: Record<string, unknown>;
  }): Promise<void> {
    const normalizedCommand = args.commandName.trim().toLowerCase();
    const archivedThreadIds = Array.isArray(args.commandResult.archivedThreadIds)
      ? args.commandResult.archivedThreadIds.map((v) => String(v))
      : [];
    if (archivedThreadIds.length > 0) {
      this.onThreadsArchived?.(args.identityKey, archivedThreadIds);
    }

    const resetThreadWire = args.commandResult.thread as Record<string, unknown> | undefined;
    const sessionReset = Boolean(args.commandResult.sessionReset) || Boolean(resetThreadWire);
    if (sessionReset) {
      if (resetThreadWire) {
        const resetThread = Thread.fromWire(resetThreadWire);
        this.threadResolver.setCachedThread(args.identityKey, resetThread.id);
        this.threadResolver.clearFreshThread(args.identityKey);
        this.onThreadContextBound?.(resetThread.id, args.channelContext);
      } else {
        this.threadResolver.deleteCachedThread(args.identityKey);
        this.threadResolver.markFreshThread(args.identityKey);
      }
      return;
    }

    if (normalizedCommand === "/new") {
      await this.threadResolver.resetIdentityThreads({
        identityKey: args.identityKey,
        userId: args.userId,
        channelContext: args.channelContext,
        workspacePath: args.workspacePath,
      });
      const thread = await this.threadResolver.getOrCreateThread({
        identityKey: args.identityKey,
        userId: args.userId,
        channelContext: args.channelContext,
        workspacePath: args.workspacePath,
      });
      this.onThreadContextBound?.(thread.id, args.channelContext);
    }
  }
}

export interface SegmentBoundaryPolicy {
  shouldFlushOnItemStarted(itemType: string): boolean;
}

export class DefaultSegmentBoundaryPolicy implements SegmentBoundaryPolicy {
  shouldFlushOnItemStarted(itemType: string): boolean {
    return shouldFlushSegmentOnItemStarted(itemType);
  }
}

export type TurnStreamDebugLogger = (
  message: string,
  data: Record<string, unknown> | (() => Record<string, unknown>),
) => void;

export interface TurnStreamReducerOptions {
  segmentBoundaryPolicy?: SegmentBoundaryPolicy;
  debug?: TurnStreamDebugLogger;
}

export interface TurnStreamContext {
  threadId: string;
  turnId: string;
  channelContext: string;
}

export interface TurnStreamReducerHandlers {
  onSegmentCompleted(
    threadId: string,
    turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<boolean | void>;
  onTurnCompleted(
    threadId: string,
    turnId: string,
    replyText: string,
    channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void>;
  onTurnFailed(threadId: string, turnId: string, error: string): Promise<void>;
  onTurnCancelled(threadId: string, turnId: string): Promise<void>;
}

export class TurnStreamReducer {
  private readonly segmentBoundaryPolicy: SegmentBoundaryPolicy;
  private readonly debug?: TurnStreamDebugLogger;

  constructor(options: TurnStreamReducerOptions = {}) {
    this.segmentBoundaryPolicy = options.segmentBoundaryPolicy ?? new DefaultSegmentBoundaryPolicy();
    this.debug = options.debug;
  }

  async consume(
    eventStream: AsyncIterableIterator<JsonRpcMessage>,
    context: TurnStreamContext,
    handlers: TurnStreamReducerHandlers,
  ): Promise<void> {
    const { threadId, turnId, channelContext } = context;
    const itemOrder: string[] = [];
    const perItemDelta = new Map<string, string>();
    const deliveredTextPerItem = new Map<string, string>();
    let activeAgentItemId: string | null = null;
    let lastDeltaAgentItemId: string | null = null;
    let orphanDeltaTail = "";
    let segmentsWereDelivered = false;

    const orderSeen = new Set<string>();
    const pushOrder = (itemId: string): void => {
      if (!itemId || orderSeen.has(itemId)) return;
      orderSeen.add(itemId);
      itemOrder.push(itemId);
    };
    const getCurrentItemText = (itemId: string | null): string => {
      if (!itemId) return "";
      return perItemDelta.get(itemId) ?? "";
    };
    const markSegmentDelivered = (itemId: string | null, segmentText: string): void => {
      if (!itemId || !segmentText) return;
      const delivered = deliveredTextPerItem.get(itemId) ?? "";
      deliveredTextPerItem.set(itemId, delivered + segmentText);
    };
    const getUnsentTail = (itemId: string | null, fallbackText = ""): string => {
      if (!itemId) return fallbackText;
      const current = getCurrentItemText(itemId) || fallbackText;
      const delivered = deliveredTextPerItem.get(itemId) ?? "";
      const frontier = getDeliveredFrontier(current, delivered);
      if (frontier >= current.length) return "";
      return current.slice(frontier);
    };
    const getUnsentFromMerged = (itemId: string | null, mergedText: string): string => {
      if (!itemId) return mergedText;
      const delivered = deliveredTextPerItem.get(itemId) ?? "";
      const frontier = getDeliveredFrontier(mergedText, delivered);
      if (frontier >= mergedText.length) return "";
      return mergedText.slice(frontier);
    };

    const snapshotStreamState = (): Record<string, unknown> => ({
      itemOrder: itemOrder.map((id) => shortWireId(id)),
      perItemChars: Object.fromEntries(
        [...perItemDelta.entries()].map(([id, t]) => [shortWireId(id), t.length]),
      ),
      deliveredChars: Object.fromEntries(
        [...deliveredTextPerItem.entries()].map(([id, t]) => [shortWireId(id), t.length]),
      ),
      activeAgentItemId: activeAgentItemId ? shortWireId(activeAgentItemId) : "",
      lastDeltaAgentItemId: lastDeltaAgentItemId ? shortWireId(lastDeltaAgentItemId) : "",
      orphanDeltaChars: orphanDeltaTail.length,
      segmentsWereDelivered,
    });
    const hasUnsentText = (): boolean =>
      itemOrder.some((id) => getUnsentTail(id, "").trim().length > 0) || orphanDeltaTail.trim().length > 0;
    const deliverSegment = async (
      segmentText: string,
      isFinal: boolean,
      deliveredParts: Array<{ itemId: string | null; text: string }>,
      clearOrphanOnSuccess: boolean,
    ): Promise<boolean> => {
      try {
        const delivered = await handlers.onSegmentCompleted(threadId, turnId, segmentText, isFinal, channelContext);
        if (delivered === false) {
          this.log("segment.delivery_failed", () => ({
            isFinal,
            segmentChars: segmentText.length,
            segmentPreview: previewWireText(segmentText),
            ...snapshotStreamState(),
          }));
          return false;
        }
        segmentsWereDelivered = true;
        for (const part of deliveredParts) {
          markSegmentDelivered(part.itemId, part.text);
        }
        if (clearOrphanOnSuccess) orphanDeltaTail = "";
        return true;
      } catch (error) {
        this.log("segment.delivery_threw", () => ({
          isFinal,
          error: error instanceof Error ? error.message : String(error),
          segmentChars: segmentText.length,
          segmentPreview: previewWireText(segmentText),
          ...snapshotStreamState(),
        }));
        return false;
      }
    };

    for await (const event of eventStream) {
      this.log("event", () => ({
        method: event.method,
        threadId: shortWireId(threadId),
        turnId: shortWireId(turnId),
        channel: shortWireId(channelContext),
      }));
      if (event.method === "item/agentMessage/delta") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const delta = String(params.delta ?? "");
        const explicitItemId = String(params.itemId ?? "");
        const resolvedItemId: string | null = explicitItemId || activeAgentItemId || lastDeltaAgentItemId || null;
        if (resolvedItemId) {
          pushOrder(resolvedItemId);
          const prev = perItemDelta.get(resolvedItemId) ?? "";
          perItemDelta.set(resolvedItemId, prev + delta);
          lastDeltaAgentItemId = resolvedItemId;
          if (!activeAgentItemId) activeAgentItemId = resolvedItemId;
        } else {
          orphanDeltaTail += delta;
        }
        this.log("event.item/agentMessage/delta", () => ({
          explicitItemId: explicitItemId ? shortWireId(explicitItemId) : "(empty)",
          resolvedItemId: resolvedItemId ? shortWireId(resolvedItemId) : "(orphan)",
          deltaChars: delta.length,
          deltaPreview: previewWireText(delta, 120),
          mergedAfterChars: resolvedItemId ? (perItemDelta.get(resolvedItemId) ?? "").length : orphanDeltaTail.length,
          ...snapshotStreamState(),
        }));
      } else if (event.method === "item/started") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const item = (params.item as Record<string, unknown>) ?? {};
        const itemType = String(item.type ?? "");
        const itemId = String(item.id ?? "");
        if (itemType === "agentMessage" && itemId) {
          activeAgentItemId = itemId;
          lastDeltaAgentItemId = itemId;
          pushOrder(itemId);
        }
        if (this.segmentBoundaryPolicy.shouldFlushOnItemStarted(itemType)) {
          const segmentItemId = activeAgentItemId ?? lastDeltaAgentItemId;
          let segmentText = "";
          if (segmentItemId) {
            const merged = perItemDelta.get(segmentItemId) ?? "";
            segmentText = getUnsentFromMerged(segmentItemId, merged);
          } else if (orphanDeltaTail) {
            segmentText = orphanDeltaTail;
          }
          if (segmentText.trim()) {
            await deliverSegment(
              segmentText,
              false,
              [{ itemId: segmentItemId, text: segmentText }],
              segmentItemId == null,
            );
          }
          this.log("event.item/started.flush_segment", () => ({
            itemType,
            itemId: itemId ? shortWireId(itemId) : "",
            flushSegment: true,
            segmentItemId: segmentItemId ? shortWireId(segmentItemId) : "",
            segmentChars: segmentText.length,
            segmentPreview: previewWireText(segmentText),
            ...snapshotStreamState(),
          }));
        } else {
          this.log("event.item/started", () => ({
            itemType,
            itemId: itemId ? shortWireId(itemId) : "",
            flushSegment: false,
            ...snapshotStreamState(),
          }));
        }
      } else if (event.method === "item/completed") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const item = (params.item as Record<string, unknown>) ?? {};
        const itemType = String(item.type ?? "");
        if (itemType !== "agentMessage") {
          this.log("event.item/completed.skipped", () => ({
            itemType,
            itemId: String(item.id ?? "") ? shortWireId(String(item.id ?? "")) : "",
            ...snapshotStreamState(),
          }));
          continue;
        }
        const itemId = String(item.id ?? "");
        const payload = (item.payload as Record<string, unknown>) ?? {};
        const snap = typeof payload.text === "string" ? payload.text : "";
        pushOrder(itemId);
        const fromD = perItemDelta.get(itemId) ?? "";
        const canon = mergeReplyTextFromDeltaAndSnapshot(fromD, snap);
        perItemDelta.set(itemId, canon);
        if (itemId === activeAgentItemId) {
          activeAgentItemId = null;
        }
        lastDeltaAgentItemId = itemId;
        this.log("event.item/completed.agentMessage", () => ({
          itemId: shortWireId(itemId),
          deltaCharsBeforeMerge: fromD.length,
          snapshotChars: snap.length,
          mergedCharsAfter: canon.length,
          deltaPreview: previewWireText(fromD),
          snapshotPreview: previewWireText(snap),
          mergedPreview: previewWireText(canon),
          ...snapshotStreamState(),
        }));
      } else if (event.method === "turn/completed") {
        const params = (event.params as Record<string, unknown>) ?? {};
        const snapshots = extractAgentReplyTextsFromTurnCompletedParams(params);
        const lastSnap = snapshots.length > 0 ? snapshots[snapshots.length - 1] ?? "" : "";
        const unsentParts: Array<{ itemId: string | null; text: string }> = [];
        const orphanTailForReply = orphanDeltaTail;
        for (const itemId of itemOrder) {
          const tail = getUnsentTail(itemId, "");
          if (tail.length > 0) unsentParts.push({ itemId, text: tail });
        }
        if (orphanDeltaTail.length > 0) {
          unsentParts.push({ itemId: null, text: orphanDeltaTail });
        }
        let segmentText = unsentParts.map((part) => part.text).join("");
        if (!segmentText.trim() && lastSnap && !segmentsWereDelivered && itemOrder.length === 0) {
          segmentText = lastSnap;
        }
        if (segmentText.trim()) {
          const deliveredParts = unsentParts.length > 0
            ? unsentParts
            : [{ itemId: itemOrder.length > 0 ? itemOrder[itemOrder.length - 1] : null, text: segmentText }];
          await deliverSegment(segmentText, true, deliveredParts, unsentParts.some((part) => part.itemId == null));
        }
        const snapshotText = extractAgentReplyTextFromTurnCompletedParams(params);
        const deltaText = itemOrder.map((id) => perItemDelta.get(id) ?? "").join("") + orphanTailForReply;
        const fullReply = mergeReplyTextFromDeltaAndSnapshot(deltaText, snapshotText);
        this.log("event.turn/completed", () => ({
          unsentParts: unsentParts.map((p) => ({
            itemId: p.itemId ? shortWireId(p.itemId) : "(orphan)",
            chars: p.text.length,
            preview: previewWireText(p.text),
          })),
          finalSegmentChars: segmentText.length,
          finalSegmentPreview: previewWireText(segmentText),
          snapshotConcatChars: snapshotText.length,
          deltaConcatChars: deltaText.length,
          deltaConcatPreview: previewWireText(deltaText),
          snapshotConcatPreview: previewWireText(snapshotText),
          fullReplyChars: fullReply.length,
          fullReplyPreview: previewWireText(fullReply),
          segmentsWereDelivered,
          ...snapshotStreamState(),
        }));
        await handlers.onTurnCompleted(threadId, turnId, fullReply, channelContext, segmentsWereDelivered && !hasUnsentText());
        break;
      } else if (event.method === "turn/failed") {
        const err = String(
          ((event.params as Record<string, unknown>)?.turn as Record<string, unknown>)?.error ??
            "Unknown error",
        );
        this.log("event.turn/failed", () => ({
          error: previewWireText(err, 500),
          ...snapshotStreamState(),
        }));
        await handlers.onTurnFailed(threadId, turnId, err);
        break;
      } else if (event.method === "turn/cancelled") {
        this.log("event.turn/cancelled", () => snapshotStreamState());
        await handlers.onTurnCancelled(threadId, turnId);
        break;
      }
    }
  }

  private log(
    message: string,
    data: Record<string, unknown> | (() => Record<string, unknown>),
  ): void {
    this.debug?.(message, data);
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
    resolveClient(this.client).setApprovalHandler(async (_id, params) => {
      try {
        return await this.onApprovalRequest(params);
      } catch (error) {
        this.onError("onApprovalRequest raised:", error);
        return "cancel";
      }
    });
  }
}

export interface DeliveryDispatcherOptions {
  client: ClientProvider;
  onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
  onSend: (
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ) => Promise<Record<string, unknown>>;
  onError?: (message: string, error: unknown) => void;
}

export class DeliveryDispatcher {
  private readonly client: ClientProvider;
  private readonly onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
  private readonly onSend: (
    target: string,
    message: Record<string, unknown>,
    metadata: Record<string, unknown>,
  ) => Promise<Record<string, unknown>>;
  private readonly onError: (message: string, error: unknown) => void;

  constructor(options: DeliveryDispatcherOptions) {
    this.client = options.client;
    this.onDeliver = options.onDeliver;
    this.onSend = options.onSend;
    this.onError = options.onError ?? ((message, error) => console.error(message, error));
  }

  register(): void {
    const client = resolveClient(this.client);
    client.registerHandler("ext/channel/deliver", async (params) => {
      await this.handleDeliverNotification(params);
    });
    client.registerServerRequestHandler("ext/channel/deliver", async (_id, params) => {
      const target = String(params.target ?? "");
      const content = String(params.content ?? "");
      const metadata = (params.metadata as Record<string, unknown>) ?? {};
      try {
        const ok = await this.onDeliver(target, content, metadata);
        return { delivered: ok };
      } catch (error) {
        this.onError("onDeliver raised:", error);
        return { delivered: false, error: String(error) };
      }
    });
    client.registerServerRequestHandler("ext/channel/send", async (_id, params) => {
      const target = String(params.target ?? "");
      const message = (params.message as Record<string, unknown>) ?? {};
      const metadata = (params.metadata as Record<string, unknown>) ?? {};
      try {
        return await this.onSend(target, message, metadata);
      } catch (error) {
        this.onError("onSend raised:", error);
        return {
          delivered: false,
          errorCode: "AdapterDeliveryFailed",
          errorMessage: String(error),
        };
      }
    });
  }

  async handleDeliverNotification(params: Record<string, unknown>): Promise<void> {
    const target = String(params.target ?? "");
    const content = String(params.content ?? "");
    const metadata = (params.metadata as Record<string, unknown>) ?? {};
    try {
      await this.onDeliver(target, content, metadata);
    } catch (error) {
      this.onError("onDeliver (notification) raised:", error);
    }
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
