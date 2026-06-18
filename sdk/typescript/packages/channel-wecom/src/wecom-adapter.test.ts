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
