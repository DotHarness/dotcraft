import assert from "node:assert/strict";
import test from "node:test";

import {
  QQMediaTools,
  QQ_SEND_GROUP_VOICE_TOOL,
  QQ_UPLOAD_PRIVATE_FILE_TOOL,
} from "./qq-media-tools.js";

class FakeOneBot {
  readonly actions: Record<string, unknown>[] = [];

  async sendAction(action: Record<string, unknown>): Promise<Record<string, unknown>> {
    this.actions.push(action);
    return { status: "ok", retcode: 0 };
  }
}

test("QQMediaTools declares legacy tool names", () => {
  const tools = new QQMediaTools().getChannelTools();
  assert.ok(tools.some((tool) => tool.name === QQ_SEND_GROUP_VOICE_TOOL));
  assert.ok(tools.some((tool) => tool.name === QQ_UPLOAD_PRIVATE_FILE_TOOL));
});

test("QQMediaTools maps group voice tool to send_group_msg", async () => {
  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_SEND_GROUP_VOICE_TOOL, {
    groupId: 123,
    file: "https://example.test/a.mp3",
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "send_group_msg");
});

test("QQMediaTools maps private upload tool to upload_private_file", async () => {
  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_UPLOAD_PRIVATE_FILE_TOOL, {
    userId: 123,
    filePath: "C:/tmp/a.txt",
    fileName: "a.txt",
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "upload_private_file");
});
