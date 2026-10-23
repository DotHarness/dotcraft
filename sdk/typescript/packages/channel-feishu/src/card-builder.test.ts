import assert from "node:assert/strict";
import test from "node:test";

import { buildUserInputCard, resolveCardTitle } from "./card-builder.js";

test("invalid card title falls back to default title", () => {
  const fallback = resolveCardTitle(undefined);
  assert.equal(resolveCardTitle(""), fallback);
  assert.equal(resolveCardTitle("   "), fallback);
  assert.equal(resolveCardTitle("A".repeat(49)), fallback);
  assert.equal(resolveCardTitle("Bot"), "Bot");
});

test("buildUserInputCard uses native callback buttons for single-choice questions", () => {
  const card = buildUserInputCard({
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
  });
  const body = (card.body as Record<string, unknown> | undefined) ?? {};
  const elements = (body.elements as Array<Record<string, unknown>> | undefined) ?? [];
  const buttons = elements.filter((element) => element.tag === "button");

  assert.equal(buttons.length, 2);
  assert.deepEqual(buttons[1]?.behaviors, [
    {
      type: "callback",
      value: {
        kind: "userInput",
        requestId: "req-1",
        optionIndex: 1,
      },
    },
  ]);
});
