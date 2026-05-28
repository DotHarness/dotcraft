import assert from "node:assert/strict";
import test from "node:test";

import { ChannelAdapter } from "./adapter.js";
import { Thread, Turn } from "./models.js";
import { shouldFlushSegmentOnItemStarted } from "./segmentBoundaries.js";
import type { Transport } from "./transport.js";

class NoopTransport implements Transport {
  async readMessage(): Promise<Record<string, unknown>> {
    throw new Error("Not implemented in tests");
  }

  async writeMessage(_msg: Record<string, unknown>): Promise<void> {}

  async close(): Promise<void> {}
}

class RecordingAdapter extends ChannelAdapter {
  readonly segments: Array<{ text: string; isFinal: boolean; channelContext: string }> = [];
  /** Full-reply deliveries only (mirrors base ChannelAdapter: skipped when segments already sent). */
  readonly completedReplies: string[] = [];

  constructor() {
    super(new NoopTransport(), "test-channel", "test-client", "0.0.0");
  }

  async onDeliver(_target: string, _content: string, _metadata: Record<string, unknown>): Promise<boolean> {
    return true;
  }

  async onApprovalRequest(_request: Record<string, unknown>): Promise<string> {
    return "cancel";
  }

  protected override async onSegmentCompleted(
    _threadId: string,
    _turnId: string,
    segmentText: string,
    isFinal: boolean,
    channelContext: string,
  ): Promise<void> {
    this.segments.push({ text: segmentText, isFinal, channelContext });
  }

  protected override async onTurnCompleted(
    _threadId: string,
    _turnId: string,
    replyText: string,
    _channelContext: string,
    segmentsWereDelivered: boolean,
  ): Promise<void> {
    await super.onTurnCompleted(_threadId, _turnId, replyText, _channelContext, segmentsWereDelivered);
    if (!segmentsWereDelivered && replyText) this.completedReplies.push(replyText);
  }
}

test("should flush segments for plugin function calls", () => {
  assert.equal(shouldFlushSegmentOnItemStarted("toolCall"), true);
  assert.equal(shouldFlushSegmentOnItemStarted("pluginFunctionCall"), true);
  assert.equal(shouldFlushSegmentOnItemStarted("agentMessage"), false);
});

test("ChannelAdapter flushes the current segment before plugin function calls", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "item-a", delta: "before " },
      };
      yield {
        method: "item/started",
        params: {
          threadId: "thread-1",
          item: { type: "pluginFunctionCall", id: "tool-1" },
        },
      };
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "item-b", delta: "after" },
      };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [
              { type: "agentMessage", id: "item-a", payload: { text: "before " } },
              { type: "agentMessage", id: "item-b", payload: { text: "after" } },
            ],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("user-1:chat-1", {
    userId: "user-1",
    userName: "Tester",
    text: "hello",
    channelContext: "chat-1",
  });

  assert.deepEqual(adapter.segments, [
    { text: "before ", isFinal: false, channelContext: "chat-1" },
    { text: "after", isFinal: true, channelContext: "chat-1" },
  ]);
  assert.deepEqual(
    adapter.completedReplies,
    [],
    "full merged reply must not be delivered again when segments were already sent",
  );
});

test("ChannelAdapter lets senderExtra override groupId and omit implicit channel context groupId", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const senders: unknown[] = [];

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async (_threadId: string, _input: unknown, sender: unknown) => {
    senders.push(sender);
    return new Turn(`turn-${senders.length}`, "thread-1", "running");
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
  }).processMessage("user-1:group:123", {
    userId: "user-1",
    userName: "Tester",
    text: "hello",
    channelContext: "group:123",
    senderExtra: { groupId: "123", senderRole: "admin" },
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
    groupId: "123",
    senderRole: "admin",
  });
  assert.deepEqual(senders[1], {
    senderId: "user-1",
    senderName: "Tester",
  });
});

test("item/completed reconciles truncated deltas before turn/completed", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "m1", delta: "hel" },
      };
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: {
            id: "m1",
            type: "agentMessage",
            status: "completed",
            payload: { text: "hello world" },
          },
        },
      };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [{ type: "agentMessage", payload: { text: "hello world" } }],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [{ text: "hello world", isFinal: true, channelContext: "c" }]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("approval-gated external tool call keeps final segment as unsent tail only", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "item/started",
        params: {
          threadId: "thread-1",
          item: { id: "agent-1", type: "agentMessage" },
        },
      };
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "agent-1", delta: "part-a " },
      };
      yield {
        method: "item/started",
        params: {
          threadId: "thread-1",
          item: { id: "tool-1", type: "toolCall" },
        },
      };
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: { id: "tool-1", type: "toolCall", status: "completed" },
        },
      };
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "agent-1", delta: "part-b " },
      };
      yield {
        method: "item/started",
        params: {
          threadId: "thread-1",
          item: { id: "ext-tool", type: "pluginFunctionCall" },
        },
      };
      yield {
        method: "item/approval/resolved",
        params: {
          threadId: "thread-1",
          turnId: "turn-1",
          item: { id: "approval-response", type: "approvalResponse", status: "completed" },
        },
      };
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: { id: "ext-tool", type: "pluginFunctionCall", status: "completed" },
        },
      };
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "agent-1", delta: "part-c" },
      };
      // This completion arrives before turn/completed in real traces.
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: {
            id: "agent-1",
            type: "agentMessage",
            status: "completed",
            payload: { text: "part-a part-b part-c" },
          },
        },
      };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [{ id: "agent-1", type: "agentMessage", payload: { text: "part-a part-b part-c" } }],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [
    { text: "part-a ", isFinal: false, channelContext: "c" },
    { text: "part-b ", isFinal: false, channelContext: "c" },
    { text: "part-c", isFinal: true, channelContext: "c" },
  ]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("two approvals and file-send style flow preserves ordered unsent transcript tails", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "item-007", type: "agentMessage" } } };
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "item-007", delta: "text-before-second-approval " },
      };
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "item-008", type: "toolCall" } } };
      yield { method: "item/completed", params: { threadId: "thread-1", item: { id: "item-008", type: "toolCall" } } };
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: {
            id: "item-007",
            type: "agentMessage",
            payload: { text: "text-before-second-approval " },
          },
        },
      };
      yield {
        method: "item/started",
        params: { threadId: "thread-1", item: { id: "item-009", type: "pluginFunctionCall" } },
      };
      yield {
        method: "item/started",
        params: { threadId: "thread-1", item: { id: "item-010", type: "approvalRequest" } },
      };
      yield {
        method: "item/completed",
        params: { threadId: "thread-1", item: { id: "item-010", type: "approvalRequest" } },
      };
      yield {
        method: "item/approval/resolved",
        params: {
          threadId: "thread-1",
          turnId: "turn-1",
          item: { id: "item-011", type: "approvalResponse", status: "completed" },
        },
      };
      yield {
        method: "item/completed",
        params: { threadId: "thread-1", item: { id: "item-009", type: "pluginFunctionCall" } },
      };
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "item-013", type: "agentMessage" } } };
      yield {
        method: "item/agentMessage/delta",
        params: { threadId: "thread-1", itemId: "item-013", delta: "text-after-file-send" },
      };
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: {
            id: "item-013",
            type: "agentMessage",
            payload: { text: "text-after-file-send" },
          },
        },
      };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [
              { id: "item-007", type: "agentMessage", payload: { text: "text-before-second-approval " } },
              { id: "item-013", type: "agentMessage", payload: { text: "text-after-file-send" } },
            ],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [
    { text: "text-before-second-approval ", isFinal: false, channelContext: "c" },
    { text: "text-after-file-send", isFinal: true, channelContext: "c" },
  ]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("multiple agentMessage items preserve remaining tails on turn completion", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "a1", type: "agentMessage" } } };
      yield { method: "item/agentMessage/delta", params: { threadId: "thread-1", itemId: "a1", delta: "first " } };
      yield { method: "item/completed", params: { threadId: "thread-1", item: { id: "a1", type: "agentMessage", payload: { text: "first " } } } };
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "a2", type: "agentMessage" } } };
      yield { method: "item/agentMessage/delta", params: { threadId: "thread-1", itemId: "a2", delta: "second" } };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [
              { id: "a1", type: "agentMessage", payload: { text: "first " } },
              { id: "a2", type: "agentMessage", payload: { text: "second" } },
            ],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [{ text: "first second", isFinal: true, channelContext: "c" }]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("delta without itemId is attributed to active agent item", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "a1", type: "agentMessage" } } };
      yield { method: "item/agentMessage/delta", params: { threadId: "thread-1", delta: "tail-without-id" } };
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "tool-1", type: "toolCall" } } };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [{ id: "a1", type: "agentMessage", payload: { text: "tail-without-id" } }],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [{ text: "tail-without-id", isFinal: false, channelContext: "c" }]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("snapshot-only turn (no deltas) still delivers one final segment", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [{ type: "agentMessage", payload: { text: "from snapshot" } }],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [{ text: "from snapshot", isFinal: true, channelContext: "c" }]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("snapshot prefix repair does not trim the next sentence prefix", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async () => new Turn("turn-1", "thread-1", "running");
  client.streamEvents = () =>
    (async function* () {
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "a1", type: "agentMessage" } } };
      yield {
        method: "item/agentMessage/delta",
        params: {
          threadId: "thread-1",
          itemId: "a1",
          delta: "我来文件发送。首先验证文件是否存在，然后发送。\n\n\n\n",
        },
      };
      yield { method: "item/started", params: { threadId: "thread-1", item: { id: "tool-1", type: "toolCall" } } };
      yield {
        method: "item/completed",
        params: {
          threadId: "thread-1",
          item: {
            id: "a1",
            type: "agentMessage",
            payload: {
              text: "我来测试将文件发送给你。首先验证文件是否存在，然后发送。\n\n\n\n文件存在。现在发送文件到当前飞书。",
            },
          },
        },
      };
      yield {
        method: "turn/completed",
        params: {
          threadId: "thread-1",
          turn: {
            items: [
              {
                id: "a1",
                type: "agentMessage",
                payload: {
                  text: "我来测试将文件发送给你。首先验证文件是否存在，然后发送。\n\n\n\n文件存在。现在发送文件到当前飞书。",
                },
              },
            ],
          },
        },
      };
    })();

  await (adapter as unknown as {
    processMessage: (identityKey: string, opts: Record<string, unknown>) => Promise<void>;
  }).processMessage("u:c", {
    userId: "u",
    userName: "t",
    text: "hi",
    channelContext: "c",
  });

  assert.deepEqual(adapter.segments, [
    { text: "我来文件发送。首先验证文件是否存在，然后发送。\n\n\n\n", isFinal: false, channelContext: "c" },
    { text: "文件存在。现在发送文件到当前飞书。", isFinal: true, channelContext: "c" },
  ]);
  assert.deepEqual(adapter.completedReplies, []);
});

test("processMessage uses inputParts when provided", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  let startedInput: unknown;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.turnStart = async (_threadId: string, input: unknown) => {
    startedInput = input;
    return new Turn("turn-1", "thread-1", "running");
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

test("processMessage turns slash command into commandRef when command expands", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  let startedInput: unknown;

  (adapter as unknown as {
    getOrCreateThread: (...args: unknown[]) => Promise<Thread>;
  }).getOrCreateThread = async () => new Thread("thread-1", "active");

  client.commandExecute = async () => ({ expandedPrompt: "expanded prompt" });
  client.turnStart = async (_threadId: string, input: unknown) => {
    startedInput = input;
    return new Turn("turn-1", "thread-1", "running");
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
    text: "/summarize abc",
    channelContext: "c",
  });

  assert.deepEqual(startedInput, [
    {
      type: "commandRef",
      name: "summarize",
      rawText: "/summarize abc",
      argsText: "abc",
    },
  ]);
});

test("newThread archives all reusable threads for the identity", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const archived: string[] = [];

  (adapter as unknown as { threadMap: Map<string, string> }).threadMap.set("u:c", "thread-cached");
  client.threadList = async () => [
    new Thread("thread-cached", "active"),
    new Thread("thread-hidden", "active"),
    new Thread("thread-paused", "paused"),
    new Thread("thread-archived", "archived"),
  ];
  client.threadArchive = async (threadId: string) => {
    archived.push(threadId);
  };

  await adapter.newThread("u", "c");

  assert.deepEqual(archived, ["thread-cached", "thread-hidden", "thread-paused"]);
  assert.equal((adapter as unknown as { threadMap: Map<string, string> }).threadMap.has("u:c"), false);
});

test("getOrCreateThread forces a fresh thread after newThread", async () => {
  const adapter = new RecordingAdapter();
  const client = (adapter as unknown as { client: Record<string, unknown> }).client;
  const archived: string[] = [];
  let threadListCalls = 0;
  let threadStartCalls = 0;

  (adapter as unknown as { threadMap: Map<string, string> }).threadMap.set("u:c", "thread-cached");
  client.threadList = async () => {
    threadListCalls += 1;
    return [new Thread("thread-hidden", "active")];
  };
  client.threadArchive = async (threadId: string) => {
    archived.push(threadId);
  };

  await adapter.newThread("u", "c");

  threadListCalls = 0;
  client.threadRead = async () => {
    throw new Error("threadRead should not run while fresh-thread marker is active");
  };
  client.threadStart = async () => {
    threadStartCalls += 1;
    return new Thread("thread-fresh", "active");
  };

  const created = await (adapter as unknown as {
    getOrCreateThread: (identityKey: string, userId: string, channelContext: string, workspacePath: string) => Promise<Thread>;
  }).getOrCreateThread("u:c", "u", "c", "/workspace");

  assert.equal(created.id, "thread-fresh");
  assert.equal(threadStartCalls, 1);
  assert.equal(threadListCalls, 0, "fresh-thread marker must bypass reusable thread discovery");
  assert.deepEqual(archived, ["thread-cached", "thread-hidden"]);
  assert.equal((adapter as unknown as { threadMap: Map<string, string> }).threadMap.get("u:c"), "thread-fresh");
});

test("command /new sessionReset payload updates identity thread mapping", async () => {
  const adapter = new RecordingAdapter();
  const map = (adapter as unknown as { threadMap: Map<string, string> }).threadMap;
  map.set("u:c", "thread-old");

  await (adapter as unknown as {
    applyCommandResetResult: (
      identityKey: string,
      userId: string,
      channelContext: string,
      workspacePath: string,
      commandName: string,
      commandResult: Record<string, unknown>,
    ) => Promise<void>;
  }).applyCommandResetResult("u:c", "u", "c", "/workspace", "/new", {
    handled: true,
    sessionReset: true,
    thread: { id: "thread-new", status: "active" },
    archivedThreadIds: ["thread-old"],
    createdLazily: true,
  });

  assert.equal(map.get("u:c"), "thread-new");
});
