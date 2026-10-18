import assert from "node:assert/strict";
import test from "node:test";

import { JsonRpcMessage } from "@dotcraft/sdk/wire";

import { consumeTurnStream, type TurnStreamHandlers } from "./turnStream.js";

const turn = { threadId: "t", turnId: "turn-1", channelContext: "c" };

async function* events(items: Array<{ method: string; params?: Record<string, unknown> }>): AsyncIterableIterator<JsonRpcMessage> {
  for (const item of items) {
    yield JsonRpcMessage.fromDict(item);
  }
}

function recorder(): { calls: string[]; errors: string[]; handlers: TurnStreamHandlers } {
  const calls: string[] = [];
  const errors: string[] = [];
  return {
    calls,
    errors,
    handlers: {
      onActivity: async (_turn, activity) => {
        calls.push(`${activity.kind}:${activity.phase}:${activity.itemId}`);
      },
      onTurnRetrying: async () => {
        calls.push("retrying");
      },
      onHandlerError: (hook) => {
        errors.push(hook);
      },
    },
  };
}

test("consumeTurnStream reports item activity and the completed outcome", async () => {
  const { calls, handlers } = recorder();

  const outcome = await consumeTurnStream(events([
    { method: "item/started", params: { item: { id: "think-1", type: "reasoningContent" } } },
    { method: "item/completed", params: { item: { id: "think-1", type: "reasoningContent" } } },
    { method: "item/started", params: { item: { id: "a", type: "agentMessage" } } },
    { method: "item/agentMessage/delta", params: { itemId: "a", delta: "draft" } },
    { method: "item/completed", params: { item: { id: "a", type: "agentMessage", payload: { text: "draft" } } } },
    { method: "item/started", params: { item: { id: "tool-1", type: "toolCall" } } },
    { method: "item/started", params: { item: { id: "note", type: "approvalRequest" } } },
    { method: "item/completed", params: { item: { id: "img-1", type: "imageGeneration", payload: { status: "completed", result: "aGk=" } } } },
    { method: "turn/completed", params: { turn: { items: [{ id: "a", type: "agentMessage", payload: { text: "final" } }] } } },
    { method: "item/started", params: { item: { id: "late", type: "toolCall" } } },
  ]), turn, handlers);

  assert.deepEqual(outcome, { status: "completed" });
  assert.deepEqual(calls, [
    "reasoning:started:think-1",
    "reasoning:completed:think-1",
    "text:started:a",
    "text:completed:a",
    "tool:started:tool-1",
  ]);
});

test("consumeTurnStream reports stream retries for its Turn only", async () => {
  const { calls, handlers } = recorder();

  const outcome = await consumeTurnStream(events([
    { method: "system/event", params: { turnId: "turn-1", kind: "streamError" } },
    { method: "system/event", params: { turnId: "other", kind: "streamError" } },
    { method: "system/event", params: { turnId: "turn-1", kind: "compacting" } },
    { method: "item/started", params: { item: { id: "a", type: "agentMessage" } } },
    { method: "turn/failed", params: { turn: { error: "provider exploded" } } },
  ]), turn, handlers);

  assert.deepEqual(outcome, { status: "failed", error: "provider exploded" });
  assert.deepEqual(calls, ["retrying", "text:started:a"]);
});

test("consumeTurnStream isolates hook failures and reports cancellation and lost streams", async () => {
  const { errors, handlers } = recorder();
  handlers.onActivity = async () => {
    throw new Error("sink unavailable");
  };

  const cancelled = await consumeTurnStream(events([
    { method: "item/started", params: { item: { id: "tool-1", type: "toolCall" } } },
    { method: "turn/cancelled", params: {} },
  ]), turn, handlers);
  const disconnected = await consumeTurnStream(events([
    { method: "item/started", params: { item: { id: "tool-2", type: "toolCall" } } },
  ]), turn, handlers);

  assert.deepEqual(cancelled, { status: "cancelled" });
  assert.deepEqual(disconnected, { status: "disconnected" });
  assert.deepEqual(errors, ["onActivity", "onActivity"]);
});
