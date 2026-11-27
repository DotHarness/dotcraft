import assert from "node:assert/strict";
import test from "node:test";

import {
  DotCraft,
  type ApprovalDecision,
  type ApprovalHandler,
  type UserInputHandler,
} from "./dotcraft.js";
import { JsonRpcMessage } from "./models.js";
import type {
  DynamicToolCallResult,
  ServerCapabilities,
  ServerInfo,
  SessionThread,
  SessionTurn,
  ThreadResumeResult,
} from "./generated/appserver/index.js";
import {
  makeServerCapabilities,
  makeServerInfo,
  makeThread,
  makeTurn,
} from "./test-contract-fixtures.js";
import type { DotCraftWireClient, ServerRequestHandler } from "./client.js";

class FakeWire {
  readonly requestHandlers = new Map<string, ServerRequestHandler>();
  readonly calls: string[] = [];
  readonly requests: Array<{ method: string; params: unknown }> = [];
  readonly requestResults = new Map<string, unknown>();
  startParams: Record<string, unknown> | null = null;
  turnStartInput: unknown;
  turnStartSender: unknown;
  turnStartSettings: unknown;
  events: JsonRpcMessage[] = [];
  cancelAfterInterrupt = false;
  interrupted: { threadId: string; turnId: string } | null = null;
  private releaseAfterInterrupt: (() => void) | null = null;

  registerServerRequestHandler(method: string, handler: ServerRequestHandler): void {
    this.requestHandlers.set(method, handler);
  }

  async threadStart(params: Record<string, unknown>, onResponse?: (thread: SessionThread) => void): Promise<SessionThread> {
    this.startParams = params;
    const thread = makeThread("thread-1", "active", String(params.workspacePath ?? ""), String(params.userId ?? ""), String(params.channelName ?? ""));
    onResponse?.(thread);
    return thread;
  }

  async threadList(): Promise<SessionThread[]> {
    return [];
  }

  async threadListPage(): Promise<{ threads: SessionThread[]; nextCursor: string | null; totalMatched: number | null; raw: Record<string, unknown> }> {
    return { threads: [], nextCursor: null, totalMatched: 0, raw: { data: [] } };
  }

  async threadRead(threadId: string): Promise<SessionThread> {
    return makeThread(threadId, "active");
  }

  async threadResume(threadId: string, _params?: unknown, onResponse?: (result: ThreadResumeResult) => void): Promise<ThreadResumeResult> {
    const result = { thread: makeThread(threadId, "active"), currentState: { itemCount: 0, pendingRequests: [] }, instructionSources: [] };
    onResponse?.(result);
    return result;
  }

  async ensureThreadAttached(): Promise<undefined> { return undefined; }

  async turnStart(_threadId: string, input: unknown, sender: unknown, settings?: unknown): Promise<SessionTurn> {
    this.calls.push("turnStart");
    this.turnStartInput = input;
    this.turnStartSender = sender;
    this.turnStartSettings = settings;
    return makeTurn("turn-1", "thread-1", "running");
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

  async requestRaw<T>(method: string, params?: unknown): Promise<T> {
    this.requests.push({ method, params: toJsonParams(params) });
    if (this.requestResults.has(method)) {
      return this.requestResults.get(method) as T;
    }
    if (method === "model/list") {
      return {
        models: [{ id: "claude-opus-4-8", displayName: "Claude Opus 4.8", provider: "anthropic" }],
      } as T;
    }
    return {} as T;
  }

  async request<T>(method: string, params?: unknown): Promise<T> {
    return this.requestRaw<T>(method, params);
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
    makeServerInfo("dotcraft", "test", "1.0"),
    makeServerCapabilities(),
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
    contentItems: [{ type: "text", text: "Echoed hi" }],
    structuredContent: { echoed: "hi" },
  };

  const thread = await sdk.threads.start({
    userId: "u",
    dynamicTools: [
      {
        type: "namespace",
        name: "local",
        description: "Local tools",
        tools: [
          {
            type: "function",
            name: "Echo",
            description: "Echo input",
            inputSchema: { type: "object" },
            handler: async () => toolResult,
          },
        ],
      },
    ],
  });

  const sentTools = wire.startParams?.dynamicTools as Array<Record<string, unknown>>;
  assert.equal(sentTools.length, 1);
  assert.equal(sentTools[0].type, "namespace");
  assert.equal(sentTools[0].name, "local");
  const sentFunctions = sentTools[0].tools as Array<Record<string, unknown>>;
  assert.equal(sentFunctions[0].type, "function");
  assert.equal(sentFunctions[0].name, "Echo");
  assert.equal("handler" in sentFunctions[0], false);

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
    }, new AbortController().signal),
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
    }, new AbortController().signal),
    {
      success: false,
      errorCode: "UnsupportedTool",
      errorMessage: "No handler registered for this dynamic tool.",
    },
  );
});

test("MCP runtime manager uses the specified methods and wire fields", async () => {
  const wire = new FakeWire();
  const sdk = createSdk(wire);

  await sdk.mcpRuntime.listStatus({ threadId: "thread-1", cursor: "2", limit: 25, detail: "full" });
  await sdk.mcpRuntime.readResource({ threadId: "thread-1", server: "docs", uri: "docs://intro" });
  await sdk.mcpRuntime.callTool({
    threadId: "thread-1",
    server: "docs",
    tool: "search",
    arguments: { query: "MCP" },
    _meta: { trace: "t1" },
  });
  await sdk.mcpRuntime.loginOAuth({ name: "docs", threadId: "thread-1", scopes: ["read"], timeoutSecs: 60 });
  await sdk.mcpRuntime.reload();

  assert.deepEqual(wire.requests.slice(-5), [
    { method: "mcpServerStatus/list", params: { threadId: "thread-1", cursor: "2", limit: 25, detail: "full" } },
    { method: "mcpServer/resource/read", params: { threadId: "thread-1", server: "docs", uri: "docs://intro" } },
    {
      method: "mcpServer/tool/call",
      params: { threadId: "thread-1", server: "docs", tool: "search", arguments: { query: "MCP" }, _meta: { trace: "t1" } },
    },
    { method: "mcpServer/oauth/login", params: { name: "docs", threadId: "thread-1", scopes: ["read"], timeoutSecs: 60 } },
    { method: "config/mcpServer/reload", params: {} },
  ]);
});

test("DotCraft registers approval and user-input handlers only when callbacks are provided", async () => {
  const bare = new FakeWire();
  createSdk(bare);
  assert.equal(bare.requestHandlers.has("item/approval/request"), false);
  assert.equal(bare.requestHandlers.has("item/tool/requestUserInput"), false);

  const wire = new FakeWire();
  createSdk(
    wire,
    async (): Promise<ApprovalDecision> => "decline",
    async () => ({ answers: { value: "42" } }),
  );

  assert.deepEqual(await wire.requestHandlers.get("item/approval/request")?.("approval", {}, new AbortController().signal), {
    decision: "decline",
  });
  assert.deepEqual(await wire.requestHandlers.get("item/tool/requestUserInput")?.("input", {}, new AbortController().signal), {
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
      params: { threadId: "thread-1", turnId: "turn-1", item: { id: "m1", type: "agentMessage" } },
    }),
    JsonRpcMessage.fromDict({
      method: "item/agentMessage/delta",
      params: { threadId: "thread-1", turnId: "turn-1", itemId: "m1", delta: "hello " },
    }),
    JsonRpcMessage.fromDict({
      method: "item/completed",
      params: { threadId: "thread-1", turnId: "turn-1", item: { id: "m1", type: "agentMessage", payload: { text: "hello world" } } },
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
  assert.deepEqual(wire.turnStartSender, { senderId: "u", senderName: "u", senderRole: "user" });
  assert.equal(result.text, "hello world");
  assert.deepEqual(result.usage, { inputTokens: 1 });
  assert.equal(result.rawEvents?.length, 5);
});

test("DotCraftThread joins a turn with model settings without waiting for another started event", async () => {
  const wire = new FakeWire();
  wire.events = [
    JsonRpcMessage.fromDict({
      method: "item/agentMessage/delta",
      params: { threadId: "thread-1", turnId: "turn-1", itemId: "m1", delta: "after" },
    }),
    JsonRpcMessage.fromDict({
      method: "turn/completed",
      params: {
        threadId: "thread-1",
        turn: {
          ...makeTurn("turn-1", "thread-1", "completed"),
          items: [
            { id: "m0", type: "agentMessage", payload: { text: "before " } },
            { id: "m1", type: "agentMessage", payload: { text: "after" } },
          ],
        },
      },
    }),
  ];
  const sdk = createSdk(wire);
  const thread = await sdk.threads.start({ userId: "u" });
  const settings = { providerId: "provider-b", model: "model-b", reasoning: { enabled: true, effort: "high" }, speed: "fast", approvalPolicy: "autoApprove" };

  const result = await thread.run("continue", settings);

  assert.deepEqual(wire.calls, ["streamEvents", "turnStart"]);
  assert.deepEqual(wire.turnStartSettings, settings);
  assert.equal(result.turn?.id, "turn-1");
  assert.equal(result.text, "before after");
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
