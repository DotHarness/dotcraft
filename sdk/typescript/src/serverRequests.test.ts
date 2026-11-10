import assert from "node:assert/strict";
import test from "node:test";
import { DotCraftWireClient } from "./client.js";
import { type Transport, TransportClosed } from "./transportCore.js";

function deferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void;
  const promise = new Promise<T>((done) => { resolve = done; });
  return { promise, resolve };
}

class RequestTransport implements Transport {
  private readonly incoming: Array<Record<string, unknown> | Error> = [];
  private readonly readers: Array<{ resolve: (value: Record<string, unknown>) => void; reject: (error: Error) => void }> = [];
  private readonly writes: Record<string, unknown>[] = [];
  private readonly writeReaders: Array<(value: Record<string, unknown>) => void> = [];
  connection = 0;

  async connect(): Promise<void> { this.connection++; }

  push(message: Record<string, unknown> | Error): void {
    const reader = this.readers.shift();
    if (!reader) this.incoming.push(message);
    else if (message instanceof Error) reader.reject(message);
    else reader.resolve(message);
  }

  async readMessage(): Promise<Record<string, unknown>> {
    const message = this.incoming.shift();
    if (message instanceof Error) throw message;
    if (message) return message;
    return new Promise((resolve, reject) => this.readers.push({ resolve, reject }));
  }

  async writeMessage(message: Record<string, unknown>): Promise<void> {
    const value = { ...message, connection: this.connection };
    const reader = this.writeReaders.shift();
    if (reader) reader(value);
    else this.writes.push(value);
  }

  async nextWrite(): Promise<Record<string, unknown>> {
    return this.writes.shift() ?? new Promise((resolve) => this.writeReaders.push(resolve));
  }

  async close(): Promise<void> {
    for (const reader of this.readers.splice(0)) reader.reject(new TransportClosed());
  }
}

function request(id: number, threadId = "A") {
  return { id, method: "mcpServer/elicitation/request", params: { threadId, serverName: "fixture", mode: "form" } };
}

async function handshake(client: DotCraftWireClient, transport: RequestTransport): Promise<void> {
  const initializing = client.initialize({ clientName: "fixture", clientVersion: "1" });
  await replyToInitialize(transport);
  await initializing;
}

async function replyToInitialize(transport: RequestTransport): Promise<void> {
  const initialize = await transport.nextWrite();
  assert.equal(initialize.method, "initialize");
  transport.push({ id: initialize.id, result: { serverInfo: { name: "fixture", version: "1" }, capabilities: {} } });
  assert.equal((await transport.nextWrite()).method, "initialized");
}

test("server request answers remain independent and may complete in reverse order", async (t) => {
  const transport = new RequestTransport();
  const client = new DotCraftWireClient(transport);
  t.after(() => client.stop());
  const first = deferred<unknown>();
  const second = deferred<unknown>();
  const started = deferred<void>();
  const signals: AbortSignal[] = [];
  client.registerServerRequestHandlerRaw("mcpServer/elicitation/request", (id, _params, signal) => {
    signals.push(signal);
    if (signals.length === 2) started.resolve();
    return id === 1 ? first.promise : second.promise;
  });
  await client.start();
  transport.push(request(1));
  transport.push(request(2, "B"));
  await started.promise;
  second.resolve({ action: "decline" });
  assert.equal((await transport.nextWrite()).id, 2);
  first.resolve({ action: "accept" });
  assert.equal((await transport.nextWrite()).id, 1);
  assert.equal(signals.some((signal) => signal.aborted), false);
});

test("resolved cancels exactly its handler in wire order and suppresses a late reply", async (t) => {
  const transport = new RequestTransport();
  const client = new DotCraftWireClient(transport);
  t.after(() => client.stop());
  const late = deferred<unknown>();
  const started = deferred<AbortSignal>();
  const observed = deferred<void>();
  let firstSignal!: AbortSignal;
  client.registerServerRequestFallbackRaw((_method, id, _params, signal) => {
    if (id === 1) {
      started.resolve(signal);
      return late.promise;
    }
    assert.equal(firstSignal.aborted, true);
    assert.equal(signal.aborted, false);
    return { action: "decline" };
  });
  client.onRaw("serverRequest/resolved", () => {
    assert.equal(firstSignal.aborted, true);
    observed.resolve();
  });
  await client.start();
  transport.push(request(1));
  firstSignal = await started.promise;
  transport.push({ method: "serverRequest/resolved", params: { threadId: "A", requestId: 1 } });
  transport.push(request(2));
  await observed.promise;
  late.resolve({ action: "accept" });
  await late.promise;
  assert.equal((await transport.nextWrite()).id, 2);
  transport.push(request(3));
  assert.equal((await transport.nextWrite()).id, 3);
});

test("disconnect cancels old handlers and their results never reach a reconnected transport", async (t) => {
  const transport = new RequestTransport();
  const client = new DotCraftWireClient(transport, {
    autoReconnect: true, reconnectBaseDelayMs: 1, reconnectMaxDelayMs: 1, random: () => 0.5,
  });
  t.after(() => client.stop());
  const late = deferred<unknown>();
  const replacement = deferred<unknown>();
  const started = deferred<AbortSignal>();
  const replacementStarted = deferred<AbortSignal>();
  const closed = deferred<void>();
  let first = true;
  client.registerServerRequestHandlerRaw("mcpServer/elicitation/request", (id, _params, signal) => {
    if (id !== 1) return { action: "decline" };
    if (first) {
      first = false;
      started.resolve(signal);
      return late.promise;
    }
    replacementStarted.resolve(signal);
    return replacement.promise;
  });
  client.onStateChanged((state) => { if (state === "disconnected") closed.resolve(); });
  await client.connect();
  await handshake(client, transport);
  transport.push(request(1));
  const signal = await started.promise;
  transport.push(new TransportClosed());
  await closed.promise;
  assert.equal(signal.aborted, true);
  await replyToInitialize(transport);
  transport.push(request(1));
  const replacementSignal = await replacementStarted.promise;
  late.resolve({ action: "accept" });
  await late.promise;
  const replacementCancelled = new Promise<void>((resolve) => replacementSignal.addEventListener("abort", () => resolve(), { once: true }));
  transport.push({ method: "serverRequest/resolved", params: { threadId: "A", requestId: 1 } });
  await replacementCancelled;
  replacement.resolve({ action: "accept" });
  await replacement.promise;
  transport.push(request(2));
  const response = await transport.nextWrite();
  assert.equal(response.id, 2);
  assert.equal(response.connection, 2);
  transport.push(request(3));
  assert.equal((await transport.nextWrite()).id, 3);
});
