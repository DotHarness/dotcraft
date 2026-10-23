import assert from "node:assert/strict";
import test from "node:test";

import { FeishuCliRunnerError } from "./feishu-cli-runner.js";
import { FeishuCliTool } from "./feishu-cli-tool.js";

test("defaults to the bot identity and forwards an explicit user identity", async () => {
  const seen: Array<string | undefined> = [];
  const tool = createTool(async (_command, _args, options) => {
    seen.push((options as { identity?: string } | undefined)?.identity);
    return { risk: "read" as const, contentItems: [], structuredResult: {} };
  });
  await tool.invoke({ command: "whoami", args: [] });
  await tool.invoke({ command: "whoami", args: [], identity: "user" });
  await tool.invoke({ command: "whoami", args: [], identity: "operator" });
  assert.deepEqual(seen, ["bot", "user", "bot"]);
});

test("returns CLI success and error details through structuredResult", async () => {
  const successTool = createTool(async () => ({
    risk: "read" as const,
    contentItems: [{ type: "text" as const, text: "{\"ok\":true}" }],
    structuredResult: { ok: true, identity: "bot" },
  }));

  assert.deepEqual(await successTool.invoke({ command: "whoami", args: [] }), {
    success: true,
    contentItems: [{ type: "text", text: "{\"ok\":true}" }],
    structuredResult: { ok: true, identity: "bot" },
  });

  const detail = {
    type: "authentication",
    subtype: "token_missing",
    message: "no access token available for bot",
    identity: "bot",
  };
  const failureTool = createTool(async () => {
    throw new FeishuCliRunnerError("FeishuCliAuthenticationFailed", detail.message, detail);
  });
  assert.deepEqual(await failureTool.invoke({ command: "docs", args: ["+fetch"] }), {
    success: false,
    errorCode: "FeishuCliAuthenticationFailed",
    errorMessage: detail.message,
    structuredResult: detail,
  });
});

function createTool(
  run: (command: string, args: string[], options?: unknown) => Promise<unknown>,
): FeishuCliTool {
  const tool = Object.create(FeishuCliTool.prototype) as FeishuCliTool;
  Object.assign(tool, {
    abortController: new AbortController(),
    state: { runner: { run } },
  });
  return tool;
}
