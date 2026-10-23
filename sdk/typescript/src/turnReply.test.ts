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

test("mergeReplyTextFromDeltaAndSnapshot reconciles streamed deltas with the final snapshot", () => {
  const cases: Array<[string, string, string]> = [
    ["存在。", "文件存在。", "文件存在。"],
    ["文件存在。", "存在。", "文件存在。"],
    ["", "snapshot", "snapshot"],
    ["delta", "", "delta"],
    ["", "", ""],
    ["abc", "ab", "abc"],
    ["ab", "abc", "abc"],
    ["hello world", "hello there", "hello world\n\nthere"],
    ["Hello streamed", "Hello snapshot", "Hello streamed\n\nsnapshot"],
    ["aaa", "bbb", "bbb"],
    ["bbb", "aaa", "aaa"],
  ];
  for (const [delta, snapshot, expected] of cases) {
    assert.equal(mergeReplyTextFromDeltaAndSnapshot(delta, snapshot), expected, `${delta} | ${snapshot}`);
  }
});
