import assert from "node:assert/strict";
import test from "node:test";

import {
  getAtQQ,
  getImageUrl,
  getPlainText,
  isActionOk,
  normalizeMessageSegments,
  parseQQApprovalDecision,
} from "./index.js";

test("OneBot helpers extract text, at, and image fields", () => {
  const segments = normalizeMessageSegments([
    { type: "at", data: { qq: "123" } },
    { type: "text", data: { text: "hello" } },
    { type: "image", data: { url: "https://example.test/a.png" } },
  ]);

  assert.equal(getAtQQ(segments[0]), "123");
  assert.equal(getPlainText(segments), "hello");
  assert.equal(getImageUrl(segments[2]), "https://example.test/a.png");
});

test("isActionOk accepts OneBot ok status and retcode zero", () => {
  assert.equal(isActionOk({ status: "ok" }), true);
  assert.equal(isActionOk({ retcode: 0 }), true);
  assert.equal(isActionOk({ status: "failed", retcode: 1400 }), false);
});

test("QQ approval parser handles common text decisions", () => {
  assert.equal(parseQQApprovalDecision("yes"), "accept");
  assert.equal(parseQQApprovalDecision("yes all"), "acceptForSession");
  assert.equal(parseQQApprovalDecision("no"), "decline");
});
