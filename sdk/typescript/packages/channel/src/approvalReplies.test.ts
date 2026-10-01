import assert from "node:assert/strict";
import test from "node:test";

import {
  APPROVAL_REPLY_PENDING_NOTICE,
  APPROVAL_REPLY_TIMEOUT_NOTICE,
  ApprovalReplyTracker,
  parseApprovalReplyDecision,
  type ApprovalReplyRecipient,
} from "./approvalReplies.js";

test("parseApprovalReplyDecision accepts keywords with a leading slash, case, and whitespace", () => {
  assert.equal(parseApprovalReplyDecision("yes"), "accept");
  assert.equal(parseApprovalReplyDecision("/yes"), "accept");
  assert.equal(parseApprovalReplyDecision("  /YES  "), "accept");
  assert.equal(parseApprovalReplyDecision("／同意"), "accept");
  assert.equal(parseApprovalReplyDecision("允许"), "accept");
  assert.equal(parseApprovalReplyDecision("/yes all"), "acceptForSession");
  assert.equal(parseApprovalReplyDecision("Yes   All"), "acceptForSession");
  assert.equal(parseApprovalReplyDecision("同意全部"), "acceptForSession");
  assert.equal(parseApprovalReplyDecision("那就同意全部"), "acceptForSession");
  assert.equal(parseApprovalReplyDecision("/no"), "decline");
  assert.equal(parseApprovalReplyDecision("不同意"), "decline");
  assert.equal(parseApprovalReplyDecision("deny"), "decline");
  assert.equal(parseApprovalReplyDecision("取消"), "cancel");
  assert.equal(parseApprovalReplyDecision("/cancel"), "cancel");
  assert.equal(parseApprovalReplyDecision("不同意全部"), null);
  assert.equal(parseApprovalReplyDecision("yes please"), null);
  assert.equal(parseApprovalReplyDecision("/new"), null);
  assert.equal(parseApprovalReplyDecision(""), null);
});

function createTracker(): { tracker: ApprovalReplyTracker; notices: Array<{ recipient: ApprovalReplyRecipient; text: string }> } {
  const notices: Array<{ recipient: ApprovalReplyRecipient; text: string }> = [];
  const tracker = new ApprovalReplyTracker({
    notify: async (recipient, text) => {
      notices.push({ recipient, text });
    },
  });
  return { tracker, notices };
}

test("ApprovalReplyTracker resolves only the matching sender and conversation", async () => {
  const { tracker } = createTracker();
  const first = tracker.wait("req-1", { channelContext: "group:1", userId: "10" }, 10_000);
  const second = tracker.wait("req-2", { channelContext: "group:2", userId: "20" }, 10_000);

  assert.equal(tracker.tryResolve({ channelContext: "group:2", userId: "10" }, "yes"), false);
  assert.equal(tracker.tryResolve({ channelContext: "group:2", userId: "20" }, "hello"), false);
  assert.equal(tracker.tryResolve({ channelContext: "group:2", userId: "20" }, "/yes all"), true);

  assert.equal(await second, "acceptForSession");

  tracker.resolveAll("cancel");
  assert.equal(await first, "cancel");
});

test("ApprovalReplyTracker reminds the approver while an approval is pending", async () => {
  const { tracker, notices } = createTracker();
  const approver = { channelContext: "user:1", userId: "1" };
  assert.equal(await tracker.remindIfPending(approver), false);

  const decision = tracker.wait("req-1", approver, 10_000);
  assert.equal(await tracker.remindIfPending({ channelContext: "user:2", userId: "2" }), false);
  assert.equal(await tracker.remindIfPending(approver), true);
  assert.deepEqual(notices, [{ recipient: approver, text: APPROVAL_REPLY_PENDING_NOTICE }]);

  tracker.cancel(approver);
  assert.equal(await decision, "cancel");
  assert.equal(await tracker.remindIfPending(approver), false);
});

test("ApprovalReplyTracker cancels on timeout and tells the approver", async () => {
  const { tracker, notices } = createTracker();
  const approver = { channelContext: "chat:1", userId: "u1" };

  assert.equal(await tracker.wait("req-1", approver, 5), "cancel");
  await new Promise((resolve) => setImmediate(resolve));

  assert.deepEqual(notices, [{ recipient: approver, text: APPROVAL_REPLY_TIMEOUT_NOTICE }]);
  assert.equal(tracker.tryResolve(approver, "yes"), false);
});
