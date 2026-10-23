import assert from "node:assert/strict";
import test from "node:test";

import { markdownToTelegramHtml, splitTelegramMessage } from "./formatting.js";

test("markdownToTelegramHtml formats markdown, escapes tags, and leaves link hrefs intact", () => {
  const cases: Array<[string, string]> = [
    ["**bold** and _italic_ and `code`", "<b>bold</b> and <i>italic</i> and <code>code</code>"],
    ["<script>alert(1)</script>", "&lt;script&gt;alert(1)&lt;/script&gt;"],
    ["[link](https://example.com/_foo_/bar)", '<a href="https://example.com/_foo_/bar">link</a>'],
    ["[link](https://example.com/**foo**/~~bar~~)", '<a href="https://example.com/**foo**/~~bar~~">link</a>'],
    ["[**bold**](https://example.com)", '<a href="https://example.com"><b>bold</b></a>'],
  ];
  for (const [markdown, html] of cases) {
    assert.equal(markdownToTelegramHtml(markdown), html);
  }
});

test("splitTelegramMessage keeps short content and splits long content on boundaries", () => {
  assert.deepEqual(splitTelegramMessage("hello"), ["hello"]);
  assert.deepEqual(splitTelegramMessage("alpha beta" + "\n" + "gamma delta" + "\n" + "epsilon", 12), ["alpha beta", "gamma delta", "epsilon"]);
});
