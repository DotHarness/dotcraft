import assert from "node:assert/strict";
import test from "node:test";

import { WeixinAdapter } from "./weixin-adapter.js";

class TestWeixinAdapter extends WeixinAdapter {
  async exposeSegmentCompleted(segmentText: string, isFinal: boolean, channelContext: string): Promise<void> {
    await this.onSegmentCompleted("thread-1", "turn-1", segmentText, isFinal, channelContext);
  }
}

test("Weixin sends non-final segments immediately instead of waiting for turn completion", async () => {
  const originalFetch = globalThis.fetch;
  const adapter = new TestWeixinAdapter();
  const internals = adapter as unknown as {
    apiBaseUrl: string;
    botToken: string;
    contextTokens: Record<string, string>;
  };
  internals.apiBaseUrl = "https://ilink.example";
  internals.botToken = "token";
  internals.contextTokens = { "user@im.wechat": "ctx" };

  const sentTexts: string[] = [];
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
    const msg = body.msg as Record<string, unknown>;
    const item = (msg.item_list as Array<Record<string, { text?: string }>>)[0];
    sentTexts.push(item?.text_item?.text ?? "");
    return new Response("", { status: 200 });
  }) as typeof fetch;

  try {
    await adapter.exposeSegmentCompleted("先给你中间结果。", false, "user@im.wechat");
    await adapter.exposeSegmentCompleted("最终结果。", true, "user@im.wechat");

    assert.deepEqual(sentTexts, ["先给你中间结果。", "最终结果。"]);
  } finally {
    globalThis.fetch = originalFetch;
  }
});
