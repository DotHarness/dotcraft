import assert from "node:assert/strict";
import test from "node:test";
import { WebSocketServer } from "ws";

import { DotCraftWireClient, type ServerRequestHandler } from "./client.js";
import { InternalAppServerClient } from "./appServerClient.js";
import { TurnInProgressError } from "./errors.js";
import { RequestTimeoutError } from "./errors.js";
import { ERR_TURN_IN_PROGRESS } from "./models.js";
import { type Transport, TransportClosed } from "./transport.js";
import { WebSocketTransport } from "./transport.js";

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

function assertWireSurfaceIsProtocolOnly(client: DotCraftWireClient): void {
  if (false) {
    // @ts-expect-error Thread helpers belong to InternalAppServerClient.
    void client.threadStart({ channelName: "test", userId: "user" });
  }
}

test("DotCraftWireClient excludes high-level Thread operations", () => {
  const client = new DotCraftWireClient(new QueueTransport());
  assertWireSurfaceIsProtocolOnly(client);
  assert.equal("threadStart" in client, false);
  assert.equal("streamEvents" in client, false);
});

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

test("DotCraftWireClient omits params when a request has no parameters", async () => {
  const transport = new QueueTransport();
  const client = new DotCraftWireClient(transport);
  await client.start();

  const pending = client.requestRaw("config/mcpServer/reload");
  const written = await transport.nextWrite();
  assert.equal("params" in written, false);
  transport.push({ jsonrpc: "2.0", id: written.id, result: {} });
  assert.deepEqual(await pending, {});

  await client.stop();
});

test("InternalAppServerClient sends workspace scope for thread list requests", async () => {
  const transport = new QueueTransport();
  const client = new InternalAppServerClient(transport);
  await client.start();

  const pending = client.threadListPage({
    channelName: "desktop",
    userId: "local-user",
    workspacePath: "C:\\workspace",
    scope: "workspace",
  });
  const written = await transport.nextWrite();
  assert.equal(written.method, "thread/list");
  assert.equal((written.params as Record<string, unknown>).scope, "workspace");

  transport.push({
    jsonrpc: "2.0",
    id: written.id,
    result: { data: [], nextCursor: null, totalMatched: 0 },
  });
  assert.deepEqual((await pending).data, []);

  await client.stop();
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
