import { FeishuApiError, type FeishuSendResult } from "./feishu-types.js";
import type { FeishuClient } from "./feishu-client.js";
import { logWarn, shortId } from "./logging.js";
import {
  FeishuTranscriptStreamer,
  type TranscriptStreamFailureStage,
} from "./transcript-streamer.js";

export type TurnTranscriptMode = "pending" | "native" | "nativeFinalized" | "fallback";

export interface TurnTranscriptState {
  threadId: string;
  channelTarget: string;
  messageId: string;
  accumulatedText: string;
  hasProgress: boolean;
  mode: TurnTranscriptMode;
  streamer?: FeishuTranscriptStreamer;
}

export interface TurnCardControllerDeps {
  streamingEnabled: () => boolean;
  client: () => FeishuClient;
  cardTitle: () => string;
  deliverCard: (target: string, cardId: string) => Promise<FeishuSendResult>;
  statusIconImgKey: () => Promise<string | undefined>;
  statusTimings?: () => { textStallMs?: number; statusSettleMs?: number } | undefined;
}

export class TurnCardController {
  private readonly states = new Map<string, TurnTranscriptState>();
  private readonly activeTurnByThread = new Map<string, string>();
  private readonly activeTurnByChannelTarget = new Map<string, string>();

  constructor(private readonly deps: TurnCardControllerDeps) {}

  get(threadId: string, turnId: string): TurnTranscriptState | undefined {
    return this.states.get(this.key(threadId, turnId));
  }

  getOrInit(threadId: string, turnId: string, channelTarget: string): TurnTranscriptState {
    const key = this.key(threadId, turnId);
    const existing = this.states.get(key);
    if (existing) return existing;
    const created: TurnTranscriptState = {
      threadId,
      channelTarget,
      messageId: "",
      accumulatedText: "",
      hasProgress: false,
      mode: this.deps.streamingEnabled() ? "pending" : "fallback",
    };
    this.states.set(key, created);
    return created;
  }

  markActive(threadId: string, turnId: string, channelTarget: string): void {
    this.activeTurnByThread.set(threadId, turnId);
    this.activeTurnByChannelTarget.set(channelTarget, turnId);
  }

  async beginTurn(threadId: string, turnId: string, channelTarget: string): Promise<void> {
    if (!this.deps.streamingEnabled()) return;
    const state = this.getOrInit(threadId, turnId, channelTarget);
    if (state.mode !== "pending") return;
    this.markActive(threadId, turnId, channelTarget);
    try {
      const streamer = await this.ensureStreamer(state, threadId, turnId);
      const started = await streamer.begin();
      state.mode = started || streamer.hasVisibleCard ? "native" : "fallback";
    } catch (error) {
      this.logStreamingFallback("start", error, threadId, turnId);
      state.mode = "fallback";
    }
  }

  // Closes whatever the turn handlers left open once the event stream ends.
  async endTurn(threadId: string, turnId: string): Promise<void> {
    const state = this.get(threadId, turnId);
    if (!state) return;
    await state.streamer?.abort();
    this.clear(threadId, turnId);
  }

  async ensureStreamer(
    state: TurnTranscriptState,
    threadId: string,
    turnId: string,
  ): Promise<FeishuTranscriptStreamer> {
    if (state.streamer) return state.streamer;
    const target = state.channelTarget;
    state.streamer = new FeishuTranscriptStreamer(this.deps.client(), target, this.deps.cardTitle(), {
      onFailure: (stage, error) => this.logStreamingFallback(stage, error, threadId, turnId),
      deliverCard: (cardId) => this.deps.deliverCard(target, cardId),
      statusIconImgKey: await this.deps.statusIconImgKey(),
      ...(this.deps.statusTimings?.() ?? {}),
    });
    return state.streamer;
  }

  clear(threadId: string, turnId: string): void {
    const key = this.key(threadId, turnId);
    const state = this.states.get(key);
    if (!state) return;
    if (this.activeTurnByThread.get(state.threadId) === turnId) {
      this.activeTurnByThread.delete(state.threadId);
    }
    if (this.activeTurnByChannelTarget.get(state.channelTarget) === turnId) {
      this.activeTurnByChannelTarget.delete(state.channelTarget);
    }
    this.states.delete(key);
  }

  clearThread(threadId: string): void {
    const activeTurnId = this.activeTurnByThread.get(threadId);
    if (!activeTurnId) return;
    void this.get(threadId, activeTurnId)?.streamer?.abort();
    this.clear(threadId, activeTurnId);
  }

  async stopAll(): Promise<void> {
    await Promise.allSettled([...this.states.values()].map((state) => state.streamer?.abort()));
    this.states.clear();
    this.activeTurnByThread.clear();
    this.activeTurnByChannelTarget.clear();
  }

  logStreamingFallback(
    stage: TranscriptStreamFailureStage,
    error: unknown,
    threadId: string,
    turnId: string,
  ): void {
    const apiError = error instanceof FeishuApiError ? error : undefined;
    logWarn("turn.streaming_fallback", {
      failureCode: "feishuCardKitStreamingFailed",
      stage,
      errorKind: apiError?.kind ?? "unknown",
      ...(apiError?.code !== undefined ? { code: apiError.code } : {}),
      ...(apiError?.msg ? { msg: apiError.msg } : {}),
      threadId: shortId(threadId),
      turnId: shortId(turnId),
    });
  }

  private key(threadId: string, turnId: string): string {
    return `${threadId}\u0000${turnId}`;
  }
}
