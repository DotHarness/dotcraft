import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import type { ChannelAppServerClient } from "./channelAppServerClient.js";
import type { NotificationHandler, ServerRequestHandler } from "@dotcraft/sdk/wire";
import {
  ApprovalDispatcher,
  ChannelMessageQueue,
  ChannelToolDispatcher,
  CommandRouter,
  DeliveryDispatcher,
  ModuleConfigLoader,
  ModuleLifecycleState,
  ThreadResolver,
  TurnStreamReducer,
  UserInputDispatcher,
} from "./channelRuntime.js";
import { JsonRpcError, JsonRpcMessage } from "@dotcraft/sdk/wire";
import type { SessionThread } from "@dotcraft/sdk/contracts";
import type { RuntimeAdditionalContextEntry } from "@dotcraft/sdk/contracts";
import { makeThread } from "./test-contract-fixtures.js";
import type { WorkspaceContext } from "./module.js";
import {
  buildUserInputPrompt,
  canUseNativeSingleChoiceUserInput,
  hasUserInputAnswer,
  mergeUserInputResponses,
  splitUserInputRequestByQuestion,
  userInputResponseForSingleChoice,
  userInputResponseFromText,
} from "./userInput.js";

function deferred<T = void>(): {
  promise: Promise<T>;
  resolve: (value: T | PromiseLike<T>) => void;
  reject: (error: unknown) => void;
} {
  let resolve!: (value: T | PromiseLike<T>) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let i = 0; i < 50; i += 1) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  assert.equal(predicate(), true);
}

async function* events(items: Array<{ method: string; params?: Record<string, unknown> }>): AsyncIterableIterator<JsonRpcMessage> {
  for (const item of items) {
    yield JsonRpcMessage.fromDict(item);
  }
}

class FakeRuntimeClient {
  readonly threads = new Map<string, SessionThread>();
  readonly archived: string[] = [];
  readonly events: string[] = [];
  readonly notificationHandlers = new Map<string, NotificationHandler>();
  readonly requestHandlers = new Map<string, ServerRequestHandler>();
  readonly resumeParams: Array<{ threadId: string; params?: Record<string, unknown> }> = [];
  readonly startParams: Record<string, unknown>[] = [];
  listResult: SessionThread[] = [];
  readFailures = new Set<string>();
  nextStartedId = "thread-created";
  commandResult: Record<string, unknown> = {};
  commandError: unknown = null;

  async threadRead(threadId: string): Promise<SessionThread> {
    this.events.push(`read:${threadId}`);
    if (this.readFailures.has(threadId)) throw new Error(`Missing ${threadId}`);
    const thread = this.threads.get(threadId);
    if (!thread) throw new Error(`Missing ${threadId}`);
    return thread;
  }

  async threadResume(threadId: string, params?: Record<string, unknown>): Promise<SessionThread> {
    this.events.push(`resume:${threadId}`);
    this.resumeParams.push({ threadId, params });
    const resumed = makeThread(threadId, "active");
    this.threads.set(threadId, resumed);
    return resumed;
  }

  async threadList(_params: Record<string, unknown>): Promise<SessionThread[]> {
    this.events.push("list");
    return this.listResult;
  }

  async threadStart(params: Record<string, unknown>): Promise<SessionThread> {
    this.events.push("start");
    this.startParams.push(params);
    const thread = makeThread(this.nextStartedId, "active");
    this.threads.set(thread.id, thread);
    return thread;
  }

  async threadArchive(threadId: string): Promise<void> {
    this.events.push(`archive:${threadId}`);
    this.archived.push(threadId);
  }

  async commandExecute(_params: Record<string, unknown>): Promise<Record<string, unknown>> {
    this.events.push("command");
    if (this.commandError) throw this.commandError;
    return this.commandResult;
  }

  registerHandler(method: string, fn: NotificationHandler): void {
    this.notificationHandlers.set(method, fn);
  }

  registerServerRequestHandler(method: string, fn: ServerRequestHandler): void {
    this.requestHandlers.set(method, fn);
  }

}

function asWire(fake: FakeRuntimeClient): ChannelAppServerClient {
  return fake as unknown as ChannelAppServerClient;
}

test("ChannelMessageQueue serializes each identity and keeps identities independent", async () => {
  const first = deferred();
  const eventsSeen: string[] = [];
  const errors: string[] = [];
  const queue = new ChannelMessageQueue({
    onError: (key) => errors.push(key),
  });

  queue.enqueue("a", async () => {
    eventsSeen.push("a1:start");
    await first.promise;
    eventsSeen.push("a1:end");
  });
  queue.enqueue("a", async () => {
    eventsSeen.push("a2");
  });
  queue.enqueue("b", async () => {
    eventsSeen.push("b1");
  });

  await waitUntil(() => eventsSeen.includes("a1:start") && eventsSeen.includes("b1"));
  assert.deepEqual(eventsSeen, ["a1:start", "b1"]);
  assert.deepEqual(queue.activeKeys().sort(), ["a"]);

  first.resolve();
  await waitUntil(() => eventsSeen.includes("a2"));
  assert.deepEqual(eventsSeen, ["a1:start", "b1", "a1:end", "a2"]);
  assert.deepEqual(errors, []);
});

test("user-input helpers format prompts and parse numeric replies", () => {
  const request = {
    requestId: "req-1",
    questions: [
      {
        id: "choice",
        header: "Pick a mode",
        question: "Which mode should DotCraft use?",
        options: [
          { label: "Auto", description: "Let DotCraft choose." },
          { label: "Manual", description: "Ask before each step." },
        ],
        isOther: true,
      },
    ],
  };

  assert.equal(canUseNativeSingleChoiceUserInput(request), true);
  assert.deepEqual(userInputResponseForSingleChoice(request, 1), {
    answers: { choice: { answers: ["Manual"] } },
  });
  assert.deepEqual(userInputResponseFromText(request, "2"), {
    answers: { choice: { answers: ["Manual"] } },
  });
  assert.deepEqual(userInputResponseFromText(request, "0 custom plan"), {
    answers: { choice: { answers: ["custom plan"] } },
  });
  assert.match(buildUserInputPrompt(request), /Reply with an option number/);
});

test("user-input helpers parse multi-question replies by question number", () => {
  const request = {
    questions: [
      { id: "first", header: "First", question: "One?", options: [{ label: "A" }, { label: "B" }] },
      { id: "second", header: "Second", question: "Two?", isOther: true },
    ],
  };

  assert.equal(canUseNativeSingleChoiceUserInput(request), false);
  assert.deepEqual(userInputResponseFromText(request, "1: 2\n2: 0 custom"), {
    answers: {
      first: { answers: ["B"] },
      second: { answers: ["custom"] },
    },
  });
});

test("user-input helpers split multi-question requests for sequential prompts", () => {
  const request = {
    requestId: "req-1",
    threadId: "thread-1",
    questions: [
      { id: "first", header: "First", question: "One?", options: [{ label: "A" }, { label: "B" }] },
      { id: "second", header: "Second", question: "Two?", isOther: true },
    ],
  };

  const steps = splitUserInputRequestByQuestion(request);

  assert.equal(steps.length, 2);
  assert.equal(steps[0]?.question.id, "first");
  assert.equal(steps[0]?.request.requestId, "req-1:1");
  assert.deepEqual((steps[0]?.request.questions as unknown[] | undefined)?.map((item) => (item as { id: string }).id), [
    "first",
  ]);
  assert.equal(steps[1]?.request.requestId, "req-1:2");

  const merged = mergeUserInputResponses([
    { answers: { first: { answers: ["B"] } } },
    { answers: { second: { answers: ["custom"] } } },
  ]);
  assert.equal(hasUserInputAnswer(merged, "first"), true);
  assert.equal(hasUserInputAnswer(merged, "missing"), false);
  assert.deepEqual(merged, {
    answers: {
      first: { answers: ["B"] },
      second: { answers: ["custom"] },
    },
  });
});

test("ChannelMessageQueue continues after a job error", async () => {
  const eventsSeen: string[] = [];
  const errors: string[] = [];
  const queue = new ChannelMessageQueue({
    onError: (key) => errors.push(key),
  });
  queue.enqueue("u", async () => {
    eventsSeen.push("first");
    throw new Error("boom");
  });
  queue.enqueue("u", async () => {
    eventsSeen.push("second");
  });

  await waitUntil(() => eventsSeen.includes("second"));
  assert.deepEqual(eventsSeen, ["first", "second"]);
  assert.deepEqual(errors, ["u"]);
});

test("ThreadResolver resolves cache, list reuse, fresh reset, and not-active recovery", async () => {
  const fake = new FakeRuntimeClient();
  fake.threads.set("cached", makeThread("cached", "active"));
  fake.threads.set("paused", makeThread("paused", "paused"));
  fake.threads.set("listed", makeThread("listed", "active"));
  const eventsSeen: string[] = [];
  const resolver = new ThreadResolver({
    client: asWire(fake),
    channelName: "test",
    onEvent: (event) => eventsSeen.push(event.action),
  });

  resolver.setCachedThread("u:c", "cached");
  assert.equal((await resolver.getOrCreateThread({
    identityKey: "u:c",
    userId: "u",
    channelContext: "c",
    workspacePath: "/w",
  })).id, "cached");

  resolver.setCachedThread("u:p", "paused");
  assert.equal((await resolver.getOrCreateThread({
    identityKey: "u:p",
    userId: "u",
    channelContext: "p",
    workspacePath: "/w",
  })).id, "paused");

  fake.readFailures.add("missing");
  fake.listResult = [makeThread("listed", "active")];
  resolver.setCachedThread("u:m", "missing");
  assert.equal((await resolver.getOrCreateThread({
    identityKey: "u:m",
    userId: "u",
    channelContext: "m",
    workspacePath: "/w",
  })).id, "listed");

  resolver.setCachedThread("u:fresh", "cached");
  fake.listResult = [makeThread("cached", "active"), makeThread("old-paused", "paused")];
  const archived = await resolver.resetIdentityThreads({
    identityKey: "u:fresh",
    userId: "u",
    channelContext: "fresh",
    workspacePath: "/w",
  });
  fake.nextStartedId = "thread-fresh";
  assert.equal((await resolver.getOrCreateThread({
    identityKey: "u:fresh",
    userId: "u",
    channelContext: "fresh",
    workspacePath: "/w",
  })).id, "thread-fresh");

  fake.readFailures.add("stale");
  fake.listResult = [makeThread("recover-paused", "paused")];
  assert.equal((await resolver.recoverThreadAfterNotActive({
    identityKey: "u:recover",
    userId: "u",
    channelContext: "recover",
    workspacePath: "/w",
  }, "stale")).id, "recover-paused");

  assert.deepEqual(archived, ["cached", "old-paused"]);
  assert.deepEqual(fake.archived, ["cached", "old-paused"]);
  assert.deepEqual(eventsSeen, [
    "cache_hit",
    "resumed_from_cache",
    "cache_invalidated",
    "listed_active",
    "archived",
    "archived",
    "force_fresh_created",
    "recovered_listed_resumed",
  ]);
});

test("ThreadResolver binds runtime context once and restores it after connection replacement", async () => {
  const fake = new FakeRuntimeClient();
  const additionalContext: Record<string, RuntimeAdditionalContextEntry> = {
    "test.runtime": { kind: "application", value: "Use the test runtime." },
  };
  const resolver = new ThreadResolver({
    client: asWire(fake),
    channelName: "test",
    getRuntimeAdditionalContext: () => additionalContext,
  });
  const lookup = {
    identityKey: "u:c",
    userId: "u",
    channelContext: "c",
    workspacePath: "/w",
  };

  const created = await resolver.getOrCreateThread(lookup);
  assert.deepEqual(fake.startParams[0]?.additionalContext, additionalContext);

  await resolver.getOrCreateThread(lookup);
  assert.deepEqual(fake.events, ["list", "start", `read:${created.id}`]);
  assert.equal(fake.resumeParams.length, 0);

  resolver.invalidateConnectionBoundRuntime();
  fake.listResult = [makeThread(created.id, "active")];
  await resolver.getOrCreateThread(lookup);
  assert.deepEqual(fake.resumeParams, [{
    threadId: created.id,
    params: { additionalContext },
  }]);
});

test("CommandRouter handles expanded prompts, reset payloads, and JsonRpcError delivery", async () => {
  const fake = new FakeRuntimeClient();
  const resolver = new ThreadResolver({ client: asWire(fake), channelName: "test" });
  const enqueued: unknown[] = [];
  const delivered: string[] = [];
  const archived: string[][] = [];
  const bound: string[] = [];
  const router = new CommandRouter({
    client: asWire(fake),
    threadResolver: resolver,
    identityKey: (userId, channelContext) => `${userId}:${channelContext}`,
    getDefaultWorkspacePath: () => "/w",
    deliver: async (_target, content) => {
      delivered.push(content);
      return true;
    },
    enqueueMessage: (opts) => enqueued.push(opts),
    onThreadsArchived: (_identityKey, ids) => archived.push(ids),
    onThreadContextBound: (threadId) => bound.push(threadId),
  });

  resolver.setCachedThread("u:c", "thread-1");
  fake.commandResult = { expandedPrompt: "expanded" };
  assert.equal(await router.routeBeforeQueue({
    userId: "u",
    userName: "User",
    text: "/sum now",
    channelContext: "c",
  }), "handled");
  assert.deepEqual(enqueued, [{
    userId: "u",
    userName: "User",
    text: "/sum now",
    channelContext: "c",
    inputParts: [{ type: "commandRef", name: "sum", rawText: "/sum now", argsText: "now" }],
    skipCommand: true,
  }]);

  fake.commandResult = {
    handled: true,
    message: "new session",
    sessionReset: true,
    thread: { id: "thread-2", status: "active" },
    archivedThreadIds: ["thread-1"],
  };
  assert.deepEqual(await router.routeForTurn({
    identityKey: "u:c",
    opts: { userId: "u", userName: "User", text: "/new", channelContext: "c" },
    threadId: "thread-1",
    sender: { senderId: "u", senderName: "User" },
    workspacePath: "/w",
  }), { kind: "handled" });
  assert.equal(resolver.getCachedThreadId("u:c"), "thread-2");
  assert.deepEqual(delivered, ["new session"]);
  assert.deepEqual(archived, [["thread-1"]]);
  assert.deepEqual(bound, ["thread-2"]);

  fake.commandError = new JsonRpcError(-32000, "Command failed");
  assert.deepEqual(await router.routeForTurn({
    identityKey: "u:c",
    opts: { userId: "u", userName: "User", text: "/bad", channelContext: "c" },
    threadId: "thread-2",
    sender: { senderId: "u", senderName: "User" },
    workspacePath: "/w",
  }), { kind: "handled" });
  assert.deepEqual(delivered, ["new session", "Command failed"]);
});

test("TurnStreamReducer preserves segment boundaries and final results", async () => {
  const reducer = new TurnStreamReducer();
  const segments: Array<{ text: string; isFinal: boolean }> = [];
  const completed: Array<{ reply: string; segmentsWereDelivered: boolean }> = [];

  await reducer.consume(events([
    { method: "item/agentMessage/delta", params: { threadId: "t", itemId: "a", delta: "before " } },
    { method: "item/started", params: { threadId: "t", item: { id: "tool", type: "toolCall" } } },
    { method: "item/agentMessage/delta", params: { threadId: "t", itemId: "b", delta: "af" } },
    {
      method: "item/completed",
      params: { threadId: "t", item: { id: "b", type: "agentMessage", payload: { text: "after" } } },
    },
    {
      method: "turn/completed",
      params: {
        threadId: "t",
        turn: {
          items: [
            { id: "a", type: "agentMessage", payload: { text: "before " } },
            { id: "b", type: "agentMessage", payload: { text: "after" } },
          ],
        },
      },
    },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onSegmentCompleted: async (_threadId, _turnId, text, isFinal) => {
      segments.push({ text, isFinal });
    },
    onTurnCompleted: async (_threadId, _turnId, reply, _channelContext, segmentsWereDelivered) => {
      completed.push({ reply, segmentsWereDelivered });
    },
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });

  assert.deepEqual(segments, [
    { text: "before ", isFinal: false },
    { text: "after", isFinal: true },
  ]);
  assert.deepEqual(completed, [{ reply: "before after", segmentsWereDelivered: true }]);
});

test("TurnStreamReducer reports ordered reply progress and isolates progress hook failures", async () => {
  const progress: Array<{ parts: readonly string[]; isFinal: boolean }> = [];
  const completed: string[] = [];
  let calls = 0;

  await new TurnStreamReducer().consume(events([
    { method: "item/agentMessage/delta", params: { itemId: "a", delta: "intro" } },
    { method: "item/started", params: { item: { id: "tool", type: "toolCall" } } },
    { method: "item/agentMessage/delta", params: { itemId: "b", delta: "# He" } },
    {
      method: "item/completed",
      params: { item: { id: "b", type: "agentMessage", payload: { text: "# Heading" } } },
    },
    {
      method: "turn/completed",
      params: {
        turn: {
          items: [
            { id: "a", type: "agentMessage", payload: { text: "intro" } },
            { id: "b", type: "agentMessage", payload: { text: "# Heading" } },
          ],
        },
      },
    },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onReplyProgress: async (_threadId, _turnId, parts, isFinal) => {
      calls += 1;
      if (calls === 1) throw new Error("progress unavailable");
      progress.push({ parts: [...parts], isFinal });
    },
    onSegmentCompleted: async () => {},
    onTurnCompleted: async (_threadId, _turnId, reply) => {
      completed.push(reply);
    },
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });

  assert.deepEqual(progress, [
    { parts: ["intro", "# He"], isFinal: false },
    { parts: ["intro", "# Heading"], isFinal: false },
    { parts: ["intro", "# Heading"], isFinal: true },
  ]);
  assert.deepEqual(completed, ["intro# Heading"]);
});

test("TurnStreamReducer reports item activity for every lifecycle edge and isolates hook failures", async () => {
  const activity: string[] = [];
  const completed: string[] = [];
  let calls = 0;

  await new TurnStreamReducer().consume(events([
    { method: "item/started", params: { item: { id: "think-1", type: "reasoningContent" } } },
    { method: "item/completed", params: { item: { id: "think-1", type: "reasoningContent" } } },
    { method: "item/started", params: { item: { id: "tool-1", type: "toolCall" } } },
    { method: "item/completed", params: { item: { id: "tool-1", type: "toolCall" } } },
    { method: "item/started", params: { item: { id: "result-1", type: "toolResult" } } },
    { method: "item/completed", params: { item: { id: "result-1", type: "toolResult" } } },
    { method: "item/started", params: { item: { id: "tool-2", type: "toolCall" } } },
    { method: "item/started", params: { item: { id: "note", type: "approvalRequest" } } },
    { method: "item/started", params: { item: { id: "a", type: "agentMessage" } } },
    { method: "item/agentMessage/delta", params: { itemId: "a", delta: "done" } },
    { method: "item/completed", params: { item: { id: "a", type: "agentMessage", payload: { text: "done" } } } },
    { method: "turn/completed", params: { turn: { items: [{ id: "a", type: "agentMessage", payload: { text: "done" } }] } } },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onActivity: async (_threadId, _turnId, item) => {
      calls += 1;
      if (calls === 1) throw new Error("activity sink unavailable");
      activity.push(`${item.kind}:${item.phase}:${item.itemId}`);
    },
    onSegmentCompleted: async () => {},
    onTurnCompleted: async (_threadId, _turnId, reply) => {
      completed.push(reply);
    },
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });

  assert.deepEqual(activity, [
    "reasoning:completed:think-1",
    "tool:started:tool-1",
    "tool:completed:tool-1",
    "tool:started:result-1",
    "tool:completed:result-1",
    "tool:started:tool-2",
    "text:started:a",
    "text:completed:a",
  ]);
  assert.deepEqual(completed, ["done"]);
});

test("TurnStreamReducer aligns final progress by AgentMessage id when early deltas were missed", async () => {
  const finalProgress: Array<readonly string[]> = [];

  await new TurnStreamReducer().consume(events([
    { method: "item/agentMessage/delta", params: { itemId: "b", delta: "second" } },
    {
      method: "turn/completed",
      params: {
        turn: {
          items: [
            { id: "a", type: "agentMessage", payload: { text: "first" } },
            { id: "b", type: "agentMessage", payload: { text: "second" } },
          ],
        },
      },
    },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onReplyProgress: async (_threadId, _turnId, parts, isFinal) => {
      if (isFinal) finalProgress.push([...parts]);
    },
    onSegmentCompleted: async () => {},
    onTurnCompleted: async () => {},
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });

  assert.deepEqual(finalProgress, [["first", "second"]]);
});

test("TurnStreamReducer preserves failed segment tails for final delivery", async () => {
  const reducer = new TurnStreamReducer();
  const segments: Array<{ text: string; isFinal: boolean }> = [];
  const completed: Array<{ reply: string; segmentsWereDelivered: boolean }> = [];

  await reducer.consume(events([
    { method: "item/agentMessage/delta", params: { threadId: "t", itemId: "a", delta: "before " } },
    { method: "item/started", params: { threadId: "t", item: { id: "tool", type: "toolCall" } } },
    {
      method: "item/completed",
      params: { threadId: "t", item: { id: "a", type: "agentMessage", payload: { text: "before " } } },
    },
    { method: "item/agentMessage/delta", params: { threadId: "t", itemId: "b", delta: "after" } },
    {
      method: "turn/completed",
      params: {
        threadId: "t",
        turn: {
          items: [
            { id: "a", type: "agentMessage", payload: { text: "before " } },
            { id: "b", type: "agentMessage", payload: { text: "after" } },
          ],
        },
      },
    },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onSegmentCompleted: async (_threadId, _turnId, text, isFinal) => {
      segments.push({ text, isFinal });
      return isFinal;
    },
    onTurnCompleted: async (_threadId, _turnId, reply, _channelContext, segmentsWereDelivered) => {
      completed.push({ reply, segmentsWereDelivered });
    },
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });

  assert.deepEqual(segments, [
    { text: "before ", isFinal: false },
    { text: "before after", isFinal: true },
  ]);
  assert.deepEqual(completed, [{ reply: "before after", segmentsWereDelivered: true }]);
});

test("TurnStreamReducer allows full-reply fallback when final segment delivery fails", async () => {
  const completed: Array<{ reply: string; segmentsWereDelivered: boolean }> = [];
  const segments: string[] = [];

  await new TurnStreamReducer().consume(events([
    {
      method: "turn/completed",
      params: { threadId: "t", turn: { items: [{ id: "a", type: "agentMessage", payload: { text: "from snapshot" } }] } },
    },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onSegmentCompleted: async (_threadId, _turnId, text) => {
      segments.push(text);
      return false;
    },
    onTurnCompleted: async (_threadId, _turnId, reply, _channelContext, segmentsWereDelivered) => {
      completed.push({ reply, segmentsWereDelivered });
    },
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });

  assert.deepEqual(segments, ["from snapshot"]);
  assert.deepEqual(completed, [{ reply: "from snapshot", segmentsWereDelivered: false }]);
});

test("TurnStreamReducer handles orphan deltas, snapshot-only turns, failures, and cancellation", async () => {
  const snapshotReducer = new TurnStreamReducer();
  const snapshotSegments: string[] = [];
  await snapshotReducer.consume(events([
    {
      method: "turn/completed",
      params: { threadId: "t", turn: { items: [{ type: "agentMessage", payload: { text: "from snapshot" } }] } },
    },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onSegmentCompleted: async (_threadId, _turnId, text) => {
      snapshotSegments.push(text);
    },
    onTurnCompleted: async () => {},
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {},
  });
  assert.deepEqual(snapshotSegments, ["from snapshot"]);

  const terminalEvents: string[] = [];
  await new TurnStreamReducer().consume(events([
    { method: "turn/failed", params: { threadId: "t", turn: { error: "boom" } } },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onSegmentCompleted: async () => {},
    onTurnCompleted: async () => {},
    onTurnFailed: async (_threadId, _turnId, error) => {
      terminalEvents.push(`failed:${error}`);
    },
    onTurnCancelled: async () => {},
  });
  await new TurnStreamReducer().consume(events([
    { method: "turn/cancelled", params: { threadId: "t" } },
  ]), { threadId: "t", turnId: "turn", channelContext: "c" }, {
    onSegmentCompleted: async () => {},
    onTurnCompleted: async () => {},
    onTurnFailed: async () => {},
    onTurnCancelled: async () => {
      terminalEvents.push("cancelled");
    },
  });
  assert.deepEqual(terminalEvents, ["failed:boom", "cancelled"]);
});

test("dispatchers register approval, user-input, delivery, tool, and heartbeat handlers", async () => {
  const fake = new FakeRuntimeClient();
  const errors: string[] = [];
  new ApprovalDispatcher({
    client: asWire(fake),
    onApprovalRequest: async () => "accept",
    onError: (message) => errors.push(message),
  }).register();
  new DeliveryDispatcher({
    client: asWire(fake),
    onSend: async (_target, message) => ({ delivered: true, kind: message.kind }),
    onError: (message) => errors.push(message),
  }).register();
  new UserInputDispatcher({
    client: asWire(fake),
    onUserInputRequest: async (request) => ({ answers: { choice: { answers: [String(request.choice ?? "A")] } } }),
    onError: (message) => errors.push(message),
  }).register();
  new ChannelToolDispatcher({
    client: asWire(fake),
    onToolCall: async (request) => ({ success: true, tool: request.tool }),
    onError: (message) => errors.push(message),
  }).register();

  assert.deepEqual(await fake.requestHandlers.get("item/approval/request")?.("approval", {}), {
    decision: "accept",
  });
  assert.deepEqual(await fake.requestHandlers.get("item/tool/requestUserInput")?.("input", {
    choice: "B",
  }), { answers: { choice: { answers: ["B"] } } });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/send")?.("s1", {
    target: "chat",
    message: { kind: "image" },
    metadata: {},
  }), { delivered: true, kind: "image" });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/toolCall")?.("tool1", {
    tool: "Upload",
  }), { success: true, tool: "Upload" });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/heartbeat")?.("h1", {}), {});

  assert.deepEqual(errors, []);
});

test("dispatchers preserve exception fallback shapes", async () => {
  const fake = new FakeRuntimeClient();
  new ApprovalDispatcher({
    client: asWire(fake),
    onApprovalRequest: async () => {
      throw new Error("approval failed");
    },
    onError: () => {},
  }).register();
  new DeliveryDispatcher({
    client: asWire(fake),
    onSend: async () => {
      throw new Error("send failed");
    },
    onError: () => {},
  }).register();
  new UserInputDispatcher({
    client: asWire(fake),
    onUserInputRequest: async () => {
      throw new Error("user input failed");
    },
    onError: () => {},
  }).register();
  new ChannelToolDispatcher({
    client: asWire(fake),
    onToolCall: async () => {
      throw new Error("tool failed");
    },
    onError: () => {},
  }).register();

  assert.deepEqual(await fake.requestHandlers.get("item/approval/request")?.("approval", {}), {
    decision: "cancel",
  });
  assert.deepEqual(await fake.requestHandlers.get("item/tool/requestUserInput")?.("input", {}), {
    answers: {},
  });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/send")?.("s1", {}), {
    delivered: false,
    errorCode: "AdapterDeliveryFailed",
    errorMessage: "Error: send failed",
  });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/toolCall")?.("tool1", {}), {
    success: false,
    errorCode: "AdapterToolCallFailed",
    errorMessage: "Error: tool failed",
  });
});

test("ModuleConfigLoader and ModuleLifecycleState cover missing, invalid, loaded, and status transitions", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-runtime-"));
  try {
    const context: WorkspaceContext = {
      workspaceRoot: dir,
      craftPath: dir,
      channelName: "demo",
      moduleId: "demo-module",
    };
    const lifecycle = new ModuleLifecycleState();
    const transitions: string[] = [];
    lifecycle.onStatusChange((status, error) => transitions.push(`${status}:${error?.code ?? ""}`));
    lifecycle.setStatus("starting");
    lifecycle.setStatus("configMissing", lifecycle.buildModuleError("configMissing", "missing"));
    lifecycle.setStatus("ready", lifecycle.getError());
    assert.deepEqual(transitions, ["starting:", "configMissing:configMissing", "ready:"]);

    const loader = new ModuleConfigLoader<{ ok: true }>({
      getConfigFileName: () => "demo.json",
      validateConfig: (raw): asserts raw is { ok: true } => {
        if (!(raw && typeof raw === "object" && (raw as { ok?: unknown }).ok === true)) {
          throw new Error("invalid config");
        }
      },
    });
    assert.equal((await loader.load(context)).status, "configMissing");

    await writeFile(join(dir, "demo.json"), JSON.stringify({ ok: false }), "utf-8");
    const invalid = await loader.load(context);
    assert.equal(invalid.status, "configInvalid");
    assert.equal(invalid.error.code, "configInvalid");

    await writeFile(join(dir, "demo.json"), JSON.stringify({ ok: true }), "utf-8");
    const loaded = await loader.load(context);
    assert.equal(loaded.status, "loaded");
    if (loaded.status === "loaded") {
      assert.deepEqual(loaded.config, { ok: true });
      assert.equal(loaded.stdioRuntime, false);
    }
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("ModuleConfigLoader applies managed WebSocket runtime endpoint overrides", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-sdk-runtime-ws-"));
  const previousTransport = process.env.DOTCRAFT_CHANNEL_TRANSPORT;
  const previousWsUrl = process.env.DOTCRAFT_CHANNEL_WS_URL;
  const previousToken = process.env.DOTCRAFT_CHANNEL_WS_TOKEN;

  try {
    process.env.DOTCRAFT_CHANNEL_TRANSPORT = "websocket";
    process.env.DOTCRAFT_CHANNEL_WS_URL = "ws://127.0.0.1:9133/ws";
    process.env.DOTCRAFT_CHANNEL_WS_TOKEN = "fresh-token";

    const context: WorkspaceContext = {
      workspaceRoot: dir,
      craftPath: dir,
      channelName: "demo",
      moduleId: "demo-module",
    };
    await writeFile(
      join(dir, "demo.json"),
      JSON.stringify({
        ok: true,
        dotcraft: {
          wsUrl: "ws://stale/ws",
          token: "stale-token",
        },
      }),
      "utf-8",
    );

    const loader = new ModuleConfigLoader<{
      ok: true;
      dotcraft: { wsUrl: string; token: string };
    }>({
      getConfigFileName: () => "demo.json",
      validateConfig: (raw): asserts raw is {
        ok: true;
        dotcraft: { wsUrl: string; token: string };
      } => {
        const config = raw as {
          ok?: unknown;
          dotcraft?: { wsUrl?: unknown; token?: unknown };
        };
        if (
          config.ok !== true ||
          typeof config.dotcraft?.wsUrl !== "string" ||
          typeof config.dotcraft?.token !== "string"
        ) {
          throw new Error("invalid config");
        }
      },
    });

    const loaded = await loader.load(context);
    assert.equal(loaded.status, "loaded");
    if (loaded.status === "loaded") {
      assert.equal(loaded.stdioRuntime, false);
      assert.equal(loaded.config.dotcraft.wsUrl, "ws://127.0.0.1:9133/ws");
      assert.equal(loaded.config.dotcraft.token, "fresh-token");
    }
  } finally {
    if (previousTransport === undefined) delete process.env.DOTCRAFT_CHANNEL_TRANSPORT;
    else process.env.DOTCRAFT_CHANNEL_TRANSPORT = previousTransport;
    if (previousWsUrl === undefined) delete process.env.DOTCRAFT_CHANNEL_WS_URL;
    else process.env.DOTCRAFT_CHANNEL_WS_URL = previousWsUrl;
    if (previousToken === undefined) delete process.env.DOTCRAFT_CHANNEL_WS_TOKEN;
    else process.env.DOTCRAFT_CHANNEL_WS_TOKEN = previousToken;
    await rm(dir, { recursive: true, force: true });
  }
});
