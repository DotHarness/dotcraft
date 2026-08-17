import assert from "node:assert/strict";
import test from "node:test";

import { FeishuCliRunnerError } from "./feishu-cli-runner.js";
import { FeishuCliTool, getFeishuCliRuntimeAdditionalContext } from "./feishu-cli-tool.js";

test("provides runtime context only when the Feishu CLI is enabled", () => {
  assert.equal(getFeishuCliRuntimeAdditionalContext(false), undefined);
  const context = getFeishuCliRuntimeAdditionalContext(true);
  assert.equal(context?.["feishu.cli"]?.kind, "application");
  assert.ok(context?.["feishu.cli"]?.value);
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
  run: (command: string, args: string[], signal?: AbortSignal) => Promise<unknown>,
): FeishuCliTool {
  const tool = Object.create(FeishuCliTool.prototype) as FeishuCliTool;
  Object.assign(tool, {
    abortController: new AbortController(),
    state: { runner: { run } },
  });
  return tool;
}
