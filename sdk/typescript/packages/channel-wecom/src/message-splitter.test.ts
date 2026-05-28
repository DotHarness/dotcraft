import assert from "node:assert/strict";
import test from "node:test";

import { splitWeComMessage } from "./message-splitter.js";

test("splitWeComMessage keeps short content intact", () => {
  assert.deepEqual(splitWeComMessage("hello", 10), ["hello"]);
});

test("splitWeComMessage respects utf8 byte limit", () => {
  const chunks = splitWeComMessage("你好世界你好世界", 12);
  assert.ok(chunks.length > 1);
  assert.ok(chunks.every((chunk) => Buffer.byteLength(chunk, "utf-8") <= 12));
});

