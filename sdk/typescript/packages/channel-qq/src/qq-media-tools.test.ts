import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import {
  QQMediaError,
  QQMediaTools,
  QQ_SEND_GROUP_VIDEO_TOOL,
  QQ_SEND_GROUP_VOICE_TOOL,
  QQ_SEND_PRIVATE_VIDEO_TOOL,
  QQ_SEND_PRIVATE_VOICE_TOOL,
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

function sentMessageFile(server: FakeOneBot): string {
  const params = server.actions[0].params as Record<string, unknown>;
  const message = params.message as Record<string, unknown>[];
  const data = message[0].data as Record<string, unknown>;
  return String(data.file);
}

test("QQMediaTools declares legacy tool names", () => {
  const tools = new QQMediaTools().getChannelTools();
  assert.ok(tools.some((tool) => tool.name === QQ_SEND_GROUP_VOICE_TOOL));
  assert.ok(tools.some((tool) => tool.name === QQ_UPLOAD_PRIVATE_FILE_TOOL));
});

test("QQMediaTools voice and video tools declare filePath approval metadata", () => {
  const tools = new QQMediaTools().getChannelTools();
  const expectedTools = [
    [QQ_SEND_GROUP_VOICE_TOOL, "groupId"],
    [QQ_SEND_PRIVATE_VOICE_TOOL, "userId"],
    [QQ_SEND_GROUP_VIDEO_TOOL, "groupId"],
    [QQ_SEND_PRIVATE_VIDEO_TOOL, "userId"],
  ] as const;

  for (const [toolName, targetIdField] of expectedTools) {
    const tool = tools.find((candidate) => candidate.name === toolName);
    assert.ok(tool, `${toolName} descriptor should exist`);
    assert.deepEqual(tool.approval, { kind: "file", targetArgument: "filePath", operation: "read" });
    const inputSchema = tool.inputSchema as { properties?: Record<string, unknown>; required?: unknown[] };
    assert.deepEqual(inputSchema.required, [targetIdField]);
    assert.ok(inputSchema.properties?.["filePath"], `${toolName} should expose filePath`);
    assert.ok(inputSchema.properties?.["fileUrl"], `${toolName} should expose fileUrl`);
    assert.ok(inputSchema.properties?.["fileBase64"], `${toolName} should expose fileBase64`);
    assert.ok(inputSchema.properties?.["file"], `${toolName} should keep legacy file`);
  }
});

test("QQMediaTools maps group voice fileUrl to send_group_msg", async () => {
  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_SEND_GROUP_VOICE_TOOL, {
    groupId: 123,
    fileUrl: "https://example.test/a.mp3",
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "send_group_msg");
  assert.equal(sentMessageFile(server), "https://example.test/a.mp3");
});

test("QQMediaTools maps group voice filePath to OneBot base64 record URI", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-qq-voice-"));
  const filePath = join(tempDir, "a.wav");
  writeFileSync(filePath, "voice bytes", "utf-8");

  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_SEND_GROUP_VOICE_TOOL, {
    groupId: 123,
    filePath,
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "send_group_msg");
  assert.equal(sentMessageFile(server), `base64://${Buffer.from("voice bytes").toString("base64")}`);
});

test("QQMediaTools maps private video fileBase64 to send_private_msg", async () => {
  const server = new FakeOneBot();
  const result = await new QQMediaTools().executeToolCall(server as never, QQ_SEND_PRIVATE_VIDEO_TOOL, {
    userId: 123,
    fileBase64: Buffer.from("video bytes").toString("base64"),
  });

  assert.equal(result.success, true);
  assert.equal(server.actions[0].action, "send_private_msg");
  assert.equal(sentMessageFile(server), `base64://${Buffer.from("video bytes").toString("base64")}`);
});

test("QQMediaTools keeps legacy file URL and base64 sources", async () => {
  const urlServer = new FakeOneBot();
  const urlResult = await new QQMediaTools().executeToolCall(urlServer as never, QQ_SEND_GROUP_VOICE_TOOL, {
    groupId: 123,
    file: "https://example.test/legacy.mp3",
  });

  assert.equal(urlResult.success, true);
  assert.equal(sentMessageFile(urlServer), "https://example.test/legacy.mp3");

  const base64Server = new FakeOneBot();
  const base64Result = await new QQMediaTools().executeToolCall(base64Server as never, QQ_SEND_PRIVATE_VOICE_TOOL, {
    userId: 456,
    file: "base64://dm9pY2U=",
  });

  assert.equal(base64Result.success, true);
  assert.equal(sentMessageFile(base64Server), "base64://dm9pY2U=");
});

test("QQMediaTools rejects legacy local file paths for voice/video tools", async () => {
  await assert.rejects(
    () => new QQMediaTools().executeToolCall(new FakeOneBot() as never, QQ_SEND_GROUP_VIDEO_TOOL, {
      groupId: 123,
      file: "C:\\temp\\clip.mp4",
    }),
    (error) => {
      assert.ok(error instanceof QQMediaError);
      assert.equal(error.code, "InvalidArguments");
      assert.match(error.message, /Use filePath for local files/);
      return true;
    },
  );
});

test("QQMediaTools requires exactly one voice/video media source", async () => {
  await assert.rejects(
    () => new QQMediaTools().executeToolCall(new FakeOneBot() as never, QQ_SEND_GROUP_VOICE_TOOL, { groupId: 123 }),
    (error) => {
      assert.ok(error instanceof QQMediaError);
      assert.equal(error.code, "InvalidArguments");
      assert.match(error.message, /Exactly one/);
      return true;
    },
  );

  await assert.rejects(
    () => new QQMediaTools().executeToolCall(new FakeOneBot() as never, QQ_SEND_PRIVATE_VIDEO_TOOL, {
      userId: 123,
      fileUrl: "https://example.test/a.mp4",
      fileBase64: "dmVyc2lvbg==",
    }),
    (error) => {
      assert.ok(error instanceof QQMediaError);
      assert.equal(error.code, "InvalidArguments");
      assert.match(error.message, /Exactly one/);
      return true;
    },
  );
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
