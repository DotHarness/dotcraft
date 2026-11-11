import assert from "node:assert/strict";
import test from "node:test";

import { HistoryDispatcher } from "./chatHistory.js";
import { UnaddressedMessageBuffer, quotedMessagePart, truncateChatText } from "./chatContext.js";
import type { ChannelAppServerClient } from "./channelAppServerClient.js";
import type { ServerRequestHandler } from "@dotcraft/sdk/wire";

test("unaddressed buffer keeps the newest messages per scope and consumes only its snapshot", () => {
  const buffer = new UnaddressedMessageBuffer(2);
  buffer.remember("chat:1", { messageId: "m1", senderName: "A", text: "first" });
  buffer.remember("chat:1", { messageId: "m2", senderName: "B", text: "second" });
  buffer.remember("chat:1", { messageId: "m3", senderName: "C", text: "third" });
  buffer.remember("chat:2", { messageId: "m4", senderName: "D", text: "other chat" });

  const snapshot = buffer.snapshot("chat:1");
  assert.ok(snapshot);
  const text = String(snapshot.part.text);
  assert.match(text, /second/);
  assert.match(text, /third/);
  assert.doesNotMatch(text, /first/);
  assert.match(text, /did not address you/);

  buffer.remember("chat:1", { messageId: "m5", senderName: "E", text: "fourth" });
  snapshot.consume();
  snapshot.consume();
  const next = buffer.snapshot("chat:1");
  assert.ok(next);
  assert.doesNotMatch(String(next.part.text), /third/);
  assert.match(String(next.part.text), /fourth/);

  buffer.remove("m5");
  assert.equal(buffer.snapshot("chat:1"), undefined);
  assert.ok(buffer.snapshot("chat:2"));
  buffer.reset("chat:2");
  assert.equal(buffer.snapshot("chat:2"), undefined);
});

test("unaddressed buffer remembers nothing when disabled", () => {
  const buffer = new UnaddressedMessageBuffer(0);
  buffer.remember("chat:1", { senderName: "A", text: "hi" });
  assert.equal(buffer.snapshot("chat:1"), undefined);
});

test("quoted messages are truncated and marked as reference data", () => {
  const long = "x".repeat(1200);
  assert.equal(Array.from(truncateChatText(long)).length, 1001);
  const part = quotedMessagePart("Alice", "line one\nline two");
  assert.match(String(part.text), /Quoted message from Alice/);
  assert.match(String(part.text), /> line one\n> line two/);
});

test("history dispatcher returns pages and reports read failures as errors", async () => {
  const handlers = new Map<string, ServerRequestHandler>();
  const client = {
    registerServerRequestHandler(method: string, handler: ServerRequestHandler) {
      handlers.set(method, handler);
    },
  } as unknown as ChannelAppServerClient;
  const requests: Array<{ target: string; limit: number; before?: string }> = [];
  new HistoryDispatcher(client, async (request) => {
    requests.push(request);
    if (request.before === "bad") throw new Error("boom");
    return { messages: [{ text: "hello" }], before: "seq-1" };
  }).register();

  const handler = handlers.get("ext/channel/history");
  assert.ok(handler);
  const signal = new AbortController().signal;
  assert.deepEqual(await handler("h1", { target: "group:1", limit: 5, before: "seq-9" }, signal), {
    messages: [{ text: "hello" }],
    before: "seq-1",
  });
  assert.deepEqual(requests, [{ target: "group:1", limit: 5, before: "seq-9" }]);

  const failed = (await handler("h2", { target: "group:1", limit: 5, before: "bad" }, signal)) as {
    messages: unknown[];
    errorCode?: string;
  };
  assert.equal(failed.errorCode, "HistoryUnavailable");
  assert.deepEqual(failed.messages, []);
});
