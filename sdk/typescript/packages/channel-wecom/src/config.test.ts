import assert from "node:assert/strict";
import test from "node:test";

import { ConfigValidationError } from "@dotcraft/channel";

import { validateWeComConfig } from "./wecom-adapter.js";

test("validateWeComConfig accepts a minimal config and rejects missing robots or invalid URL and port", () => {
  assert.doesNotThrow(() => validateWeComConfig({
    dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" },
    wecom: {
      host: "127.0.0.1",
      port: 9000,
      robots: [{ path: "/dotcraft", token: "token", aesKey: "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG" }],
    },
  }));
  assert.throws(
    () => validateWeComConfig({ dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" }, wecom: {} }),
    ConfigValidationError,
  );
  assert.throws(
    () => validateWeComConfig({ dotcraft: { wsUrl: "http://127.0.0.1:9100/ws" }, wecom: { port: 70000, robots: [] } }),
    ConfigValidationError,
  );
});
