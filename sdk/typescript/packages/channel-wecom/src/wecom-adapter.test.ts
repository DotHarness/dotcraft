import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  APPROVAL_REPLY_PENDING_NOTICE,
  APPROVAL_REPLY_TIMEOUT_NOTICE,
  type ApprovalReplyTracker,
} from "@dotcraft/channel";

import { WeComPermissionService } from "./permission.js";
import { WeComAdapter } from "./wecom-adapter.js";
import { WE_COM_SEND_FILE_TOOL, WE_COM_SEND_IMAGE_TOOL, WeComMediaTools } from "./wecom-media-tools.js";
import { parseWeComMessage, parseWeComParameters, WeComChatType } from "./wecom-types.js";

test("WeComPermissionService classifies admins, whitelisted users, chats, and unauthorized users", () => {
  const permissions = new WeComPermissionService({
    adminUsers: ["admin"],
    whitelistedUsers: ["user"],
    whitelistedChats: ["chat"],
  });

  assert.equal(permissions.getUserRole("admin"), "admin");
  assert.equal(permissions.getUserRole("user"), "whitelisted");
  assert.equal(permissions.getUserRole("someone", "chat"), "whitelisted");
  assert.equal(permissions.getUserRole("someone", "other"), "unauthorized");
});

test("parseWeComParameters strips leading mention in group chats", () => {
  assert.deepEqual(parseWeComParameters("@DotCraft hello world", WeComChatType.Group), ["hello", "world"]);
  assert.deepEqual(parseWeComParameters("@DotCraft hello", WeComChatType.Single), ["@DotCraft", "hello"]);
});

test("parseWeComMessage parses JSON mixed messages", () => {
  const message = parseWeComMessage(JSON.stringify({
    msgid: "m1",
    chattype: "group",
    msgtype: "mixed",
    chatid: "c1",
    webhook_url: "https://example.test/webhook?key=k",
    from: { userid: "u1", name: "User" },
    mixed: {
      msg_item: [
        { msgtype: "text", text: { content: "hello" } },
        { msgtype: "image", image: { url: "https://example.test/a.jpg" } },
      ],
    },
  }));
  assert.equal(message?.mixedMessage?.msgItems.length, 2);
  assert.equal(message?.mixedMessage?.msgItems[0]?.text?.content, "hello");
});

test("WeComMediaTools requires current chat context and exposes display metadata", () => {
  const tools = new WeComMediaTools().getChannelTools();
  assert.ok(tools.every((tool) => tool.requiresChatContext === true));
  assert.equal((tools[0]?.display as Record<string, unknown> | undefined)?.icon, "🎤");
  assert.equal((tools[1]?.display as Record<string, unknown> | undefined)?.icon, "📁");
});

test("WeComMediaTools uploads file tool paths as bytes", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-wecom-upload-"));
  const filePath = join(tempDir, "report.txt");
  writeFileSync(filePath, "hello wecom", "utf-8");
  const uploads: Array<{ bytes: Buffer; filename: string; type: string }> = [];
  let pushedFile = "";
  const pusher = {
    getChatId: () => "chat:chat-1",
    uploadMedia: async (bytes: Buffer, filename: string, type: "voice" | "file") => {
      uploads.push({ bytes, filename, type });
      return "media-1";
    },
    pushFile: async (mediaId: string) => {
      pushedFile = mediaId;
    },
    pushVoice: async () => undefined,
    pushText: async () => undefined,
  };

  const result = await new WeComMediaTools().executeToolCall(pusher as never, WE_COM_SEND_FILE_TOOL, { filePath });

  assert.equal(result.success, true);
  assert.equal(uploads[0]?.filename, "report.txt");
  assert.equal(uploads[0]?.type, "file");
  assert.equal(uploads[0]?.bytes.toString("utf-8"), "hello wecom");
  assert.equal(pushedFile, "media-1");
});

test("WeComMediaTools sends images inline within the WeCom size limit", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-wecom-image-"));
  const filePath = join(tempDir, "chart.png");
  const pngBytes = Buffer.concat([Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]), Buffer.from("png")]);
  const jpegBytes = Buffer.concat([Buffer.from([0xff, 0xd8, 0xff]), Buffer.from("jpeg")]);
  writeFileSync(filePath, pngBytes);
  const pushedImages: Buffer[] = [];
  const pusher = {
    getChatId: () => "chat:chat-1",
    pushImage: async (bytes: Buffer) => {
      pushedImages.push(bytes);
    },
  };
  const tools = new WeComMediaTools();

  const toolResult = await tools.executeToolCall(pusher as never, WE_COM_SEND_IMAGE_TOOL, { filePath });
  const structured = await tools.sendStructuredMessage(pusher as never, {
    kind: "image",
    fileName: "ig_1.png",
    source: { kind: "dataBase64", dataBase64: jpegBytes.toString("base64") },
  });

  assert.equal(toolResult.success, true);
  assert.equal(structured.delivered, true);
  assert.deepEqual(pushedImages, [pngBytes, jpegBytes]);
  await assert.rejects(
    () => tools.sendStructuredMessage(pusher as never, {
      kind: "image",
      source: { kind: "dataBase64", dataBase64: Buffer.alloc(2 * 1024 * 1024 + 1).toString("base64") },
    }),
    /2097152|too large|exceeds/i,
  );
});

test("WeComMediaTools sends images WeCom cannot render inline as files", async () => {
  const uploads: Array<{ fileName: string; type: string }> = [];
  let pushedFile = "";
  const pusher = {
    getChatId: () => "chat:chat-1",
    pushImage: async () => {
      throw new Error("unsupported image format must not be pushed inline");
    },
    uploadMedia: async (_bytes: Buffer, fileName: string, type: string) => {
      uploads.push({ fileName, type });
      return "media-webp";
    },
    pushFile: async (mediaId: string) => {
      pushedFile = mediaId;
    },
  };

  const result = await new WeComMediaTools().sendStructuredMessage(pusher as never, {
    kind: "image",
    fileName: "ig_1.webp",
    source: { kind: "dataBase64", dataBase64: Buffer.from("RIFF0000WEBP").toString("base64") },
  });

  assert.equal(result.delivered, true);
  assert.deepEqual(uploads, [{ fileName: "ig_1.webp", type: "file" }]);
  assert.equal(pushedFile, "media-webp");
});

test("WeComAdapter uses chat thread identity and real sender context", async () => {
  const adapter = new WeComAdapter() as unknown as {
    permission: WeComPermissionService;
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
    runInboundMessage: (
      text: string,
      from: { userId: string; name: string; alias?: string },
      pusher: { getChatId: () => string },
      inputParts: Record<string, unknown>[],
    ) => Promise<void>;
  };
  const captured: Record<string, unknown>[] = [];
  adapter.permission = new WeComPermissionService({ adminUsers: ["u1", "u2"] });
  adapter.handleMessage = async (opts: Record<string, unknown>) => {
    captured.push(opts);
  };

  await adapter.runInboundMessage(
    "hello",
    { userId: "u1", name: "User One" },
    { getChatId: () => "chat-1" },
    [],
  );

  await adapter.runInboundMessage(
    "hello again",
    { userId: "u2", name: "User Two" },
    { getChatId: () => "chat-1" },
    [],
  );

  const opts = captured[0] as Record<string, unknown> | undefined;
  assert.ok(opts);
  assert.equal(opts["userId"], "chat:chat-1");
  assert.equal(opts["userName"], "User One");
  assert.equal(opts["channelContext"], "chat:chat-1");
  assert.deepEqual(opts["sender"], {
    senderId: "u1",
    senderName: "User One",
    senderRole: "admin",
    groupId: "chat:chat-1",
  });

  const secondOpts = captured[1] as Record<string, unknown> | undefined;
  assert.ok(secondOpts);
  assert.equal(secondOpts["userId"], "chat:chat-1");
  assert.equal(secondOpts["channelContext"], "chat:chat-1");
  assert.deepEqual(secondOpts["sender"], {
    senderId: "u2",
    senderName: "User Two",
    senderRole: "admin",
    groupId: "chat:chat-1",
  });
});

test("WeComAdapter builds channel binding target from chat context", () => {
  const adapter = new WeComAdapter() as unknown as {
    buildChannelTarget: (
      opts: Record<string, unknown>,
      sender: Record<string, unknown>,
      channelContext: string,
    ) => Record<string, unknown> | null;
  };

  const target = adapter.buildChannelTarget(
    {
      userId: "chat:chat-1",
      userName: "User One",
      text: "/bind 482913",
      channelContext: "chat:chat-1",
    },
    {
      senderId: "u1",
      senderName: "User One",
      senderRole: "admin",
      groupId: "chat:chat-1",
    },
    "chat:chat-1",
  );

  assert.deepEqual(target, {
    channelName: "wecom",
    conversationKind: "chat",
    conversationId: "chat-1",
    deliveryTarget: "chat:chat-1",
    displayName: "WeCom chat chat-1",
    boundBy: {
      platformUserId: "u1",
      displayName: "User One",
    },
  });
});

test("WeComAdapter accepts channel bind codes before forwarding to the agent", async () => {
  const adapter = new WeComAdapter() as unknown as {
    client: {
      request: (method: string, params: Record<string, unknown>) => Promise<Record<string, unknown>>;
    };
    commandRouter: {
      routeBeforeQueue: () => Promise<"enqueue" | "handled">;
    };
    onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
    handleTextMessage: (
      parameters: string[],
      from: { userId: string; name: string; alias: string },
      pusher: { getChatId: () => string; pushText: (content: string) => Promise<void> },
    ) => Promise<void>;
  };
  const requests: Array<{ method: string; params: Record<string, unknown> }> = [];
  const deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];

  adapter.commandRouter.routeBeforeQueue = async () => {
    throw new Error("bind command should not reach command routing");
  };
  adapter.client.request = async (method, params) => {
    requests.push({ method, params });
    if (method === "app/channelBinding/request/get") {
      return {
        bindingRequestId: "request-1",
        appId: "com.dotharness.channel.wecom",
        threadId: "thread-1",
        bindingKind: "channel",
      };
    }
    if (method === "app/channelBinding/accept") {
      return {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.wecom",
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

  await adapter.handleTextMessage(
    ["/bind", "482913"],
    { userId: "u1", name: "User One", alias: "" },
    { getChatId: () => "chat-1", pushText: async () => undefined },
  );

  assert.deepEqual(requests[0], {
    method: "app/channelBinding/request/get",
    params: { code: "482913" },
  });
  assert.equal(requests[1]?.method, "app/channelBinding/accept");
  assert.deepEqual(requests[1]?.params.target, {
    channelName: "wecom",
    conversationKind: "chat",
    conversationId: "chat-1",
    deliveryTarget: "chat:chat-1",
    displayName: "WeCom chat chat-1",
    boundBy: {
      platformUserId: "u1",
      displayName: "User One",
    },
  });
  assert.deepEqual(deliveries, [
    {
      target: "chat:chat-1",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.wecom",
        bindingId: "binding-1",
        authorityRevision: 1,
      },
    },
  ]);
});

type WeComApprovalTestAdapter = {
  approvalTimeoutMs: number;
  threadContextMap: Map<string, string>;
  lastSenderByContext: Map<string, string>;
  approvalReplies: ApprovalReplyTracker;
  createPusher: (target: string) => { pushText: (content: string) => Promise<void> };
  handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  handleTextMessage: (
    parameters: string[],
    from: { userId: string; name: string; alias: string },
    pusher: { getChatId: () => string; pushText: (content: string) => Promise<void> },
  ) => Promise<void>;
  onApprovalRequest: (request: Record<string, unknown>) => Promise<string>;
};

function createWeComApprovalTestAdapter(): {
  adapter: WeComApprovalTestAdapter;
  pushed: Array<{ target: string; content: string }>;
  forwarded: Record<string, unknown>[];
} {
  const adapter = new WeComAdapter() as unknown as WeComApprovalTestAdapter;
  const pushed: Array<{ target: string; content: string }> = [];
  const forwarded: Record<string, unknown>[] = [];
  adapter.createPusher = (target) => ({
    pushText: async (content) => {
      pushed.push({ target, content });
    },
  });
  adapter.handleMessage = async (opts) => {
    forwarded.push(opts);
  };
  return { adapter, pushed, forwarded };
}

function chatPusher(chatId: string): { getChatId: () => string; pushText: (content: string) => Promise<void> } {
  return { getChatId: () => chatId, pushText: async () => undefined };
}

test("WeComAdapter resolves approvals only for the matching sender and chat", async () => {
  const { adapter } = createWeComApprovalTestAdapter();
  const first = adapter.approvalReplies.wait("req-1", { channelContext: "chat:chat-1", userId: "u1" }, 10_000);
  const second = adapter.approvalReplies.wait("req-2", { channelContext: "chat:chat-2", userId: "u2" }, 10_000);

  await adapter.handleTextMessage(["yes"], { userId: "u1", name: "User One", alias: "" }, chatPusher("chat-2"));
  await adapter.handleTextMessage(["/yes", "all"], { userId: "u2", name: "User Two", alias: "" }, chatPusher("chat-2"));
  assert.equal(await second, "acceptForSession");

  adapter.approvalReplies.resolveAll("cancel");
  assert.equal(await first, "cancel");
});

test("WeComAdapter holds other messages from the approver while an approval is pending", async () => {
  const { adapter, pushed, forwarded } = createWeComApprovalTestAdapter();
  const decision = adapter.approvalReplies.wait("req-1", { channelContext: "chat:chat-1", userId: "u1" }, 10_000);

  await adapter.handleTextMessage(["/new"], { userId: "u1", name: "User One", alias: "" }, chatPusher("chat-1"));
  await adapter.handleTextMessage(["hello"], { userId: "u2", name: "User Two", alias: "" }, chatPusher("chat-1"));

  assert.deepEqual(pushed, [{ target: "chat:chat-1", content: APPROVAL_REPLY_PENDING_NOTICE }]);
  assert.deepEqual(forwarded.map((opts) => opts.text), ["hello"]);

  await adapter.handleTextMessage(["拒绝"], { userId: "u1", name: "User One", alias: "" }, chatPusher("chat-1"));
  assert.equal(await decision, "decline");
});

test("WeComAdapter tells the approver when an approval times out", async () => {
  const { adapter, pushed } = createWeComApprovalTestAdapter();
  adapter.approvalTimeoutMs = 5;
  adapter.threadContextMap.set("thread-1", "chat:chat-1");
  adapter.lastSenderByContext.set("chat:chat-1", "u1");

  const decision = await adapter.onApprovalRequest({
    threadId: "thread-1",
    requestId: "req-1",
    approvalType: "shell",
    operation: "rm -rf build",
  });
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(decision, "cancel");
  assert.equal(pushed.length, 2);
  assert.equal(pushed[1]?.content, APPROVAL_REPLY_TIMEOUT_NOTICE);
});

test("WeComAdapter consumes pending user-input replies before forwarding to agent", async () => {
  type PendingUserInput = {
    channelContext: string;
    userId: string;
    request: Record<string, unknown>;
    resolve: (response: Record<string, unknown>) => void;
  };
  const adapter = new WeComAdapter() as unknown as {
    pendingUserInputs: Map<string, PendingUserInput>;
    handleTextMessage: (
      parameters: string[],
      from: { userId: string; name: string; alias: string },
      pusher: { getChatId: () => string; pushText: (content: string) => Promise<void> },
    ) => Promise<void>;
    runInboundMessage: () => Promise<void>;
  };
  let forwarded = false;
  const resolved: Record<string, unknown>[] = [];
  adapter.runInboundMessage = async () => {
    forwarded = true;
  };
  adapter.pendingUserInputs.set("req-1", {
    channelContext: "chat:chat-1",
    userId: "u1",
    request: {
      requestId: "req-1",
      questions: [
        {
          id: "mode",
          header: "Pick a mode",
          question: "Which mode?",
          options: [{ label: "Auto" }, { label: "Manual" }],
        },
      ],
    },
    resolve: (response) => resolved.push(response),
  });

  await adapter.handleTextMessage(
    ["2"],
    { userId: "u1", name: "User One", alias: "" },
    { getChatId: () => "chat-1", pushText: async () => undefined },
  );

  assert.equal(forwarded, false);
  assert.deepEqual(resolved, [{ answers: { mode: { answers: ["Manual"] } } }]);
  assert.equal(adapter.pendingUserInputs.size, 0);
});
