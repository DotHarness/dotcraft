import { DECISION_CANCEL } from "@dotcraft/channel";

import { buildApprovalCard, buildApprovalResolvedCard, buildApprovalTimeoutCard } from "./card-builder.js";
import type { FeishuCardActionEvent, FeishuSendResult } from "./feishu-types.js";
import { errorMessage, logInfo, logWarn, shortId } from "./logging.js";

type ApprovalWaiter = {
  resolve: (decision: string) => void;
  channelTarget: string;
  senderId?: string;
  messageId: string;
};

export interface FeishuApprovalCardsOptions {
  sendCard: (target: string, card: Record<string, unknown>) => Promise<FeishuSendResult>;
  updateCard: (messageId: string, card: Record<string, unknown>) => Promise<void>;
  cardTitle: () => string;
}

export class FeishuApprovalCards {
  timeoutMs = 120000;
  private readonly waiters = new Map<string, ApprovalWaiter>();

  constructor(private readonly options: FeishuApprovalCardsOptions) {}

  async request(channelTarget: string, request: Record<string, unknown>, senderId?: string): Promise<string> {
    const requestId = String(request.requestId ?? "");
    const threadId = String(request.threadId ?? "");
    const timeoutSeconds = Math.max(1, Math.round(this.timeoutMs / 1000));
    const card = buildApprovalCard({
      requestId,
      approvalType: String(request.approvalType ?? ""),
      operation: String(request.operation ?? ""),
      target: String(request.target ?? ""),
      reason: String(request.reason ?? ""),
      timeoutSeconds,
      cardTitle: this.options.cardTitle(),
    });
    const sent = await this.options.sendCard(channelTarget, card);
    logInfo("approval.card_sent", {
      requestId: shortId(requestId),
      threadId: shortId(threadId),
      timeoutSec: timeoutSeconds,
      channelTarget: shortId(channelTarget),
      messageId: shortId(sent.messageId),
    });

    return new Promise<string>((resolve) => {
      const timer = setTimeout(() => {
        const waiter = this.waiters.get(requestId);
        this.waiters.delete(requestId);
        if (waiter?.messageId) {
          void this.tryUpdateCard(waiter.messageId, buildApprovalTimeoutCard({ requestId, timeoutSeconds }));
        }
        logWarn("approval.timeout", {
          requestId: shortId(requestId),
          decision: DECISION_CANCEL,
        });
        resolve(DECISION_CANCEL);
      }, this.timeoutMs);
      this.waiters.set(requestId, {
        resolve: (decision: string) => {
          clearTimeout(timer);
          const waiter = this.waiters.get(requestId);
          this.waiters.delete(requestId);
          if (waiter?.messageId) {
            void this.tryUpdateCard(waiter.messageId, buildApprovalResolvedCard({ requestId, decision }));
          }
          logInfo("approval.resolved", {
            requestId: shortId(requestId),
            decision,
          });
          resolve(decision);
        },
        channelTarget,
        senderId,
        messageId: sent.messageId,
      });
    });
  }

  handleAction(event: FeishuCardActionEvent, value: Record<string, unknown>): boolean {
    const requestId = String(value.requestId ?? "");
    const decision = String(value.decision ?? "");
    const waiter = this.waiters.get(requestId);
    if (!waiter) {
      logWarn("approval.action_no_waiter", {
        requestId: shortId(requestId),
        openMessageId: shortId(String(event.context?.open_message_id ?? "")),
      });
      return false;
    }
    const openMessageId = String(event.context?.open_message_id ?? "");
    if (openMessageId && waiter.messageId && openMessageId !== waiter.messageId) {
      logWarn("approval.action_message_mismatch", {
        requestId: shortId(requestId),
        expectedMessageId: shortId(waiter.messageId),
        actualMessageId: shortId(openMessageId),
      });
      return false;
    }
    waiter.resolve(decision || DECISION_CANCEL);
    logInfo("approval.action_resolved", {
      requestId: shortId(requestId),
      decision: decision || DECISION_CANCEL,
      messageId: shortId(openMessageId || waiter.messageId),
    });
    return true;
  }

  resolveInChat(channelTarget: string, senderId: string, decision: string): boolean {
    const waiter = [...this.waiters.values()].find(
      (entry) => entry.channelTarget === channelTarget && entry.senderId === senderId,
    );
    waiter?.resolve(decision);
    return waiter !== undefined;
  }

  private async tryUpdateCard(messageId: string, card: Record<string, unknown>): Promise<void> {
    try {
      await this.options.updateCard(messageId, card);
      logInfo("approval.card_updated", {
        messageId: shortId(messageId),
      });
    } catch (error) {
      logWarn("approval.card_update_failed", {
        messageId: shortId(messageId),
        message: errorMessage(error),
      });
    }
  }
}
