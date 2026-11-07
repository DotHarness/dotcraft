import assert from "node:assert/strict";
import test from "node:test";
import { WebSocketServer } from "ws";

import { DotCraftWireClient } from "./client.js";
import { InternalAppServerClient } from "./appServerClient.js";
import { DotCraftThread } from "./thread.js";
import { DotCraft } from "./dotcraft.js";
import { makeThread, makeTurn } from "./test-contract-fixtures.js";
import { RequestTimeoutError, TurnInProgressError } from "./errors.js";
import { ERR_TURN_IN_PROGRESS } from "./models.js";
import { type Transport, TransportClosed, WebSocketTransport } from "./transport.js";

class QueueTransport implements Transport {
  readonly written: Record<string, unknown>[] = [];
  private readonly incoming: Record<string, unknown>[] = [];
  private readonly readers: Array<{
    resolve: (message: Record<string, unknown>) => void;
    reject: (error: unknown) => void;
  }> = [];
  private readonly writeWaiters: Array<() => void> = [];
  private closed = false;

  push(message: Record<string, unknown>): void {
    const reader = this.readers.shift();
    if (reader) {
      reader.resolve(message);
      return;
    }
    this.incoming.push(message);
  }

  async nextWrite(): Promise<Record<string, unknown>> {
    while (this.written.length === 0) {
      await new Promise<void>((resolve) => this.writeWaiters.push(resolve));
    }
    return this.written.shift()!;
  }

  async readMessage(): Promise<Record<string, unknown>> {
    if (this.closed) throw new TransportClosed();
    const queued = this.incoming.shift();
    if (queued) return queued;
    return await new Promise<Record<string, unknown>>((resolve, reject) => {
      this.readers.push({ resolve, reject });
    });
  }

  async writeMessage(message: Record<string, unknown>): Promise<void> {
    if (this.closed) throw new TransportClosed();
    this.written.push(message);
    this.writeWaiters.shift()?.();
  }

  async close(): Promise<void> {
    this.closed = true;
    for (const reader of this.readers.splice(0)) reader.reject(new TransportClosed());
  }
}

test("DotCraftWireClient correlates responses and maps common JSON-RPC errors", async () => {
  const transport = new QueueTransport();
  const client = new DotCraftWireClient(transport);
  await client.start();

  const first = client.requestRaw("alpha");
  const firstWrite = await transport.nextWrite();
  const second = client.requestRaw("beta");
  const secondWrite = await transport.nextWrite();

  transport.push({ jsonrpc: "2.0", id: secondWrite.id, result: { ok: "second" } });
  transport.push({
    jsonrpc: "2.0",
    id: firstWrite.id,
    error: { code: ERR_TURN_IN_PROGRESS, message: "busy" },
  });

  assert.deepEqual(await second, { ok: "second" });
  await assert.rejects(first, (error: unknown) => error instanceof TurnInProgressError);

  const noParams = client.requestRaw("config/mcpServer/reload");
  const noParamsWrite = await transport.nextWrite();
  assert.equal("params" in noParamsWrite, false);
  transport.push({ jsonrpc: "2.0", id: noParamsWrite.id, result: {} });
  assert.deepEqual(await noParams, {});

  await client.stop();
});

test("wire response hooks deliver success and errors before subsequent notifications", async () => {
  const transport = new QueueTransport();
  const client = new DotCraftWireClient(transport);
  const delivered: string[] = [];
  await client.start();
  let finish!: () => void;
  const finished = new Promise<void>((resolve) => { finish = resolve; });
  client.onRaw("fixture/event", (params) => {
    delivered.push(String(params.after));
    if (params.after === "after-error") finish();
  });
  const success = client.requestRaw("fixture/success", {}, undefined, {
    onResponse: () => delivered.push("response"),
  });
  const successWrite = await transport.nextWrite();
  const failure = client.requestRaw("fixture/error", {}, undefined, {
    onError: () => delivered.push("error"),
  });
  const failureWrite = await transport.nextWrite();
  const rejected = assert.rejects(failure, TurnInProgressError);
  transport.push({ id: successWrite.id, result: {} });
  transport.push({ method: "fixture/event", params: { after: "after-response" } });
  transport.push({ id: failureWrite.id, error: { code: ERR_TURN_IN_PROGRESS, message: "busy" } });
  transport.push({ method: "fixture/event", params: { after: "after-error" } });
  await Promise.all([success, rejected, finished]);
  assert.deepEqual(delivered, ["response", "after-response", "error", "after-error"]);
  const hookFailure = client.requestRaw("fixture/hook", {}, undefined, {
    onResponse: () => { throw new Error("hook failure"); },
  });
  const hookWrite = await transport.nextWrite();
  const hookRejected = assert.rejects(hookFailure, /hook failure/);
  transport.push({ id: hookWrite.id, result: {} });
  await hookRejected;
  await client.stop();
});

test("attached handles isolate interleaved feeds and resume after explicit connection-level detach", async () => {
  const transport = new QueueTransport();
  const client = new InternalAppServerClient(transport);
  await client.start();
  const start = client.threadStart({ channelName: "sdk", userId: "u" });
  const startWrite = await transport.nextWrite();
  transport.push({ id: startWrite.id, result: { thread: makeThread("A", "active") } });
  await start;
  const thread = new DotCraftThread({ wire: client } as DotCraft, makeThread("A", "active"), { channelName: "sdk", userId: "u" });
  const observer = client.streamEvents("A");
  const run = thread.run("hi", { collectRawEvents: true });
  const turnWrite = await transport.nextWrite();
  assert.equal(turnWrite.method, "turn/start");
  transport.push({ id: turnWrite.id, result: { turn: makeTurn("target", "A", "running") } });
  transport.push({ method: "turn/completed", params: { turn: makeTurn("B-turn", "B", "completed") } });
  transport.push({ method: "turn/failed", params: { turn: makeTurn("old", "A", "failed") } });
  transport.push({ method: "item/agentMessage/delta", params: { threadId: "A", turnId: "old", delta: "wrong" } });
  transport.push({ method: "item/agentMessage/delta", params: { threadId: "B", turnId: "target", delta: "wrong" } });
  transport.push({ method: "item/agentMessage/delta", params: { threadId: "A", turnId: "target", delta: "correct" } });
  transport.push({ method: "turn/completed", params: { turn: { ...makeTurn("target", "A", "completed"), tokenUsage: { inputTokens: 7 } } } });
  const read = await transport.nextWrite();
  assert.equal(read.method, "thread/read");
  transport.push({ id: read.id, result: { thread: makeThread("A", "active") } });
  const result = await run;
  assert.equal(result.text, "correct");
  assert.equal(result.usage?.inputTokens, 7);
  assert.equal(result.rawEvents?.length, 2);
  const observed = [];
  for (let i = 0; i < 4; i++) observed.push((await observer.next()).value);
  assert.equal(observed[0]?.method, "turn/failed");
  assert.equal(observed.at(-1)?.method, "turn/completed");
  transport.push({ method: "item/agentMessage/delta", params: { threadId: "A", turnId: "later", delta: "still observing" } });
  assert.equal((await observer.next()).value?.params?.delta, "still observing");
  await observer.return?.();
  const pending = observer.next();
  assert.equal((await pending).done, true);

  const detach = thread.unsubscribe();
  const detachWrite = await transport.nextWrite();
  transport.push({ id: detachWrite.id, result: {} });
  await detach;
  const iterator = thread.runStreamed("again")[Symbol.asyncIterator]();
  const next = iterator.next();
  const resumeWrite = await transport.nextWrite();
  assert.equal(resumeWrite.method, "thread/resume");
  transport.push({ id: resumeWrite.id, result: { thread: makeThread("A", "active"), currentState: { itemCount: 0, pendingRequests: [] }, instructionSources: [] } });
  const nextTurnWrite = await transport.nextWrite();
  assert.equal(nextTurnWrite.method, "turn/start");
  transport.push({ id: nextTurnWrite.id, result: { turn: makeTurn("next", "A", "running") } });
  transport.push({ method: "turn/started", params: { turn: makeTurn("next", "A", "running") } });
  assert.equal((await next).value?.type, "turn_started");
  await iterator.return?.();
  const closeWait = client.streamEvents("A");
  const waiting = closeWait.next();
  await closeWait.return?.();
  assert.equal((await waiting).done, true);
  await client.stop();
});

test("DotCraftWireClient applies the default request timeout", async () => {
  const transport = new QueueTransport();
  const client = new DotCraftWireClient(transport, { defaultTimeoutMs: 5 });
  await client.start();

  await assert.rejects(client.requestRaw("fixture/timeout"), RequestTimeoutError);
  await client.stop();
});

test("DotCraftWireClient reinitializes before releasing reconnect-queued requests", async () => {
  const server = new WebSocketServer({ port: 0 });
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Expected a TCP listener address.");
  const port = address.port;
  let connectionCount = 0;
  server.on("connection", (socket) => {
    connectionCount += 1;
    const connectionNumber = connectionCount;
    socket.on("message", (data) => {
      const message = JSON.parse(data.toString()) as Record<string, unknown>;
      if (message.method === "initialize") {
        socket.send(JSON.stringify({
          jsonrpc: "2.0",
          id: message.id,
          result: {
            serverInfo: { name: "fixture", version: "1", protocolVersion: "1" },
            capabilities: {},
          },
        }));
      } else if (message.method === "initialized" && connectionNumber === 1) {
        socket.close();
      } else if (message.method === "fixture/queued") {
        socket.send(JSON.stringify({ jsonrpc: "2.0", id: message.id, result: { ok: true } }));
      }
    });
  });

  const transport = new WebSocketTransport({ url: `ws://127.0.0.1:${port}` });
  const client = new DotCraftWireClient(transport, {
    autoReconnect: true,
    reconnectBaseDelayMs: 1,
    reconnectMaxDelayMs: 1,
    random: () => 0.5,
  });
  const reconnecting = new Promise<void>((resolve) => {
    client.onStateChanged((state) => {
      if (state === "reconnecting") resolve();
    });
  });
  await client.connect();
  await client.initialize({ clientName: "fixture", clientVersion: "1" });
  await reconnecting;
  assert.deepEqual(await client.requestRaw("fixture/queued", {}), { ok: true });
  assert.equal(connectionCount, 2);

  await client.stop();
  await new Promise<void>((resolve) => server.close(() => resolve()));
});

test("a reconnected SDK handle resumes with its bindings before running again", async (t) => {
  const server = new WebSocketServer({ port: 0 });
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Expected TCP listener.");
  const methods: string[] = [];
  let connections = 0;
  let toolReply!: (value: unknown) => void;
  const toolResult = new Promise<unknown>((resolve) => { toolReply = resolve; });
  server.on("connection", (socket) => {
    connections++;
    socket.on("message", (data) => {
      const message = JSON.parse(data.toString()) as Record<string, unknown>;
      if (message.method) methods.push(String(message.method));
      const reply = (result: unknown): void => { socket.send(JSON.stringify({ id: message.id, result })); };
      if (message.method === "initialize") {
        reply({ serverInfo: { name: "fixture", version: "1", protocolVersion: "1" }, capabilities: {} });
      } else if (message.method === "thread/start") {
        reply({ thread: makeThread("A", "active") });
        socket.send(JSON.stringify({ id: "tool-call", method: "item/tool/call", params: { threadId: "A", turnId: "old", tool: "Echo", arguments: {} } }));
      } else if (message.id === "tool-call") {
        toolReply(message.result);
      } else if (message.method === "fixture/disconnect") {
        socket.close();
      } else if (message.method === "thread/resume") {
        assert.equal(((message.params as Record<string, unknown>).dynamicTools as unknown[])?.length, 1);
        reply({ thread: makeThread("A", "active"), currentState: { itemCount: 0, pendingRequests: [] }, instructionSources: [] });
      } else if (message.method === "turn/start") {
        reply({ turn: makeTurn("target", "A", "running") });
        socket.send(JSON.stringify({ method: "item/agentMessage/delta", params: { threadId: "A", turnId: "target", delta: "restored" } }));
        socket.send(JSON.stringify({ method: "turn/completed", params: { turn: makeTurn("target", "A", "completed") } }));
      } else if (message.method === "thread/read") {
        reply({ thread: makeThread("A", "active") });
      }
    });
  });
  const sdk = await DotCraft.remote({ url: `ws://127.0.0.1:${address.port}` });
  t.after(async () => { await sdk.close(); await new Promise<void>((resolve) => server.close(() => resolve())); });
  const thread = await sdk.threads.start({
    dynamicTools: [{ type: "function", name: "Echo", description: "Echo", inputSchema: {}, handler: () => ({ success: true }) }],
  });
  assert.deepEqual(await toolResult, { success: true });
  const reconnected = new Promise<void>((resolve) => {
    sdk.wire.onStateChanged((state) => { if (connections > 1 && state === "ready") resolve(); });
  });
  const disconnected = assert.rejects(sdk.requestRaw("fixture/disconnect"), TransportClosed);
  await Promise.all([disconnected, reconnected]);
  assert.equal((await thread.run("again")).text, "restored");
  assert.ok(methods.indexOf("thread/resume") < methods.indexOf("turn/start"));
  assert.equal(connections, 2);
});

test("DotCraftWireClient reconnects after an oversized message closes the socket", async () => {
  const server = new WebSocketServer({ port: 0 });
  await new Promise<void>((resolve) => server.once("listening", resolve));
  const address = server.address();
  if (!address || typeof address === "string") throw new Error("Expected a TCP listener address.");
  let connectionCount = 0;
  server.on("connection", (socket) => {
    connectionCount += 1;
    socket.on("message", (data) => {
      const message = JSON.parse(data.toString()) as Record<string, unknown>;
      if (message.method === "initialize") {
        socket.send(JSON.stringify({
          jsonrpc: "2.0",
          id: message.id,
          result: {
            serverInfo: { name: "fixture", version: "1", protocolVersion: "1" },
            capabilities: {},
          },
        }));
      } else if (message.method === "fixture/large") {
        socket.send(JSON.stringify({ jsonrpc: "2.0", id: message.id, result: { data: "x".repeat(4096) } }));
      } else if (message.method === "fixture/small") {
        socket.send(JSON.stringify({ jsonrpc: "2.0", id: message.id, result: { ok: true } }));
      }
    });
  });

  const transport = new WebSocketTransport({ url: `ws://127.0.0.1:${address.port}`, maxPayload: 1024 });
  const client = new DotCraftWireClient(transport, {
    autoReconnect: true,
    reconnectBaseDelayMs: 1,
    reconnectMaxDelayMs: 1,
    random: () => 0.5,
  });
  const states: string[] = [];
  client.onStateChanged((state) => states.push(state));
  await client.connect();
  await client.initialize({ clientName: "fixture", clientVersion: "1" });

  await assert.rejects(client.requestRaw("fixture/large", {}), TransportClosed);
  assert.deepEqual(await client.requestRaw("fixture/small", {}), { ok: true });
  assert.ok(states.includes("disconnected"));
  assert.equal(connectionCount, 2);

  await client.stop();
  await new Promise<void>((resolve) => server.close(() => resolve()));
});

test("DotCraftWireClient dispatches server requests without blocking the reader", async () => {
  const transport = new QueueTransport();
  const client = new DotCraftWireClient(transport);
  const calls: string[] = [];
  await client.start();

  client.registerServerRequestHandler("item/approval/request", async () => {
    calls.push("approval");
    return { decision: "decline" };
  });
  client.registerServerRequestHandler("item/tool/call", async (_id, params) => {
    calls.push(String(params.tool));
    return {
      success: true,
      contentItems: [{ type: "text", text: "Tool completed" }],
      structuredContent: params.arguments,
    };
  });

  transport.push({ jsonrpc: "2.0", id: "approval-1", method: "item/approval/request", params: {} });
  transport.push({
    jsonrpc: "2.0",
    id: "tool-1",
    method: "item/tool/call",
    params: { tool: "Echo", arguments: { text: "hi" } },
  });

  assert.deepEqual(await transport.nextWrite(), {
    jsonrpc: "2.0",
    id: "approval-1",
    result: { decision: "decline" },
  });
  assert.deepEqual(await transport.nextWrite(), {
    jsonrpc: "2.0",
    id: "tool-1",
    result: {
      success: true,
      contentItems: [{ type: "text", text: "Tool completed" }],
      structuredContent: { text: "hi" },
    },
  });
  assert.deepEqual(calls, ["approval", "Echo"]);

  await client.stop();
});

test("DotCraftWireClient initialize sends initialized notification", async () => {
  const transport = new QueueTransport();
  const client = new DotCraftWireClient(transport);
  client.registerServerRequestHandler("item/tool/requestUserInput", async () => ({ answers: {} }));

  const initialized = client.initialize({
    clientName: "test-client",
    clientVersion: "1.0.0",
    requestUserInputSupport: true,
    configChange: true,
  });
  const request = await transport.nextWrite();
  assert.equal(request.method, "initialize");
  assert.deepEqual((request.params as Record<string, unknown>).clientInfo, {
    name: "test-client",
    version: "1.0.0",
  });

  transport.push({
    jsonrpc: "2.0",
    id: request.id,
    result: {
      serverInfo: { name: "dotcraft", version: "test", protocolVersion: "1.0" },
      capabilities: { requestUserInput: true, configOverride: true },
    },
  });

  const notification = await transport.nextWrite();
  assert.equal(notification.method, "initialized");
  assert.equal((await initialized).serverInfo.name, "dotcraft");

  await client.stop();
});
