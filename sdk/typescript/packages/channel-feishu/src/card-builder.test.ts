import assert from "node:assert/strict";
import test from "node:test";

import { buildApprovalCard, buildReplyCards, buildTranscriptCard, DEFAULT_CARD_TITLE } from "./card-builder.js";

function getCardTitle(card: Record<string, unknown>): string {
  const header = (card.header as Record<string, unknown> | undefined) ?? {};
  const title = (header.title as Record<string, unknown> | undefined) ?? {};
  return String(title.content ?? "");
}

function getMarkdown(card: Record<string, unknown>): string {
  const body = (card.body as Record<string, unknown> | undefined) ?? {};
  const elements = (body.elements as Array<Record<string, unknown>> | undefined) ?? [];
  for (const element of elements) {
    if (element.tag === "markdown") {
      return String(element.content ?? "");
    }
  }
  return "";
}

test("buildTranscriptCard uses default card title", () => {
  const card = buildTranscriptCard("hi", true);
  assert.equal(getCardTitle(card), DEFAULT_CARD_TITLE);
});

test("custom card title is used in reply/transcript/approval cards", () => {
  const transcriptCard = buildTranscriptCard("hi", true, "我的助手");
  assert.equal(getCardTitle(transcriptCard), "我的助手");

  const replyCards = buildReplyCards("hello", "Bot");
  assert.equal(getCardTitle(replyCards[0] ?? {}), "Bot Reply");

  const approvalCard = buildApprovalCard({
    requestId: "req-1",
    approvalType: "file",
    operation: "write",
    target: "/tmp/out.txt",
    reason: "",
    timeoutSeconds: 120,
    cardTitle: "Bot",
  });
  assert.ok(getMarkdown(approvalCard).startsWith("Bot needs approval before continuing."));
});

test("invalid card title falls back to default title", () => {
  const tooLongTitle = "A".repeat(49);
  const byEmpty = buildTranscriptCard("hello", false, "");
  const byWhitespace = buildTranscriptCard("hello", false, "   ");
  const byTooLong = buildTranscriptCard("hello", false, tooLongTitle);

  assert.equal(getCardTitle(byEmpty), DEFAULT_CARD_TITLE);
  assert.equal(getCardTitle(byWhitespace), DEFAULT_CARD_TITLE);
  assert.equal(getCardTitle(byTooLong), DEFAULT_CARD_TITLE);
});
