import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import test from "node:test";

import { FeishuAdapter } from "./feishu-adapter.js";
import { createFeishuEventHandlers } from "./event-handler.js";
import type { FeishuClient } from "./feishu-client.js";
import type {
  FeishuConfig,
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
  hasPendingUserInput: (channelContext: string) => boolean;
  tryHandlePendingUserInputMessage: (message: ParsedInboundMessage) => Promise<boolean>;
};

type MockClient = {
  reactedMessages: Array<{ messageId: string; emojiType: string }>;
  addMessageReaction: (messageId: string, emojiType: string) => Promise<void>;
  downloadMessageResource: FeishuClient["downloadMessageResource"];
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
    hasPendingUserInput(_channelContext: string): boolean {
      return false;
    },
    async tryHandlePendingUserInputMessage(_message: ParsedInboundMessage): Promise<boolean> {
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
    async downloadMessageResource(): Promise<{ bytes: Buffer; mediaType: string }> {
      throw new Error("downloadMessageResource should not be called in text-message tests.");
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
  config?: Partial<FeishuConfig["feishu"]>;
  craftPath?: string;
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
  const config: FeishuConfig["feishu"] = {
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
    craftPath: options?.craftPath ?? "",
  });

  return { handlers, adapter, client, bot, config };
}

test("FeishuAdapter keeps group thread identity while forwarding real sender context", async () => {
  const adapter = new FeishuAdapter() as unknown as {
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
    handleInboundMessage: (message: ParsedInboundMessage) => Promise<void>;
  };
  let captured: Record<string, unknown> | null = null;
  adapter.handleMessage = async (opts) => {
    captured = opts;
  };

  await adapter.handleInboundMessage({
    kind: "text",
    userId: "ou_user_123",
    userName: "Alice",
    threadUserId: "group:oc_group_1",
    channelContext: "group:oc_group_1",
    chatId: "oc_group_1",
    chatType: "group",
    text: "hello",
    parts: [{ type: "text", text: "hello" }],
    messageId: "om_group_1",
    mentions: [],
    sender: { openId: "ou_user_123", userId: "user_123", unionId: "union_123" },
  });

  assert.ok(captured);
  assert.equal(captured["userId"], "group:oc_group_1");
  assert.equal(captured["channelContext"], "group:oc_group_1");
  assert.deepEqual(captured["sender"], {
    senderId: "ou_user_123",
    senderName: "Alice",
    senderRole: "user",
    groupId: "group:oc_group_1",
  });
});

test("FeishuAdapter accepts channel bind codes for group context", async () => {
  const adapter = new FeishuAdapter() as unknown as {
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
        appId: "com.dotharness.channel.feishu",
        threadId: "thread-1",
        bindingKind: "channel",
      };
    }
    if (method === "app/channelBinding/accept") {
      return {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.feishu",
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
    userId: "group:oc_group_1",
    userName: "Alice",
    text: "/bind 482913",
    channelContext: "group:oc_group_1",
    sender: {
      senderId: "ou_user_1",
      senderName: "Alice",
      groupId: "group:oc_group_1",
    },
  });

  assert.deepEqual(requests[0], {
    method: "app/channelBinding/request/get",
    params: { code: "482913" },
  });
  assert.equal(requests[1]?.method, "app/channelBinding/accept");
  assert.deepEqual(requests[1]?.params.target, {
    channelName: "feishu",
    conversationKind: "group",
    conversationId: "oc_group_1",
    deliveryTarget: "group:oc_group_1",
    displayName: "Feishu group oc_group_1",
    boundBy: {
      platformUserId: "ou_user_1",
      displayName: "Alice",
    },
  });
  assert.deepEqual(deliveries, [
    {
      target: "group:oc_group_1",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.feishu",
        bindingId: "binding-1",
        authorityRevision: 1,
      },
    },
  ]);
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

test("Feishu event handler uses a configured ack reaction emoji and skips an invalid one", async () => {
  const client = createClientMock();
  const { handlers } = createHandlers({ client, config: { ackReactionEmoji: "SMILE" } });
  await handlers.onMessage(createTextEvent());
  assert.deepEqual(client.reactedMessages, [{ messageId: "om_message_123", emojiType: "SMILE" }]);

  const invalidClient = createClientMock();
  const adapter = createAdapterMock();
  const invalid = createHandlers({ client: invalidClient, adapter, config: { ackReactionEmoji: "eyes" } });
  await invalid.handlers.onMessage(createTextEvent());
  assert.equal(invalidClient.reactedMessages.length, 0);
  assert.equal(adapter.handledMessages.length, 1);
});

test("Feishu event handler ignores unmentioned group, unsupported, and empty messages without reacting", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ client, adapter });

  for (const message of [
    { message_id: "om_group_1", chat_id: "oc_group_1", chat_type: "group" as const, message_type: "text", content: JSON.stringify({ text: "hello group" }) },
    { message_id: "om_sticker_1", chat_id: "oc_chat_123", chat_type: "p2p" as const, message_type: "sticker", content: JSON.stringify({ file_key: "file_123" }) },
    { message_id: "om_empty_1", chat_id: "oc_chat_123", chat_type: "p2p" as const, message_type: "text", content: JSON.stringify({ text: "   " }) },
  ]) {
    await handlers.onMessage(createTextEvent({ message: { ...message, mentions: [] } }));
  }

  assert.equal(client.reactedMessages.length, 0);
  assert.equal(adapter.handledMessages.length, 0);
});

test("Feishu event handler lets pending user-input replies bypass group mention gate", async () => {
  const client = createClientMock();
  const adapter = createAdapterMock() as MockAdapter;
  const consumed: string[] = [];
  adapter.hasPendingUserInput = (channelContext) => channelContext === "group:oc_group_1";
  adapter.tryHandlePendingUserInputMessage = async (message) => {
    consumed.push(`${message.channelContext}:${message.text}`);
    return true;
  };
  const { handlers } = createHandlers({ client, adapter });

  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_group_pending_1",
        chat_id: "oc_group_1",
        chat_type: "group",
        message_type: "text",
        content: JSON.stringify({ text: "2" }),
        mentions: [],
      },
    }),
  );

  assert.deepEqual(consumed, ["group:oc_group_1:2"]);
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

test("Feishu event handler reads flat post content and drops the bot's own mention", async () => {
  const adapter = createAdapterMock();
  const { handlers } = createHandlers({ adapter, config: { groupMentionRequired: false } });

  await handlers.onMessage(
    createTextEvent({
      message: {
        message_id: "om_post_flat",
        chat_id: "oc_group_1",
        chat_type: "group",
        message_type: "post",
        content: JSON.stringify({
          title: "",
          content: [[
            { tag: "at", user_id: "@_user_1", user_name: "" },
            { tag: "text", text: " help " },
            { tag: "at", user_id: "@_user_2", user_name: "" },
            { tag: "text", text: " with this" },
          ]],
        }),
        mentions: [
          { key: "@_user_1", id: { open_id: "ou_bot_123" }, name: "DotCraft Bot" },
          { key: "@_user_2", id: { open_id: "ou_user_9" }, name: "Teammate" },
        ],
      },
    }),
  );

  assert.equal(adapter.handledMessages.length, 1);
  assert.equal(adapter.handledMessages[0]?.text, "help Teammate with this");
});

test("Feishu event handler preserves metadata for post and image messages", async () => {
  const craftPath = await mkdtemp(join(tmpdir(), "dotcraft-feishu-image-"));
  const adapter = createAdapterMock();
  const client = {
    ...createClientMock(),
    async downloadMessageResource(): Promise<{ bytes: Buffer; mediaType: string }> {
      return { bytes: Buffer.from("jpeg bytes"), mediaType: "image/jpeg" };
    },
  };
  const { handlers } = createHandlers({
    client,
    adapter,
    craftPath,
    config: {
      groupMentionRequired: false,
    },
  });
  try {
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
    const imagePart = adapter.handledMessages[1]?.parts?.[1];
    assert.equal(imagePart?.type, "localImage");
    assert.equal(imagePart?.mimeType, "image/jpeg");
    assert.equal(dirname(String(imagePart?.path)), join(craftPath, "attachments", "images"));
  } finally {
    await rm(craftPath, { recursive: true, force: true });
  }
});

function createMediaClientMock(failKeys: string[] = []) {
  const downloads: Array<{ messageId: string; fileKey: string; type: string }> = [];
  const client = {
    ...createClientMock(),
    downloads,
    async downloadMessageResource(
      messageId: string,
      fileKey: string,
      type: "image" | "file",
    ): Promise<{ bytes: Buffer; mediaType: string }> {
      downloads.push({ messageId, fileKey, type });
      if (failKeys.includes(fileKey)) throw new Error("resource expired");
      return type === "image"
        ? { bytes: Buffer.from(`image ${fileKey}`), mediaType: "image/png" }
        : { bytes: Buffer.from(`video ${fileKey}`), mediaType: "video/mp4" };
    },
  };
  return client;
}

function createPostEvent(messageId: string, content: unknown[][]): FeishuMessageEvent {
  return createTextEvent({
    message: {
      message_id: messageId,
      chat_id: "oc_group_1",
      chat_type: "group",
      message_type: "post",
      content: JSON.stringify({ title: "", content }),
      mentions: [{ key: "@_user_1", id: { open_id: "ou_bot_123" }, name: "DotCraft Bot" }],
    },
  });
}

test("Feishu post delivers its images and videos in place among the words", async () => {
  const craftPath = await mkdtemp(join(tmpdir(), "dotcraft-feishu-post-media-"));
  const adapter = createAdapterMock();
  const client = createMediaClientMock();
  const { handlers } = createHandlers({ client, adapter, craftPath, config: { groupMentionRequired: false } });
  try {
    await handlers.onMessage(
      createPostEvent("om_post_media", [
        [
          { tag: "at", user_id: "@_user_1", user_name: "" },
          { tag: "text", text: " where do I filter lights?" },
        ],
        [{ tag: "img", image_key: "img_screen", width: 800, height: 600 }],
        [
          { tag: "text", text: "and this recording " },
          { tag: "media", file_key: "file_clip", image_key: "img_cover" },
        ],
      ]),
    );

    assert.deepEqual(client.downloads, [
      { messageId: "om_post_media", fileKey: "img_screen", type: "image" },
      { messageId: "om_post_media", fileKey: "file_clip", type: "file" },
    ]);
    const parts = adapter.handledMessages[0]?.parts ?? [];
    assert.deepEqual(parts.map((part) => part.type), ["text", "localImage", "text", "fileRef"]);
    assert.equal(await readFile(String(parts[1]?.path), "utf-8"), "image img_screen");
    assert.equal(await readFile(String(parts[3]?.path), "utf-8"), "video file_clip");
  } finally {
    await rm(craftPath, { recursive: true, force: true });
  }
});

test("Feishu post keeps its words and names the key when an image cannot be downloaded", async () => {
  const craftPath = await mkdtemp(join(tmpdir(), "dotcraft-feishu-post-media-"));
  const adapter = createAdapterMock();
  const client = createMediaClientMock(["img_gone"]);
  const { handlers } = createHandlers({ client, adapter, craftPath, config: { groupMentionRequired: false } });
  try {
    await handlers.onMessage(
      createPostEvent("om_post_failed", [
        [{ tag: "text", text: "what is wrong here?" }],
        [{ tag: "img", image_key: "img_gone" }],
        [{ tag: "img", image_key: "img_ok" }],
      ]),
    );

    const parts = adapter.handledMessages[0]?.parts ?? [];
    assert.deepEqual(parts.map((part) => part.type), ["text", "localImage"]);
    assert.match(String(parts[0]?.text), /what is wrong here\?[\s\S]*img_gone/);
  } finally {
    await rm(craftPath, { recursive: true, force: true });
  }
});

test("Feishu video message is saved under its own name and referenced by path", async () => {
  const craftPath = await mkdtemp(join(tmpdir(), "dotcraft-feishu-video-"));
  const adapter = createAdapterMock();
  const client = createMediaClientMock();
  const { handlers } = createHandlers({ client, adapter, craftPath });
  try {
    await handlers.onMessage(
      createTextEvent({
        message: {
          message_id: "om_video_1",
          chat_id: "oc_chat_123",
          chat_type: "p2p",
          message_type: "media",
          content: JSON.stringify({ file_key: "file_demo", image_key: "img_cover", file_name: "demo.mp4", duration: 3000 }),
        },
      }),
    );

    assert.deepEqual(client.downloads, [{ messageId: "om_video_1", fileKey: "file_demo", type: "file" }]);
    const video = adapter.handledMessages[0]?.parts.find((part) => part.type === "fileRef");
    assert.equal(basename(String(video?.path)), "demo.mp4");
  } finally {
    await rm(craftPath, { recursive: true, force: true });
  }
});
