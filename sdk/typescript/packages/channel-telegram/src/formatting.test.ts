import assert from "node:assert/strict";
import test from "node:test";

import { markdownToTelegramHtml, splitTelegramMessage } from "./formatting.js";

test("markdownToTelegramHtml converts basic markdown", () => {
  const html = markdownToTelegramHtml("**bold** and _italic_ and `code`");
  assert.equal(html, "<b>bold</b> and <i>italic</i> and <code>code</code>");
});

test("markdownToTelegramHtml escapes unsafe tags", () => {
  const html = markdownToTelegramHtml("<script>alert(1)</script>");
  assert.equal(html, "&lt;script&gt;alert(1)&lt;/script&gt;");
});

test("markdownToTelegramHtml keeps underscore patterns inside link href intact", () => {
  const html = markdownToTelegramHtml("[link](https://example.com/_foo_/bar)");
  assert.equal(html, '<a href="https://example.com/_foo_/bar">link</a>');
});

test("markdownToTelegramHtml keeps bold and strike markers inside link href intact", () => {
  const html = markdownToTelegramHtml("[link](https://example.com/**foo**/~~bar~~)");
  assert.equal(html, '<a href="https://example.com/**foo**/~~bar~~">link</a>');
});

test("markdownToTelegramHtml still formats markdown inside link text", () => {
  const html = markdownToTelegramHtml("[**bold**](https://example.com)");
  assert.equal(html, '<a href="https://example.com"><b>bold</b></a>');
});

test("splitTelegramMessage keeps shorter content intact", () => {
  assert.deepEqual(splitTelegramMessage("hello"), ["hello"]);
});

test("splitTelegramMessage splits long content on boundaries", () => {
  const chunks = splitTelegramMessage("alpha beta\ngamma delta\nepsilon", 12);
  assert.deepEqual(chunks, ["alpha beta", "gamma delta", "epsilon"]);
});
