import assert from "node:assert/strict";
import test from "node:test";

import { FeishuClient } from "./feishu-client.js";
import { FeishuApiError } from "./feishu-types.js";

type ApiCall = { url: string; init?: RequestInit };

function createClient(): FeishuClient {
  return new FeishuClient({
    appId: "cli_test",
    appSecret: "secret_test",
  });
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

async function withApi(
  respond: (url: string, init?: RequestInit) => Response,
  run: (client: FeishuClient, calls: ApiCall[]) => Promise<void>,
): Promise<void> {
  const originalFetch = globalThis.fetch;
  const calls: ApiCall[] = [];
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    const url = String(input);
    if (url.includes("/tenant_access_token/internal")) {
      return json({ code: 0, tenant_access_token: "tenant_token", expire: 7200 });
    }
    calls.push({ url, init });
    return respond(url, init);
  }) as typeof fetch;
  try {
    await run(createClient(), calls);
  } finally {
    globalThis.fetch = originalFetch;
  }
}

function isApiError(kind: string, extra: Partial<FeishuApiError> = {}) {
  return (error: unknown) => {
    assert.ok(error instanceof FeishuApiError);
    assert.equal(error.kind, kind);
    for (const [key, value] of Object.entries(extra)) {
      assert.equal((error as unknown as Record<string, unknown>)[key], value);
    }
    return true;
  };
}

test("Feishu client classifies HTTP, SDK, and authorization failures as FeishuApiError", async () => {
  await withApi(() => json({ code: 99991429, msg: "rate limited" }, 429), async (client) => {
    await assert.rejects(
      client.sendTextMessage("group:oc_chat_123", "hello"),
      isApiError("rateLimited", { retryable: true, code: 99991429, httpStatus: 429 }),
    );
  });

  await withApi(() => json({ code: 99991403, msg: "permission denied" }, 403), async (client) => {
    await assert.rejects(client.replyToMessage("om_origin_1", "reply text"), isApiError("permission", { httpStatus: 403 }));
    await assert.rejects(
      client.listChatMessages("oc_chat_123", { startTime: "1710000000000" }),
      isApiError("permission", { httpStatus: 403 }),
    );
  });

  const sdkClient = createClient();
  (sdkClient as unknown as { sdk: unknown }).sdk = {
    im: {
      messageReaction: {
        async create(): Promise<void> {
          throw { code: 99991403, msg: "permission denied" };
        },
      },
    },
  };
  await assert.rejects(
    sdkClient.addMessageReaction("om_test_1", "GLANCE"),
    isApiError("permission", { retryable: false, code: 99991403 }),
  );

  const originalFetch = globalThis.fetch;
  globalThis.fetch = (async () => json({ code: 99991663, msg: "token invalid" }, 401)) as typeof fetch;
  try {
    await assert.rejects(createClient().probeBot(), isApiError("auth"));
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("Feishu client sends text and replies with tenant token auth and the expected bodies", async () => {
  await withApi(
    (url) => json({ code: 0, data: { message_id: url.endsWith("/reply") ? "om_reply_1" : "om_text_1", chat_id: "oc_chat_123" } }),
    async (client, calls) => {
      assert.deepEqual(await client.sendTextMessage("group:oc_chat_123", "hello text"), {
        messageId: "om_text_1",
        chatId: "oc_chat_123",
      });
      assert.deepEqual(await client.replyToMessage("om_origin_1", "reply text", { replyInThread: true, uuid: "uuid-1" }), {
        messageId: "om_reply_1",
        chatId: "oc_chat_123",
      });
      await assert.rejects(client.sendTextMessage("group:oc_chat_123", "   "), /non-empty text/);

      assert.equal(calls.length, 2);
      assert.match(calls[0]!.url, /receive_id_type=chat_id/);
      const sent = JSON.parse(String(calls[0]!.init?.body)) as Record<string, string>;
      assert.equal(sent.msg_type, "text");
      assert.deepEqual(JSON.parse(sent.content), { text: "hello text" });

      assert.match(calls[1]!.url, /\/messages\/om_origin_1\/reply$/);
      assert.equal((calls[1]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");
      const reply = JSON.parse(String(calls[1]!.init?.body)) as Record<string, string | boolean>;
      assert.equal(reply.reply_in_thread, true);
      assert.equal(reply.uuid, "uuid-1");
      assert.deepEqual(JSON.parse(String(reply.content)), { text: "reply text" });
    },
  );
});

test("Feishu client probes bot identity and diagnoses a missing one", async () => {
  const responses = [
    { code: 0, data: { app_name: "DotCraft", activate_status: 1 }, bot: { open_id: "ou_bot_123", app_name: "DotCraft" } },
    { code: 0, data: { app_name: "DotCraft", activate_status: 0 } },
    { code: 0, data: { app_name: "DotCraft" } },
  ];
  await withApi(() => json(responses.shift()), async (client, calls) => {
    const found = await client.probeBot();
    assert.equal(found.openId, "ou_bot_123");
    assert.equal(found.hasBotIdentity, true);
    assert.equal(found.diagnosticTag, undefined);
    assert.match(calls[0]!.url, /\/bot\/v3\/info$/);
    assert.equal((calls[0]!.init?.headers as Record<string, string>).Authorization, "Bearer tenant_token");

    const disabled = await client.probeBot();
    assert.equal(disabled.hasBotIdentity, false);
    assert.equal(disabled.diagnosticTag, "botCapabilityDisabled");

    const missing = await client.probeBot();
    assert.equal(missing.hasBotIdentity, false);
    assert.equal(missing.diagnosticTag, "identityFieldsMissing");
  });
});

test("Feishu client lists chat messages with normalized metadata and validates arguments", async () => {
  await withApi(() => json({
    code: 0,
    data: {
      has_more: true,
      page_token: "next-page",
      items: [{
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
          id: { open_id: "ou_user_1", user_id: "user_1", union_id: "union_1" },
        },
        mentions: [{ key: "@_user_1", id: { open_id: "ou_mention_1" }, name: "Mentioned User" }],
        body: { content: "{\"text\":\"hello history\"}" },
      }],
    },
  }), async (client, calls) => {
    const result = await client.listChatMessages("oc_chat_123", {
      startTime: "1710000000000",
      endTime: "1710003600000",
      pageSize: 50,
      pageToken: "current-page",
    });
    assert.equal(result.nextPageToken, "next-page");
    assert.equal(result.hasMore, true);
    assert.deepEqual(result.items, [{
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
      mentions: [{
        key: "@_user_1",
        id: { open_id: "ou_mention_1", user_id: undefined, union_id: undefined },
        name: "Mentioned User",
        tenant_key: undefined,
      }],
      rawContent: "{\"text\":\"hello history\"}",
    }]);
    const query = new URL(calls[0]!.url).searchParams;
    assert.equal(query.get("container_id_type"), "chat");
    assert.equal(query.get("container_id"), "oc_chat_123");
    assert.equal(query.get("start_time"), "1710000000000");
    assert.equal(query.get("end_time"), "1710003600000");
    assert.equal(query.get("page_size"), "50");
    assert.equal(query.get("page_token"), "current-page");

    await assert.rejects(client.listChatMessages("", { startTime: "1710000000000" }), /chatId/);
    await assert.rejects(client.listChatMessages("oc_chat_123", { startTime: "   " }), /startTime/);
    await assert.rejects(client.listChatMessages("oc_chat_123", { startTime: "1710000000000", pageSize: 0 }), /pageSize/);
    assert.equal(calls.length, 1);
  });
});

test("Feishu client lists the user-token scopes enabled for the app", async () => {
  await withApi(() => json({
    code: 0,
    data: {
      app: {
        scopes: [
          { scope: "calendar:calendar.event:read", token_types: ["user"] },
          { scope: "im:message", token_types: ["tenant"] },
          { scope: "docx:document:readonly", token_types: ["tenant", "user"] },
        ],
      },
    },
  }), async (client, calls) => {
    assert.deepEqual(await client.listAppUserScopes(), ["calendar:calendar.event:read", "docx:document:readonly"]);
    assert.match(calls[0]!.url, /\/application\/v6\/applications\/cli_test\?lang=en_us$/);
  });
});
