import assert from "node:assert/strict";
import test from "node:test";

import { FeishuClient } from "./feishu-client.js";
import { FeishuApiError } from "./feishu-types.js";

function createClient(): FeishuClient {
  return new FeishuClient({
    appId: "cli_test",
    appSecret: "secret_test",
  });
}

test("FeishuApiError preserves public fields and readable message", () => {
  const error = new FeishuApiError({
    kind: "rateLimited",
    retryable: true,
    code: 99991429,
    msg: "too many requests",
    httpStatus: 429,
    raw: { code: 99991429 },
    message: "Failed to send Feishu text message. too many requests code=99991429 httpStatus=429",
  });

  assert.equal(error.kind, "rateLimited");
  assert.equal(error.retryable, true);
  assert.equal(error.code, 99991429);
  assert.equal(error.msg, "too many requests");
  assert.equal(error.httpStatus, 429);
  assert.match(error.message, /too many requests/);
});

test("Feishu client classifies fetch failures with FeishuApiError", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 99991429,
        msg: "rate limited",
      }),
      { status: 429, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    await assert.rejects(
      async () =>
        await (client as unknown as {
          sendMessage: (
            receiveId: string,
            receiveIdType: "chat_id" | "open_id",
            msgType: string,
            content: Record<string, unknown>,
          ) => Promise<Record<string, unknown>>;
        }).sendMessage("oc_chat_123", "chat_id", "text", { text: "hello" }),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "rateLimited");
        assert.equal(error.retryable, true);
        assert.equal(error.code, 99991429);
        assert.equal(error.httpStatus, 429);
        assert.match(error.message, /rate limited/);
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client classifies SDK failures with FeishuApiError", async () => {
  const client = createClient();
  (client as unknown as { sdk: unknown }).sdk = {
    im: {
      messageReaction: {
        async create(): Promise<void> {
          throw {
            code: 99991403,
            msg: "permission denied",
          };
        },
      },
    },
  };

  await assert.rejects(
    async () => await client.addMessageReaction("om_test_1", "GLANCE"),
    (error: unknown) => {
      assert.ok(error instanceof FeishuApiError);
      assert.equal(error.kind, "permission");
      assert.equal(error.retryable, false);
      assert.equal(error.code, 99991403);
      assert.match(error.message, /permission denied/);
      return true;
    },
  );
});

test("Feishu client keeps successful SDK response shape", async () => {
  const client = createClient();
  (client as unknown as { sdk: unknown }).sdk = {
    im: {
      message: {
        async create(): Promise<{ data: { message_id: string; chat_id: string } }> {
          return {
            data: {
              message_id: "om_success_1",
              chat_id: "oc_chat_123",
            },
          };
        },
      },
    },
  };

  const result = await client.sendInteractiveCard("group:oc_chat_123", {
    schema: "1.0",
  });

  assert.deepEqual(result, {
    messageId: "om_success_1",
    chatId: "oc_chat_123",
  });
});

test("Feishu client sends text messages through the shared message API", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          message_id: "om_text_1",
          chat_id: "oc_chat_123",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.sendTextMessage("group:oc_chat_123", "hello text");
    assert.deepEqual(result, {
      messageId: "om_text_1",
      chatId: "oc_chat_123",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /receive_id_type=chat_id/);
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, string>;
    assert.equal(body.msg_type, "text");
    assert.deepEqual(JSON.parse(body.content), { text: "hello text" });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client rejects empty text sends before calling the API", async () => {
  const client = createClient();
  await assert.rejects(
    async () => await client.sendTextMessage("group:oc_chat_123", "   "),
    (error: unknown) => {
      assert.ok(error instanceof TypeError);
      assert.match(String((error as Error).message), /non-empty text/);
      return true;
    },
  );
});

test("Feishu client replies to a message with explicit token auth", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          message_id: "om_reply_1",
          chat_id: "oc_chat_456",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.replyToMessage("om_origin_1", "reply text", {
      replyInThread: true,
      uuid: "uuid-1",
    });
    assert.deepEqual(result, {
      messageId: "om_reply_1",
      chatId: "oc_chat_456",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /\/messages\/om_origin_1\/reply$/);
    assert.equal((calls[1]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, string | boolean>;
    assert.equal(body.msg_type, "text");
    assert.equal(body.reply_in_thread, true);
    assert.equal(body.uuid, "uuid-1");
    assert.deepEqual(JSON.parse(String(body.content)), { text: "reply text" });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client classifies reply failures with FeishuApiError", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 99991403,
        msg: "permission denied",
      }),
      { status: 403, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    await assert.rejects(
      async () => await client.replyToMessage("om_origin_1", "reply text"),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "permission");
        assert.equal(error.httpStatus, 403);
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client probes bot info with explicit tenant token and parses identity", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          app_name: "DotCraft",
          tenant_key: "tenant-1",
          activate_status: 1,
        },
        bot: {
          open_id: "ou_bot_123",
          app_name: "DotCraft",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.probeBot();
    assert.equal(result.openId, "ou_bot_123");
    assert.equal(result.hasBotIdentity, true);
    assert.equal(result.appName, "DotCraft");
    assert.equal(result.diagnosticTag, undefined);
    assert.ok(result.rawFieldKeys?.includes("bot.open_id"));
    assert.match(calls[1]!.url, /\/bot\/v3\/info$/);
    assert.equal((calls[1]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client marks missing bot identity as capability disabled when status hints say so", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          app_name: "DotCraft",
          activate_status: 0,
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.probeBot();
    assert.equal(result.hasBotIdentity, false);
    assert.equal(result.diagnosticTag, "botCapabilityDisabled");
    assert.match(result.diagnosticMessage ?? "", /disabled|unpublished/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client marks missing bot identity as shape mismatch when capability hints are absent", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          app_name: "DotCraft",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.probeBot();
    assert.equal(result.hasBotIdentity, false);
    assert.equal(result.diagnosticTag, "identityFieldsMissing");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client raises auth errors when bot probe authorization fails", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (_input: string | URL | Request) =>
    new Response(
      JSON.stringify({
        code: 99991663,
        msg: "token invalid",
      }),
      { status: 401, headers: { "Content-Type": "application/json" } },
    )) as typeof fetch;

  try {
    await assert.rejects(
      async () => await client.probeBot(),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "auth");
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client lists chat messages with normalized metadata and raw content", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          has_more: true,
          page_token: "next-page",
          items: [
            {
              message_id: "om_hist_1",
              chat_id: "oc_chat_123",
              chat_type: "group",
              msg_type: "text",
              create_time: "1710000000000",
              parent_id: "om_parent_1",
              root_id: "om_root_1",
              sender: {
                sender_type: "user",
                tenant_key: "tenant-1",
                id: {
                  open_id: "ou_user_1",
                  user_id: "user_1",
                  union_id: "union_1",
                },
              },
              mentions: [
                {
                  key: "@_user_1",
                  id: {
                    open_id: "ou_mention_1",
                  },
                  name: "Mentioned User",
                },
              ],
              body: {
                content: "{\"text\":\"hello history\"}",
              },
            },
          ],
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.listChatMessages("oc_chat_123", {
      startTime: "1710000000000",
      endTime: "1710003600000",
      pageSize: 50,
      pageToken: "current-page",
    });
    assert.equal(result.nextPageToken, "next-page");
    assert.equal(result.hasMore, true);
    assert.equal(result.items.length, 1);
    assert.deepEqual(result.items[0], {
      messageId: "om_hist_1",
      chatId: "oc_chat_123",
      chatType: "group",
      messageType: "text",
      createTime: "1710000000000",
      parentId: "om_parent_1",
      rootId: "om_root_1",
      sender: {
        openId: "ou_user_1",
        userId: "user_1",
        unionId: "union_1",
        senderType: "user",
        tenantKey: "tenant-1",
      },
      mentions: [
        {
          key: "@_user_1",
          id: {
            open_id: "ou_mention_1",
            user_id: undefined,
            union_id: undefined,
          },
          name: "Mentioned User",
          tenant_key: undefined,
        },
      ],
      rawContent: "{\"text\":\"hello history\"}",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /container_id_type=chat/);
    assert.match(calls[1]!.url, /container_id=oc_chat_123/);
    assert.match(calls[1]!.url, /start_time=1710000000000/);
    assert.match(calls[1]!.url, /end_time=1710003600000/);
    assert.match(calls[1]!.url, /page_size=50/);
    assert.match(calls[1]!.url, /page_token=current-page/);
    assert.equal((calls[1]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client rejects invalid history lookup arguments before calling the API", async () => {
  const client = createClient();
  await assert.rejects(
    async () =>
      await client.listChatMessages("", {
        startTime: "1710000000000",
      }),
    (error: unknown) => {
      assert.ok(error instanceof TypeError);
      assert.match(String((error as Error).message), /chatId/);
      return true;
    },
  );

  await assert.rejects(
    async () =>
      await client.listChatMessages("oc_chat_123", {
        startTime: "   ",
      }),
    (error: unknown) => {
      assert.ok(error instanceof TypeError);
      assert.match(String((error as Error).message), /startTime/);
      return true;
    },
  );

  await assert.rejects(
    async () =>
      await client.listChatMessages("oc_chat_123", {
        startTime: "1710000000000",
        pageSize: 0,
      }),
    (error: unknown) => {
      assert.ok(error instanceof TypeError);
      assert.match(String((error as Error).message), /pageSize/);
      return true;
    },
  );
});

test("Feishu client classifies history lookup failures with FeishuApiError", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 99991403,
        msg: "permission denied",
      }),
      { status: 403, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    await assert.rejects(
      async () =>
        await client.listChatMessages("oc_chat_123", {
          startTime: "1710000000000",
        }),
      (error: unknown) => {
        assert.ok(error instanceof FeishuApiError);
        assert.equal(error.kind, "permission");
        assert.equal(error.httpStatus, 403);
        return true;
      },
    );
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client creates docx documents and derives share URLs", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          document: {
            document_id: "doxABCDEFGHIJKLMNOPQRSTUVWX",
            revision_id: 11,
            title: "Team Notes",
          },
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.createDocxDocument({
      title: "Team Notes",
      folderToken: "fldcn123",
    });

    assert.deepEqual(result, {
      documentId: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      revisionId: 11,
      title: "Team Notes",
      url: "https://feishu.cn/docx/doxABCDEFGHIJKLMNOPQRSTUVWX",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /\/open-apis\/docx\/v1\/documents$/);
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, string>;
    assert.equal(body.title, "Team Notes");
    assert.equal(body.folder_token, "fldcn123");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client reads docx raw content", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          content: "hello docx",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.getDocxRawContent("doxABCDEFGHIJKLMNOPQRSTUVWX");
    assert.deepEqual(result, {
      documentId: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      content: "hello docx",
    });
    assert.equal(calls.length, 2);
    assert.match(calls[1]!.url, /\/raw_content$/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client appends docx blocks at the root with revision and client token query params", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }

    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          document_revision_id: 12,
          children: [
            {
              block_id: "blk_1",
              block_type: 3,
            },
            {
              block_id: "blk_2",
              block_type: 2,
            },
          ],
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.createDocxBlocks("doxABCDEFGHIJKLMNOPQRSTUVWX", "doxABCDEFGHIJKLMNOPQRSTUVWX", {
      children: [
        { block_type: 3, heading1: { elements: [{ text_run: { content: "Heading" } }] } },
        { block_type: 2, text: { elements: [{ text_run: { content: "Paragraph" } }] } },
      ],
      documentRevisionId: 9,
      index: -1,
      clientToken: "client-token-1",
    });

    assert.deepEqual(result, {
      documentId: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      revisionId: 12,
      blocks: [
        { blockId: "blk_1", blockType: 3 },
        { blockId: "blk_2", blockType: 2 },
      ],
    });
    assert.equal(calls.length, 2);
    assert.match(
      calls[1]!.url,
      /\/open-apis\/docx\/v1\/documents\/doxABCDEFGHIJKLMNOPQRSTUVWX\/blocks\/doxABCDEFGHIJKLMNOPQRSTUVWX\/children\?/,
    );
    assert.match(calls[1]!.url, /document_revision_id=9/);
    assert.match(calls[1]!.url, /client_token=client-token-1/);
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, unknown>;
    assert.deepEqual(body, {
      children: [
        { block_type: 3, heading1: { elements: [{ text_run: { content: "Heading" } }] } },
        { block_type: 2, text: { elements: [{ text_run: { content: "Paragraph" } }] } },
      ],
      index: -1,
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client creates wiki nodes and updates wiki titles", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    if (url.includes("/update_title")) {
      return new Response(JSON.stringify({ code: 0, data: {} }), {
        status: 200,
        headers: { "Content-Type": "application/json" },
      });
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          node: {
            space_id: "6946843325487906839",
            node_token: "wikABCDEFGHIJKLMNOPQRSTUVWX",
            obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
            obj_type: "docx",
            node_type: "origin",
            parent_node_token: "wikParentABCDEFGHIJKLMNOPQRST",
          },
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const created = await client.createWikiNode({
      spaceId: "6946843325487906839",
      parentNodeToken: "wikParentABCDEFGHIJKLMNOPQRST",
    });
    assert.deepEqual(created, {
      spaceId: "6946843325487906839",
      nodeToken: "wikABCDEFGHIJKLMNOPQRSTUVWX",
      objToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      objType: "docx",
      nodeType: "origin",
      parentNodeToken: "wikParentABCDEFGHIJKLMNOPQRST",
      originNodeToken: undefined,
      originSpaceId: undefined,
      hasChild: undefined,
      title: undefined,
      objCreateTime: undefined,
      objEditTime: undefined,
      nodeCreateTime: undefined,
    });
    await client.updateWikiNodeTitle("6946843325487906839", "wikABCDEFGHIJKLMNOPQRSTUVWX", "Release Notes");
    assert.equal(calls.length, 3);
    assert.match(calls[1]!.url, /\/wiki\/v2\/spaces\/6946843325487906839\/nodes$/);
    assert.match(calls[2]!.url, /\/update_title$/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client gets and lists wiki nodes", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    if (url.includes("/spaces/get_node")) {
      return new Response(
        JSON.stringify({
          code: 0,
          data: {
            node: {
              space_id: "6946843325487906839",
              node_token: "wikABCDEFGHIJKLMNOPQRSTUVWX",
              obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
              obj_type: "docx",
              node_type: "origin",
            },
          },
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [
            {
              space_id: "6946843325487906839",
              node_token: "wikABCDEFGHIJKLMNOPQRSTUVWX",
              obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
              obj_type: "docx",
              node_type: "origin",
              title: "Release Notes",
              has_child: true,
            },
          ],
          has_more: true,
          page_token: "next-page",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const node = await client.getWikiNode("wikABCDEFGHIJKLMNOPQRSTUVWX");
    assert.equal(node.objToken, "doxABCDEFGHIJKLMNOPQRSTUVWX");
    const page = await client.listWikiNodes({
      spaceId: "6946843325487906839",
      parentNodeToken: "wikABCDEFGHIJKLMNOPQRSTUVWX",
      pageSize: 20,
      pageToken: "current-page",
    });
    assert.equal(page.items.length, 1);
    assert.equal(page.nextPageToken, "next-page");
    assert.equal(page.hasMore, true);
    assert.match(calls[1]!.url, /\/spaces\/get_node\?/);
    assert.match(calls[2]!.url, /parent_node_token=wikABCDEFGHIJKLMNOPQRSTUVWX/);
    assert.match(calls[2]!.url, /page_size=20/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client moves docx into wiki and returns task fields", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          task_id: "7037044037068177428-abc",
          applied: true,
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const result = await client.moveDocxToWiki({
      spaceId: "6946843325487906839",
      objToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      parentWikiToken: "wikABCDEFGHIJKLMNOPQRSTUVWX",
      apply: true,
    });
    assert.deepEqual(result, {
      wikiToken: undefined,
      taskId: "7037044037068177428-abc",
      applied: true,
    });
    assert.match(calls[1]!.url, /\/nodes\/move_docs_to_wiki$/);
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, unknown>;
    assert.deepEqual(body, {
      obj_type: "docx",
      obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      parent_wiki_token: "wikABCDEFGHIJKLMNOPQRSTUVWX",
      apply: true,
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client queries wiki move task status", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          task: {
            task_id: "7037044037068177428-abc",
            move_result: {
              status: 2,
              status_msg: "success",
              wiki_token: "wikNEWTOKENXXXXXXXXXXXXXXX",
              obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
              obj_type: "docx",
            },
          },
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const status = await client.getWikiMoveTask("7037044037068177428-abc");
    assert.deepEqual(status, {
      taskId: "7037044037068177428-abc",
      status: 2,
      statusMessage: "success",
      wikiToken: "wikNEWTOKENXXXXXXXXXXXXXXX",
      objToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      objType: "docx",
    });
    assert.match(calls[1]!.url, /\/wiki\/v2\/tasks\/7037044037068177428-abc\?/);
    assert.match(calls[1]!.url, /task_type=move/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client moves wiki node and maps returned node info", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          node: {
            space_id: "6946843325487906839",
            node_token: "wikABCDEFGHIJKLMNOPQRSTUVWX",
            obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
            obj_type: "docx",
            node_type: "origin",
            parent_node_token: "wikPARENTXXXXXXXXXXXXXXXXXX",
            title: "Relocated Page",
          },
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const node = await client.moveWikiNode({
      spaceId: "6946843325487906839",
      nodeToken: "wikABCDEFGHIJKLMNOPQRSTUVWX",
      targetParentToken: "wikPARENTXXXXXXXXXXXXXXXXXX",
    });
    assert.equal(node.parentNodeToken, "wikPARENTXXXXXXXXXXXXXXXXXX");
    assert.equal(node.title, "Relocated Page");
    assert.match(
      calls[1]!.url,
      /\/spaces\/6946843325487906839\/nodes\/wikABCDEFGHIJKLMNOPQRSTUVWX\/move$/,
    );
    const body = JSON.parse(String(calls[1]!.init?.body ?? "{}")) as Record<string, unknown>;
    assert.deepEqual(body, {
      target_parent_token: "wikPARENTXXXXXXXXXXXXXXXXXX",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client lists wiki spaces with pagination", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [
            {
              space_id: "6946843325487906839",
              name: "Team Wiki",
              description: "Shared workspace",
              visibility: "public",
              space_type: "team",
              open_sharing: "open",
            },
          ],
          has_more: true,
          page_token: "next-page",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const page = await client.listWikiSpaces({ pageSize: 20, pageToken: "current-page" });
    assert.equal(page.items.length, 1);
    assert.equal(page.items[0]!.spaceId, "6946843325487906839");
    assert.equal(page.items[0]!.name, "Team Wiki");
    assert.equal(page.items[0]!.visibility, "public");
    assert.equal(page.nextPageToken, "next-page");
    assert.equal(page.hasMore, true);
    assert.match(calls[1]!.url, /\/wiki\/v2\/spaces\?/);
    assert.match(calls[1]!.url, /page_size=20/);
    assert.match(calls[1]!.url, /page_token=current-page/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client gets wiki space metadata", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          space: {
            space_id: "6946843325487906839",
            name: "Team Wiki",
            description: "Shared workspace",
            visibility: "public",
            space_type: "team",
            open_sharing: "open",
          },
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const space = await client.getWikiSpace("6946843325487906839");
    assert.equal(space.spaceId, "6946843325487906839");
    assert.equal(space.name, "Team Wiki");
    assert.equal(space.visibility, "public");
    assert.match(calls[1]!.url, /\/wiki\/v2\/spaces\/6946843325487906839$/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client reverse-looks-up wiki node from docx obj_token", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({
          code: 0,
          tenant_access_token: "tenant_token",
          expire: 7200,
        }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          node: {
            space_id: "6946843325487906839",
            node_token: "wikABCDEFGHIJKLMNOPQRSTUVWX",
            obj_token: "doxABCDEFGHIJKLMNOPQRSTUVWX",
            obj_type: "docx",
            node_type: "origin",
          },
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;

  try {
    const node = await client.getWikiNode("doxABCDEFGHIJKLMNOPQRSTUVWX", "docx");
    assert.equal(node.nodeToken, "wikABCDEFGHIJKLMNOPQRSTUVWX");
    assert.equal(node.objType, "docx");
    assert.match(calls[1]!.url, /\/spaces\/get_node\?/);
    assert.match(calls[1]!.url, /obj_type=docx/);
    assert.match(calls[1]!.url, /token=doxABCDEFGHIJKLMNOPQRSTUVWX/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client lists docx blocks", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [{ block_id: "blk_1", block_type: 2, parent_id: "doc_1", children: [] }],
          has_more: false,
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const page = await client.listDocxBlocks({ documentId: "DocxPlaceholder000000000001", pageSize: 20 });
    assert.equal(page.items.length, 1);
    assert.equal(page.items[0]?.blockId, "blk_1");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client updates docx blocks through batch_update", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/blocks\/batch_update$/);
    const body = JSON.parse(String(init?.body ?? "{}")) as { requests?: unknown[] };
    assert.equal(Array.isArray(body.requests), true);
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [{ block_id: "blk_1", block_type: 2 }],
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const result = await client.updateDocxBlocks("DocxPlaceholder000000000001", [
      { block_id: "blk_1", replace_text: { elements: [] } },
    ]);
    assert.equal(result.updatedBlocks.length, 1);
    assert.equal(result.updatedBlocks[0]?.blockId, "blk_1");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client deletes docx child range", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/children\/batch_delete$/);
    assert.equal(init?.method, "DELETE");
    return new Response(JSON.stringify({ code: 0, data: {} }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  }) as typeof fetch;
  try {
    const result = await client.deleteDocxBlockChildren("DocxPlaceholder000000000001", "DocxPlaceholder000000000001", 0, 1);
    assert.equal(result.startIndex, 0);
    assert.equal(result.endIndex, 1);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client lists docx comments", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  const calls: Array<{ url: string; init?: RequestInit }> = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    calls.push({ url, init });
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [
            {
              comment_id: "comment_1",
              user_id: "ou_user_1",
              create_time: "1710000000000",
              is_solved: false,
              is_whole: false,
              reply_list: {
                replies: [{ reply_id: "reply_1", user_id: "ou_user_1", create_time: "1710000000001" }],
                has_more: false,
              },
            },
          ],
          has_more: true,
          page_token: "next-page",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const page = await client.listDocxComments({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      pageSize: 20,
      pageToken: "current-page",
      isSolved: false,
      isWhole: false,
    });
    assert.equal(page.items.length, 1);
    assert.equal(page.items[0]?.commentId, "comment_1");
    assert.equal(page.items[0]?.replyList.replies[0]?.replyId, "reply_1");
    assert.equal(page.nextPageToken, "next-page");
    assert.equal(page.hasMore, true);
    assert.match(calls[1]!.url, /\/drive\/v1\/files\/doxABCDEFGHIJKLMNOPQRSTUVWX\/comments\?/);
    assert.match(calls[1]!.url, /file_type=docx/);
    assert.match(calls[1]!.url, /page_size=20/);
    assert.match(calls[1]!.url, /page_token=current-page/);
    assert.match(calls[1]!.url, /is_solved=false/);
    assert.match(calls[1]!.url, /is_whole=false/);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client maps comment reply content with text_run / docs_link / person elements", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [
            {
              comment_id: "comment_1",
              reply_list: {
                replies: [
                  {
                    reply_id: "reply_1",
                    content: {
                      elements: [
                        { type: "text_run", text_run: { text: "hello" } },
                        { type: "docs_link", docs_link: { url: "https://a" } },
                        { type: "person", person: { user_id: "ou_x" } },
                      ],
                    },
                  },
                ],
              },
            },
          ],
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const page = await client.listDocxComments({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
    });
    assert.deepEqual(page.items[0]?.replyList.replies[0]?.content?.elements, [
      { type: "text_run", text: "hello" },
      { type: "docs_link", link: "https://a" },
      { type: "person", mentionUser: "ou_x" },
    ]);
    assert.equal(page.items[0]?.replyList.replies[0]?.text, "hellohttps://aou_x");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client batch queries docx comments", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/comments\/batch_query\?file_type=docx$/);
    const body = JSON.parse(String(init?.body ?? "{}")) as { comment_ids?: string[] };
    assert.deepEqual(body.comment_ids, ["comment_1", "comment_2"]);
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [{ comment_id: "comment_1", reply_list: { replies: [] } }],
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const result = await client.batchQueryDocxComments({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      commentIds: ["comment_1", "comment_2"],
    });
    assert.equal(result.items.length, 1);
    assert.equal(result.items[0]?.commentId, "comment_1");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client lists docx comment replies", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/comments\/comment_1\/replies\?/);
    assert.match(url, /file_type=docx/);
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          items: [{ reply_id: "reply_1", user_id: "ou_user_1", create_time: "1710000000001" }],
          has_more: false,
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const page = await client.listDocxCommentReplies({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      commentId: "comment_1",
      pageSize: 50,
    });
    assert.equal(page.items.length, 1);
    assert.equal(page.items[0]?.replyId, "reply_1");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client creates docx comment with optional anchor", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/new_comments$/);
    const body = JSON.parse(String(init?.body ?? "{}")) as Record<string, unknown>;
    assert.equal(body.file_type, "docx");
    assert.deepEqual(body.reply_elements, [{ type: "text", text: "hello" }]);
    assert.deepEqual(body.anchor, { block_id: "blk_anchor_1" });
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          comment_id: "comment_1",
          created_at: "1710000000002",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const result = await client.createDocxComment({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      replyElements: [{ type: "text", text: "hello" }],
      anchorBlockId: "blk_anchor_1",
    });
    assert.deepEqual(result, {
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      commentId: "comment_1",
      createTime: "1710000000002",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client creates docx comment reply", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/comments\/comment_1\/replies\?file_type=docx$/);
    const body = JSON.parse(String(init?.body ?? "{}")) as Record<string, unknown>;
    assert.deepEqual(body.reply_elements, [{ type: "text", text: "reply" }]);
    return new Response(
      JSON.stringify({
        code: 0,
        data: {
          reply_id: "reply_1",
          create_time: "1710000000003",
        },
      }),
      { status: 200, headers: { "Content-Type": "application/json" } },
    );
  }) as typeof fetch;
  try {
    const result = await client.createDocxCommentReply({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      commentId: "comment_1",
      replyElements: [{ type: "text", text: "reply" }],
    });
    assert.deepEqual(result, {
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      commentId: "comment_1",
      replyId: "reply_1",
      createTime: "1710000000003",
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client patches docx comment solved state", async () => {
  const client = createClient();
  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return new Response(
        JSON.stringify({ code: 0, tenant_access_token: "tenant_token", expire: 7200 }),
        { status: 200, headers: { "Content-Type": "application/json" } },
      );
    }
    assert.match(url, /\/comments\/comment_1\?file_type=docx$/);
    assert.equal(init?.method, "PATCH");
    const body = JSON.parse(String(init?.body ?? "{}")) as Record<string, unknown>;
    assert.deepEqual(body, { is_solved: true });
    return new Response(JSON.stringify({ code: 0, data: {} }), {
      status: 200,
      headers: { "Content-Type": "application/json" },
    });
  }) as typeof fetch;
  try {
    await client.patchDocxCommentSolved({
      fileToken: "doxABCDEFGHIJKLMNOPQRSTUVWX",
      commentId: "comment_1",
      isSolved: true,
    });
  } finally {
    globalThis.fetch = originalFetch;
  }
});
