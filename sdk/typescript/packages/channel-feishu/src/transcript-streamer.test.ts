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
  }

  async finalizeCardKitInstance(cardId: string, sequence: number, _summary: string): Promise<void> {
    this.finalized.push({ cardId, sequence });
  }

  async replaceCardKitInstance(
    cardId: string,
    card: Record<string, unknown>,
    sequence: number,
  ): Promise<void> {
    if (this.failReplace) throw new Error("replace unavailable");
    this.replaced.push({ cardId, card, sequence });
  }
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
