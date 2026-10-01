import {
  DECISION_ACCEPT,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
} from "@dotcraft/sdk";

export const APPROVAL_REPLY_PENDING_NOTICE =
  "当前有待审批的操作，请回复 同意/yes、同意全部/yes all 或 拒绝/no。";
export const APPROVAL_REPLY_TIMEOUT_NOTICE = "审批已超时，操作已取消。";

const ACCEPT_FOR_SESSION_REPLIES = new Set(["同意全部", "允许全部", "yes all", "approve all", "y all"]);
const ACCEPT_REPLIES = new Set(["同意", "允许", "yes", "y", "approve"]);
const DECLINE_REPLIES = new Set(["拒绝", "不同意", "no", "n", "reject", "deny"]);
const CANCEL_REPLIES = new Set(["取消", "cancel"]);

export function parseApprovalReplyDecision(text: string): string | null {
  const reply = text
    .trim()
    .replace(/^[/／]/, "")
    .trim()
    .replace(/\s+/g, " ")
    .toLowerCase();
  if (!reply) return null;

  if (ACCEPT_FOR_SESSION_REPLIES.has(reply) || /(?<!不)(同意|允许)全部/.test(reply)) {
    return DECISION_ACCEPT_FOR_SESSION;
  }
  if (ACCEPT_REPLIES.has(reply)) return DECISION_ACCEPT;
  if (DECLINE_REPLIES.has(reply)) return DECISION_DECLINE;
  if (CANCEL_REPLIES.has(reply)) return DECISION_CANCEL;
  return null;
}

export interface ApprovalReplyRecipient {
  channelContext: string;
  userId: string;
}

export interface ApprovalReplyTrackerOptions {
  notify(recipient: ApprovalReplyRecipient, text: string): Promise<void>;
}

type PendingApprovalReply = ApprovalReplyRecipient & {
  resolve(decision: string): void;
};

export class ApprovalReplyTracker {
  private readonly pending = new Map<string, PendingApprovalReply>();

  constructor(private readonly options: ApprovalReplyTrackerOptions) {}

  wait(requestId: string, recipient: ApprovalReplyRecipient, timeoutMs: number): Promise<string> {
    return new Promise<string>((resolveDecision) => {
      const timer = setTimeout(() => {
        if (this.pending.get(requestId) !== entry) return;
        this.pending.delete(requestId);
        resolveDecision(DECISION_CANCEL);
        void this.notify(recipient, APPROVAL_REPLY_TIMEOUT_NOTICE);
      }, timeoutMs);
      const entry: PendingApprovalReply = {
        channelContext: recipient.channelContext,
        userId: recipient.userId,
        resolve: (decision) => {
          clearTimeout(timer);
          if (this.pending.get(requestId) === entry) this.pending.delete(requestId);
          resolveDecision(decision);
        },
      };
      this.pending.get(requestId)?.resolve(DECISION_CANCEL);
      this.pending.set(requestId, entry);
    });
  }

  tryResolve(recipient: ApprovalReplyRecipient, text: string): boolean {
    const decision = parseApprovalReplyDecision(text);
    if (!decision) return false;
    const entry = this.find(recipient);
    if (!entry) return false;
    entry.resolve(decision);
    return true;
  }

  async remindIfPending(recipient: ApprovalReplyRecipient): Promise<boolean> {
    if (!this.find(recipient)) return false;
    await this.notify(recipient, APPROVAL_REPLY_PENDING_NOTICE);
    return true;
  }

  cancel(recipient: ApprovalReplyRecipient): void {
    for (const entry of [...this.pending.values()]) {
      if (matches(entry, recipient)) entry.resolve(DECISION_CANCEL);
    }
  }

  resolveAll(decision: string): void {
    for (const entry of [...this.pending.values()]) {
      entry.resolve(decision);
    }
  }

  private find(recipient: ApprovalReplyRecipient): PendingApprovalReply | undefined {
    for (const entry of this.pending.values()) {
      if (matches(entry, recipient)) return entry;
    }
    return undefined;
  }

  private async notify(recipient: ApprovalReplyRecipient, text: string): Promise<void> {
    try {
      await this.options.notify(recipient, text);
    } catch (error) {
      console.error("[channel] approval notice delivery failed:", error instanceof Error ? error.message : String(error));
    }
  }
}

function matches(entry: ApprovalReplyRecipient, recipient: ApprovalReplyRecipient): boolean {
  return entry.userId === recipient.userId && entry.channelContext === recipient.channelContext;
}
