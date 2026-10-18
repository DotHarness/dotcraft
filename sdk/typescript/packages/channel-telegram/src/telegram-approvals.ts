import { InlineKeyboard } from "grammy";

import type { TelegramApiLike } from "./telegram-media.js";

const APPROVAL_BUTTONS = [
  { decision: "accept", label: "✅ Approve" },
  { decision: "acceptForSession", label: "✅ Approve (this session)" },
  { decision: "decline", label: "❌ Decline" },
  { decision: "cancel", label: "\u{1F6D1} Cancel turn" },
] as const;

type TelegramApprovalApi = Pick<TelegramApiLike, "sendMessage"> & {
  editMessageReplyMarkup(chatId: number, messageId: number, other: Record<string, unknown>): Promise<unknown>;
  editMessageText(chatId: number, messageId: number, text: string, other: Record<string, unknown>): Promise<unknown>;
};

type PendingApproval = {
  resolve: (decision: string) => void;
  timer: ReturnType<typeof setTimeout>;
  chatId: number;
  messageId?: number;
  prompt: string;
};

export function escapeHtml(value: string): string {
  return value.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;");
}

export class TelegramApprovals {
  private readonly pending = new Map<string, PendingApproval>();
  timeoutMs = 120_000;

  constructor(private readonly api: () => TelegramApprovalApi) {}

  async request(chatId: number, request: Record<string, unknown>): Promise<string> {
    const requestId = String(request.requestId ?? "");
    const keyboard = new InlineKeyboard();
    for (const button of APPROVAL_BUTTONS) {
      keyboard.text(button.label, `approval:${requestId}:${button.decision}`).row();
    }
    const prompt =
      "<b>Agent approval required</b>\n" +
      `Type: <code>${escapeHtml(String(request.approvalType ?? ""))}</code>\n` +
      `Operation: <code>${escapeHtml(String(request.operation ?? ""))}</code>\n` +
      `Reason: ${escapeHtml(String(request.reason ?? ""))}`;

    const sent = await this.api().sendMessage(chatId, prompt, { parse_mode: "HTML", reply_markup: keyboard });

    return await new Promise<string>((resolveDecision) => {
      const timer = setTimeout(() => {
        this.pending.delete(requestId);
        resolveDecision("cancel");
      }, this.timeoutMs);
      this.pending.set(requestId, {
        resolve: (value) => {
          clearTimeout(timer);
          this.pending.delete(requestId);
          resolveDecision(value);
        },
        timer,
        chatId,
        messageId: (sent as { message_id?: number } | undefined)?.message_id,
        prompt,
      });
    });
  }

  async resolveFromButton(
    requestId: string,
    decision: string,
    message: { chatId: number; messageId: number; text: string } | undefined,
  ): Promise<void> {
    const pending = this.pending.get(requestId);
    pending?.resolve(decision);
    if (message) {
      await this.markDecided(message.chatId, message.messageId, pending?.prompt ?? escapeHtml(message.text), decision);
    }
  }

  async resolveInChat(chatId: number, decision: string): Promise<boolean> {
    const pending = [...this.pending.values()].find((entry) => entry.chatId === chatId);
    if (!pending) return false;
    pending.resolve(decision);
    if (pending.messageId !== undefined) {
      await this.markDecided(chatId, pending.messageId, pending.prompt, decision);
    }
    return true;
  }

  resolveAll(decision: string): void {
    for (const pending of [...this.pending.values()]) {
      pending.resolve(decision);
    }
  }

  private async markDecided(chatId: number, messageId: number, promptHtml: string, decision: string): Promise<void> {
    const chosenLabel = APPROVAL_BUTTONS.find((button) => button.decision === decision)?.label ?? decision;
    try {
      await this.api().editMessageReplyMarkup(chatId, messageId, { reply_markup: undefined });
      await this.api().editMessageText(
        chatId,
        messageId,
        `${promptHtml}\n\n<i>Decision: ${escapeHtml(chosenLabel)}</i>`,
        { parse_mode: "HTML" },
      );
    } catch {
      // Ignore edit failures for stale/deleted messages.
    }
  }
}
