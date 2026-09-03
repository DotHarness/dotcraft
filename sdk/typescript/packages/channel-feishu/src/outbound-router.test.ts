import assert from "node:assert/strict";
import test from "node:test";

import { FeishuChatInfoCache } from "./chat-info-cache.js";
import { FeishuApiError, type FeishuSendResult } from "./feishu-types.js";
import { FeishuOutboundRouter, type OutboundClient } from "./outbound-router.js";

class FakeOutboundClient implements OutboundClient {
  readonly calls: Array<{ method: string; target: string; replyInThread?: boolean }> = [];
  rejectAnchors = new Set<string>();

  async sendInteractiveCard(target: string, _card: Record<string, unknown>): Promise<FeishuSendResult> {
    this.calls.push({ method: "create.card", target });
    return { messageId: "om_created", chatId: target };
  }

  async sendCardKitReference(target: string, _cardId: string): Promise<FeishuSendResult> {
    this.calls.push({ method: "create.cardkit", target });
    return { messageId: "om_created_cardkit", chatId: target };
  }

  async sendFile(target: string, _file: { fileName: string; data: Buffer }): Promise<FeishuSendResult & { fileKey: string }> {
    this.calls.push({ method: "create.file", target });
    return { messageId: "om_created_file", chatId: target, fileKey: "fk" };
  }

  async replyInteractiveCard(messageId: string, _card: Record<string, unknown>, replyInThread: boolean): Promise<FeishuSendResult> {
    this.reject(messageId);
    this.calls.push({ method: "reply.card", target: messageId, replyInThread });
    return { messageId: "om_replied", chatId: "oc_chat" };
  }

  async replyCardKitReference(messageId: string, _cardId: string, replyInThread: boolean): Promise<FeishuSendResult> {
    this.reject(messageId);
    this.calls.push({ method: "reply.cardkit", target: messageId, replyInThread });
    return { messageId: "om_replied_cardkit", chatId: "oc_chat" };
  }

  async replyFile(
    messageId: string,
    _file: { fileName: string; data: Buffer },
    replyInThread: boolean,
  ): Promise<FeishuSendResult & { fileKey: string }> {
    this.reject(messageId);
    this.calls.push({ method: "reply.file", target: messageId, replyInThread });
    return { messageId: "om_replied_file", chatId: "oc_chat", fileKey: "fk" };
  }

  private reject(messageId: string): void {
    if (!this.rejectAnchors.has(messageId)) return;
    throw new FeishuApiError({ kind: "invalidArgument", message: "anchor gone", retryable: false, code: 230019 });
  }
}

test("router creates messages at the chat root for plain groups and DMs", async () => {
  const client = new FakeOutboundClient();
  const router = new FeishuOutboundRouter(client);
  router.noteInbound("group:oc_1", "om_in");

  await router.sendCard("group:oc_1", { schema: "2.0" });
  await router.sendCardKit("dm:ou_1", "card-1");
  await router.sendFile("group:oc_1", { fileName: "a.txt", data: Buffer.from("x") });

  assert.deepEqual(client.calls.map((call) => call.method), ["create.card", "create.cardkit", "create.file"]);
  assert.ok(client.calls.every((call) => call.replyInThread === undefined));
});

test("router replies to the latest inbound anchor inside a topic", async () => {
  const client = new FakeOutboundClient();
  const router = new FeishuOutboundRouter(client);
  const target = "group:oc_1/thread:om_root";
  router.noteInbound(target, "om_first");
  router.noteInbound(target, "om_latest");

  await router.sendCardKit(target, "card-1");
  await router.sendCard(target, { schema: "2.0" });

  assert.deepEqual(client.calls, [
    { method: "reply.cardkit", target: "om_latest", replyInThread: true },
    { method: "reply.card", target: "om_latest", replyInThread: true },
  ]);
});

test("router falls back to the topic root and then the chat root when anchors are gone", async () => {
  const client = new FakeOutboundClient();
  const router = new FeishuOutboundRouter(client);
  const target = "group:oc_1/thread:om_root";
  router.noteInbound(target, "om_latest");
  client.rejectAnchors.add("om_latest");

  await router.sendCard(target, { schema: "2.0" });
  assert.deepEqual(client.calls.at(-1), { method: "reply.card", target: "om_root", replyInThread: true });

  client.rejectAnchors.add("om_root");
  await router.sendCard(target, { schema: "2.0" });
  assert.deepEqual(client.calls.at(-1), { method: "create.card", target: "group:oc_1" });

  router.forget(target);
  client.rejectAnchors.clear();
  await router.sendCard(target, { schema: "2.0" });
  assert.deepEqual(client.calls.at(-1), { method: "reply.card", target: "om_root", replyInThread: true });
});

test("router rethrows non-anchor failures", async () => {
  const client = new FakeOutboundClient();
  client.replyInteractiveCard = async () => {
    throw new FeishuApiError({ kind: "rateLimited", message: "slow down", retryable: true });
  };
  const router = new FeishuOutboundRouter(client);
  await assert.rejects(router.sendCard("group:oc_1/thread:om_root", { schema: "2.0" }), /slow down/);
});

test("chat info cache resolves topic capability once per TTL and fails closed", async () => {
  let now = 0;
  let lookups = 0;
  const cache = new FeishuChatInfoCache(
    {
      async getChatInfo(chatId: string) {
        lookups += 1;
        if (chatId === "oc_error") throw new Error("boom");
        return chatId === "oc_topic"
          ? { chatMode: "topic", groupMessageType: "" }
          : { chatMode: "group", groupMessageType: chatId === "oc_thread" ? "thread" : "chat" };
      },
    },
    { ttlMs: 1000, now: () => now },
  );

  assert.equal(await cache.isThreadCapable("oc_topic"), true);
  assert.equal(await cache.isThreadCapable("oc_thread"), true);
  assert.equal(await cache.isThreadCapable("oc_plain"), false);
  assert.equal(await cache.isThreadCapable("oc_topic"), true);
  assert.equal(lookups, 3);
  now = 2000;
  assert.equal(await cache.isThreadCapable("oc_topic"), true);
  assert.equal(lookups, 4);
  assert.equal(await cache.isThreadCapable("oc_error"), false);
});
