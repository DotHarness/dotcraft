import assert from "node:assert/strict";
import test from "node:test";

import { parseWeComApprovalDecision } from "./approval.js";
import { WeComPermissionService } from "./permission.js";
import { WeComAdapter } from "./wecom-adapter.js";
import { WE_COM_SEND_FILE_TOOL, WE_COM_SEND_VOICE_TOOL, WeComMediaTools } from "./wecom-media-tools.js";
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

test("parseWeComApprovalDecision accepts Chinese and English keywords", () => {
  assert.equal(parseWeComApprovalDecision("同意"), "accept");
  assert.equal(parseWeComApprovalDecision("yes all"), "acceptForSession");
  assert.equal(parseWeComApprovalDecision("拒绝"), "decline");
  assert.equal(parseWeComApprovalDecision("hello"), null);
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

test("WeComMediaTools preserves legacy tool names and current-chat requirement", () => {
  const tools = new WeComMediaTools().getChannelTools();
  assert.deepEqual(tools.map((tool) => tool.name), [WE_COM_SEND_VOICE_TOOL, WE_COM_SEND_FILE_TOOL]);
  assert.ok(tools.every((tool) => tool.requiresChatContext === true));
  assert.equal((tools[0]?.display as Record<string, unknown> | undefined)?.icon, "🎤");
  assert.equal((tools[1]?.display as Record<string, unknown> | undefined)?.icon, "📁");
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

test("WeComAdapter builds social binding target from chat context", () => {
  const adapter = new WeComAdapter() as unknown as {
    buildSocialTarget: (
      opts: Record<string, unknown>,
      sender: Record<string, unknown>,
      channelContext: string,
    ) => Record<string, unknown> | null;
  };

  const target = adapter.buildSocialTarget(
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

test("WeComAdapter accepts social bind codes before forwarding to the agent", async () => {
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
    if (method === "app/binding/request/get") {
      return {
        bindingRequestId: "request-1",
        appId: "com.dotharness.channel.wecom",
        threadId: "thread-1",
        bindingKind: "socialChannel",
        requestedScopes: ["conversation.receive", "message.send"],
      };
    }
    if (method === "app/binding/accept") {
      return {
        binding: {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.wecom",
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

  await adapter.handleTextMessage(
    ["/bind", "482913"],
    { userId: "u1", name: "User One", alias: "" },
    { getChatId: () => "chat-1", pushText: async () => undefined },
  );

  assert.deepEqual(requests[0], {
    method: "app/binding/request/get",
    params: {
      appId: "com.dotharness.channel.wecom",
      bindCode: "482913",
      requestToken: "482913",
    },
  });
  assert.equal(requests[1]?.method, "app/binding/accept");
  assert.deepEqual(requests[1]?.params.socialTarget, {
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
  assert.equal(requests[1]?.params.grantId, "social:wecom::chat:chat-1");
  assert.equal(requests[1]?.params.approvedBy, "u1");
  assert.equal(requests[1]?.params.auditRef, "channel:wecom:chat:chat-1");
  assert.deepEqual(deliveries, [
    {
      target: "chat:chat-1",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.wecom",
        bindingId: "binding-1",
        bindingKind: "socialChannel",
      },
    },
  ]);
});

test("WeComAdapter resolves approvals only for the matching sender and chat", async () => {
  type PendingApproval = {
    channelContext: string;
    userId: string;
    resolve: (decision: string) => void;
    timer: ReturnType<typeof setTimeout>;
  };
  const adapter = new WeComAdapter() as unknown as {
    pendingApprovals: Map<string, PendingApproval>;
    handleTextMessage: (
      parameters: string[],
      from: { userId: string; name: string; alias: string },
      pusher: { getChatId: () => string; pushText: (content: string) => Promise<void> },
    ) => Promise<void>;
    runInboundMessage: () => Promise<void>;
  };
  const resolved: string[] = [];
  const timers: ReturnType<typeof setTimeout>[] = [];
  const addPending = (requestId: string, userId: string, channelContext: string) => {
    const timer = setTimeout(() => undefined, 10_000);
    timers.push(timer);
    adapter.pendingApprovals.set(requestId, {
      channelContext,
      userId,
      timer,
      resolve: (decision) => {
        clearTimeout(timer);
        resolved.push(`${requestId}:${decision}`);
      },
    });
  };
  adapter.runInboundMessage = async () => undefined;

  try {
    addPending("req-1", "u1", "chat:chat-1");
    addPending("req-2", "u2", "chat:chat-2");

    await adapter.handleTextMessage(
      ["yes"],
      { userId: "u1", name: "User One", alias: "" },
      { getChatId: () => "chat-2", pushText: async () => undefined },
    );

    assert.deepEqual(resolved, []);
    assert.equal(adapter.pendingApprovals.size, 2);

    await adapter.handleTextMessage(
      ["yes"],
      { userId: "u2", name: "User Two", alias: "" },
      { getChatId: () => "chat-2", pushText: async () => undefined },
    );

    assert.deepEqual(resolved, ["req-2:accept"]);
    assert.equal(adapter.pendingApprovals.has("req-1"), true);
    assert.equal(adapter.pendingApprovals.has("req-2"), false);
  } finally {
    for (const timer of timers) clearTimeout(timer);
  }
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
