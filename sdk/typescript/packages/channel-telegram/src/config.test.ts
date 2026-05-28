import assert from "node:assert/strict";
import test from "node:test";

import { validateTelegramConfig } from "./telegram-adapter.js";

test("validateTelegramConfig accepts a valid config", () => {
  const config = {
    dotcraft: {
      wsUrl: "ws://127.0.0.1:9100/ws",
      token: "",
    },
    telegram: {
      botToken: "123:token",
    },
  };

  assert.doesNotThrow(() => validateTelegramConfig(config));
});

test("validateTelegramConfig rejects missing required fields", () => {
  assert.throws(
    () =>
      validateTelegramConfig({
        dotcraft: {},
        telegram: {},
      }),
    /Missing required fields/,
  );
});

test("validateTelegramConfig rejects invalid wsUrl", () => {
  assert.throws(
    () =>
      validateTelegramConfig({
        dotcraft: {
          wsUrl: "http://127.0.0.1:9100/ws",
        },
        telegram: {
          botToken: "123:token",
        },
      }),
    /must use ws:\/\/ or wss:\/\//,
  );
});
