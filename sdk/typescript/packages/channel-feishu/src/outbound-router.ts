import { conversationTargetBase, conversationTargetThreadKey } from "./conversation-target.js";
import type { FeishuClient } from "./feishu-client.js";
import { FeishuApiError, type FeishuSendResult } from "./feishu-types.js";
import { errorMessage, logWarn, shortId } from "./logging.js";

export type OutboundClient = Pick<
  FeishuClient,
  | "sendInteractiveCard"
  | "sendCardKitReference"
  | "sendFile"
  | "replyInteractiveCard"
  | "replyCardKitReference"
  | "replyFile"
>;

export type OutboundFile = { fileName: string; data: Buffer; mediaType?: string };

// Feishu has no create-into-thread API, so topic delivery replies to an anchor: the latest inbound
// message for the target, falling back to the topic root (the thread key itself).
export class FeishuOutboundRouter {
  private readonly anchors = new Map<string, string>();
  private readonly resolveClient: () => OutboundClient;

  // A getter lets anchors be recorded before the Feishu client exists.
  constructor(client: OutboundClient | (() => OutboundClient)) {
    this.resolveClient = typeof client === "function" ? client : () => client;
  }

  private get client(): OutboundClient {
    return this.resolveClient();
  }

  noteInbound(target: string, messageId: string): void {
    if (!conversationTargetThreadKey(target) || !messageId.trim()) return;
    this.anchors.set(target, messageId.trim());
  }

  forget(target: string): void {
    this.anchors.delete(target);
  }

  async sendCard(target: string, card: Record<string, unknown>): Promise<FeishuSendResult> {
    return await this.route(
      target,
      (anchor) => this.client.replyInteractiveCard(anchor, card, true),
      (base) => this.client.sendInteractiveCard(base, card),
    );
  }

  async sendCardKit(target: string, cardId: string): Promise<FeishuSendResult> {
    return await this.route(
      target,
      (anchor) => this.client.replyCardKitReference(anchor, cardId, true),
      (base) => this.client.sendCardKitReference(base, cardId),
    );
  }

  async sendFile(target: string, file: OutboundFile): Promise<FeishuSendResult & { fileKey: string }> {
    return await this.route(
      target,
      (anchor) => this.client.replyFile(anchor, file, true),
      (base) => this.client.sendFile(base, file),
    );
  }

  private async route<T>(
    target: string,
    viaReply: (anchor: string) => Promise<T>,
    viaCreate: (base: string) => Promise<T>,
  ): Promise<T> {
    const base = conversationTargetBase(target);
    const threadKey = conversationTargetThreadKey(target);
    if (!threadKey) return await viaCreate(base);

    const candidates = [this.anchors.get(target), threadKey].filter(
      (value, index, all): value is string => Boolean(value) && all.indexOf(value) === index,
    );
    for (const anchor of candidates) {
      try {
        return await viaReply(anchor);
      } catch (error) {
        if (!isAnchorLoss(error)) throw error;
        if (this.anchors.get(target) === anchor) this.anchors.delete(target);
        logWarn("outbound.anchor_fallback", {
          target: shortId(target),
          anchor: shortId(anchor),
          message: errorMessage(error),
        });
      }
    }
    return await viaCreate(base);
  }
}

function isAnchorLoss(error: unknown): boolean {
  if (!(error instanceof FeishuApiError)) return false;
  return error.kind === "invalidArgument" || error.kind === "permission" || error.kind === "unknown";
}
