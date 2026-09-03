import type { TurnItemActivity } from "@dotcraft/channel";
import { chunkMarkdown, normalizeMarkdownForFeishu } from "./formatting.js";
import {
  buildStatusElement,
  buildStatusPatch,
  buildStreamingTranscriptCard,
  buildReplySummary,
  STREAMING_STATUS_ELEMENT_ID,
  STREAMING_TRANSCRIPT_ELEMENT_ID,
  type TurnStatusPhase,
} from "./card-builder.js";
import type { FeishuClient } from "./feishu-client.js";
import type { FeishuSendResult } from "./feishu-types.js";

const DEFAULT_THROTTLE_MS = 150;
const DEFAULT_MAX_ELEMENT_CHARS = 30000;
// Same stall threshold Desktop uses before it stops treating a reply item as live.
const DEFAULT_TEXT_STALL_MS = 2000;
const DEFAULT_STATUS_SETTLE_MS = 300;

type CardKitClient = Pick<
  FeishuClient,
  | "createCardKitInstance"
  | "sendCardKitReference"
  | "updateCardKitElement"
  | "patchCardKitElement"
  | "appendCardKitElement"
  | "deleteCardKitElement"
  | "finalizeCardKitInstance"
  | "replaceCardKitInstance"
  | "recallMessage"
>;

export type TranscriptStreamFailureStage =
  | "start"
  | "update"
  | "status"
  | "rollover"
  | "finalize"
  | "recover";

export interface FeishuTranscriptStreamerOptions {
  throttleMs?: number;
  maxElementChars?: number;
  onFailure?: (stage: TranscriptStreamFailureStage, error: unknown) => void;
  deliverCard?: (cardId: string) => Promise<FeishuSendResult>;
  statusIconImgKey?: string;
  textStallMs?: number;
  statusSettleMs?: number;
}

export class FeishuTranscriptStreamer {
  private readonly throttleMs: number;
  private readonly maxElementChars: number;
  private readonly textStallMs: number;
  private readonly statusSettleMs: number;
  private readonly onFailure?: (stage: TranscriptStreamFailureStage, error: unknown) => void;
  private readonly deliverCard?: (cardId: string) => Promise<FeishuSendResult>;
  private readonly statusIconImgKey?: string;
  private status: "idle" | "native" | "failed" | "completed" = "idle";
  private cardId = "";
  private messageId = "";
  private sequence = 0;
  private activeChunkIndex = 0;
  private latestText = "";
  private contentPushed = false;
  private visibleStatusPhase: TurnStatusPhase | "none" = "none";
  private readonly openTools = new Set<string>();
  private lastDeltaAt = 0;
  private lastPushAt = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private stallTimer: ReturnType<typeof setTimeout> | undefined;
  private settleTimer: ReturnType<typeof setTimeout> | undefined;
  private queue: Promise<void> = Promise.resolve();

  constructor(
    private readonly client: CardKitClient,
    private readonly target: string,
    private readonly cardTitle: string,
    options: FeishuTranscriptStreamerOptions = {},
  ) {
    this.throttleMs = Math.max(100, options.throttleMs ?? DEFAULT_THROTTLE_MS);
    this.maxElementChars = Math.max(1000, options.maxElementChars ?? DEFAULT_MAX_ELEMENT_CHARS);
    this.textStallMs = Math.max(100, options.textStallMs ?? DEFAULT_TEXT_STALL_MS);
    this.statusSettleMs = Math.max(0, options.statusSettleMs ?? DEFAULT_STATUS_SETTLE_MS);
    this.onFailure = options.onFailure;
    this.deliverCard = options.deliverCard;
    this.statusIconImgKey = options.statusIconImgKey;
  }

  get hasVisibleCard(): boolean {
    return this.cardId.length > 0;
  }

  /** Posts the card with a "thinking" row before any reply text exists. */
  async begin(): Promise<boolean> {
    if (this.status !== "idle") return this.status === "native";
    try {
      await this.serialized(() => this.startCard(""));
      this.status = "native";
      return true;
    } catch (error) {
      this.status = "failed";
      this.onFailure?.("start", error);
      return false;
    }
  }

  /** Feeds item lifecycle so the status row can follow the agent between reply segments. */
  noteActivity(activity: TurnItemActivity): void {
    if (this.status !== "native") return;
    if (activity.kind === "tool") {
      if (activity.phase === "started") this.openTools.add(activity.itemId);
      else this.openTools.delete(activity.itemId);
    }
    this.requestStatusSync();
  }

  async update(text: string): Promise<boolean> {
    if (!text.trim() || this.status === "failed" || this.status === "completed") return false;
    this.latestText = text;
    this.lastDeltaAt = Date.now();
    this.armStallTimer();
    if (this.status === "idle") {
      try {
        await this.serialized(() => this.startCard(""));
        this.status = "native";
      } catch (error) {
        this.status = "failed";
        this.onFailure?.("start", error);
        return false;
      }
      return await this.flushNow();
    }
    this.scheduleUpdate();
    return true;
  }

  async complete(text: string): Promise<boolean> {
    this.latestText = text;
    this.clearStatusTimers();
    if (this.status === "idle" || this.status === "completed") return false;

    const flushed = this.status === "native" && await this.flushNow();
    if (flushed) {
      try {
        await this.serialized(async () => {
          await this.removeStatus();
          await this.client.finalizeCardKitInstance(
            this.cardId,
            ++this.sequence,
            buildReplySummary(this.cardTitle),
          );
        });
        this.status = "completed";
        return true;
      } catch (error) {
        this.onFailure?.("finalize", error);
      }
    }

    return await this.recoverFinalCard();
  }

  async abort(): Promise<void> {
    this.clearTimer();
    this.clearStatusTimers();
    if (!this.cardId || this.status === "completed") return;
    if (this.status === "native" && this.latestText.trim()) await this.flushNow();
    if (!this.cardId) return;
    if (!this.contentPushed && !this.latestText.trim() && this.messageId) {
      await this.recallEmptyCard();
      return;
    }
    try {
      await this.serialized(async () => {
        await this.removeStatus();
        await this.client.finalizeCardKitInstance(
          this.cardId,
          ++this.sequence,
          buildReplySummary(this.cardTitle),
        );
      });
    } catch (error) {
      this.onFailure?.("finalize", error);
    } finally {
      this.status = "completed";
    }
  }

  private async recallEmptyCard(): Promise<void> {
    try {
      await this.serialized(() => this.client.recallMessage(this.messageId));
    } catch (error) {
      this.onFailure?.("finalize", error);
    } finally {
      this.status = "completed";
    }
  }

  private scheduleUpdate(): void {
    if (this.timer || this.status !== "native") return;
    const delay = Math.max(0, this.lastPushAt + this.throttleMs - Date.now());
    this.timer = setTimeout(() => {
      this.timer = undefined;
      void this.flushNow();
    }, delay);
  }

  private async flushNow(): Promise<boolean> {
    this.clearTimer();
    if (this.status !== "native") return false;
    try {
      await this.serialized(async () => this.pushSnapshot());
      this.lastPushAt = Date.now();
      return true;
    } catch (error) {
      this.status = "failed";
      this.onFailure?.("update", error);
      return false;
    }
  }

  private async pushSnapshot(): Promise<void> {
    const chunks = chunkMarkdown(this.latestText, this.maxElementChars);

    while (this.activeChunkIndex < chunks.length - 1) {
      try {
        const head = chunks[this.activeChunkIndex];
        await this.client.updateCardKitElement(
          this.cardId,
          STREAMING_TRANSCRIPT_ELEMENT_ID,
          head,
          ++this.sequence,
        );
        this.contentPushed = true;
        await this.removeStatus();
        await this.client.finalizeCardKitInstance(
          this.cardId,
          ++this.sequence,
          buildReplySummary(this.cardTitle),
        );
        this.activeChunkIndex += 1;
        await this.startCard(chunks[this.activeChunkIndex]);
      } catch (error) {
        this.onFailure?.("rollover", error);
        throw error;
      }
    }

    if (chunks.length === 0) return;
    await this.client.updateCardKitElement(
      this.cardId,
      STREAMING_TRANSCRIPT_ELEMENT_ID,
      chunks[this.activeChunkIndex],
      ++this.sequence,
    );
    this.contentPushed = true;
    await this.removeStatus();
  }

  // Mirrors Desktop: streaming text hides the row, an open tool shows "working", anything else
  // while the turn runs shows "thinking".
  private desiredStatusPhase(): TurnStatusPhase | "none" {
    if (this.lastDeltaAt > 0 && Date.now() - this.lastDeltaAt < this.textStallMs) return "none";
    return this.openTools.size > 0 ? "working" : "thinking";
  }

  private requestStatusSync(): void {
    if (this.settleTimer || this.status !== "native") return;
    this.settleTimer = setTimeout(() => {
      this.settleTimer = undefined;
      void this.syncStatus();
    }, this.statusSettleMs);
  }

  private armStallTimer(): void {
    if (this.stallTimer) clearTimeout(this.stallTimer);
    this.stallTimer = setTimeout(() => {
      this.stallTimer = undefined;
      void this.syncStatus();
    }, this.textStallMs);
  }

  private async syncStatus(): Promise<void> {
    if (this.status !== "native") return;
    await this.serialized(async () => {
      if (this.status !== "native") return;
      const phase = this.desiredStatusPhase();
      if (phase === this.visibleStatusPhase) return;
      if (phase === "none") {
        await this.removeStatus();
        return;
      }
      try {
        if (this.visibleStatusPhase === "none") {
          await this.client.appendCardKitElement(
            this.cardId,
            buildStatusElement(phase, this.statusIconImgKey),
            ++this.sequence,
          );
        } else {
          await this.client.patchCardKitElement(
            this.cardId,
            STREAMING_STATUS_ELEMENT_ID,
            { ...buildStatusPatch(phase) },
            ++this.sequence,
          );
        }
        this.visibleStatusPhase = phase;
      } catch (error) {
        this.onFailure?.("status", error);
      }
    });
  }

  private async removeStatus(): Promise<void> {
    if (this.visibleStatusPhase === "none") return;
    this.visibleStatusPhase = "none";
    try {
      await this.client.deleteCardKitElement(this.cardId, STREAMING_STATUS_ELEMENT_ID, ++this.sequence);
    } catch (error) {
      this.onFailure?.("status", error);
    }
  }

  private async recoverFinalCard(): Promise<boolean> {
    this.clearTimer();
    if (!this.cardId) return false;
    const chunks = chunkMarkdown(this.latestText, this.maxElementChars);
    if (chunks.length !== this.activeChunkIndex + 1) return false;
    try {
      await this.serialized(async () => {
        await this.client.replaceCardKitInstance(
          this.cardId,
          buildStreamingTranscriptCard(chunks[this.activeChunkIndex], true, this.cardTitle),
          ++this.sequence,
        );
      });
      this.visibleStatusPhase = "none";
      this.status = "completed";
      return true;
    } catch (error) {
      this.status = "failed";
      this.onFailure?.("recover", error);
      return false;
    }
  }

  private async startCard(initialText: string): Promise<void> {
    const phase = this.desiredStatusPhase();
    const status = phase === "none" ? undefined : phase;
    const card = buildStreamingTranscriptCard(normalizeMarkdownForFeishu(initialText), false, this.cardTitle, {
      status,
      statusIconImgKey: this.statusIconImgKey,
    });
    const cardId = await this.client.createCardKitInstance(card);
    const sent = this.deliverCard
      ? await this.deliverCard(cardId)
      : await this.client.sendCardKitReference(this.target, cardId);
    this.cardId = cardId;
    this.messageId = sent.messageId;
    this.sequence = 0;
    this.visibleStatusPhase = status ?? "none";
  }

  private serialized<T>(operation: () => Promise<T>): Promise<T> {
    const run = this.queue.then(operation, operation);
    this.queue = run.then(() => undefined, () => undefined);
    return run;
  }

  private clearTimer(): void {
    if (!this.timer) return;
    clearTimeout(this.timer);
    this.timer = undefined;
  }

  private clearStatusTimers(): void {
    if (this.stallTimer) clearTimeout(this.stallTimer);
    if (this.settleTimer) clearTimeout(this.settleTimer);
    this.stallTimer = undefined;
    this.settleTimer = undefined;
  }
}
