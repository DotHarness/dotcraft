import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  QQMediaTools,
  QQ_SEND_GROUP_VOICE_TOOL,
  QQ_UPLOAD_GROUP_FILE_TOOL,
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
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-qq-upload-"));
  const filePath = join(tempDir, "a.txt");
  writeFileSync(filePath, "hello napcat", "utf-8");

  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_UPLOAD_PRIVATE_FILE_TOOL, {
    userId: 123,
    filePath,
    fileName: "a.txt",
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "upload_private_file");
  const params = server.actions[0].params as Record<string, unknown>;
  assert.equal(params.file, `base64://${Buffer.from("hello napcat").toString("base64")}`);
  assert.equal(params.name, "a.txt");
});

test("QQMediaTools maps group upload tool host paths to OneBot base64 file URI", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-qq-group-upload-"));
  const filePath = join(tempDir, "report.txt");
  writeFileSync(filePath, "group file", "utf-8");

  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_UPLOAD_GROUP_FILE_TOOL, {
    groupId: 456,
    filePath,
    fileName: "report.txt",
    folder: "/",
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "upload_group_file");
  const params = server.actions[0].params as Record<string, unknown>;
  assert.equal(params.file, `base64://${Buffer.from("group file").toString("base64")}`);
  assert.equal(params.name, "report.txt");
});

test("QQMediaTools structured file delivery passes URL sources through", async () => {
  const server = new FakeOneBot();
  const result = await new QQMediaTools().sendStructuredMessage(server as never, "group:456", {
    kind: "file",
    fileName: "report.pdf",
    source: { kind: "url", url: "https://example.test/report.pdf" },
  });

  assert.equal(result.delivered, true);
  assert.equal(server.actions[0].action, "upload_group_file");
  const params = server.actions[0].params as Record<string, unknown>;
  assert.equal(params.file, "https://example.test/report.pdf");
  assert.equal(params.name, "report.pdf");
});
