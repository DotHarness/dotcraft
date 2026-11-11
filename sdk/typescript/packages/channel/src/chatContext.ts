import { textPart, type InputPart } from "@dotcraft/sdk";

const QUOTED_TEXT_MAX_CHARS = 1000;
const MAX_UNADDRESSED_MESSAGES = 50;

export type ChatContextMessage = {
  messageId?: string;
  senderName: string;
  text: string;
  /** Epoch milliseconds. */
  sentAt?: number;
};

function truncateChatText(text: string, maxChars = QUOTED_TEXT_MAX_CHARS): string {
  const chars = Array.from(text.trim());
  return chars.length <= maxChars ? chars.join("") : `${chars.slice(0, maxChars).join("")}…`;
}

export function quotedMessagePart(senderName: string, text: string): InputPart {
  return textPart(
    `[Quoted message from ${senderName || "unknown"}]\n${quoteLines(truncateChatText(text))}`,
  );
}

function unaddressedMessagesPart(messages: readonly ChatContextMessage[]): InputPart {
  const lines = messages.map((message) => {
    const time = message.sentAt === undefined ? "" : `[${new Date(message.sentAt).toISOString()}] `;
    return `${time}${message.senderName}: ${message.text.replace(/\n/g, "\n  ")}`;
  });
  return textPart(
    `[Recent messages in this chat that did not address you]\n${lines.join("\n")}`,
  );
}

function quoteLines(text: string): string {
  return text.split("\n").map((line) => `> ${line}`).join("\n");
}

type BufferedMessage = ChatContextMessage & { seq: number };

export type UnaddressedSnapshot = {
  part: InputPart;
  /** Drops the snapshot's messages; call once the server accepted the input carrying them. */
  consume: () => void;
};

export class UnaddressedMessageBuffer {
  private readonly scopes = new Map<string, BufferedMessage[]>();
  private seq = 0;
  private readonly limit: number;

  constructor(limit: number) {
    this.limit = Math.max(0, Math.min(MAX_UNADDRESSED_MESSAGES, Math.floor(limit)));
  }

  get enabled(): boolean {
    return this.limit > 0;
  }

  remember(scope: string, message: ChatContextMessage): void {
    const text = message.text.trim();
    if (!this.enabled || !scope || !text) return;
    const list = this.scopes.get(scope) ?? [];
    list.push({ ...message, text: truncateChatText(text), seq: ++this.seq });
    if (list.length > this.limit) list.splice(0, list.length - this.limit);
    this.scopes.set(scope, list);
  }

  snapshot(scope: string): UnaddressedSnapshot | undefined {
    const list = this.scopes.get(scope);
    if (!list?.length) return undefined;
    const lastSeq = list[list.length - 1].seq;
    let consumed = false;
    return {
      part: unaddressedMessagesPart(list),
      consume: () => {
        if (consumed) return;
        consumed = true;
        const remaining = (this.scopes.get(scope) ?? []).filter((message) => message.seq > lastSeq);
        if (remaining.length) this.scopes.set(scope, remaining);
        else this.scopes.delete(scope);
      },
    };
  }

  remove(messageId: string): void {
    for (const [scope, list] of this.scopes) {
      const remaining = list.filter((message) => message.messageId !== messageId);
      if (remaining.length) this.scopes.set(scope, remaining);
      else this.scopes.delete(scope);
    }
  }

  reset(scope: string): void {
    this.scopes.delete(scope);
  }
}
