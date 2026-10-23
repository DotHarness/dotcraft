import assert from "node:assert/strict";
import test from "node:test";

import {
  getAtQQ,
  getImageUrl,
  getPlainText,
  isActionOk,
  normalizeMessageSegments,
} from "./index.js";

test("OneBot helpers extract segment fields and recognize successful actions", () => {
  const segments = normalizeMessageSegments([
    { type: "at", data: { qq: "123" } },
    { type: "text", data: { text: "hello" } },
    { type: "image", data: { url: "https://example.test/a.png" } },
  ]);

  assert.equal(getAtQQ(segments[0]), "123");
  assert.equal(getPlainText(segments), "hello");
  assert.equal(getImageUrl(segments[2]), "https://example.test/a.png");
  assert.equal(isActionOk({ status: "ok" }), true);
  assert.equal(isActionOk({ retcode: 0 }), true);
  assert.equal(isActionOk({ status: "failed", retcode: 1400 }), false);
});
