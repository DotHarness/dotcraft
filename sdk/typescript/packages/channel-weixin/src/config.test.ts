import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/sdk/channel";

import { validateWeixinConfig } from "./weixin-adapter.js";
import type { WeixinConfig } from "./weixin-config.js";

function validConfig(): WeixinConfig {
  return {
    dotcraft: {
      wsUrl: "ws://127.0.0.1:9100/ws",
    },
    weixin: {
      apiBaseUrl: "https://ilinkai.weixin.qq.com",
    },
  };
}

test("validateConfig throws when weixin.apiBaseUrl is missing", () => {
  const config = validConfig();
  config.weixin.apiBaseUrl = "";
  assert.throws(
    () => validateWeixinConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("weixin.apiBaseUrl"),
  );
});

test("validateConfig throws when dotcraft.wsUrl is missing", () => {
  const config = validConfig();
  config.dotcraft.wsUrl = "";
  assert.throws(
    () => validateWeixinConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("dotcraft.wsUrl"),
  );
});

test("validateConfig succeeds with minimal valid config", () => {
  const config = validConfig();
  assert.doesNotThrow(() => validateWeixinConfig(config));
});
