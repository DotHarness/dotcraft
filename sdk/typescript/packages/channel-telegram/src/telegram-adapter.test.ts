import assert from "node:assert/strict";
import test from "node:test";

import {
  buildTelegramBotCommands,
  isTelegramConflictError,
  parseTargetChatId,
} from "./telegram-adapter.js";

test("buildTelegramBotCommands keeps defaults and filters unsupported names", async () => {
  const commands = await buildTelegramBotCommands(async () => [
    { name: "/deploy", description: "Deploy the current change." },
    { name: "INVALID-NAME", description: "Should be skipped." },
    { name: "help", description: "Duplicate built-in command." },
  ]);

  assert.deepEqual(commands, [
    { command: "new", description: "Start a new conversation" },
    { command: "help", description: "Show available commands" },
    { command: "deploy", description: "Deploy the current change." },
  ]);
});

test("parseTargetChatId supports raw and prefixed targets", () => {
  assert.equal(parseTargetChatId("-100123"), -100123);
  assert.equal(parseTargetChatId("group:-100123"), -100123);
  assert.equal(parseTargetChatId("user:42"), 42);
  assert.equal(parseTargetChatId("bad-target"), null);
});

test("isTelegramConflictError detects 409 conflict messages", () => {
  assert.equal(isTelegramConflictError(new Error("409 Conflict: terminated by other getUpdates request")), true);
  assert.equal(isTelegramConflictError(new Error("400 Bad Request")), false);
});
