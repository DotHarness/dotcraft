import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/channel";

import { validateFeishuConfig } from "./feishu-adapter.js";
import type { FeishuConfig } from "./feishu-types.js";

function validConfig(): FeishuConfig {
  return {
    dotcraft: {
      wsUrl: "ws://127.0.0.1:9100/ws",
    },
    feishu: {
      appId: "cli_test",
      appSecret: "test-secret",
    },
  };
}

test("throws ConfigValidationError when feishu.appId is missing", () => {
  const config = validConfig();
  config.feishu.appId = "";
  assert.throws(
    () => validateFeishuConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("feishu.appId"),
  );
});

test("throws ConfigValidationError when feishu.appSecret is missing", () => {
  const config = validConfig();
  config.feishu.appSecret = "";
  assert.throws(
    () => validateFeishuConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("feishu.appSecret"),
  );
});

test("throws ConfigValidationError when dotcraft.wsUrl is missing or invalid", () => {
  const missing = validConfig();
  missing.dotcraft.wsUrl = "";
  assert.throws(() => validateFeishuConfig(missing), ConfigValidationError);

  const invalid = validConfig();
  invalid.dotcraft.wsUrl = "http://127.0.0.1:9100/ws";
  assert.throws(() => validateFeishuConfig(invalid), ConfigValidationError);
});

test("accepts minimal valid config", () => {
  const config = validConfig();
  assert.doesNotThrow(() => validateFeishuConfig(config));
});

test("accepts brand=lark", () => {
  const config = validConfig();
  config.feishu.brand = "lark";
  assert.doesNotThrow(() => validateFeishuConfig(config));
});

test("accepts and validates the official CLI toggle", () => {
  const config = validConfig();
  config.feishu.cli = { enabled: true };
  assert.doesNotThrow(() => validateFeishuConfig(config));

  const invalid = validConfig() as unknown as {
    dotcraft: Record<string, unknown>;
    feishu: Record<string, unknown>;
  };
  invalid.feishu.cli = { enabled: "yes" };
  assert.throws(() => validateFeishuConfig(invalid), ConfigValidationError);

  invalid.feishu.cli = true;
  assert.throws(() => validateFeishuConfig(invalid), ConfigValidationError);
});

test("accepts optional feishu.cardTitle", () => {
  const config = validConfig();
  config.feishu.cardTitle = "Bot";
  assert.doesNotThrow(() => validateFeishuConfig(config));
});

test("accepts and validates the native streaming toggle", () => {
  const enabled = validConfig();
  enabled.feishu.streaming = { enabled: true };
  assert.doesNotThrow(() => validateFeishuConfig(enabled));

  const invalid = validConfig() as unknown as {
    dotcraft: Record<string, unknown>;
    feishu: Record<string, unknown>;
  };
  invalid.feishu.streaming = { enabled: "yes" };
  assert.throws(
    () => validateFeishuConfig(invalid),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("feishu.streaming.enabled"),
  );
});

test("throws ConfigValidationError when feishu.brand is invalid", () => {
  const config = validConfig() as unknown as {
    dotcraft: Record<string, unknown>;
    feishu: Record<string, unknown>;
  };
  config.feishu.brand = "custom.example.com";
  assert.throws(
    () => validateFeishuConfig(config),
    (error: unknown) =>
      error instanceof ConfigValidationError &&
      Array.isArray(error.fields) &&
      error.fields.includes("feishu.brand"),
  );
});
