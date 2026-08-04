import assert from "node:assert/strict";
import test from "node:test";

import {
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";

test("extractAgentReplyTextsFromTurnCompletedParams keeps wire order", () => {
  const texts = extractAgentReplyTextsFromTurnCompletedParams({
    turn: {
      items: [
        { id: "tool-1", type: "toolCall", payload: {} },
        { id: "a1", type: "agentMessage", payload: { text: "first " } },
        { id: "a2", type: "agentMessage", payload: { text: "second" } },
      ],
    },
  });

  assert.deepEqual(texts, ["first ", "second"]);
  assert.equal(extractAgentReplyTextFromTurnCompletedParams({ turn: { items: [{ type: "agentMessage", payload: { text: "ok" } }] } }), "ok");
});

test("mergeReplyTextFromDeltaAndSnapshot prefers containing variant", () => {
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("存在。", "文件存在。"), "文件存在。");
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("文件存在。", "存在。"), "文件存在。");
});

test("mergeReplyTextFromDeltaAndSnapshot handles empty values", () => {
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("", "snapshot"), "snapshot");
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("delta", ""), "delta");
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("", ""), "");
});

test("mergeReplyTextFromDeltaAndSnapshot falls back to longer text", () => {
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("abc", "ab"), "abc");
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("ab", "abc"), "abc");
});

test("mergeReplyTextFromDeltaAndSnapshot appends divergent tail after common prefix", () => {
  assert.equal(
    mergeReplyTextFromDeltaAndSnapshot("hello world", "hello there"),
    "hello world\n\nthere",
  );
  assert.equal(
    mergeReplyTextFromDeltaAndSnapshot("hello there", "hello world"),
    "hello there\n\nworld",
  );
});

test("mergeReplyTextFromDeltaAndSnapshot does not split on a shared first letter of different words", () => {
  assert.equal(
    mergeReplyTextFromDeltaAndSnapshot("Hello streamed", "Hello snapshot"),
    "Hello streamed\n\nsnapshot",
  );
});

test("mergeReplyTextFromDeltaAndSnapshot falls back when no shared prefix", () => {
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("aaa", "bbb"), "bbb");
  assert.equal(mergeReplyTextFromDeltaAndSnapshot("bbb", "aaa"), "aaa");
});
