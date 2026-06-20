import assert from "node:assert/strict";
import test from "node:test";

import { WeixinAdapter } from "./weixin-adapter.js";

class TestWeixinAdapter extends WeixinAdapter {
  async exposeSegmentCompleted(segmentText: string, isFinal: boolean, channelContext: string): Promise<boolean | void> {
    return await this.onSegmentCompleted("thread-1", "turn-1", segmentText, isFinal, channelContext);
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
  internals.contextTokens = { "wx-user-1": "ctx" };

  const sentTexts: string[] = [];
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
    const msg = body.msg as Record<string, unknown>;
    const item = (msg.item_list as Array<Record<string, { text?: string }>>)[0];
    sentTexts.push(item?.text_item?.text ?? "");
    return new Response("", { status: 200 });
  }) as typeof fetch;

  try {
    await adapter.exposeSegmentCompleted("先给你中间结果。", false, "wx-user-1");
    await adapter.exposeSegmentCompleted("最终结果。", true, "wx-user-1");

    assert.deepEqual(sentTexts, ["先给你中间结果。", "最终结果。"]);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Weixin reports segment delivery failure instead of acknowledging it", async () => {
  const originalFetch = globalThis.fetch;
  const originalError = console.error;
  const adapter = new TestWeixinAdapter();
  const internals = adapter as unknown as {
    apiBaseUrl: string;
    botToken: string;
    contextTokens: Record<string, string>;
  };
  internals.apiBaseUrl = "https://ilink.example";
  internals.botToken = "token";
  internals.contextTokens = { "wx-user-1": "ctx" };

  globalThis.fetch = (async () => new Response("bad request", { status: 400 })) as typeof fetch;
  console.error = () => {};

  try {
    const delivered = await adapter.exposeSegmentCompleted("不会成功发送。", false, "wx-user-1");
    assert.equal(delivered, false);
  } finally {
    globalThis.fetch = originalFetch;
    console.error = originalError;
  }
});

test("Weixin retries transient text fetch failures with the same client id", async () => {
  const originalFetch = globalThis.fetch;
  const adapter = new TestWeixinAdapter();
  const internals = adapter as unknown as {
    apiBaseUrl: string;
    botToken: string;
    contextTokens: Record<string, string>;
  };
  internals.apiBaseUrl = "https://ilink.example";
  internals.botToken = "token";
  internals.contextTokens = { "wx-user-1": "ctx" };

  const clientIds: string[] = [];
  let attempts = 0;
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    attempts += 1;
    const body = JSON.parse(String(init?.body)) as Record<string, unknown>;
    const msg = body.msg as Record<string, unknown>;
    clientIds.push(String(msg.client_id ?? ""));
    if (attempts < 3) throw new TypeError("fetch failed");
    return new Response("", { status: 200 });
  }) as typeof fetch;

  try {
    const delivered = await adapter.exposeSegmentCompleted("重试后成功。", false, "wx-user-1");
    assert.equal(delivered, true);
    assert.equal(attempts, 3);
    assert.equal(new Set(clientIds).size, 1);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("WeixinAdapter builds social binding target from user context", () => {
  const adapter = new WeixinAdapter() as unknown as {
    buildSocialTarget: (
      opts: Record<string, unknown>,
      sender: Record<string, unknown>,
      channelContext: string,
    ) => Record<string, unknown> | null;
  };

  const target = adapter.buildSocialTarget(
    {
      userId: "wx-user-1",
      userName: "Weixin User",
      text: "/bind 482913",
      channelContext: "wx-user-1",
    },
    {
      senderId: "wx-user-1",
      senderName: "Weixin User",
      senderRole: "admin",
      groupId: "wx-user-1",
    },
    "wx-user-1",
  );

  assert.deepEqual(target, {
    channelName: "weixin",
    conversationKind: "user",
    conversationId: "wx-user-1",
    deliveryTarget: "wx-user-1",
    displayName: "Weixin User",
    boundBy: {
      platformUserId: "wx-user-1",
      displayName: "Weixin User",
    },
  });
});

test("WeixinAdapter accepts social bind codes for user context", async () => {
  const adapter = new WeixinAdapter() as unknown as {
    client: {
      request: (method: string, params: Record<string, unknown>) => Promise<Record<string, unknown>>;
    };
    commandRouter: {
      routeBeforeQueue: () => Promise<"enqueue" | "handled">;
    };
    onDeliver: (target: string, content: string, metadata: Record<string, unknown>) => Promise<boolean>;
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  };
  const requests: Array<{ method: string; params: Record<string, unknown> }> = [];
  const deliveries: Array<{ target: string; content: string; metadata: Record<string, unknown> }> = [];

  adapter.commandRouter.routeBeforeQueue = async () => {
    throw new Error("bind command should not reach command routing");
  };
  adapter.client.request = async (method, params) => {
    requests.push({ method, params });
    if (method === "app/binding/request/get") {
      return {
        bindingRequestId: "request-1",
        appId: "com.dotharness.channel.weixin",
        threadId: "thread-1",
        bindingKind: "socialChannel",
        requestedScopes: ["conversation.receive", "message.send"],
      };
    }
    if (method === "app/binding/accept") {
      return {
        binding: {
          bindingId: "binding-1",
          appId: "com.dotharness.channel.weixin",
          threadId: "thread-1",
          state: "active",
          bindingKind: "socialChannel",
          socialTarget: params.socialTarget,
        },
      };
    }
    throw new Error(`unexpected request ${method}`);
  };
  adapter.onDeliver = async (target, content, metadata) => {
    deliveries.push({ target, content, metadata });
    return true;
  };

  await adapter.handleMessage({
    userId: "wx-user-1",
    userName: "Weixin User",
    text: "/bind 482913",
    channelContext: "wx-user-1",
    senderExtra: { senderRole: "admin" },
  });

  assert.deepEqual(requests[0], {
    method: "app/binding/request/get",
    params: {
      appId: "com.dotharness.channel.weixin",
      bindCode: "482913",
      requestToken: "482913",
    },
  });
  assert.equal(requests[1]?.method, "app/binding/accept");
  assert.deepEqual(requests[1]?.params.socialTarget, {
    channelName: "weixin",
    conversationKind: "user",
    conversationId: "wx-user-1",
    deliveryTarget: "wx-user-1",
    displayName: "Weixin User",
    boundBy: {
      platformUserId: "wx-user-1",
      displayName: "Weixin User",
    },
  });
  assert.equal(requests[1]?.params.grantId, "social:weixin::user:wx-user-1");
  assert.equal(requests[1]?.params.approvedBy, "wx-user-1");
  assert.equal(requests[1]?.params.auditRef, "channel:weixin:user:wx-user-1");
  assert.deepEqual(deliveries, [
    {
      target: "wx-user-1",
      content: "Bound this conversation to thread thread-1.",
      metadata: {
        appId: "com.dotharness.channel.weixin",
        bindingId: "binding-1",
        bindingKind: "socialChannel",
      },
    },
  ]);
});

test("Weixin consumes pending user-input replies before forwarding to agent", async () => {
  type PendingUserInput = {
    request: Record<string, unknown>;
    resolve: (response: Record<string, unknown>) => void;
  };
  const adapter = new WeixinAdapter() as unknown as {
    userInputWaiters: Map<string, PendingUserInput>;
    handleInboundUserMessage: (msg: {
      from_user_id?: string;
      item_list?: { type?: number; text_item?: { text?: string } }[];
      context_token?: string;
    }) => Promise<void>;
    handleMessage: (opts: Record<string, unknown>) => Promise<void>;
  };
  let forwarded = false;
  const resolved: Record<string, unknown>[] = [];
  adapter.handleMessage = async () => {
    forwarded = true;
  };
  adapter.userInputWaiters.set("wx-user-1", {
    request: {
      requestId: "req-1",
      questions: [
        {
          id: "mode",
          header: "Pick a mode",
          question: "Which mode?",
          options: [{ label: "Auto" }, { label: "Manual" }],
        },
      ],
    },
    resolve: (response) => resolved.push(response),
  });

  await adapter.handleInboundUserMessage({
    from_user_id: "wx-user-1",
    item_list: [{ type: 1, text_item: { text: "2" } }],
  });

  assert.equal(forwarded, false);
  assert.deepEqual(resolved, [{ answers: { mode: { answers: ["Manual"] } } }]);
  assert.equal(adapter.userInputWaiters.size, 0);
});
