import type { ChannelTurn, ChannelTurnOutcome } from "@dotcraft/channel";

import { buildTurnStatusCard, type TurnStatusPhase } from "./card-builder.js";
import type { FeishuSendResult } from "./feishu-types.js";
import { errorMessage, logWarn, shortId } from "./logging.js";

export const STATUS_CARD_DELAY_MS = 20_000;

export interface StatusCardDeps {
  sendCard(target: string, card: Record<string, unknown>): Promise<FeishuSendResult>;
  updateCard(messageId: string, card: Record<string, unknown>): Promise<void>;
  loadingIconImgKey(): Promise<string | undefined>;
  now?: () => number;
  delayMs?: number;
}

interface StatusCardState {
  turn: ChannelTurn;
  startedAt: number;
  phase: TurnStatusPhase;
  timer: ReturnType<typeof setTimeout> | undefined;
  messageId: string;
  queue: Promise<void>;
}

const SETTLED_PHASES: Record<Exclude<ChannelTurnOutcome["status"], "disconnected">, TurnStatusPhase> = {
  completed: "done",
  failed: "failed",
  cancelled: "stopped",
};

export class FeishuStatusCards {
  private readonly cards = new Map<string, StatusCardState>();
  private readonly now: () => number;
  private readonly delayMs: number;

  constructor(private readonly deps: StatusCardDeps) {
    this.now = deps.now ?? Date.now;
    this.delayMs = deps.delayMs ?? STATUS_CARD_DELAY_MS;
  }

  start(turn: ChannelTurn): void {
    const state: StatusCardState = {
      turn,
      startedAt: this.now(),
      phase: "working",
      timer: undefined,
      messageId: "",
      queue: Promise.resolve(),
    };
    state.timer = setTimeout(() => {
      state.timer = undefined;
      this.enqueue(state, () => this.post(state));
    }, this.delayMs);
    this.cards.set(this.key(turn), state);
  }

  retrying(turn: ChannelTurn): void {
    this.transition(turn, "retrying");
  }

  working(turn: ChannelTurn): void {
    this.transition(turn, "working");
  }

  async end(turn: ChannelTurn, outcome: ChannelTurnOutcome): Promise<void> {
    const key = this.key(turn);
    const state = this.cards.get(key);
    if (!state) return;
    this.cards.delete(key);
    const posted = !state.timer;
    if (state.timer) {
      clearTimeout(state.timer);
      state.timer = undefined;
    }
    if (outcome.status !== "disconnected") {
      state.phase = SETTLED_PHASES[outcome.status];
      if (posted) this.enqueue(state, () => this.patch(state));
      else if (outcome.status === "failed") this.enqueue(state, () => this.post(state));
    }
    await state.queue;
  }

  async stopAll(): Promise<void> {
    const states = [...this.cards.values()];
    this.cards.clear();
    for (const state of states) {
      if (state.timer) clearTimeout(state.timer);
    }
    await Promise.allSettled(states.map((state) => state.queue));
  }

  private transition(turn: ChannelTurn, phase: "working" | "retrying"): void {
    const state = this.cards.get(this.key(turn));
    if (!state || state.phase === phase) return;
    state.phase = phase;
    this.enqueue(state, () => this.patch(state));
  }

  private async post(state: StatusCardState): Promise<void> {
    const card = buildTurnStatusCard(state.phase, this.now() - state.startedAt, await this.deps.loadingIconImgKey());
    const sent = await this.deps.sendCard(state.turn.channelContext, card);
    state.messageId = sent.messageId;
  }

  private async patch(state: StatusCardState): Promise<void> {
    if (!state.messageId) return;
    const card = buildTurnStatusCard(state.phase, this.now() - state.startedAt, await this.deps.loadingIconImgKey());
    await this.deps.updateCard(state.messageId, card);
  }

  private enqueue(state: StatusCardState, run: () => Promise<void>): void {
    state.queue = state.queue.then(run).catch((error) => {
      logWarn("turn.status_card_failed", {
        threadId: shortId(state.turn.threadId),
        turnId: shortId(state.turn.turnId),
        phase: state.phase,
        message: errorMessage(error),
      });
    });
  }

  private key(turn: ChannelTurn): string {
    return `${turn.threadId}\u0000${turn.turnId}`;
  }
}
