import assert from "node:assert/strict";
import test from "node:test";

import { buildCardActionDedupKey } from "./card-action-dedup.js";
import type { FeishuCardActionEvent } from "./feishu-types.js";

test("buildCardActionDedupKey prefers event_id when present", () => {
  const a = buildCardActionDedupKey({
    event_id: "evt_1",
    token: "t",
    context: { open_message_id: "om_1" },
    action: { value: { kind: "approval", requestId: "r1", decision: "accept" } },
  });
  assert.equal(a.weak, false);
  assert.equal(a.key, "action:event:evt_1");

  const b = buildCardActionDedupKey({
    event_id: "evt_2",
    token: "t",
    context: { open_message_id: "om_1" },
    action: { value: { kind: "approval", requestId: "r1", decision: "accept" } },
  });
  assert.notEqual(a.key, b.key);
});

test("buildCardActionDedupKey without event_id uses message, request, decision, operator", () => {
  const ev: FeishuCardActionEvent = {
    token: "constant-token",
    context: { open_message_id: "om_card_1" },
    action: { value: { kind: "approval", requestId: "req-a", decision: "accept" } },
    operator: { open_id: "ou_user" },
  };
  const x = buildCardActionDedupKey(ev);
  assert.equal(x.weak, false);
  const y = buildCardActionDedupKey({
    ...ev,
    action: { value: { kind: "approval", requestId: "req-b", decision: "accept" } },
  });
  assert.notEqual(x.key, y.key);
});

test("buildCardActionDedupKey marks weak when fields insufficient", () => {
  const w = buildCardActionDedupKey({
    token: "t",
    action: { value: { kind: "approval", requestId: "", decision: "accept" } },
  });
  assert.equal(w.weak, true);
  assert.ok(w.key.startsWith("action:weak:"));
});
