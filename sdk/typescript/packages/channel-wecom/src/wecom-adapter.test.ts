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

test("WeComAdapter passes chat id as sender groupId", async () => {
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
  let captured: Record<string, unknown> | null = null;
  adapter.permission = new WeComPermissionService({ adminUsers: ["u1"] });
  adapter.handleMessage = async (opts: Record<string, unknown>) => {
    captured = opts;
  };

  await adapter.runInboundMessage(
    "hello",
    { userId: "u1", name: "User One" },
    { getChatId: () => "chat-1" },
    [],
  );

  const opts = captured as Record<string, unknown> | null;
  assert.ok(opts);
  assert.equal(opts["userId"], "u1");
  assert.equal(opts["userName"], "User One");
  assert.equal(opts["channelContext"], "chat:chat-1");
  assert.deepEqual(opts["senderExtra"], { senderRole: "admin", groupId: "chat-1" });
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
