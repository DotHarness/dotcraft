import { chunkMarkdown, normalizeMarkdownForFeishu } from "./formatting.js";
import {
  buildStreamingTranscriptCard,
  buildReplySummary,
  STREAMING_TRANSCRIPT_ELEMENT_ID,
} from "./card-builder.js";
import type { FeishuClient } from "./feishu-client.js";

const DEFAULT_THROTTLE_MS = 150;
const DEFAULT_MAX_ELEMENT_CHARS = 30000;

type CardKitClient = Pick<
  FeishuClient,
  | "createCardKitInstance"
  | "sendCardKitReference"
  | "updateCardKitElement"
  | "finalizeCardKitInstance"
  | "replaceCardKitInstance"
>;

export type TranscriptStreamFailureStage =
  | "start"
  | "update"
  | "rollover"
  | "finalize"
  | "recover";

export interface FeishuTranscriptStreamerOptions {
  throttleMs?: number;
  maxElementChars?: number;
  onFailure?: (stage: TranscriptStreamFailureStage, error: unknown) => void;
}

export class FeishuTranscriptStreamer {
  private readonly throttleMs: number;
  private readonly maxElementChars: number;
  private readonly onFailure?: (stage: TranscriptStreamFailureStage, error: unknown) => void;
  private status: "idle" | "native" | "failed" | "completed" = "idle";
  private cardId = "";
  private sequence = 0;
  private activeChunkIndex = 0;
  private latestText = "";
  private lastPushAt = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private queue: Promise<void> = Promise.resolve();

  constructor(
    private readonly client: CardKitClient,
    private readonly target: string,
    private readonly cardTitle: string,
    options: FeishuTranscriptStreamerOptions = {},
  ) {
    this.throttleMs = Math.max(100, options.throttleMs ?? DEFAULT_THROTTLE_MS);
    this.maxElementChars = Math.max(1000, options.maxElementChars ?? DEFAULT_MAX_ELEMENT_CHARS);
    this.onFailure = options.onFailure;
  }

  get hasVisibleCard(): boolean {
    return this.cardId.length > 0;
  }

  async update(text: string): Promise<boolean> {
    if (!text.trim() || this.status === "failed" || this.status === "completed") return false;
    this.latestText = text;
    if (this.status === "idle") {
      try {
        await this.startCard("…");
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
    if (this.status === "idle" || this.status === "completed") return false;

    const flushed = this.status === "native" && await this.flushNow();
    if (flushed) {
      try {
        await this.serialized(async () => {
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
    if (!this.cardId || this.status === "completed") return;
    if (this.status === "native" && this.latestText.trim()) await this.flushNow();
    if (!this.cardId) return;
    try {
      await this.serialized(async () => {
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
    if (chunks.length === 0) return;

    while (this.activeChunkIndex < chunks.length - 1) {
      try {
        const head = chunks[this.activeChunkIndex];
        await this.client.updateCardKitElement(
          this.cardId,
          STREAMING_TRANSCRIPT_ELEMENT_ID,
          head,
          ++this.sequence,
        );
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

    await this.client.updateCardKitElement(
      this.cardId,
      STREAMING_TRANSCRIPT_ELEMENT_ID,
      chunks[this.activeChunkIndex],
      ++this.sequence,
    );
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
      this.status = "completed";
      return true;
    } catch (error) {
      this.status = "failed";
      this.onFailure?.("recover", error);
      return false;
    }
  }

  private async startCard(initialText: string): Promise<void> {
    const card = buildStreamingTranscriptCard(
      normalizeMarkdownForFeishu(initialText) || "…",
      false,
      this.cardTitle,
    );
    const cardId = await this.client.createCardKitInstance(card);
    await this.client.sendCardKitReference(this.target, cardId);
    this.cardId = cardId;
    this.sequence = 0;
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
}
