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
  ModuleConfigLoader,
  ModuleLifecycleState,
  ThreadResolver,
  UserInputDispatcher,
} from "./channelRuntime.js";
import { DeliveryDispatcher } from "./deliveryDispatcher.js";
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

class FakeRuntimeClient {
  readonly threads = new Map<string, SessionThread>();
  readonly events: string[] = [];
  readonly notificationHandlers = new Map<string, NotificationHandler>();
  readonly requestHandlers = new Map<string, ServerRequestHandler>();
  readonly resumeParams: Array<{ threadId: string; params?: Record<string, unknown> }> = [];
  readonly startParams: Record<string, unknown>[] = [];
  listResult: SessionThread[] = [];
  readFailures = new Set<string>();
  nextStartedId = "thread-created";
  listGate: Promise<void> | null = null;

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
    if (this.listGate) await this.listGate;
    return this.listResult;
  }

  async threadStart(params: Record<string, unknown>): Promise<SessionThread> {
    this.events.push("start");
    this.startParams.push(params);
    const thread = makeThread(this.nextStartedId, "active");
    this.threads.set(thread.id, thread);
    return thread;
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

test("ThreadResolver resolves cache, list reuse, and not-active recovery", async () => {
  const fake = new FakeRuntimeClient();
  fake.threads.set("cached", makeThread("cached", "active"));
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

  fake.readFailures.add("missing");
  fake.listResult = [makeThread("listed", "active")];
  resolver.setCachedThread("u:m", "missing");
  assert.equal((await resolver.getOrCreateThread({
    identityKey: "u:m",
    userId: "u",
    channelContext: "m",
    workspacePath: "/w",
  })).id, "listed");

  fake.readFailures.add("stale");
  fake.threads.set("recover-active", makeThread("recover-active", "active"));
  fake.listResult = [makeThread("recover-active", "active")];
  assert.equal((await resolver.recoverThreadAfterNotActive({
    identityKey: "u:recover",
    userId: "u",
    channelContext: "recover",
    workspacePath: "/w",
  }, "stale")).id, "recover-active");

  assert.deepEqual(eventsSeen, [
    "cache_hit",
    "cache_invalidated",
    "listed_active",
    "recovered_listed_active",
  ]);
});

test("ThreadResolver shares one resolution between concurrent callers for an identity", async () => {
  const fake = new FakeRuntimeClient();
  const gate = deferred();
  fake.listGate = gate.promise;
  const resolver = new ThreadResolver({ client: asWire(fake), channelName: "test" });
  const lookup = { identityKey: "u:c", userId: "u", channelContext: "c", workspacePath: "/w" };

  const first = resolver.getOrCreateThread(lookup);
  const second = resolver.getOrCreateThread(lookup);
  gate.resolve();

  assert.equal((await first).id, "thread-created");
  assert.equal((await second).id, "thread-created");
  assert.equal(fake.startParams.length, 1);
});

test("ThreadResolver binds runtime context and chat once and restores them after connection replacement", async () => {
  const fake = new FakeRuntimeClient();
  const additionalContext: Record<string, RuntimeAdditionalContextEntry> = {
    "test.runtime": { kind: "application", value: "Use the test runtime." },
  };
  const resolver = new ThreadResolver({
    client: asWire(fake),
    channelName: "test",
    getRuntimeAdditionalContext: () => additionalContext,
  });
  const chat = { kind: "group", label: "Team", membership: "shared" } as const;
  const lookup = {
    identityKey: "u:c",
    userId: "u",
    channelContext: "c",
    workspacePath: "/w",
    chat,
  };

  const created = await resolver.getOrCreateThread(lookup);
  assert.deepEqual(fake.startParams[0]?.additionalContext, additionalContext);
  assert.deepEqual(fake.startParams[0]?.chat, chat);

  await resolver.getOrCreateThread(lookup);
  assert.deepEqual(fake.events, ["list", "start", `read:${created.id}`]);
  assert.equal(fake.resumeParams.length, 0);

  resolver.invalidateConnectionBoundRuntime();
  fake.listResult = [makeThread(created.id, "active")];
  await resolver.getOrCreateThread(lookup);
  assert.deepEqual(fake.resumeParams, [{
    threadId: created.id,
    params: { additionalContext, chat },
  }]);
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
    deliveryId: "d1",
    message: { kind: "image" },
    metadata: {},
  }), { delivered: true, kind: "image" });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/toolCall")?.("tool1", {
    tool: "Upload",
  }), { success: true, tool: "Upload" });
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/heartbeat")?.("h1", {}), {});

  assert.deepEqual(errors, []);
});

test("delivery dispatcher answers a repeated delivery id with the first result without sending again", async () => {
  const fake = new FakeRuntimeClient();
  const sent: string[] = [];
  new DeliveryDispatcher({
    client: asWire(fake),
    onSend: async (target) => {
      sent.push(target);
      return { delivered: true, remoteMessageId: `m${sent.length}` };
    },
  }).register();
  const send = fake.requestHandlers.get("ext/channel/send")!;
  const params = { target: "chat", deliveryId: "d1", message: { kind: "text", text: "hi" } };

  const [first, concurrent] = await Promise.all([send("s1", params), send("s2", params)]);
  const retried = await send("s3", params);
  const other = await send("s4", { ...params, deliveryId: "d2" });

  assert.deepEqual(sent, ["chat", "chat"]);
  assert.deepEqual(first, { delivered: true, remoteMessageId: "m1" });
  assert.deepEqual(concurrent, first);
  assert.deepEqual(retried, first);
  assert.deepEqual(other, { delivered: true, remoteMessageId: "m2" });
});

test("delivery dispatcher sends again after a failed delivery with the same id", async () => {
  const fake = new FakeRuntimeClient();
  let attempts = 0;
  new DeliveryDispatcher({
    client: asWire(fake),
    onSend: async () => {
      attempts += 1;
      return attempts === 1 ? { delivered: false, errorCode: "AdapterDeliveryFailed" } : { delivered: true };
    },
  }).register();
  const send = fake.requestHandlers.get("ext/channel/send")!;
  const params = { target: "chat", deliveryId: "d1", message: { kind: "text", text: "hi" } };

  assert.deepEqual(await send("s1", params), { delivered: false, errorCode: "AdapterDeliveryFailed" });
  assert.deepEqual(await send("s2", params), { delivered: true });
  assert.equal(attempts, 2);
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
  assert.deepEqual(await fake.requestHandlers.get("ext/channel/send")?.("s1", { deliveryId: "d1" }), {
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
