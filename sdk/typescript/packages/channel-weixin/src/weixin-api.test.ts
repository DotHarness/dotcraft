import assert from "node:assert/strict";
import test from "node:test";

import { getUploadUrl } from "./weixin-api.js";

test("getUploadUrl posts media metadata with base_info", async () => {
  const originalFetch = globalThis.fetch;
  let capturedUrl = "";
  let capturedBody: Record<string, unknown> = {};
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    capturedUrl = String(input);
    capturedBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
    return new Response(JSON.stringify({ upload_param: "upload-token" }), { status: 200 });
  }) as typeof fetch;

  try {
    const result = await getUploadUrl({
      baseUrl: "https://ilink.example",
      token: "token",
      body: {
        filekey: "file-1",
        media_type: 3,
        to_user_id: "user@im.wechat",
        rawsize: 12,
        rawfilemd5: "md5",
        filesize: 16,
        no_need_thumb: true,
        aeskey: "aes",
      },
    });

    assert.equal(capturedUrl, "https://ilink.example/ilink/bot/getuploadurl");
    assert.equal(capturedBody.filekey, "file-1");
    assert.equal(capturedBody.media_type, 3);
    assert.equal(capturedBody.to_user_id, "user@im.wechat");
    assert.deepEqual(capturedBody.base_info, { channel_version: "0.1.0" });
    assert.equal(result.upload_param, "upload-token");
  } finally {
    globalThis.fetch = originalFetch;
  }
});
