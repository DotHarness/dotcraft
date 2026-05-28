import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/sdk/channel";

import { validateQQConfig } from "./qq-adapter.js";

const validConfig = {
  dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" },
  qq: {
    host: "127.0.0.1",
    port: 6700,
    adminUsers: [10001],
    whitelistedUsers: ["10002"],
    whitelistedGroups: [20001],
  },
};

test("validateQQConfig accepts a valid config", () => {
  assert.doesNotThrow(() => validateQQConfig(validConfig));
});

test("validateQQConfig rejects missing wsUrl", () => {
  assert.throws(
    () => validateQQConfig({ dotcraft: {}, qq: {} }),
    (error) => error instanceof ConfigValidationError && error.fields?.includes("dotcraft.wsUrl") === true,
  );
});

test("validateQQConfig rejects invalid port", () => {
  assert.throws(
    () => validateQQConfig({ ...validConfig, qq: { port: 70000 } }),
    /qq\.port/,
  );
});

test("validateQQConfig rejects invalid QQ ids", () => {
  assert.throws(
    () => validateQQConfig({ ...validConfig, qq: { adminUsers: ["abc"] } }),
    /Invalid QQ id/,
  );
});
