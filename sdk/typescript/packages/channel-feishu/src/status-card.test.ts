import assert from "node:assert/strict";
import { mock, test } from "node:test";

import { FeishuStatusCards } from "./status-card.js";

const turn = { threadId: "thread-1", turnId: "turn-1", channelContext: "group:oc_1" };

function statusText(card: Record<string, unknown>): string {
  const body = card.body as { elements: Array<{ i18n_content: { en_us: string } }> };
  return body.elements[0]!.i18n_content.en_us.replace(/<[^>]+>/g, "");
}

function harness() {
  let now = 0;
  const posted: Array<{ target: string; text: string }> = [];
  const patched: Array<{ messageId: string; text: string }> = [];
  const cards = new FeishuStatusCards({
    sendCard: async (target, card) => {
      posted.push({ target, text: statusText(card) });
      return { messageId: "om_status", chatId: "oc_1" };
    },
    updateCard: async (messageId, card) => {
      patched.push({ messageId, text: statusText(card) });
    },
    loadingIconImgKey: async () => undefined,
    now: () => now,
  });
  const advance = async (ms: number): Promise<void> => {
    now += ms;
    mock.timers.tick(ms);
    for (let i = 0; i < 5; i += 1) await Promise.resolve();
  };
  return { cards, posted, patched, advance };
}

test("a Turn that ends within 20 seconds posts no status card unless it failed", async (t) => {
  t.after(() => mock.timers.reset());
  mock.timers.enable({ apis: ["setTimeout"] });
  const { cards, posted, patched, advance } = harness();

  cards.start(turn);
  await advance(19_000);
  await cards.end(turn, { status: "completed" });
  await advance(5_000);

  assert.deepEqual(posted, []);
  assert.deepEqual(patched, []);

  cards.start(turn);
  await advance(4_000);
  await cards.end(turn, { status: "failed", error: "provider detail" });
  assert.deepEqual(posted, [{ target: "group:oc_1", text: "Failed after 4s" }]);
  assert.deepEqual(patched, []);
});

test("a long Turn posts a working card after 20 seconds and settles on done with the elapsed time", async (t) => {
  t.after(() => mock.timers.reset());
  mock.timers.enable({ apis: ["setTimeout"] });
  const { cards, posted, patched, advance } = harness();

  cards.start(turn);
  await advance(20_000);
  assert.deepEqual(posted, [{ target: "group:oc_1", text: "Working…" }]);

  cards.retrying(turn);
  await advance(1_000);
  cards.working(turn);
  cards.working(turn);
  await advance(51_000);
  await cards.end(turn, { status: "completed" });

  assert.deepEqual(patched, [
    { messageId: "om_status", text: "Reconnecting…" },
    { messageId: "om_status", text: "Working…" },
    { messageId: "om_status", text: "Done after 1m 12s" },
  ]);
});

test("a long Turn that fails or is stopped settles its card and keeps it", async (t) => {
  t.after(() => mock.timers.reset());
  mock.timers.enable({ apis: ["setTimeout"] });
  const failed = harness();
  const stopped = harness();

  failed.cards.start(turn);
  stopped.cards.start(turn);
  await failed.advance(25_000);
  await stopped.advance(30_000);
  await failed.cards.end(turn, { status: "failed", error: "provider detail" });
  await stopped.cards.end(turn, { status: "cancelled" });

  assert.deepEqual(failed.patched, [{ messageId: "om_status", text: "Failed after 25s" }]);
  assert.deepEqual(stopped.patched, [{ messageId: "om_status", text: "Stopped after 30s" }]);
});
