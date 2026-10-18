import assert from "node:assert/strict";
import test from "node:test";

import {
  APPROVAL_REPLY_PENDING_NOTICE,
  APPROVAL_REPLY_TIMEOUT_NOTICE,
  type ApprovalReplyTracker,
} from "@dotcraft/channel";

import { WeixinAdapter } from "./weixin-adapter.js";

type WeixinApprovalTestAdapter = {
  client: Record<string, unknown>;
  approvalTimeoutMs: number;
  threadMap: Map<string, string>;
  approvalReplies: ApprovalReplyTracker;
  sendWeixinText: (toUserId: string, text: string) => Promise<void>;
  handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  handleInboundUserMessage: (msg: Record<string, unknown>) => Promise<void>;
  onApprovalRequest: (request: Record<string, unknown>) => Promise<string>;
};

function createAdapter(): {
  adapter: WeixinApprovalTestAdapter;
  sent: Array<{ userId: string; text: string }>;
  forwarded: Record<string, unknown>[];
} {
  const adapter = new WeixinAdapter() as unknown as WeixinApprovalTestAdapter;
  const sent: Array<{ userId: string; text: string }> = [];
  const forwarded: Record<string, unknown>[] = [];
  adapter.sendWeixinText = async (userId, text) => {
    sent.push({ userId, text });
  };
  adapter.client.commandList = async () => [{ name: "/new", aliases: [] }, { name: "/help", aliases: [] }];
  adapter.handleMessage = async (opts) => {
    forwarded.push(opts);
  };
  return { adapter, sent, forwarded };
}

function textMessage(from: string, text: string): Record<string, unknown> {
  return { from_user_id: from, item_list: [{ type: 1, text_item: { text } }] };
}

test("Weixin approval replies accept slash keywords and hold other messages except commands", async () => {
  const { adapter, sent, forwarded } = createAdapter();
  const approver = { channelContext: "wx-1", userId: "wx-1" };
  const decision = adapter.approvalReplies.wait("req-1", approver, 10_000);

  await adapter.handleInboundUserMessage(textMessage("wx-1", "/help"));
  await adapter.handleInboundUserMessage(textMessage("wx-1", "chatting"));
  await adapter.handleInboundUserMessage(textMessage("wx-2", "hello"));
  assert.deepEqual(sent, [{ userId: "wx-1", text: APPROVAL_REPLY_PENDING_NOTICE }]);
  assert.deepEqual(forwarded.map((opts) => opts.text), ["/help", "hello"]);

  await adapter.handleInboundUserMessage(textMessage("wx-1", "/approve"));
  assert.equal(await decision, "accept");
});

test("Weixin /new reaches the server as a command from an admin sender", async () => {
  const adapter = new WeixinAdapter() as unknown as WeixinApprovalTestAdapter;
  const executed: Array<Record<string, unknown>> = [];
  adapter.sendWeixinText = async () => undefined;
  adapter.client.commandList = async () => [{ name: "/new", aliases: [] }];
  adapter.client.threadList = async () => [];
  adapter.client.threadStart = async () => ({ id: "thread-1", status: "active" });
  adapter.client.request = async (method: string) => {
    if (method === "app/channelBinding/resolve") return { binding: null };
    throw new Error(`unexpected request ${method}`);
  };
  adapter.client.commandExecute = async (params: Record<string, unknown>) => {
    executed.push(params);
    return { handled: true };
  };

  await adapter.handleInboundUserMessage(textMessage("wx-1", "/new"));
  for (let i = 0; i < 50 && executed.length < 1; i += 1) {
    await new Promise((resolve) => setTimeout(resolve, 5));
  }

  assert.deepEqual(executed.map((params) => params.command), ["/new"]);
  const sender = executed[0]?.sender as Record<string, unknown>;
  assert.deepEqual([sender.senderId, sender.senderRole], ["wx-1", "admin"]);
});

test("Weixin tells the approver when an approval times out", async () => {
  const { adapter, sent } = createAdapter();
  adapter.approvalTimeoutMs = 5;
  adapter.threadMap.set("wx-1:wx-1", "thread-1");

  const decision = await adapter.onApprovalRequest({
    threadId: "thread-1",
    requestId: "req-1",
    approvalType: "shell",
    operation: "rm -rf build",
  });
  await new Promise((resolve) => setImmediate(resolve));

  assert.equal(decision, "cancel");
  assert.equal(sent.length, 2);
  assert.equal(sent[1]?.text, APPROVAL_REPLY_TIMEOUT_NOTICE);
});
