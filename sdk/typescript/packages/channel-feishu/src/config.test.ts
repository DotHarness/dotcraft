import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/channel";

import { validateFeishuConfig } from "./feishu-adapter.js";
import type { FeishuConfig } from "./feishu-types.js";

type LooseConfig = { dotcraft: Record<string, unknown>; feishu: Record<string, unknown> };

function validConfig(): LooseConfig {
  return {
    dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" },
    feishu: { appId: "cli_test", appSecret: "test-secret" },
  };
}

function validate(config: LooseConfig): void {
  validateFeishuConfig(config as unknown as FeishuConfig);
}

test("validateFeishuConfig accepts the minimal config and its optional settings", () => {
  for (const extra of [{}, { brand: "lark" }, { cli: { enabled: true } }, { cardTitle: "Bot" }]) {
    const config = validConfig();
    Object.assign(config.feishu, extra);
    assert.doesNotThrow(() => validate(config));
  }
});

test("validateFeishuConfig rejects missing credentials, bad endpoints, brands, and CLI toggles", () => {
  const cases: Array<[string, (config: LooseConfig) => void, string | undefined]> = [
    ["appId", (c) => { c.feishu.appId = ""; }, "feishu.appId"],
    ["appSecret", (c) => { c.feishu.appSecret = ""; }, "feishu.appSecret"],
    ["brand", (c) => { c.feishu.brand = "custom.example.com"; }, "feishu.brand"],
    ["missing wsUrl", (c) => { c.dotcraft.wsUrl = ""; }, undefined],
    ["http wsUrl", (c) => { c.dotcraft.wsUrl = "http://127.0.0.1:9100/ws"; }, undefined],
    ["cli string", (c) => { c.feishu.cli = { enabled: "yes" }; }, undefined],
    ["cli boolean", (c) => { c.feishu.cli = true; }, undefined],
  ];
  for (const [name, mutate, field] of cases) {
    const config = validConfig();
    mutate(config);
    assert.throws(
      () => validate(config),
      (error: unknown) => error instanceof ConfigValidationError && (!field || error.fields?.includes(field) === true),
      name,
    );
  }
});
