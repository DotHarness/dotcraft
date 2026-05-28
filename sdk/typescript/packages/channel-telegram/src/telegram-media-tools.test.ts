import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

import {
  DOCUMENT_TOOL_NAME,
  TelegramMediaError,
  TelegramMediaTools,
  VOICE_TOOL_NAME,
  type TelegramApiLike,
} from "./telegram-media-tools.js";

class FakeTelegramApi implements TelegramApiLike {
  readonly calls: Array<{ method: string; chatId: number | string; other?: Record<string, unknown> }> = [];

  async sendMessage(
    chatId: number | string,
    _text: string,
    other?: Record<string, unknown>,
  ): Promise<{ message_id: number }> {
    this.calls.push({ method: "sendMessage", chatId, other });
    return { message_id: 9 };
  }

  async sendDocument(
    chatId: number | string,
    _document: unknown,
    other?: Record<string, unknown>,
  ): Promise<{ message_id: number; document: { file_id: string } }> {
    this.calls.push({ method: "sendDocument", chatId, other });
    return { message_id: 10, document: { file_id: "doc-file-id" } };
  }

  async sendVoice(
    chatId: number | string,
    _voice: unknown,
    other?: Record<string, unknown>,
  ): Promise<{ message_id: number; voice: { file_id: string } }> {
    this.calls.push({ method: "sendVoice", chatId, other });
    return { message_id: 11, voice: { file_id: "voice-file-id" } };
  }
}

test("executeToolCall sends document and returns structured result", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "telegram-doc-"));
  const filePath = join(tempDir, "report.pdf");
  writeFileSync(filePath, "pdf-data", "utf-8");

  const api = new FakeTelegramApi();
  const tools = new TelegramMediaTools();
  const result = await tools.executeToolCall(api, DOCUMENT_TOOL_NAME, 123, {
    filePath,
    fileName: "report.pdf",
    caption: "hello",
  });

  assert.equal(result.success, true);
  assert.equal(api.calls[0]?.method, "sendDocument");
  assert.equal(api.calls[0]?.other?.caption, "hello");
  assert.equal(
    (result.structuredResult as Record<string, unknown>).mediaId,
    "doc-file-id",
  );
});

test("executeToolCall rejects invalid voice extension", async () => {
  const api = new FakeTelegramApi();
  const tools = new TelegramMediaTools();

  await assert.rejects(
    async () =>
      await tools.executeToolCall(api, VOICE_TOOL_NAME, 123, {
        fileUrl: "https://example.com/voice.mp3",
        fileName: "voice.mp3",
      }),
    (error: unknown) =>
      error instanceof TelegramMediaError &&
      /voice notes should use OGG\/Opus/.test(error.message),
  );
});

test("sendStructuredMessage sends audio with duration", async () => {
  const api = new FakeTelegramApi();
  const tools = new TelegramMediaTools();
  const result = await tools.sendStructuredMessage(
    api,
    123,
    {
      kind: "audio",
      fileName: "voice.ogg",
      duration: 12,
      source: {
        kind: "dataBase64",
        dataBase64: Buffer.from("ogg-data").toString("base64"),
      },
    },
    {},
  );

  assert.equal(result.delivered, true);
  assert.equal(api.calls[0]?.method, "sendVoice");
  assert.equal(api.calls[0]?.other?.duration, 12);
});
