import assert from "node:assert/strict";
import test from "node:test";

import { getFeishuCliToolDescriptors } from "./feishu-cli-tool.js";

test("declares the approved Feishu CLI tool only when enabled", () => {
  assert.deepEqual(getFeishuCliToolDescriptors(false), []);

  const tools = getFeishuCliToolDescriptors(true);
  const tool = assertSingle(tools);
  assert.equal(tool.name, "FeishuCli");
  assert.equal(tool.requiresChatContext, false);
  assert.deepEqual(tool.approval, {
    kind: "remoteResource",
    targetArgument: "command",
    operation: "invoke",
  });
  assert.deepEqual(
    (tool.inputSchema as { required?: string[] }).required,
    ["command", "args"],
  );
});

function assertSingle<T>(items: T[]): T {
  assert.equal(items.length, 1);
  return items[0]!;
}
