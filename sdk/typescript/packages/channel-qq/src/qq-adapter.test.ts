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

test("QQAdapter builds social binding targets from native QQ context", () => {
  const adapter = new QQAdapter() as unknown as {
    buildSocialTarget: (
      opts: Record<string, unknown>,
      sender: Record<string, unknown>,
      channelContext: string,
    ) => Record<string, unknown> | null;
  };

  assert.deepEqual(adapter.buildSocialTarget(
    {
      userId: "group:123",
      userName: "Alice",
      text: "/bind 482913",
      channelContext: "group:123",
    },
    {
      senderId: "456",
      senderName: "Alice",
      senderRole: "admin",
      groupId: "group:123",
    },
    "group:123",
  ), {
    channelName: "qq",
    conversationKind: "group",
    conversationId: "123",
    deliveryTarget: "group:123",
    displayName: "QQ group 123",
    boundBy: {
      platformUserId: "456",
      displayName: "Alice",
    },
  });

  assert.deepEqual(adapter.buildSocialTarget(
    {
      userId: "456",
      userName: "Alice",
      text: "/bind 482913",
      channelContext: "user:456",
    },
    {
      senderId: "456",
      senderName: "Alice",
      senderRole: "admin",
    },
    "user:456",
  ), {
    channelName: "qq",
    conversationKind: "user",
    conversationId: "456",
    deliveryTarget: "user:456",
    displayName: "Alice",
    boundBy: {
      platformUserId: "456",
      displayName: "Alice",
    },
  });
});

test("QQAdapter accepts social bind codes before group mention gating", async () => {
  const adapter = new QQAdapter() as unknown as {
    permission: QQPermissionService;
    requireMentionInGroups: boolean;
    client: {
      request: (method: string, params: Record<string, unknown>) => Promise<Record<string, unknown>>;
    };
    commandRouter: {
      routeBeforeQueue: () => Promise<"enqueue" | "handled">;
    };
    onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
    handleOneBotMessage: (evt: OneBotMessageEvent) => Promise<void>;
  };
  const requests: Array<{ method: string; params: Record<string, unknown> }> = [];
  const deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];

  adapter.permission = new QQPermissionService({ adminUsers: [456] });
  adapter.requireMentionInGroups = true;
  adapter.commandRouter.routeBeforeQueue = async () => {
    throw new Error("bind command should not reach command routing");
  };
  adapter.client.request = async (method, params) => {
    requests.push({ method, params });
    if (method === "app/socialBinding/request/get") {
      return {
        bindingRequestId: "request-1",
        appId: "com.dotharness.channel.qq",
        threadId: "thread-1",
        bindingKind: "socialChannel",
      };
    }
    if (method === "app/socialBinding/accept") {
      return {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.qq",
          threadId: "thread-1",
          state: "active",
          authorityRevision: 1,
          socialTarget: params.target,
      };
    }
    throw new Error(`unexpected request ${method}`);
  };
  adapter.onDeliver = async (target, content, metadata) => {
    deliveries.push({ target, content, metadata });
    return true;
  };

  await adapter.handleOneBotMessage({
    post_type: "message",
    message_type: "group",
    user_id: 456,
    group_id: 123,
    sender: { card: "Alice" },
    message: [{ type: "text", data: { text: "/bind 482913" } }],
  });

  assert.deepEqual(requests[0], {
    method: "app/socialBinding/request/get",
    params: { code: "482913" },
  });
  assert.equal(requests[1]?.method, "app/socialBinding/accept");
  assert.deepEqual(requests[1]?.params.target, {
    channelName: "qq",
    conversationKind: "group",
    conversationId: "123",
    deliveryTarget: "group:123",
    displayName: "QQ group 123",
    boundBy: {
      platformUserId: "456",
      displayName: "Alice",
    },
  });
  assert.deepEqual(deliveries, [
    {
      target: "group:123",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.qq",
        bindingId: "binding-1",
        authorityRevision: 1,
      },
    },
  ]);
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
