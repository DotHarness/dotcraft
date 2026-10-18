import assert from "node:assert/strict";
import test from "node:test";

import { ERR_TURN_IN_PROGRESS, JsonRpcError, JsonRpcMessage, type Transport } from "@dotcraft/sdk/wire";
import type { SessionThread, ThreadRuntimeState } from "@dotcraft/sdk/contracts";

import { ChannelAdapter, type ChannelAdapterMessageOpts } from "./adapter.js";
import type { ChannelChatDescriptor, ChannelDeliveryCapabilities } from "./capability.js";
import type { ChannelTurn, ChannelTurnOutcome } from "./turnStream.js";
import { makeThread, makeTurn } from "./test-contract-fixtures.js";

class NoopTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new Error("Not implemented in tests");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {}

  async close(): Promise<void> {}
}

class ControlledStream implements AsyncIterableIterator<JsonRpcMessage> {
  private readonly queue: JsonRpcMessage[] = [];
  private waiter: (() => void) | null = null;
  closed = false;

  push(method: string, params: Record<string, unknown> = {}): void {
    this.queue.push(JsonRpcMessage.fromDict({ method, params }));
    this.waiter?.();
    this.waiter = null;
  }

  async next(): Promise<IteratorResult<JsonRpcMessage>> {
    while (this.queue.length === 0) {
      if (this.closed) return { value: undefined, done: true };
      await new Promise<void>((resolve) => { this.waiter = resolve; });
    }
    return { value: this.queue.shift()!, done: false };
  }

  async return(): Promise<IteratorResult<JsonRpcMessage>> {
    this.closed = true;
    this.waiter?.();
    return { value: undefined, done: true };
  }

  [Symbol.asyncIterator](): AsyncIterableIterator<JsonRpcMessage> {
    return this;
  }
}

class TurnAdapter extends ChannelAdapter {
  readonly deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];
  readonly hooks: string[] = [];
  readonly calls: string[] = [];
  readonly stream = new ControlledStream();
  thread: SessionThread = makeThread("thread-1", "active");
  private endedResolve: (() => void) | null = null;
  readonly ended = new Promise<void>((resolve) => { this.endedResolve = resolve; });

  constructor() {
    super(new NoopTransport(), "test-channel", "test-client", "0.0.0");
    const client = this.client as unknown as Record<string, unknown>;
    client.streamEvents = () => this.stream;
    client.turnStart = async (threadId: string) => {
      this.calls.push(`start:${threadId}`);
      return makeTurn("turn-new", threadId, "running");
    };
    client.turnSteer = async (threadId: string, expectedTurnId: string) => {
      this.calls.push(`steer:${threadId}:${expectedTurnId}`);
      return expectedTurnId;
    };
    client.turnEnqueue = async (threadId: string) => {
      this.calls.push(`enqueue:${threadId}`);
      return {};
    };
    client.threadRead = async () => this.thread;
    client.request = async (method: string) => {
      if (method === "app/channelBinding/resolve") return { binding: null };
      throw new Error(`unexpected request ${method}`);
    };
  }

  async onDeliver(target: string, content: string, metadata: Record<string, unknown>): Promise<boolean> {
    this.deliveries.push({ target, content, metadata });
    return true;
  }

  async onApprovalRequest(): Promise<string> {
    return "cancel";
  }

  protected getDisplayName(): string {
    return "Test";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return { text: { format: "plain" }, targets: { group: false, direct: "none" } };
  }

  protected getChatDescriptor(): ChannelChatDescriptor {
    return { kind: "group", membership: "shared" };
  }

  protected override async getOrCreateThread(): Promise<SessionThread> {
    return this.thread;
  }

  protected override async onTurnStarted(turn: ChannelTurn): Promise<void> {
    this.hooks.push(`started:${turn.turnId}`);
  }

  protected override async onTurnRetrying(turn: ChannelTurn): Promise<void> {
    this.hooks.push(`retrying:${turn.turnId}`);
  }

  protected override async onTurnEnded(turn: ChannelTurn, outcome: ChannelTurnOutcome): Promise<void> {
    this.hooks.push(`ended:${turn.turnId}:${outcome.status}`);
    await super.onTurnEnded(turn, outcome);
    this.endedResolve?.();
  }

  async send(text: string): Promise<void> {
    const opts: ChannelAdapterMessageOpts = { userId: "u", userName: "Tester", text, channelContext: "chat-1" };
    await (this as unknown as {
      processMessage: (identityKey: string, opts: ChannelAdapterMessageOpts) => Promise<void>;
    }).processMessage("u:chat-1", opts);
  }

  setRuntime(runtime: ThreadRuntimeState): void {
    this.thread = { ...this.thread, runtime };
  }
}

function quietly<T>(run: () => Promise<T>): Promise<T> {
  const { error, info, warn } = console;
  console.error = () => {};
  console.info = () => {};
  console.warn = () => {};
  return run().finally(() => {
    console.error = error;
    console.info = info;
    console.warn = warn;
  });
}

test("a message to an idle thread starts a Turn and returns before the Turn ends", async () => {
  const adapter = new TurnAdapter();

  await adapter.send("hello");

  assert.deepEqual(adapter.calls, ["start:thread-1"]);
  assert.deepEqual(adapter.hooks, ["started:turn-new"]);

  adapter.stream.push("item/started", { item: { id: "a", type: "agentMessage" } });
  adapter.stream.push("item/completed", { item: { id: "a", type: "agentMessage", payload: { text: "Reply text" } } });
  adapter.stream.push("system/event", { turnId: "turn-new", kind: "streamError" });
  adapter.stream.push("item/completed", { item: { id: "img", type: "imageGeneration", payload: { status: "completed", result: "aGk=" } } });
  adapter.stream.push("turn/completed", { turn: { id: "turn-new", items: [{ id: "a", type: "agentMessage", payload: { text: "Reply text" } }] } });
  await quietly(() => adapter.ended);

  assert.deepEqual(adapter.hooks, ["started:turn-new", "retrying:turn-new", "ended:turn-new:completed"]);
  assert.deepEqual(adapter.deliveries, []);
});

test("a message during an active Turn is steered into it", async () => {
  const adapter = new TurnAdapter();
  adapter.setRuntime({ running: true, activeTurnId: "turn-active" });

  await adapter.send("also check the logs");

  assert.deepEqual(adapter.calls, ["steer:thread-1:turn-active"]);
  assert.deepEqual(adapter.hooks, []);
});

test("a message the active Turn cannot take is queued", async () => {
  const adapter = new TurnAdapter();
  adapter.setRuntime({ running: true, activeTurnId: "turn-active" });
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  client.turnSteer = async () => {
    throw new JsonRpcError(-32603, "Thread is running maintenance and cannot be steered.");
  };

  await quietly(() => adapter.send("next"));

  assert.deepEqual(adapter.calls, ["enqueue:thread-1"]);
  assert.deepEqual(adapter.deliveries, []);
});

test("a message whose Turn ended before steering starts a new Turn", async () => {
  const adapter = new TurnAdapter();
  adapter.setRuntime({ running: true, activeTurnId: "turn-active" });
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  client.turnSteer = async () => {
    adapter.setRuntime({ running: false });
    throw new JsonRpcError(-32603, "No active Turn.");
  };

  await adapter.send("next");

  assert.deepEqual(adapter.calls, ["start:thread-1"]);
});

test("a message during thread maintenance is queued without steering", async () => {
  const adapter = new TurnAdapter();
  adapter.setRuntime({ busy: true, maintenanceKind: "compacting" });

  await adapter.send("next");

  assert.deepEqual(adapter.calls, ["enqueue:thread-1"]);
});

test("a Turn that starts first from another sender receives the message as guidance", async () => {
  const adapter = new TurnAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  client.turnStart = async () => {
    adapter.calls.push("start:thread-1");
    throw new JsonRpcError(ERR_TURN_IN_PROGRESS, "Turn in progress");
  };
  client.threadRead = async () => ({ ...adapter.thread, runtime: { running: true, activeTurnId: "turn-other" } });

  await adapter.send("me too");

  assert.deepEqual(adapter.calls, ["start:thread-1", "steer:thread-1:turn-other"]);
  assert.equal(adapter.stream.closed, true);
  assert.deepEqual(adapter.deliveries, []);
});

test("a failed Turn sends one generic notice without diagnostics", async () => {
  const adapter = new TurnAdapter();

  await adapter.send("hello");
  adapter.stream.push("turn/failed", { turn: { id: "turn-new", error: "HTTP 500 from provider secret-host" } });
  await quietly(() => adapter.ended);

  assert.equal(adapter.deliveries.length, 1);
  assert.equal(adapter.deliveries[0]?.target, "chat-1");
  assert.equal(adapter.deliveries[0]?.metadata.failureCode, "channelTurnFailed");
  assert.equal(adapter.deliveries[0]?.content.includes("secret-host"), false);
});

test("a cancelled Turn sends no notice", async () => {
  const adapter = new TurnAdapter();

  await adapter.send("hello");
  adapter.stream.push("turn/cancelled", { turn: { id: "turn-new" } });
  await quietly(() => adapter.ended);

  assert.deepEqual(adapter.hooks, ["started:turn-new", "ended:turn-new:cancelled"]);
  assert.deepEqual(adapter.deliveries, []);
});

test("an event stream that fails after the Turn started sends no notice", async () => {
  const adapter = new TurnAdapter();
  adapter.stream.next = async () => {
    throw new Error("stream failed after turn start");
  };

  await quietly(async () => {
    await adapter.send("hello");
    await adapter.ended;
  });

  assert.deepEqual(adapter.hooks, ["started:turn-new", "ended:turn-new:disconnected"]);
  assert.deepEqual(adapter.deliveries, []);
});
