import assert from "node:assert/strict";
import test from "node:test";

import {
  TelegramAdapter,
  isTelegramConflictError,
} from "./telegram-adapter.js";

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let i = 0; i < 50; i += 1) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  assert.equal(predicate(), true);
}

type CommandTestAdapter = {
  client: Record<string, unknown>;
  bot: { api: Record<string, (...args: never[]) => Promise<unknown>> };
  adminUsers: Set<string>;
  botUsername: string | undefined;
  threadContextMap: Map<string, string>;
  handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  handleIncomingText: (ctx: Record<string, unknown>) => Promise<void>;
  handleApprovalCallback: (ctx: Record<string, unknown>) => Promise<void>;
  publishCommandMenus: (bot: unknown) => Promise<void>;
  onApprovalRequest: (request: Record<string, unknown>) => Promise<string>;
  onUserInputRequest: (request: Record<string, unknown>) => Promise<Record<string, unknown>>;
  running: boolean;
  startTyping: (chatId: string) => void;
  renderCommandChoices: (target: string, choices: Array<Record<string, string>>, context: unknown) => Promise<void>;
  executeCommandChoice: (command: string, opts: Record<string, unknown>) => Promise<void>;
};

function textUpdate(fromId: number, chatId: number, text: string): Record<string, unknown> {
  return {
    from: { id: fromId, first_name: `User ${fromId}` },
    chatId,
    chat: { id: chatId, type: chatId < 0 ? "supergroup" : "private" },
    msg: { text },
  };
}

test("TelegramAdapter publishes admin-only commands only in admin chat scopes", async () => {
  const adapter = new TelegramAdapter() as unknown as CommandTestAdapter;
  const published: Array<{ scope: Record<string, unknown>; commands: string[] }> = [];
  adapter.adminUsers = new Set(["42"]);
  adapter.client.commandList = async () => [
    { name: "/new", description: "Start a new conversation", requiresAdmin: false },
    { name: "/model", description: "Choose the model", requiresAdmin: true },
    { name: "/code-review", description: "Not a valid Telegram name", requiresAdmin: false },
  ];
  const bot = {
    api: {
      async setMyCommands(commands: Array<{ command: string }>, other: { scope: Record<string, unknown> }) {
        published.push({ scope: other.scope, commands: commands.map(({ command }) => command) });
        return true;
      },
    },
  };

  await adapter.publishCommandMenus(bot);

  assert.deepEqual(published, [
    { scope: { type: "default" }, commands: ["new"] },
    { scope: { type: "all_private_chats" }, commands: ["new"] },
    { scope: { type: "all_group_chats" }, commands: ["new"] },
    { scope: { type: "chat", chat_id: 42 }, commands: ["new", "model"] },
  ]);
});

test("TelegramAdapter marks only configured admins as admin and strips the bot mention from commands", async () => {
  const adapter = new TelegramAdapter() as unknown as CommandTestAdapter;
  const handled: Array<Record<string, unknown>> = [];
  adapter.adminUsers = new Set(["42"]);
  adapter.botUsername = "DotCraftBot";
  adapter.handleMessage = async (opts) => {
    handled.push(opts);
  };

  await adapter.handleIncomingText(textUpdate(42, -100, "/stop@DotCraftBot all"));
  await adapter.handleIncomingText(textUpdate(7, -100, "/stop@OtherBot"));

  assert.deepEqual(handled.map((opts) => opts.text), ["/stop all", "/stop@OtherBot"]);
  assert.deepEqual(
    handled.map((opts) => (opts.senderExtra as Record<string, unknown>).senderRole),
    ["admin", "user"],
  );
});

function routeThroughFakeServer(adapter: CommandTestAdapter): {
  started: Array<{ threadId: string; senderId: unknown }>;
  executed: Array<Record<string, unknown>>;
} {
  const started: Array<{ threadId: string; senderId: unknown }> = [];
  const executed: Array<Record<string, unknown>> = [];
  let threads = 0;
  adapter.running = true;
  adapter.startTyping = () => undefined;
  adapter.client.commandList = async () => [{ name: "/stop", aliases: [] }];
  adapter.client.threadList = async () => [];
  adapter.client.threadStart = async () => ({ id: `thread-${++threads}`, status: "active" });
  adapter.client.request = async (method: string) => {
    if (method === "app/channelBinding/resolve") return { binding: null };
    throw new Error(`unexpected request ${method}`);
  };
  adapter.client.streamEvents = () => ({
    next: () => new Promise(() => undefined),
    return: async () => ({ done: true, value: undefined }),
    [Symbol.asyncIterator]() {
      return this;
    },
  });
  adapter.client.turnStart = async (threadId: string, _input: unknown, sender: Record<string, unknown>) => {
    started.push({ threadId, senderId: sender.senderId });
    return { id: `turn-${threadId}`, threadId, status: "running" };
  };
  adapter.client.commandExecute = async (params: Record<string, unknown>) => {
    executed.push(params);
    return { handled: true };
  };
  return { started, executed };
}

test("TelegramAdapter slash approval aliases answer only the sending member's own approval", async () => {
  const adapter = new TelegramAdapter() as unknown as CommandTestAdapter;
  adapter.bot = {
    api: {
      sendMessage: async () => ({ message_id: 7 }),
      editMessageReplyMarkup: async () => true,
      editMessageText: async () => true,
    },
  };
  const { started } = routeThroughFakeServer(adapter);
  await adapter.handleIncomingText(textUpdate(7, -100, "clean the build folder"));
  await adapter.handleIncomingText(textUpdate(8, -100, "push the release tag"));
  await waitUntil(() => started.length === 2);
  const turnOf = (senderId: string) => {
    const turn = started.find((entry) => entry.senderId === senderId)!;
    return { threadId: turn.threadId, turnId: `turn-${turn.threadId}` };
  };

  const alice = adapter.onApprovalRequest({ ...turnOf("7"), requestId: "req-a", operation: "rm" });
  const bob = adapter.onApprovalRequest({ ...turnOf("8"), requestId: "req-b", operation: "git push" });
  await new Promise((resolve) => setTimeout(resolve, 0));
  await adapter.handleIncomingText(textUpdate(8, -100, "/deny"));
  await adapter.handleIncomingText(textUpdate(7, -100, "/approve session"));

  assert.equal(await bob, "decline");
  assert.equal(await alice, "acceptForSession");
});

for (const [kind, question, reply, expected] of [
  ["free-text", { id: "name", header: "Name", question: "What should the branch be called?" }, "release-1", "release-1"],
  ["fixed-choice", { id: "mode", header: "Mode", question: "Which mode?", options: [{ label: "Auto" }, { label: "Manual" }] }, "2", "Manual"],
] as const) {
  test(`TelegramAdapter routes /stop to the command router while a ${kind} question is pending`, async () => {
    const adapter = new TelegramAdapter() as unknown as CommandTestAdapter;
    adapter.bot = { api: { sendMessage: async () => ({ message_id: 1 }) } };
    const { executed } = routeThroughFakeServer(adapter);
    adapter.threadContextMap.set("thread-1", "123");
    const answer = adapter.onUserInputRequest({ threadId: "thread-1", requestId: "req-1", questions: [question] });
    await new Promise((resolve) => setTimeout(resolve, 0));

    await adapter.handleIncomingText(textUpdate(123, 123, "/stop"));
    await waitUntil(() => executed.length === 1);
    await adapter.handleIncomingText(textUpdate(123, 123, reply));

    assert.equal(executed[0]?.command, "/stop");
    assert.deepEqual(await answer, { answers: { [question.id]: { answers: [expected] } } });
  });
}

test("TelegramAdapter runs a selected choice for the selecting user and clears the keyboard", async () => {
  const adapter = new TelegramAdapter() as unknown as CommandTestAdapter;
  const sent: Array<{ chatId: unknown; options?: Record<string, unknown> }> = [];
  const executed: Array<{ command: string; opts: Record<string, unknown> }> = [];
  const edited: unknown[] = [];
  adapter.adminUsers = new Set(["42"]);
  adapter.bot = {
    api: {
      sendMessage: async (chatId: never, _text: never, options: never) => {
        sent.push({ chatId, options });
        return { message_id: 9 };
      },
      editMessageText: async (chatId: never) => {
        edited.push(chatId);
        return true;
      },
    },
  };
  adapter.executeCommandChoice = async (command, opts) => {
    executed.push({ command, opts });
  };

  await adapter.renderCommandChoices("-100", [
    { label: "GPT-5", command: "/model gpt-5" },
    { label: "Mini", command: "/model mini" },
  ], {});
  const keyboard = sent[0]?.options?.reply_markup as { inline_keyboard: Array<Array<{ callback_data: string }>> };
  const data = keyboard.inline_keyboard[1]![0]!.callback_data;
  assert.ok(Buffer.byteLength(data) <= 64);

  const callback = (fromId: number) => ({
    from: { id: fromId, first_name: "Ada" },
    callbackQuery: { data, message: { message_id: 9, chat: { id: -100 } } },
    answerCallbackQuery: async () => true,
  });
  await adapter.handleApprovalCallback(callback(42));
  await adapter.handleApprovalCallback(callback(42));

  assert.deepEqual(executed.map(({ command }) => command), ["/model mini"]);
  assert.equal((executed[0]?.opts.senderExtra as Record<string, unknown>).senderRole, "admin");
  assert.equal(executed[0]?.opts.channelContext, "-100");
  assert.deepEqual(edited, [-100]);
});

test("TelegramAdapter builds channel binding target from chat context", () => {
  const adapter = new TelegramAdapter() as unknown as {
    buildChannelTarget: (
      opts: Record<string, unknown>,
      sender: Record<string, unknown>,
      channelContext: string,
    ) => Record<string, unknown> | null;
  };

  const target = adapter.buildChannelTarget(
    {
      userId: "42",
      userName: "Ada",
      text: "/bind 482913",
      channelContext: "group:-100123",
    },
    {
      senderId: "42",
      senderName: "Ada",
      senderRole: "admin",
      groupId: "group:-100123",
    },
    "group:-100123",
  );

  assert.deepEqual(target, {
    channelName: "telegram",
    conversationKind: "group",
    conversationId: "-100123",
    deliveryTarget: "group:-100123",
    displayName: "Telegram chat -100123",
    boundBy: {
      platformUserId: "42",
      displayName: "Ada",
    },
  });
});

test("TelegramAdapter accepts channel bind codes for chat context", async () => {
  const adapter = new TelegramAdapter() as unknown as {
    client: {
      request: (method: string, params: Record<string, unknown>) => Promise<Record<string, unknown>>;
    };
    commandRouter: {
      run: () => Promise<void>;
    };
    onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  };
  const requests: Array<{ method: string; params: Record<string, unknown> }> = [];
  const deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];

  adapter.commandRouter.run = async () => {
    throw new Error("bind command should not reach command routing");
  };
  adapter.client.request = async (method, params) => {
    requests.push({ method, params });
    if (method === "app/channelBinding/request/get") {
      return {
        bindingRequestId: "request-1",
        appId: "com.dotharness.channel.telegram",
        threadId: "thread-1",
        bindingKind: "channel",
      };
    }
    if (method === "app/channelBinding/accept") {
      return {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.telegram",
          threadId: "thread-1",
          state: "active",
          authorityRevision: 1,
          channelTarget: params.target,
      };
    }
    throw new Error(`unexpected request ${method}`);
  };
  adapter.onDeliver = async (target, content, metadata) => {
    deliveries.push({ target, content, metadata });
    return true;
  };

  await adapter.handleMessage({
    userId: "42",
    userName: "Ada",
    text: "/bind 482913",
    channelContext: "group:-100123",
    senderExtra: { senderRole: "admin" },
  });

  assert.deepEqual(requests[0], {
    method: "app/channelBinding/request/get",
    params: { code: "482913" },
  });
  assert.equal(requests[1]?.method, "app/channelBinding/accept");
  assert.deepEqual(requests[1]?.params.target, {
    channelName: "telegram",
    conversationKind: "group",
    conversationId: "-100123",
    deliveryTarget: "group:-100123",
    displayName: "Telegram chat -100123",
    boundBy: {
      platformUserId: "42",
      displayName: "Ada",
    },
  });
  assert.deepEqual(deliveries, [
    {
      target: "group:-100123",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.telegram",
        bindingId: "binding-1",
        authorityRevision: 1,
      },
    },
  ]);
});

test("TelegramAdapter binds group senders to their own thread and reports their direct chat", async () => {
  const adapter = new TelegramAdapter() as unknown as {
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
    handleIncomingText: (ctx: Record<string, unknown>) => Promise<void>;
    getChatDescriptor: (opts: Record<string, unknown>, channelContext: string) => Record<string, unknown>;
    startTyping: (chatId: string) => void;
  };
  const handled: Array<Record<string, unknown>> = [];
  adapter.startTyping = () => undefined;
  adapter.handleMessage = async (opts) => {
    handled.push(opts);
  };

  await adapter.handleIncomingText({
    from: { id: 42, first_name: "Ada" },
    chatId: -100123,
    chat: { id: -100123, type: "supergroup", title: "Release crew" },
    msg: { text: "hello" },
  });
  await adapter.handleIncomingText({
    from: { id: 42, first_name: "Ada" },
    chatId: 42,
    chat: { id: 42, type: "private", first_name: "Ada" },
    msg: { text: "hi" },
  });

  assert.equal(handled[0]?.channelContext, "-100123");
  assert.equal(handled[0]?.directTarget, "42");
  assert.deepEqual(adapter.getChatDescriptor(handled[0]!, "-100123"), {
    kind: "group",
    label: "Release crew",
    membership: "sender",
  });
  assert.deepEqual(adapter.getChatDescriptor(handled[1]!, "42"), {
    kind: "direct",
    label: "Ada",
    membership: "shared",
  });
});

test("isTelegramConflictError detects 409 conflict messages", () => {
  assert.equal(isTelegramConflictError(new Error("409 Conflict: terminated by other getUpdates request")), true);
  assert.equal(isTelegramConflictError(new Error("400 Bad Request")), false);
});

test("TelegramAdapter sends native user-input buttons and consumes numeric replies", async () => {
  const sentMessages: Array<{ chatId: number | string; text: string; options?: Record<string, unknown> }> = [];
  const adapter = new TelegramAdapter() as unknown as {
    bot: {
      api: {
        sendMessage: (
          chatId: number | string,
          text: string,
          options?: Record<string, unknown>,
        ) => Promise<Record<string, unknown>>;
      };
    };
    threadContextMap: Map<string, string>;
    onUserInputRequest: (request: Record<string, unknown>) => Promise<Record<string, unknown>>;
    tryHandlePendingUserInputText: (chatTarget: string, text: string) => Promise<boolean>;
  };
  adapter.bot = {
    api: {
      async sendMessage(chatId, text, options) {
        sentMessages.push({ chatId, text, options });
        return { message_id: sentMessages.length, chat: { id: chatId }, text };
      },
    },
  };
  adapter.threadContextMap.set("thread-1", "123");

  const responsePromise = adapter.onUserInputRequest({
    threadId: "thread-1",
    requestId: "req-1",
    questions: [
      {
        id: "mode",
        header: "Pick a mode",
        question: "Which mode?",
        options: [{ label: "Auto" }, { label: "Manual" }],
      },
    ],
  });
  await new Promise((resolve) => setTimeout(resolve, 0));

  assert.equal(sentMessages[0]?.chatId, 123);
  assert.ok(sentMessages[0]?.options?.reply_markup);
  assert.equal(await adapter.tryHandlePendingUserInputText("123", "2"), true);
  assert.deepEqual(await responsePromise, { answers: { mode: { answers: ["Manual"] } } });
});

test("TelegramAdapter asks multi-question user input sequentially", async () => {
  const sentMessages: Array<{ chatId: number | string; text: string; options?: Record<string, unknown> }> = [];
  const adapter = new TelegramAdapter() as unknown as {
    bot: {
      api: {
        sendMessage: (
          chatId: number | string,
          text: string,
          options?: Record<string, unknown>,
        ) => Promise<Record<string, unknown>>;
      };
    };
    threadContextMap: Map<string, string>;
    onUserInputRequest: (request: Record<string, unknown>) => Promise<Record<string, unknown>>;
    tryHandlePendingUserInputText: (chatTarget: string, text: string) => Promise<boolean>;
  };
  adapter.bot = {
    api: {
      async sendMessage(chatId, text, options) {
        sentMessages.push({ chatId, text, options });
        return { message_id: sentMessages.length, chat: { id: chatId }, text };
      },
    },
  };
  adapter.threadContextMap.set("thread-1", "123");

  const responsePromise = adapter.onUserInputRequest({
    threadId: "thread-1",
    requestId: "req-1",
    questions: [
      {
        id: "mode",
        header: "Pick a mode",
        question: "Which mode?",
        options: [{ label: "Auto" }, { label: "Manual" }],
      },
      {
        id: "note",
        header: "Add context",
        question: "Anything else?",
        isOther: true,
      },
    ],
  });

  await waitUntil(() => sentMessages.length === 1);
  assert.match(sentMessages[0]?.text ?? "", /\(1\/2\)/);
  assert.ok(sentMessages[0]?.options?.reply_markup);

  assert.equal(await adapter.tryHandlePendingUserInputText("123", "2"), true);
  await waitUntil(() => sentMessages.length === 2);
  assert.match(sentMessages[1]?.text ?? "", /\(2\/2\)/);
  assert.equal(sentMessages[1]?.options?.reply_markup, undefined);

  assert.equal(await adapter.tryHandlePendingUserInputText("123", "some extra context"), true);
  assert.deepEqual(await responsePromise, {
    answers: {
      mode: { answers: ["Manual"] },
      note: { answers: ["some extra context"] },
    },
  });
});

test("TelegramAdapter shows typing while a started Turn runs and stops on any terminal outcome", async (t) => {
  t.mock.timers.enable({ apis: ["setTimeout"] });
  const actions: Array<number | string> = [];
  const sentMessages: string[] = [];
  const adapter = new TelegramAdapter() as unknown as {
    bot: Record<string, unknown>;
    onTurnStarted: (turn: Record<string, string>) => Promise<void>;
    onTurnEnded: (turn: Record<string, string>, outcome: Record<string, string>) => Promise<void>;
  };
  adapter.bot = {
    api: {
      async sendChatAction(chatId: number | string) {
        actions.push(chatId);
        return true;
      },
      async sendMessage(_chatId: number | string, text: string) {
        sentMessages.push(text);
        return { message_id: 1 };
      },
    },
  };
  const flush = async () => {
    for (let i = 0; i < 5; i += 1) await Promise.resolve();
  };
  const turn = (turnId: string) => ({ threadId: "thread-1", turnId, channelContext: "-100" });
  const originalError = console.error;
  const originalInfo = console.info;
  console.error = () => {};
  console.info = () => {};
  t.after(() => {
    console.error = originalError;
    console.info = originalInfo;
  });

  await adapter.onTurnStarted(turn("turn-1"));
  await adapter.onTurnStarted(turn("turn-2"));
  await flush();
  t.mock.timers.tick(4000);
  await flush();
  assert.deepEqual(actions, [-100, -100]);

  await adapter.onTurnEnded(turn("turn-1"), { status: "cancelled" });
  t.mock.timers.tick(4000);
  await flush();
  assert.equal(actions.length, 3);

  await adapter.onTurnEnded(turn("turn-2"), { status: "failed", error: "provider detail" });
  t.mock.timers.tick(8000);
  await flush();
  assert.equal(actions.length, 3);
  assert.equal(sentMessages.length, 1);
  assert.equal(sentMessages[0]?.includes("provider detail"), false);
});
