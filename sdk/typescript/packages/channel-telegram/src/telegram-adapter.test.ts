import assert from "node:assert/strict";
import test from "node:test";

import {
  TelegramAdapter,
  buildTelegramBotCommands,
  isTelegramConflictError,
  parseTargetChatId,
} from "./telegram-adapter.js";

async function waitUntil(predicate: () => boolean): Promise<void> {
  for (let i = 0; i < 50; i += 1) {
    if (predicate()) return;
    await new Promise((resolve) => setTimeout(resolve, 5));
  }
  assert.equal(predicate(), true);
}

test("buildTelegramBotCommands keeps defaults and filters unsupported names", async () => {
  const commands = await buildTelegramBotCommands(async () => [
    { name: "/deploy", description: "Deploy the current change." },
    { name: "INVALID-NAME", description: "Should be skipped." },
    { name: "help", description: "Duplicate built-in command." },
  ]);

  assert.deepEqual(commands, [
    { command: "new", description: "Start a new conversation" },
    { command: "help", description: "Show available commands" },
    { command: "deploy", description: "Deploy the current change." },
  ]);
});

test("parseTargetChatId supports raw and prefixed targets", () => {
  assert.equal(parseTargetChatId("-100123"), -100123);
  assert.equal(parseTargetChatId("group:-100123"), -100123);
  assert.equal(parseTargetChatId("user:42"), 42);
  assert.equal(parseTargetChatId("bad-target"), null);
});

test("TelegramAdapter builds social binding target from chat context", () => {
  const adapter = new TelegramAdapter() as unknown as {
    buildSocialTarget: (
      opts: Record<string, unknown>,
      sender: Record<string, unknown>,
      channelContext: string,
    ) => Record<string, unknown> | null;
  };

  const target = adapter.buildSocialTarget(
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

test("TelegramAdapter accepts social bind codes for chat context", async () => {
  const adapter = new TelegramAdapter() as unknown as {
    client: {
      request: (method: string, params: Record<string, unknown>) => Promise<Record<string, unknown>>;
    };
    commandRouter: {
      routeBeforeQueue: () => Promise<"enqueue" | "handled">;
    };
    onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  };
  const requests: Array<{ method: string; params: Record<string, unknown> }> = [];
  const deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];

  adapter.commandRouter.routeBeforeQueue = async () => {
    throw new Error("bind command should not reach command routing");
  };
  adapter.client.request = async (method, params) => {
    requests.push({ method, params });
    if (method === "app/binding/request/get") {
      return {
        bindingRequestId: "request-1",
        appId: "com.dotharness.channel.telegram",
        threadId: "thread-1",
        bindingKind: "socialChannel",
        requestedScopes: ["conversation.receive", "message.send"],
      };
    }
    if (method === "app/binding/accept") {
      return {
        binding: {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.telegram",
          threadId: "thread-1",
          state: "active",
          bindingKind: "socialChannel",
          socialTarget: params.socialTarget,
        },
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
    method: "app/binding/request/get",
    params: {
      appId: "com.dotharness.channel.telegram",
      bindCode: "482913",
      requestToken: "482913",
    },
  });
  assert.equal(requests[1]?.method, "app/binding/accept");
  assert.deepEqual(requests[1]?.params.socialTarget, {
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
  assert.equal(requests[1]?.params.grantId, "social:telegram::group:-100123");
  assert.equal(requests[1]?.params.approvedBy, "42");
  assert.equal(requests[1]?.params.auditRef, "channel:telegram:group:-100123");
  assert.deepEqual(deliveries, [
    {
      target: "group:-100123",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.telegram",
        bindingId: "binding-1",
        bindingKind: "socialChannel",
      },
    },
  ]);
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
