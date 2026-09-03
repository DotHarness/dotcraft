import assert from "node:assert/strict";
import test from "node:test";

import {
  conversationTargetBase,
  conversationTargetThreadKey,
  deriveConversationTarget,
  formatConversationTarget,
  needsThreadCapabilityLookup,
  parseConversationTarget,
  resolveTopicKey,
} from "./conversation-target.js";
import type { FeishuMessageEvent } from "./feishu-types.js";

function groupEvent(message: Partial<FeishuMessageEvent["message"]>): FeishuMessageEvent {
  return {
    sender: { sender_id: { open_id: "ou_sender" } },
    message: {
      message_id: "om_msg",
      chat_id: "oc_chat",
      chat_type: "group",
      message_type: "text",
      content: "{}",
      ...message,
    },
  };
}

test("conversation targets round-trip and keep plain group/dm targets unchanged", () => {
  assert.equal(formatConversationTarget({ kind: "group", id: "oc_1" }), "group:oc_1");
  assert.equal(formatConversationTarget({ kind: "group", id: "oc_1", threadKey: "om_root" }), "group:oc_1/thread:om_root");
  assert.deepEqual(parseConversationTarget("group:oc_1"), { kind: "group", id: "oc_1" });
  assert.deepEqual(parseConversationTarget("group:oc_1/thread:om_root"), { kind: "group", id: "oc_1", threadKey: "om_root" });
  assert.deepEqual(parseConversationTarget("dm:ou_1"), { kind: "dm", id: "ou_1" });
  assert.deepEqual(parseConversationTarget("oc_bare"), { kind: "group", id: "oc_bare" });
  assert.equal(parseConversationTarget("group:"), null);
  assert.equal(conversationTargetBase("group:oc_1/thread:om_root"), "group:oc_1");
  assert.equal(conversationTargetBase(conversationTargetBase("group:oc_1/thread:om_root")), "group:oc_1");
  assert.equal(conversationTargetThreadKey("group:oc_1/thread:om_root"), "om_root");
  assert.equal(conversationTargetThreadKey("group:oc_1"), "");
});

test("resolveTopicKey uses the topic root message id and stays case-sensitive", () => {
  const root = groupEvent({ message_id: "om_Root", thread_id: "omt_topic" }).message;
  const reply = groupEvent({ message_id: "om_reply", thread_id: "omt_topic", root_id: "om_Root", parent_id: "om_Root" }).message;
  const replyWithoutThreadId = groupEvent({ message_id: "om_reply2", root_id: "om_Root", parent_id: "om_Root" }).message;
  const quoted = groupEvent({ message_id: "om_q", root_id: "om_a", parent_id: "om_b" }).message;

  assert.equal(resolveTopicKey(root, false), "om_Root");
  assert.equal(resolveTopicKey(reply, false), "om_Root");
  assert.equal(resolveTopicKey(replyWithoutThreadId, true), "om_Root");
  assert.equal(resolveTopicKey(replyWithoutThreadId, false), undefined);
  assert.equal(resolveTopicKey(quoted, true), undefined);
  assert.equal(resolveTopicKey(groupEvent({}).message, true), undefined);
});

test("needsThreadCapabilityLookup only fires for ambiguous group replies", () => {
  assert.equal(needsThreadCapabilityLookup(groupEvent({ root_id: "om_a", parent_id: "om_a" }).message), true);
  assert.equal(needsThreadCapabilityLookup(groupEvent({ root_id: "om_a", parent_id: "om_a", thread_id: "omt" }).message), false);
  assert.equal(needsThreadCapabilityLookup(groupEvent({ root_id: "om_a", parent_id: "om_b" }).message), false);
  assert.equal(needsThreadCapabilityLookup(groupEvent({ chat_type: "p2p", root_id: "om_a", parent_id: "om_a" }).message), false);
});

test("deriveConversationTarget partitions group messages by topic", () => {
  const topic = groupEvent({ message_id: "om_root", thread_id: "omt_topic" });
  assert.deepEqual(deriveConversationTarget(topic, "ou_sender", false), {
    channelContext: "group:oc_chat/thread:om_root",
    threadUserId: "group:oc_chat/thread:om_root",
    threadKey: "om_root",
  });
  assert.deepEqual(deriveConversationTarget(groupEvent({}), "ou_sender", true), {
    channelContext: "group:oc_chat",
    threadUserId: "group:oc_chat",
  });
  assert.deepEqual(
    deriveConversationTarget(groupEvent({ chat_type: "p2p", thread_id: "omt" }), "ou_sender", true),
    { channelContext: "dm:ou_sender", threadUserId: "ou_sender" },
  );
});
