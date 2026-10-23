import assert from "node:assert/strict";
import test from "node:test";

import { WeComPusher } from "./wecom-pusher.js";

test("WeComPusher builds upload_media URLs from the webhook key and requires one", () => {
  const pusher = new WeComPusher("chat1", "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc123");
  assert.equal(
    pusher.buildUploadUrl("file"),
    "https://qyapi.weixin.qq.com/cgi-bin/webhook/upload_media?key=abc123&type=file",
  );
  const keyless = new WeComPusher("chat1", "https://qyapi.weixin.qq.com/cgi-bin/webhook/send");
  assert.throws(() => keyless.buildUploadUrl("file"), /key/);
});

test("WeComPusher rejects webhook responses with a nonzero errcode", async () => {
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async () =>
    new Response(JSON.stringify({ errcode: 40008, errmsg: "invalid message type" }), { status: 200 })) as typeof fetch;
  try {
    const pusher = new WeComPusher("chat1", "https://qyapi.weixin.qq.com/cgi-bin/webhook/send?key=abc123");
    await assert.rejects(() => pusher.pushImage(Buffer.from("image")), /40008/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});
