import assert from "node:assert/strict";
import test from "node:test";

import { FeishuHistory } from "./feishu-history.js";
import type { FeishuChatMessageItem, FeishuChatMessagePage, FeishuListChatMessagesOptions } from "./feishu-types.js";

function item(messageId: string, text: string, sender: FeishuChatMessageItem["sender"], extra: Partial<FeishuChatMessageItem> = {}): FeishuChatMessageItem {
  return {
    messageId,
    chatId: "oc_group",
    messageType: "text",
    createTime: "1710000000000",
    sender,
    mentions: [],
    rawContent: JSON.stringify({ text }),
    ...extra,
  };
}

function fakeClient(pages: Record<string, FeishuChatMessagePage>, messages: Record<string, FeishuChatMessageItem> = {}) {
  const lists: Array<{ containerId: string; options: FeishuListChatMessagesOptions }> = [];
  return {
    lists,
    async getMessage(messageId: string) {
      return messages[messageId];
    },
    async listChatMessages(containerId: string, options: FeishuListChatMessagesOptions = {}) {
      lists.push({ containerId, options });
      return pages[options.pageToken ?? "first"] ?? { items: [] };
    },
  };
}

function history(client: ReturnType<typeof fakeClient>): FeishuHistory {
  return new FeishuHistory({ client: () => client, botAppId: () => "cli_bot", botOpenId: () => "ou_bot" }, 0);
}

test("Feishu history pages backwards with the platform page token", async () => {
  const client = fakeClient({
    first: {
      items: [
        item("om_3", "newest", { senderType: "app", appId: "cli_bot" }),
        item("om_2", "middle", { senderType: "user", openId: "ou_bob" }),
      ],
      hasMore: true,
      nextPageToken: "page-2",
    },
    "page-2": { items: [item("om_1", "oldest", { senderType: "app", appId: "cli_other" })], hasMore: false },
  });
  const reader = history(client);

  const first = await reader.read({ target: "group:oc_group", limit: 2 });
  assert.deepEqual(first.messages.map((message) => message.text), ["middle", "newest"]);
  assert.equal(first.messages[1].fromBot, true);
  assert.equal(first.before, "page-2");
  assert.deepEqual(client.lists[0], {
    containerId: "oc_group",
    options: { containerType: "chat", sortType: "ByCreateTimeDesc", pageSize: 2 },
  });

  const second = await reader.read({ target: "group:oc_group", limit: 2, before: first.before });
  assert.deepEqual(second.messages.map((message) => message.text), ["oldest"]);
  assert.equal(second.messages[0].fromBot, undefined);
  assert.equal(second.before, undefined);
});

test("Feishu topic history reads the topic's thread container", async () => {
  const client = fakeClient(
    { first: { items: [item("om_reply", "in topic", { openId: "ou_bob" })] } },
    { om_root: item("om_root", "root", { openId: "ou_alice" }, { threadId: "omt_topic" }) },
  );

  const page = await history(client).read({ target: "group:oc_group/thread:om_root", limit: 10 });

  assert.deepEqual(page.messages.map((message) => message.text), ["in topic"]);
  assert.equal(client.lists[0]?.containerId, "omt_topic");
  assert.equal(client.lists[0]?.options.containerType, "thread");
});

test("Feishu direct history reads only a chat learned from a direct message", async () => {
  const client = fakeClient({ first: { items: [item("om_1", "hi", { openId: "ou_alice" })] } });
  const reader = history(client);

  assert.equal((await reader.read({ target: "dm:ou_alice", limit: 5 })).errorCode, "UnsupportedTarget");
  reader.rememberDirectChat("ou_alice", "oc_dm_alice");
  await reader.read({ target: "dm:ou_alice", limit: 5 });

  assert.equal(client.lists.length, 1);
  assert.equal(client.lists[0]?.containerId, "oc_dm_alice");
});

test("Feishu quotes are read by message id", async () => {
  const client = fakeClient({}, { om_parent: item("om_parent", "the build is red", { openId: "ou_bob" }) });
  const reader = history(client);

  const part = await reader.quotedPart("om_parent");

  assert.match(String(part?.text), /Quoted message from ou_bob/);
  assert.match(String(part?.text), /> the build is red/);
  assert.equal(await reader.quotedPart("om_missing"), undefined);
});

test("Feishu media messages are described as video", async () => {
  const client = fakeClient({
    first: { items: [item("om_1", "", { openId: "ou_bob" }, { messageType: "media", rawContent: JSON.stringify({ file_key: "file_clip" }) })] },
  });

  const page = await history(client).read({ target: "group:oc_group", limit: 5 });

  assert.equal(page.messages[0]?.text, "[video]");
});
