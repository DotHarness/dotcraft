import assert from "node:assert/strict";
import test from "node:test";

import { JsonRpcError, type Transport } from "@dotcraft/sdk/wire";
import type { CommandExecuteResult, CommandInfo, SessionThread } from "@dotcraft/sdk/contracts";

import { ChannelAdapter, type ChannelAdapterMessageOpts } from "./adapter.js";
import type { ChannelChatDescriptor, ChannelDeliveryCapabilities } from "./capability.js";
import type { ChannelAppServerClient } from "./channelAppServerClient.js";
import { CommandRouter } from "./commandRouter.js";
import { makeThread, makeTurn } from "./test-contract-fixtures.js";

class NoopTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new Error("Not implemented in tests");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {}

  async close(): Promise<void> {}
}

function deferred(): { promise: Promise<void>; resolve: () => void } {
  let resolve!: () => void;
  const promise = new Promise<void>((res) => { resolve = res; });
  return { promise, resolve };
}

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let i = 0; i < 100; i += 1) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  assert.equal(predicate(), true);
}

const CHAT: ChannelChatDescriptor = { kind: "group", label: "Team", membership: "sender" };

class CommandAdapter extends ChannelAdapter {
  readonly deliveries: string[] = [];
  readonly threadChanges: Array<[string | undefined, string, string]> = [];
  readonly executed: Array<Record<string, unknown>> = [];
  readonly resumed: Array<{ threadId: string; params?: Record<string, unknown> }> = [];
  readonly turnStarts: Array<{ threadId: string; input: unknown }> = [];
  commands: CommandInfo[] = [
    { name: "/new", aliases: [] },
    { name: "/stop", aliases: [] },
    { name: "/model", aliases: [], requiresAdmin: true },
    { name: "/summarize", aliases: [] },
  ];
  result: CommandExecuteResult | Error = { handled: true };

  constructor() {
    super(new NoopTransport(), "test-channel", "test-client", "0.0.0");
    (this as unknown as { running: boolean }).running = true;
    const client = this.client as unknown as Record<string, unknown>;
    client.commandList = async () => this.commands;
    client.commandExecute = async (params: Record<string, unknown>) => {
      this.executed.push(params);
      if (this.result instanceof Error) throw this.result;
      return this.result;
    };
    client.threadRead = async (threadId: string) => makeThread(threadId, "active");
    client.threadList = async () => [];
    client.threadStart = async () => makeThread("thread-1", "active");
    client.threadResume = async (threadId: string, params?: Record<string, unknown>) => {
      this.resumed.push({ threadId, params });
      return makeThread(threadId, "active");
    };
    client.turnStart = async (threadId: string, input: unknown) => {
      this.turnStarts.push({ threadId, input });
      return makeTurn(`turn-${this.turnStarts.length}`, threadId, "running");
    };
    client.streamEvents = () => (async function* () {})();
    client.request = async (method: string) => {
      if (method === "app/channelBinding/resolve") return { binding: null };
      throw new Error(`unexpected request ${method}`);
    };
  }

  async onDeliver(_target: string, content: string): Promise<boolean> {
    this.deliveries.push(content);
    return true;
  }

  async onApprovalRequest(): Promise<string> {
    return "cancel";
  }

  protected getDisplayName(): string {
    return "Test";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return { text: { format: "plain" }, targets: { group: true, direct: "none" } };
  }

  protected getChatDescriptor(): ChannelChatDescriptor {
    return CHAT;
  }

  protected override onCurrentThreadChanged(previousThreadId: string | undefined, threadId: string, channelContext: string): void {
    this.threadChanges.push([previousThreadId, threadId, channelContext]);
  }

  async selectChoice(command: string, opts: Omit<ChannelAdapterMessageOpts, "text">): Promise<void> {
    await this.executeCommandChoice(command, opts);
  }

  stop(): Promise<void> {
    (this as unknown as { running: boolean }).running = false;
    return Promise.resolve();
  }
}

function message(text: string, extra: Partial<ChannelAdapterMessageOpts> = {}): ChannelAdapterMessageOpts {
  return { userId: "chat-1", userName: "Ada", text, channelContext: "chat-1", ...extra };
}

test("a command runs immediately while an earlier message is still blocked in the queue", async (t) => {
  const adapter = new CommandAdapter();
  t.after(() => adapter.stop());
  const gate = deferred();
  let resolutions = 0;
  (adapter as unknown as { getOrCreateThread: () => Promise<SessionThread> }).getOrCreateThread = async () => {
    resolutions += 1;
    if (resolutions === 1) await gate.promise;
    return makeThread("thread-1", "active");
  };

  await adapter.handleMessage(message("hello"));
  await waitUntil(() => resolutions === 1);
  await adapter.handleMessage(message("/stop all"));

  assert.deepEqual(adapter.executed.map(({ threadId, command, arguments: args }) => ({ threadId, command, args })), [
    { threadId: "thread-1", command: "/stop", args: ["all"] },
  ]);
  assert.equal(adapter.turnStarts.length, 0);

  gate.resolve();
  await waitUntil(() => adapter.turnStarts.length === 1);
});

test("slash text that is not a registered command goes to the agent as a turn", async (t) => {
  const adapter = new CommandAdapter();
  t.after(() => adapter.stop());

  await adapter.handleMessage(message("/tmp/build.log is empty"));
  await waitUntil(() => adapter.turnStarts.length === 1);

  assert.deepEqual(adapter.executed, []);
  assert.deepEqual(adapter.turnStarts[0]?.input, [{ type: "text", text: "/tmp/build.log is empty" }]);
});

test("a command result thread becomes the conversation's current thread", async (t) => {
  const adapter = new CommandAdapter();
  t.after(() => adapter.stop());
  (adapter as unknown as { threadMap: Map<string, string> }).threadMap.set("chat-1:chat-1", "thread-1");
  adapter.result = { handled: true, message: "Started a new conversation.", thread: makeThread("thread-2", "active") };

  await adapter.handleMessage(message("/NEW"));

  assert.equal(adapter.executed[0]?.threadId, "thread-1");
  assert.equal(adapter.executed[0]?.command, "/new");
  assert.deepEqual(adapter.resumed.map(({ threadId, params }) => ({ threadId, chat: params?.chat })), [
    { threadId: "thread-2", chat: CHAT },
  ]);
  assert.deepEqual(adapter.threadChanges, [["thread-1", "thread-2", "chat-1"]]);
  assert.deepEqual(adapter.deliveries, ["Started a new conversation."]);

  await adapter.handleMessage(message("hello"));
  await waitUntil(() => adapter.turnStarts.length === 1);
  assert.equal(adapter.turnStarts[0]?.threadId, "thread-2");
});

test("an expanded command is submitted as a commandRef turn", async (t) => {
  const adapter = new CommandAdapter();
  t.after(() => adapter.stop());
  adapter.result = { handled: true, expandedPrompt: "Summarize abc" };

  await adapter.handleMessage(message("/summarize abc"));
  await waitUntil(() => adapter.turnStarts.length === 1);

  assert.deepEqual(adapter.turnStarts[0]?.input, [
    { type: "commandRef", name: "summarize", rawText: "/summarize abc", argsText: "abc" },
  ]);
});

test("choices follow the message as a list of their commands and run for the selecting sender", async (t) => {
  const adapter = new CommandAdapter();
  t.after(() => adapter.stop());
  adapter.result = {
    handled: true,
    message: "Available models",
    choices: [
      { label: "GPT-5", command: "/model gpt-5" },
      { label: "Mini", command: "/model mini" },
    ],
  };

  await adapter.handleMessage(message("/model", { sender: { senderId: "u1", senderRole: "admin" } }));

  assert.equal(adapter.deliveries[0], "Available models");
  assert.equal(adapter.deliveries.length, 2);
  assert.ok(adapter.deliveries[1]?.includes("/model gpt-5"));
  assert.ok(adapter.deliveries[1]?.includes("/model mini"));

  adapter.result = { handled: true, message: "Model set." };
  await adapter.selectChoice("/model mini", {
    userId: "chat-1",
    userName: "Bob",
    channelContext: "chat-1",
    sender: { senderId: "u2", senderName: "Bob", senderRole: "user" },
  });

  const selected = adapter.executed[1]!;
  assert.equal(selected.command, "/model");
  assert.deepEqual(selected.arguments, ["mini"]);
  assert.equal((selected.sender as Record<string, unknown>).senderId, "u2");
  assert.equal((selected.sender as Record<string, unknown>).senderRole, "user");
});

test("a rejected command delivers the server's error message", async (t) => {
  const adapter = new CommandAdapter();
  t.after(() => adapter.stop());
  adapter.result = new JsonRpcError(-32061, "This command requires admin permission.");

  await adapter.handleMessage(message("/model gpt-5"));

  assert.deepEqual(adapter.deliveries, ["This command requires admin permission."]);
});

test("CommandRouter matches names and aliases case-insensitively and refreshes a stale catalog", async () => {
  let now = 0;
  let loads = 0;
  const client = {
    commandList: async () => {
      loads += 1;
      return [{ name: "/stop", aliases: ["/halt"] }];
    },
  } as unknown as ChannelAppServerClient;
  const router = new CommandRouter({
    client,
    deliver: async () => true,
    enqueueMessage: () => {},
    switchThread: async () => {},
    renderChoices: async () => {},
    catalogTtlMs: 1000,
    now: () => now,
  });

  assert.equal(await router.isCommand("/STOP all"), true);
  assert.equal(await router.isCommand("/halt"), true);
  assert.equal(await router.isCommand("/tmp/file"), false);
  assert.equal(await router.isCommand("stop"), false);
  assert.equal(loads, 1);

  now = 1500;
  await router.isCommand("/stop");
  assert.equal(loads, 2);

  router.invalidate();
  await router.isCommand("/stop");
  assert.equal(loads, 3);
});
