import assert from "node:assert/strict";
import test from "node:test";

import { ChannelAdapter, type ChannelAdapterMessageOpts } from "./adapter.js";
import type { ChannelChatDescriptor, ChannelDeliveryCapabilities } from "./capability.js";
import type { SessionThread, SessionTurn } from "@dotcraft/sdk/contracts";
import { makeThread, makeTurn } from "./test-contract-fixtures.js";
import type { Transport } from "@dotcraft/sdk/wire";

class NoopTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new Error("Not implemented in tests");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {}

  async close(): Promise<void> {}
}

async function withTimeout<T>(promise: Promise<T>, message: string, ms = 1000): Promise<T> {
  let timeout: ReturnType<typeof setTimeout> | undefined;
  const timer = new Promise<never>((_resolve, reject) => {
    timeout = setTimeout(() => reject(new Error(message)), ms);
  });
  try {
    return await Promise.race([promise, timer]);
  } finally {
    if (timeout) clearTimeout(timeout);
  }
}

class RecordingAdapter extends ChannelAdapter {
  readonly deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];
  deliveryError: Error | null = null;

  constructor() {
    super(new NoopTransport(), "test-channel", "test-client", "0.0.0");
  }

  async onDeliver(target: string, content: string, metadata: Record<string, unknown>): Promise<boolean> {
    if (this.deliveryError) throw this.deliveryError;
    this.deliveries.push({ target, content, metadata });
    return true;
  }

  async onApprovalRequest(_request: Record<string, unknown>): Promise<string> {
    return "cancel";
  }

  protected getDisplayName(): string {
    return "Test";
  }

  protected getDeliveryCapabilities(): ChannelDeliveryCapabilities {
    return { text: { format: "plain" }, targets: { group: false, direct: "none" } };
  }

  protected getChatDescriptor(): ChannelChatDescriptor {
    return { kind: "direct", membership: "shared" };
  }
}

class ChannelRecordingAdapter extends RecordingAdapter {
  parseBindCodeForTest(text: string): string | null {
    return this.parseChannelBindCode(text);
  }

  protected override buildChannelTarget(
    opts: ChannelAdapterMessageOpts,
    sender: Record<string, unknown>,
    channelContext: string,
  ) {
    const conversationId = channelContext || String(sender.groupId ?? opts.userId);
    return {
      channelName: "test-channel",
      conversationKind: "group",
      conversationId,
      displayName: `Group ${conversationId}`,
      deliveryTarget: `group:${conversationId}`,
    };
  }
}

test("ChannelAdapter parses numeric channel bind codes", () => {
  const adapter = new ChannelRecordingAdapter();

  assert.equal(adapter.parseBindCodeForTest("/bind 482913"), "482913");
  assert.equal(adapter.parseBindCodeForTest("bind 482913"), null);
});

test("processMessage reports one generic failure when turn startup fails", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const diagnostic = "provider diagnostic detail";

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async () => makeThread("thread-1", "active");
  client.streamEvents = () => (async function* () {})();
  client.turnStart = async () => { throw new Error(diagnostic); };

  await assert.rejects(
    (adapter as unknown as {
      processMessage: (identityKey: string, opts: ChannelAdapterMessageOpts) => Promise<void>;
    }).processMessage("user-1:chat-1", {
      userId: "user-1",
      userName: "Tester",
      text: "hello",
      channelContext: "chat-1",
    }),
    new RegExp(diagnostic),
  );

  assert.deepEqual(adapter.deliveries, [{
    target: "chat-1",
    content: "DotCraft couldn't start this request. Please try again or check the channel logs.",
    metadata: { failureCode: "channelRequestStartFailed" },
  }]);
  assert.equal(adapter.deliveries[0]?.content.includes(diagnostic), false);
});

test("failure notification delivery errors preserve the original startup failure", async () => {
  const adapter = new RecordingAdapter();
  adapter.deliveryError = new Error("platform send unavailable");
  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async () => { throw new Error("startup failed"); };

  await assert.rejects(
    (adapter as unknown as {
      processMessage: (identityKey: string, opts: ChannelAdapterMessageOpts) => Promise<void>;
    }).processMessage("user-1:chat-1", {
      userId: "user-1",
      userName: "Tester",
      text: "hello",
      channelContext: "chat-1",
    }),
    /startup failed/,
  );
  assert.deepEqual(adapter.deliveries, []);
});

test("processMessage enqueues bound channel input instead of streaming a turn", async () => {
  const adapter = new ChannelRecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  let resolveParams: Record<string, unknown> | null = null;
  let enqueueParams: Record<string, unknown> | null = null;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async () => {
    throw new Error("bound channel input must not create a thread");
  };

  client.threadResume = async () => {
    throw new Error("bound channel input must not resume for streaming");
  };
  client.streamEvents = () => {
    throw new Error("bound channel input must not open a stream");
  };
  client.turnStart = async () => {
    throw new Error("bound channel input must not start a streaming turn");
  };
  client.request = async (method: unknown, params: unknown) => {
    if (method === "app/channelBinding/resolve") {
      resolveParams = params as Record<string, unknown>;
      return {
        binding: {
          bindingId: "bind-channel-1",
          threadId: "thread-bound-1",
          appId: "com.dotharness.channel.test-channel",
          state: "active",
          authorityRevision: 1,
          approvedCapabilityRevision: 1,
          channelTarget: {
            channelName: "test-channel",
            conversationKind: "group",
            conversationId: "group-123",
            deliveryTarget: "group:group-123",
          },
        },
      };
    }
    if (method === "app/threadInput/enqueue") {
      enqueueParams = params as Record<string, unknown>;
      return { queuedInput: { id: "queue-1", threadId: "thread-bound-1", status: "queued" }, queuedInputs: [] };
    }
    throw new Error(`unexpected request ${String(method)}`);
  };

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:group-123", {
    userId: "u",
    userName: "Tester",
    text: "hello bound",
    channelContext: "group-123",
  });

  assert.deepEqual(resolveParams, {
    channelName: "test-channel",
    accountId: undefined,
    conversationKind: "group",
    conversationId: "group-123",
  });
  assert.deepEqual(enqueueParams, {
    bindingId: "bind-channel-1",
    input: [{ type: "text", text: "hello bound" }],
    displayText: "hello bound",
    triggerLabel: "test-channel message",
    triggerRefId: "test-channel::group:group-123",
    startPolicy: "runWhenIdle",
    sender: { senderId: "u", senderName: "Tester", senderRole: "user", groupId: "group-123" },
  });
});

test("processMessage falls back to normal channel routing for unbound channel input", async () => {
  const adapter = new ChannelRecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  let resolveParams: Record<string, unknown> | null = null;
  let turnStartParams: Record<string, unknown> | null = null;
  let getOrCreateCalls = 0;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async (...args: unknown[]) => {
    getOrCreateCalls += 1;
    assert.deepEqual(args, ["u:group-123", "u", "group-123", "", { kind: "direct", membership: "shared" }]);
    return makeThread("thread-unbound-1", "active");
  };
  client.turnStart = async (threadId: unknown, input: unknown, sender: unknown) => {
    turnStartParams = { threadId, input, sender };
    return makeTurn("turn-unbound-1", "thread-unbound-1", "running");
  };
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "turn/completed",
        params: { threadId: "thread-unbound-1", turn: { items: [] } },
      };
    })();
  client.request = async (method: unknown, params: unknown) => {
    if (method === "app/channelBinding/resolve") {
      resolveParams = params as Record<string, unknown>;
      return { binding: null };
    }
    throw new Error(`unexpected request ${String(method)}`);
  };

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:group-123", {
    userId: "u",
    userName: "Tester",
    text: "hello unbound",
    channelContext: "group-123",
  });

  assert.deepEqual(resolveParams, {
    channelName: "test-channel",
    accountId: undefined,
    conversationKind: "group",
    conversationId: "group-123",
  });
  assert.equal(getOrCreateCalls, 1);
  assert.deepEqual(turnStartParams, {
    threadId: "thread-unbound-1",
    input: [{ type: "text", text: "hello unbound" }],
    sender: { senderId: "u", senderName: "Tester", senderRole: "user", groupId: "group-123" },
  });
});

test("processMessage falls back to normal channel routing when channel binding resolve fails", async () => {
  const adapter = new ChannelRecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const warnMessages: string[] = [];
  let turnStarted = false;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async () => makeThread("thread-fallback-1", "active");
  client.turnStart = async () => {
    turnStarted = true;
    return makeTurn("turn-fallback-1", "thread-fallback-1", "running");
  };
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "turn/completed",
        params: { threadId: "thread-fallback-1", turn: { items: [] } },
      };
    })();
  client.request = async (method: unknown) => {
    if (method === "app/channelBinding/resolve") throw new Error("resolve unavailable");
    throw new Error(`unexpected request ${String(method)}`);
  };

  const originalWarn = console.warn;
  console.warn = (...args: unknown[]) => {
    warnMessages.push(args.map(String).join(" "));
  };
  try {
    await (adapter as unknown as {
      processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
    }).processMessage("u:group-123", {
      userId: "u",
      userName: "Tester",
      text: "hello fallback",
      channelContext: "group-123",
    });
  } finally {
    console.warn = originalWarn;
  }

  assert.equal(turnStarted, true);
  assert.match(warnMessages[0] ?? "", /falling back to normal channel routing/);
});

test("handleMessage caches accepted channel binding thread", async () => {
  const adapter = new ChannelRecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const requests: string[] = [];

  client.request = async (method: unknown) => {
    requests.push(String(method));
    if (method === "app/channelBinding/request/get") {
      return {
        bindingRequestId: "req-channel-1",
      };
    }
    if (method === "app/channelBinding/accept") {
      return {
        bindingId: "bind-channel-1",
        threadId: "thread-bound-1",
        appId: "com.dotharness.channel.test-channel",
        state: "active",
        authorityRevision: 1,
        approvedCapabilityRevision: 1,
      };
    }
    throw new Error(`unexpected request ${String(method)}`);
  };

  await adapter.handleMessage({
    userId: "u",
    userName: "Tester",
    text: "/bind 482913",
    channelContext: "group-123",
  });

  assert.deepEqual(requests, ["app/channelBinding/request/get", "app/channelBinding/accept"]);
  assert.equal(
    (adapter as unknown as { threadResolver: { getCachedThreadId(identityKey: string): string | undefined } })
      .threadResolver.getCachedThreadId("u:group-123"),
    "thread-bound-1",
  );
});

test("handleMessage resolves channel binding before running slash commands", async () => {
  const adapter = new ChannelRecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  let resolveParams: Record<string, unknown> | null = null;
  let commandThreadId: string | null = null;
  let commandResolved!: () => void;
  const commandSeen = new Promise<void>((resolve) => {
    commandResolved = resolve;
  });

  (adapter as unknown as { running: boolean }).running = true;
  (adapter as unknown as { threadResolver: { setCachedThread(identityKey: string, threadId: string): void } })
    .threadResolver.setCachedThread("u:group-123", "thread-cached-1");
  client.request = async (method: unknown, params: unknown) => {
    if (method === "app/channelBinding/resolve") {
      resolveParams = params as Record<string, unknown>;
      return {
        binding: {
          bindingId: "bind-channel-1",
          threadId: "thread-bound-1",
          appId: "com.dotharness.channel.test-channel",
          state: "active",
          authorityRevision: 1,
          approvedCapabilityRevision: 1,
        },
      };
    }
    throw new Error(`unexpected request ${String(method)}`);
  };
  client.commandList = async () => [{ name: "/help", aliases: [] }];
  client.commandExecute = async (params: { threadId: string }) => {
    commandThreadId = params.threadId;
    commandResolved();
    return { handled: true, message: "help text" };
  };
  client.turnStart = async () => {
    throw new Error("handled bound slash command must not start a streaming turn");
  };
  client.streamEvents = () => {
    throw new Error("handled bound slash command must not open a stream");
  };

  try {
    await adapter.handleMessage({
      userId: "u",
      userName: "Tester",
      text: "/help",
      channelContext: "group-123",
    });
    await withTimeout(commandSeen, "slash command was not executed");
  } finally {
    (adapter as unknown as { running: boolean }).running = false;
  }

  assert.deepEqual(resolveParams, {
    channelName: "test-channel",
    accountId: undefined,
    conversationKind: "group",
    conversationId: "group-123",
  });
  assert.equal(commandThreadId, "thread-bound-1");
  assert.equal(
    (adapter as unknown as { threadResolver: { getCachedThreadId(identityKey: string): string | undefined } })
      .threadResolver.getCachedThreadId("u:group-123"),
    "thread-bound-1",
  );
});

test("ChannelAdapter lets explicit sender override thread identity and omit implicit channel context groupId", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const senders: unknown[] = [];

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async () => makeThread("thread-1", "active");

  client.turnStart = async (_threadId: string, _input: unknown, sender: unknown) => {
    senders.push(sender);
    return makeTurn(`turn-${senders.length}`, "thread-1", "running");
  };
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "turn/completed",
        params: { threadId: "thread-1", turn: { items: [] } },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("group:123:group:123", {
    userId: "group:123",
    userName: "QQ Group 123",
    text: "hello",
    channelContext: "group:123",
    sender: { senderId: "user-1", senderName: "Tester", groupId: "group:123" },
    senderExtra: { senderRole: "admin" },
  });

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("user-1:user:1", {
    userId: "user-1",
    userName: "Tester",
    text: "hello",
    channelContext: "user:1",
    omitSenderGroupId: true,
  });

  assert.deepEqual(senders[0], {
    senderId: "user-1",
    senderName: "Tester",
    groupId: "group:123",
    senderRole: "admin",
  });
  assert.deepEqual(senders[1], {
    senderId: "user-1",
    senderName: "Tester",
    senderRole: "user",
  });
});

test("processMessage uses inputParts when provided", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  let startedInput: unknown;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<SessionThread>;
  }).getOrCreateThread = async () => makeThread("thread-1", "active");

  client.turnStart = async (_threadId: string, input: unknown) => {
    startedInput = input;
    return makeTurn("turn-1", "thread-1", "running");
  };
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "turn/completed",
        params: { threadId: "thread-1", turn: { items: [] } },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: import("./adapter.js").ChannelAdapterMessageOpts) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "ignored",
    channelContext: "c",
    inputParts: [{ type: "text", text: "custom part" }],
  });

  assert.deepEqual(startedInput, [{ type: "text", text: "custom part" }]);
});
