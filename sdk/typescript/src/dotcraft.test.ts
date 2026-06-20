import assert from "node:assert/strict";
import test from "node:test";

import {
  DotCraft,
  parseAppBindingHandoff,
  type ApprovalDecision,
  type ApprovalHandler,
  type DynamicToolCallResult,
  type UserInputHandler,
} from "./dotcraft.js";
import { TurnInProgressError } from "./errors.js";
import { ERR_TURN_IN_PROGRESS, JsonRpcMessage, ServerCapabilities, ServerInfo, Thread, Turn } from "./models.js";
import type { DotCraftWireClient, ServerRequestHandler } from "./client.js";

class FakeWire {
  approvalHandler: ServerRequestHandler | null = null;
  readonly requestHandlers = new Map<string, ServerRequestHandler>();
  readonly calls: string[] = [];
  readonly requests: Array<{ method: string; params: unknown }> = [];
  startParams: Record<string, unknown> | null = null;
  turnStartInput: unknown;
  turnStartSender: unknown;
  turnStartError: unknown = null;
  events: JsonRpcMessage[] = [];
  queuedInput: unknown = { id: "queued-1" };
  cancelAfterInterrupt = false;
  interrupted: { threadId: string; turnId: string } | null = null;
  private releaseAfterInterrupt: (() => void) | null = null;

  setApprovalHandler(handler: ServerRequestHandler | null): void {
    this.approvalHandler = handler;
  }

  registerServerRequestHandler(method: string, handler: ServerRequestHandler): void {
    this.requestHandlers.set(method, handler);
  }

  async threadStart(params: Record<string, unknown>): Promise<Thread> {
    this.startParams = params;
    return new Thread("thread-1", "active", String(params.workspacePath ?? ""), String(params.userId ?? ""), String(params.channelName ?? ""));
  }

  async threadList(): Promise<Thread[]> {
    return [];
  }

  async threadListPage(): Promise<{ threads: Thread[]; nextCursor: string | null; totalMatched: number | null; raw: Record<string, unknown> }> {
    return { threads: [], nextCursor: null, totalMatched: 0, raw: { data: [] } };
  }

  async threadRead(threadId: string): Promise<Thread> {
    return new Thread(threadId, "active");
  }

  async threadResume(threadId: string): Promise<Thread> {
    return new Thread(threadId, "active");
  }

  async turnStart(_threadId: string, input: unknown, sender: unknown): Promise<Turn> {
    this.calls.push("turnStart");
    this.turnStartInput = input;
    this.turnStartSender = sender;
    if (this.turnStartError) throw this.turnStartError;
    return new Turn("turn-1", "thread-1", "running");
  }

  streamEvents(_threadId: string): AsyncIterableIterator<JsonRpcMessage> {
    this.calls.push("streamEvents");
    const events = [...this.events];
    if (this.cancelAfterInterrupt) {
      const wire = this;
      return (async function* () {
        const interrupted = new Promise<void>((resolve) => {
          wire.releaseAfterInterrupt = resolve;
        });
        yield JsonRpcMessage.fromDict({
          method: "turn/started",
          params: { threadId: "thread-1", turn: { id: "turn-1", threadId: "thread-1", status: "running" } },
        });
        await interrupted;
        yield JsonRpcMessage.fromDict({
          method: "turn/cancelled",
          params: { threadId: "thread-1", turn: { id: "turn-1", threadId: "thread-1", status: "cancelled" } },
        });
      })();
    }
    return (async function* () {
      for (const event of events) yield event;
    })();
  }

  async turnInterrupt(threadId: string, turnId: string): Promise<void> {
    this.interrupted = { threadId, turnId };
    this.releaseAfterInterrupt?.();
  }

  async request<T>(method: string, params?: unknown): Promise<T> {
    this.requests.push({ method, params: toJsonParams(params) });
    if (method === "model/list") {
      return {
        models: [{ id: "claude-opus-4-8", displayName: "Claude Opus 4.8", provider: "anthropic" }],
      } as T;
    }
    return {} as T;
  }

  async turnEnqueue(_threadId: string, input: unknown, sender: unknown): Promise<Record<string, unknown>> {
    this.turnStartInput = input;
    this.turnStartSender = sender;
    return { queuedInput: this.queuedInput };
  }
}

function createSdk(
  wire: FakeWire,
  approvalHandler?: ApprovalHandler,
  userInputHandler?: UserInputHandler,
): DotCraft {
  const ctor = DotCraft as unknown as new (
    wire: DotCraftWireClient,
    serverInfo: ServerInfo,
    capabilities: ServerCapabilities,
    approvalHandler?: ApprovalHandler,
    userInputHandler?: UserInputHandler,
  ) => DotCraft;
  return new ctor(
    wire as unknown as DotCraftWireClient,
    new ServerInfo("dotcraft", "test", "1.0"),
    new ServerCapabilities(),
    approvalHandler,
    userInputHandler,
  );
}

function toJsonParams(params: unknown): unknown {
  if (params === undefined) return undefined;
  return JSON.parse(JSON.stringify(params)) as unknown;
}

test("DotCraft thread start strips dynamic tool handlers and binds callbacks", async () => {
  const wire = new FakeWire();
  const sdk = createSdk(wire);
  const toolResult: DynamicToolCallResult = {
    success: true,
    structuredResult: { echoed: "hi" },
  };

  const thread = await sdk.threads.start({
    userId: "u",
    dynamicTools: [
      {
        namespace: "local",
        name: "Echo",
        description: "Echo input",
        inputSchema: { type: "object" },
        handler: async () => toolResult,
      },
    ],
  });

  const sentTools = wire.startParams?.dynamicTools as Array<Record<string, unknown>>;
  assert.equal(sentTools.length, 1);
  assert.equal(sentTools[0].name, "Echo");
  assert.equal("handler" in sentTools[0], false);

  const handler = wire.requestHandlers.get("item/tool/call");
  assert.ok(handler);
  assert.deepEqual(
    await handler("tool-req", {
      threadId: thread.id,
      turnId: "turn-1",
      callId: "call-1",
      namespace: "local",
      tool: "Echo",
      arguments: { text: "hi" },
    }),
    toolResult,
  );
  assert.deepEqual(
    await handler("tool-req-2", {
      threadId: thread.id,
      turnId: "turn-1",
      callId: "call-2",
      namespace: "local",
      tool: "Missing",
      arguments: {},
    }),
    {
      success: false,
      errorCode: "UnsupportedTool",
      errorMessage: "No handler registered for this dynamic tool.",
    },
  );
});

test("DotCraft default callbacks are non-blocking fallbacks", async () => {
  const wire = new FakeWire();
  createSdk(wire);

  assert.equal(await wire.approvalHandler?.("approval", {}), "accept");
  assert.deepEqual(await wire.requestHandlers.get("item/tool/requestUserInput")?.("input", {}), { answers: {} });
});

test("DotCraft uses explicit approval and user-input callbacks when provided", async () => {
  const wire = new FakeWire();
  createSdk(
    wire,
    async (): Promise<ApprovalDecision> => "decline",
    async () => ({ answers: { value: "42" } }),
  );

  assert.equal(await wire.approvalHandler?.("approval", {}), "decline");
  assert.deepEqual(await wire.requestHandlers.get("item/tool/requestUserInput")?.("input", {}), {
    answers: { value: "42" },
  });
});

test("DotCraftThread run registers stream capture before turn start and merges final text", async () => {
  const wire = new FakeWire();
  wire.events = [
    JsonRpcMessage.fromDict({
      method: "turn/started",
      params: { threadId: "thread-1", turn: { id: "turn-1", threadId: "thread-1", status: "running" } },
    }),
    JsonRpcMessage.fromDict({
      method: "item/started",
      params: { threadId: "thread-1", item: { id: "m1", type: "agentMessage" } },
    }),
    JsonRpcMessage.fromDict({
      method: "item/agentMessage/delta",
      params: { threadId: "thread-1", itemId: "m1", delta: "hello " },
    }),
    JsonRpcMessage.fromDict({
      method: "item/completed",
      params: { threadId: "thread-1", item: { id: "m1", type: "agentMessage", payload: { text: "hello world" } } },
    }),
    JsonRpcMessage.fromDict({
      method: "turn/completed",
      params: {
        threadId: "thread-1",
        turn: {
          id: "turn-1",
          threadId: "thread-1",
          status: "completed",
          items: [{ id: "m1", type: "agentMessage", payload: { text: "hello world" } }],
          tokenUsage: { inputTokens: 1 },
        },
      },
    }),
  ];
  const sdk = createSdk(wire);
  const thread = await sdk.threads.start({ userId: "u" });

  const result = await thread.run("hi", { collectRawEvents: true, sender: { senderId: "u" } });

  assert.deepEqual(wire.calls, ["streamEvents", "turnStart"]);
  assert.deepEqual(wire.turnStartInput, [{ type: "text", text: "hi" }]);
  assert.deepEqual(wire.turnStartSender, { senderId: "u" });
  assert.equal(result.text, "hello world");
  assert.deepEqual(result.usage, { inputTokens: 1 });
  assert.equal(result.rawEvents?.length, 5);
});

test("DotCraftThread run enqueues when requested and a turn is already active", async () => {
  const wire = new FakeWire();
  wire.turnStartError = new TurnInProgressError(ERR_TURN_IN_PROGRESS, "busy");
  const sdk = createSdk(wire);
  const thread = await sdk.threads.start({ userId: "u" });

  const result = await thread.run("later", { enqueueIfBusy: true, sender: { senderId: "u" } });

  assert.equal(result.turn, null);
  assert.deepEqual(result.queuedInput, { id: "queued-1" });
  assert.deepEqual(wire.turnStartInput, [{ type: "text", text: "later" }]);
  assert.deepEqual(wire.turnStartSender, { senderId: "u" });
});

test("DotCraftThread runStreamed interrupts the active turn on abort", async () => {
  const wire = new FakeWire();
  wire.cancelAfterInterrupt = true;
  const sdk = createSdk(wire);
  const thread = await sdk.threads.start({ userId: "u" });
  const abort = new AbortController();
  const seen: string[] = [];

  for await (const event of thread.runStreamed("stop soon", { abortSignal: abort.signal })) {
    seen.push(event.type);
    if (event.type === "turn_started") abort.abort();
  }

  assert.deepEqual(seen, ["turn_started", "cancelled"]);
  assert.deepEqual(wire.interrupted, { threadId: "thread-1", turnId: "turn-1" });
});

test("parseAppBindingHandoff extracts fields and validates scheme and appId", () => {
  const handoff = parseAppBindingHandoff(
    "oratorio://dotcraft/connect?app=com.dotharness.oratorio&request=req_1&token=tok_1&endpoint=ws://127.0.0.1:1234/x",
    { expectedScheme: "oratorio", expectedAppId: "com.dotharness.oratorio" },
  );
  assert.equal(handoff.operation, "connect");
  assert.equal(handoff.appId, "com.dotharness.oratorio");
  assert.equal(handoff.requestId, "req_1");
  assert.equal(handoff.requestToken, "tok_1");
  assert.equal(handoff.appServerUrl, "ws://127.0.0.1:1234/x");
});

test("parseAppBindingHandoff rejects an unexpected scheme", () => {
  assert.throws(() =>
    parseAppBindingHandoff("evil://dotcraft/connect?app=x&request=r&token=t", {
      expectedScheme: "oratorio",
    }),
  );
});

test("DotCraft models.list returns typed model info", async () => {
  const wire = new FakeWire();
  const sdk = createSdk(wire);
  const models = await sdk.models.list();
  assert.equal(models.length, 1);
  assert.equal(models[0].id, "claude-opus-4-8");
  assert.equal(models[0].displayName, "Claude Opus 4.8");
  assert.equal(models[0].provider, "anthropic");
});

test("DotCraft appBindings social helpers send expected JSON-RPC params", async () => {
  const wire = new FakeWire();
  const sdk = createSdk(wire);
  const socialTarget = {
    channelName: "qq",
    conversationKind: "group",
    conversationId: "123",
    deliveryTarget: "group:123",
    displayName: "QQ group 123",
    boundBy: {
      platformUserId: "456",
      displayName: "Alice",
    },
  };

  await sdk.appBindings.createSocialBindingRequest({
    threadId: "thread-1",
    channelName: "qq",
    requestedTools: ["SendMessageToBoundConversation"],
    displayHint: "QQ",
  });
  await sdk.appBindings.getBindingRequest({
    appId: "com.dotharness.channel.qq",
    bindCode: "482913",
    requestToken: "482913",
  });
  await sdk.appBindings.acceptSocialBinding({
    bindingRequestId: "request-1",
    requestToken: "482913",
    socialTarget,
  });
  await sdk.appBindings.resolveSocialBinding({
    channelName: "qq",
    conversationKind: "group",
    conversationId: "123",
  });
  await sdk.appBindings.enqueueThreadInput({
    bindingId: "binding-1",
    appId: "com.dotharness.channel.qq",
    grantId: "social:qq::group:123",
    input: [{ type: "text", text: "hello" }],
    displayText: "hello",
    triggerLabel: "qq message",
    triggerRefId: "qq::group:123",
    startPolicy: "runWhenIdle",
    sender: { senderId: "456", senderName: "Alice", groupId: "group:123" },
  });

  assert.deepEqual(wire.requests.map((request) => request.method), [
    "app/binding/request/create",
    "app/binding/request/get",
    "app/binding/accept",
    "app/socialBinding/resolve",
    "app/threadInput/enqueue",
  ]);
  assert.deepEqual(wire.requests[0]?.params, {
    threadId: "thread-1",
    appId: "com.dotharness.channel.qq",
    requestedScopes: ["conversation.receive", "message.send"],
    requestedTools: ["SendMessageToBoundConversation"],
    source: "sdk",
    bindingKind: "socialChannel",
    socialIntent: {
      channelName: "qq",
      targetSelection: "confirmInChannel",
      displayHint: "QQ",
    },
  });
  assert.deepEqual(wire.requests[1]?.params, {
    appId: "com.dotharness.channel.qq",
    bindCode: "482913",
    requestToken: "482913",
  });
  assert.deepEqual(wire.requests[2]?.params, {
    bindingRequestId: "request-1",
    requestToken: "482913",
    grantId: "social:qq::group:123",
    grantedScopes: ["conversation.receive", "message.send"],
    approvalMode: "channelBindCode",
    approvedBy: "456",
    socialTarget,
  });
  assert.deepEqual(wire.requests[3]?.params, {
    appId: "com.dotharness.channel.qq",
    channelName: "qq",
    conversationKind: "group",
    conversationId: "123",
  });
  assert.deepEqual(wire.requests[4]?.params, {
    bindingId: "binding-1",
    appId: "com.dotharness.channel.qq",
    grantId: "social:qq::group:123",
    input: [{ type: "text", text: "hello" }],
    displayText: "hello",
    triggerLabel: "qq message",
    triggerRefId: "qq::group:123",
    startPolicy: "runWhenIdle",
    sender: { senderId: "456", senderName: "Alice", groupId: "group:123" },
  });
});
