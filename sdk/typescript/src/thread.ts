import type { Unsubscribe } from "./client.js";
import { threadEventIdentity } from "./threadEvents.js";
import { JsonRpcMessage } from "./models.js";
import { normalizeRunInput, toContractSender, stripRuntimeDynamicToolHandlers } from "./threadInput.js";
import { TurnCancelledError, TurnFailedError } from "./errors.js";
import { extractAgentReplyTextFromTurnCompletedParams, mergeReplyTextFromDeltaAndSnapshot } from "./turnReply.js";
import type { SessionIdentity, SessionThread, SessionTurn, ThreadItemsListResult, ThreadTurnsListResult, ThreadResumeResult } from "./generated/appserver/index.js";
import type { DotCraft, DotCraftRunEvent, DotCraftRunResult, DynamicToolBinding, DynamicToolSpec, DynamicToolHandler, EnqueueOptions, QueuedInputResult, RunInput, RunOptions, ThreadHistoryPageOptions, ThreadItemPageOptions } from "./dotcraft.js";

function methodToRunEventType(method: string | null | undefined): string {
  switch (method) {
    case "thread/started": return "thread_started";
    case "thread/resumed": return "thread_resumed";
    case "thread/archived": return "thread_archived";
    case "thread/unarchived": return "thread_unarchived";
    case "thread/status/changed": return "thread_status_changed";
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

function eventTurn(params: Record<string, unknown>): SessionTurn | null {
  const raw = params.turn;
  return raw && typeof raw === "object" ? raw as SessionTurn : null;
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

export class DotCraftThread {
  readonly id: string;
  private cached: SessionThread;
  private readonly dynamicToolUnsubscribes: Unsubscribe[] = [];
  private dynamicTools: DynamicToolSpec[] | undefined;

  constructor(
    private readonly sdk: DotCraft,
    snapshot: SessionThread,
    readonly identity: SessionIdentity,
    private restored?: ThreadResumeResult,
  ) {
    this.cached = snapshot;
    this.id = snapshot.id;
  }

  snapshot(): SessionThread {
    return this.cached;
  }

  restoration(): ThreadResumeResult | undefined {
    return this.restored;
  }

  async refresh(): Promise<SessionThread> {
    this.cached = await this.sdk.wire.threadRead(this.id);
    return this.cached;
  }

  async listTurns(options: ThreadHistoryPageOptions = {}): Promise<ThreadTurnsListResult> {
    return await this.sdk.wire.threadTurnsList({ threadId: this.id, ...options });
  }

  async listItems(options: ThreadItemPageOptions = {}): Promise<ThreadItemsListResult> {
    return await this.sdk.wire.threadItemsList({ threadId: this.id, ...options });
  }

  async unsubscribe(): Promise<void> {
    await this.sdk.wire.threadUnsubscribe(this.id);
  }

  async run(input: RunInput, options: RunOptions = {}): Promise<DotCraftRunResult> {
    for await (const event of this.runStreamed(input, options)) {
      if (event.type === "completed" && event.result) return event.result;
      if (event.type === "failed") {
        throw new TurnFailedError(event.error ?? "SessionTurn failed.", event.turn);
      }
      if (event.type === "cancelled") {
        throw new TurnCancelledError("SessionTurn was cancelled.", event.turn);
      }
    }
    throw new TurnFailedError("Run finished without a terminal event.");
  }

  async *runStreamed(input: RunInput, options: RunOptions = {}): AsyncIterable<DotCraftRunEvent> {
    if (options.abortSignal?.aborted) throw abortError();

    const normalized = normalizeRunInput(input, options.sender);
    const settings = {
      providerId: options.providerId,
      model: options.model,
      reasoning: options.reasoning ? { ...options.reasoning } : options.reasoning,
      speed: options.speed,
    };
    const reducer = new RunReducer();
    const rawEvents: JsonRpcMessage[] = [];
    const eventStream = this.sdk.wire.streamEvents(this.id);
    let turn: SessionTurn;
    let abortListener: (() => void) | null = null;

    try {
      const restoration = await this.sdk.wire.ensureThreadAttached(this.id, {
        dynamicTools: this.dynamicTools,
      });
      if (restoration) {
        this.restored = restoration;
        this.cached = restoration.thread;
      }
      if (options.abortSignal?.aborted) throw abortError();
      turn = await this.sdk.wire.turnStart(this.id, normalized.input, toContractSender(normalized.sender), settings);

      if (options.abortSignal) {
        abortListener = () => {
          void this.interrupt(turn.id).catch(() => {});
        };
        options.abortSignal.addEventListener("abort", abortListener, { once: true });
        if (options.abortSignal.aborted) abortListener();
      }

      for await (const raw of eventStream) {
        const params = paramsRecord(raw.params);
        const identity = threadEventIdentity(raw.method, params);
        if (identity.threadId !== this.id || identity.turnId !== turn.id) continue;
        rawEvents.push(raw);
        reducer.apply(raw);
        const resolvedTurn = eventTurn(params);
        const type = methodToRunEventType(raw.method);
        const base: DotCraftRunEvent = {
          type,
          threadId: this.id,
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
            items: resolvedTurn.items ?? [],
            usage: resolvedTurn.tokenUsage,
            rawEvents: options.collectRawEvents ? [...rawEvents] : undefined,
          };
          yield base;
          return;
        }
        if (type === "failed") {
          base.error = typeof params.error === "string" ? params.error : resolvedTurn?.error ?? "SessionTurn failed.";
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
    const raw = await this.sdk.wire.turnEnqueue(this.id, normalized.input, toContractSender(normalized.sender));
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
    this.dynamicTools = stripRuntimeDynamicToolHandlers(tools);
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
