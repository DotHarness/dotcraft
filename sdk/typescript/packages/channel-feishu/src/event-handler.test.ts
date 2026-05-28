import assert from "node:assert/strict";
import test from "node:test";

import { createFeishuEventHandlers } from "./event-handler.js";
import { FeishuClient } from "./feishu-client.js";
import type {
  AppConfig,
  FeishuBotInfo,
  FeishuCardActionEvent,
  FeishuMessageEvent,
  ParsedInboundMessage,
} from "./feishu-types.js";

type MockAdapter = {
  handledMessages: ParsedInboundMessage[];
  cardActionCalls: number;
  handleInboundMessage: (message: ParsedInboundMessage) => Promise<void>;
  handleCardAction: (event: FeishuCardActionEvent) => boolean;
};

type MockClient = {
  reactedMessages: Array<{ messageId: string; emojiType: string }>;
  addMessageReaction: (messageId: string, emojiType: string) => Promise<void>;
  downloadMessageImage: FeishuClient["downloadMessageImage"];
};

function createAdapterMock(onHandle?: (message: ParsedInboundMessage) => void): MockAdapter {
  return {
    handledMessages: [],
    cardActionCalls: 0,
    async handleInboundMessage(message: ParsedInboundMessage): Promise<void> {
      this.handledMessages.push(message);
      onHandle?.(message);
    },
    handleCardAction(_event: FeishuCardActionEvent): boolean {
      this.cardActionCalls += 1;
      return false;
    },
  };
}

function createClientMock(
  onReaction?: (messageId: string, emojiType: string) => Promise<void> | void,
): MockClient {
  return {
    reactedMessages: [],
    async addMessageReaction(messageId: string, emojiType: string): Promise<void> {
      this.reactedMessages.push({ messageId, emojiType });
      await onReaction?.(messageId, emojiType);
    },
    async downloadMessageImage(): Promise<string> {
      throw new Error("downloadMessageImage should not be called in text-message tests.");
    },
  };
}

function createTextEvent(overrides?: Partial<FeishuMessageEvent>): FeishuMessageEvent {
  const sender = {
    sender_id: {
      open_id: "ou_user_123",
      user_id: "user_123",
      union_id: "union_123",
      ...(overrides?.sender?.sender_id ?? {}),
    },
    ...(overrides?.sender ?? {}),
  };
  const message = {
    message_id: "om_message_123",
    chat_id: "oc_chat_123",
    parent_id: "om_parent_123",
    root_id: "om_root_123",
    chat_type: "p2p" as const,
    message_type: "text",
    content: JSON.stringify({ text: "hello from user" }),
    mentions: [
      {
        key: "@_user_1",
        id: {
          open_id: "ou_mention_1",
        },
        name: "Mentioned User",
      },
    ],
    ...(overrides?.message ?? {}),
  };
  return {
    ...overrides,
    sender,
    message,
  };
}

function createHandlers(options?: {
  client?: MockClient;
  adapter?: MockAdapter;
  bot?: Partial<FeishuBotInfo>;
  config?: Partial<AppConfig["feishu"]>;
}) {
  const adapter = options?.adapter ?? createAdapterMock();
  const client = options?.client ?? createClientMock();
  const bot: FeishuBotInfo = {
    appName: "DotCraft",
    botName: "DotCraft Bot",
    openId: "ou_bot_123",
    hasBotIdentity: true,
    ...options?.bot,
  };
  const config: AppConfig["feishu"] = {
    appId: "cli_test",
    appSecret: "secret_test",
    groupMentionRequired: true,
    ...options?.config,
  };

  const handlers = createFeishuEventHandlers({
    adapter: adapter as unknown as never,
    client: client as unknown as never,
    bot,
    config,
  });

  return { handlers, adapter, client, bot, config };
}

test("Feishu client adds a reaction with the expected SDK payload", async () => {
  const calls: Array<Record<string, unknown>> = [];
  const client = Object.create(FeishuClient.prototype) as FeishuClient;
  (client as unknown as { sdk: unknown }).sdk = {
    im: {
      messageReaction: {
        async create(payload?: unknown): Promise<void> {
          calls.push((payload ?? {}) as Record<string, unknown>);
        },
      },
    },
  };

  await client.addMessageReaction("om_test_1", "GLANCE");

  assert.equal(calls.length, 1);
  assert.deepEqual(calls[0], {
    path: {
      message_id: "om_test_1",
    },
    data: {
      reaction_type: {
        emoji_type: "GLANCE",
      },
    },
  });
});

test("Feishu event handler adds reaction before forwarding inbound message", async () => {
  const steps: string[] = [];
  const client = createClientMock(async (messageId, emojiType) => {
    steps.push(`reaction:${messageId}:${emojiType}`);
  });
  const adapter = createAdapterMock((message) => {
    steps.push(`handle:${message.messageId}`);
  });
  const { handlers } = createHandlers({ client, adapter });

  await handlers.onMessage(createTextEvent());

  assert.deepEqual(steps, ["reaction:om_message_123:GLANCE", "handle:om_message_123"]);
  assert.deepEqual(client.reactedMessages, [{ messageId: "om_message_123", emojiType: "GLANCE" }]);
  assert.equal(adapter.handledMessages.length, 1);
  assert.deepEqual(adapter.handledMessages[0]?.sender, {
    openId: "ou_user_123",
    userId: "user_123",
    unionId: "union_123",
  });
  assert.equal(adapter.handledMessages[0]?.parentId, "om_parent_123");
  assert.equal(adapter.handledMessages[0]?.rootId, "om_root_123");
  assert.equal(adapter.handledMessages[0]?.mentions.length, 1);
});

test("Feishu event handler uses configured ack reaction emoji", async () => {
  const client = createClientMock();
  const { handlers } = createHandlers({
    client,
    config: {
      ackReactionEmoji: "SMILE",
    },
  });

  await handlers.onMessage(createTextEvent());

  assert.deepEqual(client.reactedMessages, [{ messageId: "om_message_123", emojiType: "SMILE" }]);
});

test("Feishu event handler skips reaction when group message does not mention the bot", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ client, adapter });

  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_group_1",
        chat_id: "oc_group_1",
        chat_type: "group",
        message_type: "text",
        content: JSON.stringify({ text: "hello group" }),
        mentions: [],
      },
    }),
  );

  assert.equal(client.reactedMessages.length, 0);
  assert.equal(adapter.handledMessages.length, 0);
});

test("Feishu event handler skips reaction for unsupported messages", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ client, adapter });

  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_file_1",
        chat_id: "oc_chat_123",
        chat_type: "p2p",
        message_type: "file",
        content: JSON.stringify({ file_key: "file_123" }),
        mentions: [],
      },
    }),
  );

  assert.equal(client.reactedMessages.length, 0);
  assert.equal(adapter.handledMessages.length, 0);
});

test("Feishu event handler skips reaction for empty text messages after parse", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ client, adapter });

  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_empty_1",
        chat_id: "oc_chat_123",
        chat_type: "p2p",
        message_type: "text",
        content: JSON.stringify({ text: "   " }),
        mentions: [],
      },
    }),
  );

  assert.equal(client.reactedMessages.length, 0);
  assert.equal(adapter.handledMessages.length, 0);
});

test("Feishu event handler does not add reaction twice for duplicate events", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ client, adapter });
  const event = createTextEvent();

  await handlers.onMessage(event);
  await handlers.onMessage(event);

  assert.equal(client.reactedMessages.length, 1);
  assert.equal(adapter.handledMessages.length, 1);
});

test("Feishu event handler skips invalid reaction config but still processes the message", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({
    client,
    adapter,
    config: {
      ackReactionEmoji: "eyes",
    },
  });

  await handlers.onMessage(createTextEvent());

  assert.equal(client.reactedMessages.length, 0);
  assert.equal(adapter.handledMessages.length, 1);
  assert.equal(adapter.handledMessages[0]?.messageId, "om_message_123");
});

test("Feishu event handler keeps processing when adding reaction fails", async () => {
  const client = createClientMock(async () => {
    throw new Error("reaction failed");
  });
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ client, adapter });

  await handlers.onMessage(createTextEvent());

  assert.equal(client.reactedMessages.length, 1);
  assert.equal(adapter.handledMessages.length, 1);
  assert.equal(adapter.handledMessages[0]?.messageId, "om_message_123");
});

test("Feishu event handler preserves metadata for post and image messages", async () => {
  const adapter = createAdapterMock();
  const client = {
    ...createClientMock(),
    async downloadMessageImage(): Promise<string> {
      return "C:\\temp\\image.jpg";
    },
  };
  const { handlers } = createHandlers({
    client,
    adapter,
    config: {
      groupMentionRequired: false,
    },
  });

  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_post_1",
        chat_id: "oc_group_1",
        chat_type: "group",
        message_type: "post",
        content: JSON.stringify({
          zh_cn: {
            content: [[{ tag: "text", text: "hello post" }]],
          },
        }),
      },
    }),
  );
  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_image_1",
        chat_id: "oc_group_1",
        chat_type: "group",
        message_type: "image",
        content: JSON.stringify({ image_key: "img_123" }),
      },
    }),
  );

  assert.equal(adapter.handledMessages.length, 2);
  for (const message of adapter.handledMessages) {
    assert.equal(message.parentId, "om_parent_123");
    assert.equal(message.rootId, "om_root_123");
    assert.deepEqual(message.sender, {
      openId: "ou_user_123",
      userId: "user_123",
      unionId: "union_123",
    });
    assert.equal(message.mentions.length, 1);
  }
});

