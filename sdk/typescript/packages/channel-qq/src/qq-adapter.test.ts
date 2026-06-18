import assert from "node:assert/strict";
import test from "node:test";

import { QQPermissionService } from "./permission.js";
import { QQAdapter } from "./qq-adapter.js";
import { channelContextForQQEvent, parseQQTarget } from "./target.js";
import type { OneBotMessageEvent } from "./onebot.js";

test("QQPermissionService classifies admins, users, groups, and unauthorized users", () => {
  const permissions = new QQPermissionService({
    adminUsers: [1],
    whitelistedUsers: [2],
    whitelistedGroups: [10],
  });

  assert.equal(permissions.getUserRole(1), "admin");
  assert.equal(permissions.getUserRole(2), "whitelisted");
  assert.equal(permissions.getUserRole(3, 10), "whitelisted");
  assert.equal(permissions.getUserRole(3, 11), "unauthorized");
});

test("QQ target parsing accepts group, user, and bare user ids", () => {
  assert.deepEqual(parseQQTarget("group:123"), { kind: "group", id: "123" });
  assert.deepEqual(parseQQTarget("user:456"), { kind: "user", id: "456" });
  assert.deepEqual(parseQQTarget("789"), { kind: "user", id: "789" });
  assert.equal(parseQQTarget("group:abc"), null);
});

test("channelContextForQQEvent preserves native QQ session semantics", () => {
  assert.equal(channelContextForQQEvent(true, 123, 456), "group:123");
  assert.equal(channelContextForQQEvent(false, undefined, 456), "user:456");
});

test("QQAdapter uses group thread identity and real sender context", async () => {
  const groupOpts = await captureHandleMessageOptions({
    post_type: "message",
    message_type: "group",
    user_id: 456,
    group_id: 123,
    sender: { card: "Alice" },
    message: [{ type: "text", data: { text: "hello" } }],
  });

  assert.equal(groupOpts.userId, "group:123");
  assert.equal(groupOpts.channelContext, "group:123");
  assert.deepEqual(groupOpts.sender, {
    senderId: "456",
    senderName: "Alice",
    senderRole: "admin",
    groupId: "group:123",
  });
  assert.equal(groupOpts.omitSenderGroupId, false);

  const secondGroupOpts = await captureHandleMessageOptions({
    post_type: "message",
    message_type: "group",
    user_id: 789,
    group_id: 123,
    sender: { card: "Bob" },
    message: [{ type: "text", data: { text: "hello again" } }],
  });

  assert.equal(secondGroupOpts.userId, "group:123");
  assert.equal(secondGroupOpts.channelContext, "group:123");
  assert.deepEqual(secondGroupOpts.sender, {
    senderId: "789",
    senderName: "Bob",
    senderRole: "admin",
    groupId: "group:123",
  });

  const privateOpts = await captureHandleMessageOptions({
    post_type: "message",
    message_type: "private",
    user_id: 456,
    sender: { nickname: "Alice" },
    message: [{ type: "text", data: { text: "hello" } }],
  });

  assert.equal(privateOpts.userId, "456");
  assert.equal(privateOpts.channelContext, "user:456");
  assert.deepEqual(privateOpts.sender, {
    senderId: "456",
    senderName: "Alice",
    senderRole: "admin",
  });
  assert.equal(privateOpts.omitSenderGroupId, true);
});

test("QQAdapter resolves approvals only for the matching sender and chat", async () => {
  type PendingApproval = {
    channelContext: string;
    userId: string;
    resolve: (decision: string) => void;
    timer: ReturnType<typeof setTimeout>;
  };
  const adapter = new QQAdapter() as unknown as {
    pendingApprovals: Map<string, PendingApproval>;
    handleOneBotMessage: (evt: OneBotMessageEvent) => Promise<void>;
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

  try {
    addPending("req-1", "10", "group:1");
    addPending("req-2", "20", "group:2");

    await adapter.handleOneBotMessage({
      post_type: "message",
      message_type: "group",
      user_id: 10,
      group_id: 2,
      message: [{ type: "text", data: { text: "yes" } }],
    });

    assert.deepEqual(resolved, []);
    assert.equal(adapter.pendingApprovals.size, 2);

    await adapter.handleOneBotMessage({
      post_type: "message",
      message_type: "group",
      user_id: 20,
      group_id: 2,
      message: [{ type: "text", data: { text: "yes" } }],
    });

    assert.deepEqual(resolved, ["req-2:accept"]);
    assert.equal(adapter.pendingApprovals.has("req-1"), true);
    assert.equal(adapter.pendingApprovals.has("req-2"), false);
  } finally {
    for (const timer of timers) clearTimeout(timer);
  }
});

test("QQAdapter consumes pending user-input replies before group mention gating", async () => {
  type PendingUserInput = {
    channelContext: string;
    userId: string;
    request: Record<string, unknown>;
    resolve: (response: Record<string, unknown>) => void;
  };
  const adapter = new QQAdapter() as unknown as {
    pendingUserInputs: Map<string, PendingUserInput>;
    handleOneBotMessage: (evt: OneBotMessageEvent) => Promise<void>;
  };
  const resolved: Record<string, unknown>[] = [];
  adapter.pendingUserInputs.set("req-1", {
    channelContext: "group:123",
    userId: "456",
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

  await adapter.handleOneBotMessage({
    post_type: "message",
    message_type: "group",
    user_id: 456,
    group_id: 123,
    self_id: 999,
    message: [{ type: "text", data: { text: "2" } }],
  });

  assert.deepEqual(resolved, [{ answers: { mode: { answers: ["Manual"] } } }]);
  assert.equal(adapter.pendingUserInputs.size, 0);
});

async function captureHandleMessageOptions(evt: OneBotMessageEvent): Promise<Record<string, unknown>> {
  const adapter = new QQAdapter() as unknown as {
    permission: QQPermissionService;
    requireMentionInGroups: boolean;
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
    handleOneBotMessage: (evt: OneBotMessageEvent) => Promise<void>;
  };
  let captured: Record<string, unknown> | null = null;
  adapter.permission = new QQPermissionService({ adminUsers: [456, 789] });
  adapter.requireMentionInGroups = false;
  adapter.handleMessage = async (opts: Record<string, unknown>) => {
    captured = opts;
  };

  await adapter.handleOneBotMessage(evt);

  assert.ok(captured);
  return captured;
}
