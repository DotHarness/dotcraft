import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/sdk/channel";

import { validateWeComConfig } from "./wecom-adapter.js";

const validConfig = {
  dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" },
  wecom: {
    host: "127.0.0.1",
    port: 9000,
    robots: [{ path: "/dotcraft", token: "token", aesKey: "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG" }],
  },
};

test("validateWeComConfig accepts minimal valid config", () => {
  assert.doesNotThrow(() => validateWeComConfig(validConfig));
});

test("validateWeComConfig rejects missing robots", () => {
  assert.throws(
    () => validateWeComConfig({ dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" }, wecom: {} }),
    ConfigValidationError,
  );
});

test("validateWeComConfig rejects invalid websocket URL and port", () => {
  assert.throws(
    () => validateWeComConfig({ dotcraft: { wsUrl: "http://127.0.0.1:9100/ws" }, wecom: { port: 70000, robots: [] } }),
    ConfigValidationError,
  );
});

