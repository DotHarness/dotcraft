import assert from "node:assert/strict";
import test from "node:test";

import { getDeliveredFrontier } from "./deliveredFrontier.js";

test("getDeliveredFrontier treats delivered as prefix of current", () => {
  assert.equal(getDeliveredFrontier("hello world", "hello "), 6);
  assert.equal(getDeliveredFrontier("abcabc", "abc"), 3);
});

test("getDeliveredFrontier uses first suffix match for repeated patterns", () => {
  assert.equal(getDeliveredFrontier("xabcabc", "abc"), 4);
});

test("getDeliveredFrontier returns 0 when nothing matches", () => {
  assert.equal(getDeliveredFrontier("hello", "xyz"), 0);
});

test("getDeliveredFrontier handles delivered longer than current (suffix case)", () => {
  assert.equal(getDeliveredFrontier("world", "hello world"), 5);
});
