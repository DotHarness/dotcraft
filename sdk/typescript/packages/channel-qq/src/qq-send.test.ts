import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { sendQQMessage } from "./qq-send.js";

type SentAction = { action: string; params: Record<string, unknown> };

class FakeOneBot {
  readonly actions: SentAction[] = [];

  constructor(private readonly response: Record<string, unknown> = { status: "ok", retcode: 0, data: { message_id: 42 } }) {}

  async sendAction(action: Record<string, unknown>): Promise<Record<string, unknown>> {
    this.actions.push(action as SentAction);
    return this.response;
  }
}

function base64(text: string): string {
  return Buffer.from(text).toString("base64");
}

test("sendQQMessage sends a captioned image as one group message", async () => {
  const server = new FakeOneBot();
  const result = await sendQQMessage(server as never, "group:123", {
    kind: "image",
    caption: "Weekly chart",
    source: { kind: "dataBase64", dataBase64: base64("png bytes") },
  });

  assert.deepEqual(result, { delivered: true, remoteMessageId: "42" });
  assert.equal(server.actions.length, 1);
  assert.deepEqual(server.actions[0], {
    action: "send_group_msg",
    params: {
      group_id: 123,
      message: [
        { type: "text", data: { text: "Weekly chart" } },
        { type: "image", data: { file: `base64://${base64("png bytes")}` } },
      ],
    },
  });
});

test("sendQQMessage sends voice and video to a private chat", async () => {
  const server = new FakeOneBot();
  const voice = await sendQQMessage(server as never, "user:456", {
    kind: "audio",
    source: { kind: "url", url: "https://example.test/a.mp3" },
  });
  const video = await sendQQMessage(server as never, "user:456", {
    kind: "video",
    source: { kind: "dataBase64", dataBase64: base64("video bytes") },
  });

  assert.equal(voice.delivered, true);
  assert.equal(video.delivered, true);
  assert.deepEqual(server.actions.map((action) => [action.action, action.params.user_id, action.params.message]), [
    ["send_private_msg", 456, [{ type: "record", data: { file: "https://example.test/a.mp3" } }]],
    ["send_private_msg", 456, [{ type: "video", data: { file: `base64://${base64("video bytes")}` } }]],
  ]);
});

test("sendQQMessage uploads files to groups and private chats with the given name", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-qq-send-"));
  const filePath = join(tempDir, "report.txt");
  writeFileSync(filePath, "report", "utf-8");

  const server = new FakeOneBot();
  const group = await sendQQMessage(server as never, "group:123", {
    kind: "file",
    fileName: "weekly.txt",
    source: { kind: "hostPath", hostPath: filePath },
  });
  const direct = await sendQQMessage(server as never, "user:456", {
    kind: "file",
    source: { kind: "url", url: "https://example.test/report.pdf" },
  });

  assert.equal(group.delivered, true);
  assert.equal(direct.delivered, true);
  assert.deepEqual(server.actions, [
    { action: "upload_group_file", params: { group_id: 123, file: `base64://${base64("report")}`, name: "weekly.txt" } },
    { action: "upload_private_file", params: { user_id: 456, file: "https://example.test/report.pdf", name: "report.pdf" } },
  ]);
});

test("sendQQMessage rejects unsupported targets and reports OneBot failures", async () => {
  const server = new FakeOneBot();
  for (const target of ["channel:1", "group:abc", ""]) {
    const result = await sendQQMessage(server as never, target, { kind: "text", text: "hi" });
    assert.equal(result.delivered, false);
    assert.equal(result.errorCode, "UnsupportedTarget");
  }
  assert.equal(server.actions.length, 0);

  const failing = new FakeOneBot({ status: "failed", retcode: 100, wording: "not a friend" });
  const result = await sendQQMessage(failing as never, "user:456", { kind: "text", text: "hi" });
  assert.deepEqual(result, { delivered: false, errorCode: "AdapterDeliveryFailed", errorMessage: "not a friend" });
});
