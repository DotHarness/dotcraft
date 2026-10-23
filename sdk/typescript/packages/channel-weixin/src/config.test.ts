import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/channel";

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

test("validateWeixinConfig accepts a minimal config and reports each missing required field", () => {
  assert.doesNotThrow(() => validateWeixinConfig(validConfig()));
  for (const [field, mutate] of [
    ["weixin.apiBaseUrl", (config: WeixinConfig) => { config.weixin.apiBaseUrl = ""; }],
    ["dotcraft.wsUrl", (config: WeixinConfig) => { config.dotcraft.wsUrl = ""; }],
  ] as const) {
    const config = validConfig();
    mutate(config);
    assert.throws(
      () => validateWeixinConfig(config),
      (error: unknown) => error instanceof ConfigValidationError && error.fields?.includes(field) === true,
    );
  }
});
