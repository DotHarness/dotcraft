import assert from "node:assert/strict";
import test from "node:test";

import type { OneBotAction, OneBotActionResponse } from "./onebot.js";
import { readQQHistory, resolveQuotedPart } from "./qq-history.js";

function fakeServer(responses: Record<string, OneBotActionResponse>) {
  const actions: OneBotAction[] = [];
  return {
    actions,
    async sendAction(action: OneBotAction): Promise<OneBotActionResponse> {
      actions.push(action);
      return responses[action.action] ?? { status: "failed", retcode: 1404, wording: "unsupported action" };
    },
  };
}

test("quoted replies are fetched with get_msg and attached as reference text", async () => {
  const server = fakeServer({
    get_msg: {
      status: "ok",
      retcode: 0,
      data: {
        message_id: 9001,
        user_id: 42,
        time: 1_700_000_000,
        sender: { nickname: "Bob" },
        message: [{ type: "text", data: { text: "the build is red" } }],
      },
    },
  });
  const part = await resolveQuotedPart(server, [
    { type: "reply", data: { id: "9001" } },
    { type: "text", data: { text: "why?" } },
  ]);
  assert.deepEqual(server.actions[0], { action: "get_msg", params: { message_id: 9001 } });
  assert.match(String(part?.text), /Quoted message from Bob/);
  assert.match(String(part?.text), /> the build is red/);
  assert.equal(await resolveQuotedPart(server, [{ type: "text", data: { text: "no reply" } }]), undefined);
});

test("QQ history pages backwards from the cursor and marks the bot's own messages", async () => {
  const server = fakeServer({
    get_group_msg_history: {
      status: "ok",
      retcode: 0,
      data: {
        messages: [
          { message_id: 3, message_seq: 30, user_id: 7, time: 1_700_000_030, sender: { card: "Carol" }, message: [{ type: "text", data: { text: "third" } }] },
          { message_id: 2, message_seq: 20, user_id: 9, time: 1_700_000_020, message: [{ type: "text", data: { text: "second" } }] },
          { message_id: 1, message_seq: 10, user_id: 42, time: 1_700_000_010, sender: { nickname: "Bot" }, message: [{ type: "text", data: { text: "first" } }] },
        ],
      },
    },
  });

  const page = await readQQHistory(server, { target: "group:123", limit: 3 }, "42");
  assert.deepEqual(server.actions[0], {
    action: "get_group_msg_history",
    params: { group_id: 123, count: 3 },
  });
  assert.deepEqual(page.messages.map((message) => message.text), ["first", "second", "third"]);
  assert.equal(page.messages[0].fromBot, true);
  assert.equal(page.messages[1].fromBot, undefined);
  assert.equal(page.before, "10");

  const older = await readQQHistory(server, { target: "group:123", limit: 3, before: "10" }, "42");
  assert.deepEqual(server.actions[1]?.params, {
    group_id: 123,
    count: 4,
    message_seq: 10,
    reverse_order: true,
  });
  assert.equal(older.messages.length, 2);
  assert.equal(older.before, undefined);
});

test("QQ history reports an unsupported or malformed request as an error page", async () => {
  const unsupported = fakeServer({});
  const refused = await readQQHistory(unsupported, { target: "user:88", limit: 5 }, undefined);
  assert.equal(refused.errorCode, "HistoryUnavailable");
  assert.deepEqual(refused.messages, []);

  const malformed = await readQQHistory(unsupported, { target: "group:123", limit: 5, before: "abc" }, undefined);
  assert.equal(malformed.errorCode, "InvalidCursor");

  const badTarget = await readQQHistory(unsupported, { target: "weird", limit: 5 }, undefined);
  assert.equal(badTarget.errorCode, "UnsupportedTarget");
});
