import assert from "node:assert/strict";
import test from "node:test";

import { buildCardActionDedupKey } from "./card-action-dedup.js";
import type { FeishuCardActionEvent } from "./feishu-types.js";

test("buildCardActionDedupKey prefers event_id, falls back to card fields, and marks weak keys", () => {
  const withEvent = buildCardActionDedupKey({
    event_id: "evt_1",
    token: "t",
    context: { open_message_id: "om_1" },
    action: { value: { kind: "approval", requestId: "r1", decision: "accept" } },
  });
  assert.equal(withEvent.weak, false);
  assert.equal(withEvent.key, "action:event:evt_1");

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

  const weak = buildCardActionDedupKey({
    token: "t",
    action: { value: { kind: "approval", requestId: "", decision: "accept" } },
  });
  assert.equal(weak.weak, true);
  assert.ok(weak.key.startsWith("action:weak:"));
});
