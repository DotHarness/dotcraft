import assert from "node:assert/strict";
import test from "node:test";

import { composeTranscriptMarkdown } from "./transcript.js";
import { FeishuTranscriptStreamer } from "./transcript-streamer.js";

class FakeCardKitClient {
  readonly created: Record<string, unknown>[] = [];
  readonly sent: Array<{ target: string; cardId: string }> = [];
  readonly updates: Array<{ cardId: string; content: string; sequence: number }> = [];
  readonly finalized: Array<{ cardId: string; sequence: number }> = [];
  readonly replaced: Array<{ cardId: string; card: Record<string, unknown>; sequence: number }> = [];
  failCreate = false;
  failNextUpdate = false;
  failReplace = false;

  async createCardKitInstance(card: Record<string, unknown>): Promise<string> {
    if (this.failCreate) throw new Error("permission unavailable");
    this.created.push(card);
    return `card-${this.created.length}`;
  }

  async sendCardKitReference(target: string, cardId: string): Promise<{ messageId: string; chatId: string }> {
    this.sent.push({ target, cardId });
    return { messageId: `message-${this.sent.length}`, chatId: target };
  }

  async updateCardKitElement(
    cardId: string,
    _elementId: string,
    content: string,
    sequence: number,
  ): Promise<void> {
    if (this.failNextUpdate) {
      this.failNextUpdate = false;
      throw new Error("update unavailable");
    }
    this.updates.push({ cardId, content, sequence });
    this.sequenceLog.push({ cardId, sequence });
  }

  async finalizeCardKitInstance(cardId: string, sequence: number, _summary: unknown): Promise<void> {
    this.finalized.push({ cardId, sequence });
    this.sequenceLog.push({ cardId, sequence });
  }

  async replaceCardKitInstance(
    cardId: string,
    card: Record<string, unknown>,
    sequence: number,
  ): Promise<void> {
    if (this.failReplace) throw new Error("replace unavailable");
    this.replaced.push({ cardId, card, sequence });
    this.sequenceLog.push({ cardId, sequence });
  }

  readonly deleted: Array<{ cardId: string; elementId: string; sequence: number }> = [];
  readonly recalled: string[] = [];
  readonly sequenceLog: Array<{ cardId: string; sequence: number }> = [];
  failDelete = false;

  readonly patched: Array<{ cardId: string; elementId: string; element: Record<string, unknown>; sequence: number }> = [];
  readonly appended: Array<{ cardId: string; element: Record<string, unknown>; sequence: number }> = [];
  failPatch = false;
  failAppend = false;

  async appendCardKitElement(cardId: string, element: Record<string, unknown>, sequence: number): Promise<void> {
    if (this.failAppend) throw new Error("append rejected");
    this.appended.push({ cardId, element, sequence });
    this.sequenceLog.push({ cardId, sequence });
  }

  async patchCardKitElement(
    cardId: string,
    elementId: string,
    element: Record<string, unknown>,
    sequence: number,
  ): Promise<void> {
    if (this.failPatch) throw new Error("patch rejected");
    this.patched.push({ cardId, elementId, element, sequence });
    this.sequenceLog.push({ cardId, sequence });
  }

  async deleteCardKitElement(cardId: string, elementId: string, sequence: number): Promise<void> {
    if (this.failDelete) throw new Error("delete unavailable");
    this.deleted.push({ cardId, elementId, sequence });
    this.sequenceLog.push({ cardId, sequence });
  }

  async recallMessage(messageId: string): Promise<void> {
    this.recalled.push(messageId);
  }

  sequencesFor(cardId: string): number[] {
    return this.sequenceLog.filter((call) => call.cardId === cardId).map((call) => call.sequence);
  }
}

function statusElementOf(card: Record<string, unknown>): Record<string, unknown> | undefined {
  const body = card.body as { elements?: Array<Record<string, unknown>> };
  return body.elements?.find((element) => element.element_id === "dotcraft_status");
}

test("composeTranscriptMarkdown preserves content and ensures a blank line between message items", () => {
  assert.equal(composeTranscriptMarkdown(["Intro.", "## Heading"]), "Intro.\n\n## Heading");
  assert.equal(composeTranscriptMarkdown(["Intro.\n", "\n- item"]), "Intro.\n\n- item");
  assert.equal(
    composeTranscriptMarkdown(["```ts\nconst value = 1;\n```", "Next paragraph."]),
    "```ts\nconst value = 1;\n```\n\nNext paragraph.",
  );
});

test("FeishuTranscriptStreamer coalesces content and finalizes the native card", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");

  assert.equal(await streamer.update("Hello"), true);
  assert.equal(await streamer.update("Hello world"), true);
  assert.equal(await streamer.update("Hello world!"), true);
  assert.equal(await streamer.complete("Hello world!"), true);

  assert.equal(client.created.length, 1);
  assert.equal(client.sent.length, 1);
  assert.equal(client.updates.at(-1)?.content, "Hello world!");
  assert.equal(client.finalized.length, 1);
  const sequences = [
    ...client.updates.map((call) => call.sequence),
    ...client.finalized.map((call) => call.sequence),
  ];
  assert.deepEqual(sequences, [...sequences].sort((left, right) => left - right));
  assert.equal(new Set(sequences).size, sequences.length);
});

test("FeishuTranscriptStreamer reports unavailable CardKit before sending a visible card", async () => {
  const client = new FakeCardKitClient();
  client.failCreate = true;
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");

  assert.equal(await streamer.update("Hello"), false);
  assert.equal(streamer.hasVisibleCard, false);
  assert.equal(client.sent.length, 0);
});

test("FeishuTranscriptStreamer recovers a sent card with a final full replacement", async () => {
  const client = new FakeCardKitClient();
  client.failNextUpdate = true;
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");

  assert.equal(await streamer.update("Partial"), false);
  assert.equal(streamer.hasVisibleCard, true);
  assert.equal(await streamer.complete("Complete reply"), true);
  assert.equal(client.replaced.length, 1);
});

test("FeishuTranscriptStreamer returns control to fallback delivery when terminal recovery fails", async () => {
  const client = new FakeCardKitClient();
  client.failNextUpdate = true;
  client.failReplace = true;
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");

  assert.equal(await streamer.update("Partial"), false);
  assert.equal(await streamer.complete("Complete reply"), false);
});

test("FeishuTranscriptStreamer rolls long Markdown into finalized continuation cards", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", {
    maxElementChars: 1000,
  });
  const longReply = Array.from({ length: 30 }, (_, index) => `Paragraph ${index}: ${"x".repeat(70)}`).join("\n\n");

  assert.equal(await streamer.update(longReply), true);
  assert.equal(await streamer.complete(longReply), true);
  assert.ok(client.created.length > 1);
  assert.equal(client.sent.length, client.created.length);
  assert.equal(client.finalized.length, client.created.length);
});

test("FeishuTranscriptStreamer finalizes a partial card when the turn stops", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");
  await streamer.update("Partial reply");
  await streamer.abort();
  assert.equal(client.finalized.length, 1);
});

const FAST_STATUS = { textStallMs: 300, statusSettleMs: 0, throttleMs: 100 };
const STALL_STATUS = { textStallMs: 100, statusSettleMs: 0, throttleMs: 100 };
const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));
const tool = (phase: "started" | "completed", itemId: string) => ({ kind: "tool" as const, phase, itemId });

function statusOps(client: FakeCardKitClient): string[] {
  return client.sequenceLog
    .map((entry) => {
      const appended = client.appended.find((call) => call.sequence === entry.sequence && call.cardId === entry.cardId);
      if (appended) return `append:${(appended.element.i18n_content as Record<string, string>).zh_cn.includes("工作中") ? "working" : "thinking"}`;
      const patched = client.patched.find((call) => call.sequence === entry.sequence && call.cardId === entry.cardId);
      if (patched) return `patch:${(patched.element.i18n_content as Record<string, string>).zh_cn.includes("工作中") ? "working" : "thinking"}`;
      const deleted = client.deleted.find((call) => call.sequence === entry.sequence && call.cardId === entry.cardId);
      if (deleted) return "delete";
      return null;
    })
    .filter((op): op is string => op !== null);
}

test("FeishuTranscriptStreamer follows Desktop: thinking, working while a tool runs, hidden while text streams", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", { statusIconImgKey: "img_loading", ...FAST_STATUS });

  assert.equal(await streamer.begin(), true);
  const initial = client.created[0]!.body as { elements: Array<Record<string, unknown>> };
  assert.deepEqual(initial.elements.map((element) => element.element_id), ["dotcraft_reply", "dotcraft_status"]);
  assert.match(String((statusElementOf(client.created[0]!)?.i18n_content as Record<string, string>).zh_cn), /思考中/);

  streamer.noteActivity(tool("started", "tool-1"));
  streamer.noteActivity(tool("started", "tool-2"));
  await sleep(20);
  assert.deepEqual(statusOps(client), ["patch:working"]);

  streamer.noteActivity(tool("completed", "tool-1"));
  await sleep(20);
  assert.deepEqual(statusOps(client), ["patch:working"]);
  streamer.noteActivity(tool("completed", "tool-2"));
  await sleep(20);
  assert.deepEqual(statusOps(client), ["patch:working", "patch:thinking"]);

  assert.equal(await streamer.update("First paragraph."), true);
  await sleep(50);
  assert.deepEqual(statusOps(client), ["patch:working", "patch:thinking", "delete"]);

  streamer.noteActivity(tool("started", "tool-3"));
  await sleep(50);
  assert.deepEqual(statusOps(client), ["patch:working", "patch:thinking", "delete"]);
  await sleep(350);
  assert.deepEqual(statusOps(client), ["patch:working", "patch:thinking", "delete", "append:working"]);
  const working = client.appended[0]!.element;
  assert.equal((working.icon as Record<string, unknown>).img_key, "img_loading");

  assert.equal(await streamer.update("First paragraph.\n\nSecond paragraph."), true);
  await sleep(50);
  assert.deepEqual(statusOps(client), ["patch:working", "patch:thinking", "delete", "append:working", "delete"]);
  assert.equal(await streamer.complete("First paragraph.\n\nSecond paragraph."), true);
  await sleep(350);
  assert.deepEqual(statusOps(client), ["patch:working", "patch:thinking", "delete", "append:working", "delete"]);
  assert.equal(client.finalized.length, 1);
  const sequences = client.sequencesFor("card-1");
  assert.deepEqual(sequences, [...sequences].sort((left, right) => left - right));
  assert.equal(new Set(sequences).size, sequences.length);
});

test("FeishuTranscriptStreamer shows thinking again when streamed text stalls", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", STALL_STATUS);

  await streamer.begin();
  await streamer.update("Partial");
  await sleep(30);
  assert.deepEqual(statusOps(client), ["delete"]);
  await sleep(120);
  assert.deepEqual(statusOps(client), ["delete", "append:thinking"]);
  await streamer.update("Partial answer");
  await sleep(30);
  assert.deepEqual(statusOps(client), ["delete", "append:thinking", "delete"]);
});

test("FeishuTranscriptStreamer keeps streaming when the status row cannot be appended", async () => {
  const client = new FakeCardKitClient();
  client.failAppend = true;
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", FAST_STATUS);

  await streamer.begin();
  await streamer.update("Reply");
  await sleep(150);
  streamer.noteActivity(tool("started", "tool-1"));
  await sleep(150);
  assert.equal(await streamer.complete("Reply"), true);

  assert.equal(client.finalized.length, 1);
  assert.equal(client.updates.at(-1)?.content, "Reply");
});

test("FeishuTranscriptStreamer stops touching the card after the turn ends", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", FAST_STATUS);

  await streamer.begin();
  await streamer.update("Reply");
  streamer.noteActivity(tool("started", "late"));
  assert.equal(await streamer.complete("Reply"), true);
  const before = client.sequenceLog.length;
  streamer.noteActivity(tool("completed", "late"));
  await sleep(200);
  assert.equal(client.sequenceLog.length, before);
});

test("FeishuTranscriptStreamer still finalizes when the status row cannot be deleted", async () => {
  const client = new FakeCardKitClient();
  client.failDelete = true;
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");
  const failures: string[] = [];
  (streamer as unknown as { onFailure: (stage: string) => void }).onFailure = (stage) => failures.push(stage);

  await streamer.begin();
  await streamer.update("Reply");
  assert.equal(await streamer.complete("Reply"), true);
  assert.equal(client.finalized.length, 1);
});

test("FeishuTranscriptStreamer hides the status row once text starts, including across rollover cards", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", { maxElementChars: 1000 });
  const longReply = Array.from({ length: 30 }, (_, index) => `Paragraph ${index}: ${"x".repeat(70)}`).join("\n\n");

  await streamer.begin();
  await streamer.update(longReply);
  await streamer.complete(longReply);

  assert.ok(client.created.length > 1);
  assert.notEqual(statusElementOf(client.created[0]!), undefined);
  assert.ok(client.created.slice(1).every((card) => statusElementOf(card) === undefined));
  assert.deepEqual(client.deleted.map((deletion) => deletion.cardId), ["card-1"]);
});

test("FeishuTranscriptStreamer recalls a status-only card when the turn ends without text", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");

  await streamer.begin();
  await streamer.abort();

  assert.deepEqual(client.recalled, ["message-1"]);
  assert.equal(client.finalized.length, 0);
});

test("FeishuTranscriptStreamer delivers the card through the supplied sender", async () => {
  const client = new FakeCardKitClient();
  const delivered: string[] = [];
  const streamer = new FeishuTranscriptStreamer(client, "group:oc_1/thread:om_root", "DotCraft", {
    deliverCard: async (cardId) => {
      delivered.push(cardId);
      return { messageId: "om_reply", chatId: "oc_1" };
    },
  });

  await streamer.begin();
  await streamer.update("Reply");
  await streamer.complete("Reply");

  assert.deepEqual(delivered, ["card-1"]);
  assert.equal(client.sent.length, 0);
});
