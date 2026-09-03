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
  failPatch = false;

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

test("FeishuTranscriptStreamer shows thinking, switches to working at the first tool call, and drops the row at the end", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", { statusIconImgKey: "img_loading" });

  assert.equal(await streamer.begin(), true);
  assert.equal(client.created.length, 1);
  assert.equal(client.sent.length, 1);
  assert.equal(client.updates.length, 0);
  const status = statusElementOf(client.created[0]!);
  assert.match(String((status?.i18n_content as Record<string, string>).zh_cn), /思考中/);
  assert.equal((status?.icon as Record<string, unknown>).img_key, "img_loading");

  assert.equal(await streamer.update("Hello"), true);
  await streamer.markWorking();
  await streamer.markWorking();
  assert.equal(client.patched.length, 1);
  const working = client.patched[0]!;
  assert.equal(working.elementId, "dotcraft_status");
  assert.equal(working.element.tag, undefined);
  assert.equal(working.element.icon, undefined);
  assert.match(String((working.element.i18n_content as Record<string, string>).zh_cn), /工作中/);

  assert.equal(await streamer.update("Hello world"), true);
  assert.equal(await streamer.complete("Hello world"), true);

  assert.equal(client.deleted.length, 1);
  assert.equal(client.deleted[0]!.elementId, "dotcraft_status");
  assert.ok(client.updates.at(-1)!.sequence < client.deleted[0]!.sequence);
  assert.ok(client.deleted[0]!.sequence < client.finalized[0]!.sequence);
  const sequences = client.sequencesFor("card-1");
  assert.deepEqual(sequences, [...sequences].sort((left, right) => left - right));
  assert.equal(new Set(sequences).size, sequences.length);
});

test("FeishuTranscriptStreamer keeps streaming when the working patch is rejected", async () => {
  const client = new FakeCardKitClient();
  client.failPatch = true;
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft");

  await streamer.begin();
  await streamer.update("Reply");
  await streamer.markWorking();
  assert.equal(await streamer.complete("Reply"), true);

  assert.equal(client.finalized.length, 1);
  assert.equal(client.deleted.length, 1);
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

test("FeishuTranscriptStreamer carries the status row across rollover cards", async () => {
  const client = new FakeCardKitClient();
  const streamer = new FeishuTranscriptStreamer(client, "dm:test-user", "DotCraft", { maxElementChars: 1000 });
  const longReply = Array.from({ length: 30 }, (_, index) => `Paragraph ${index}: ${"x".repeat(70)}`).join("\n\n");

  await streamer.begin();
  await streamer.update(longReply);
  await streamer.complete(longReply);

  assert.ok(client.created.length > 1);
  assert.ok(client.created.every((card) => statusElementOf(card) !== undefined));
  assert.equal(client.deleted.length, client.created.length);
  assert.equal(client.patched.length, 0);
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
