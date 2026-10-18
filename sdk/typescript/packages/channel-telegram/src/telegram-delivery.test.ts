import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

import { InputFile } from "grammy";

import { parseTargetChatId, sendTelegramMessage } from "./telegram-delivery.js";
import type { TelegramApiLike, TelegramMessageLike } from "./telegram-media.js";

type Call = { method: string; chatId: number | string; payload: unknown; other?: Record<string, unknown> };

function recordingApi(): { api: TelegramApiLike; calls: Call[] } {
  const calls: Call[] = [];
  const record = (method: string) =>
    async (chatId: number | string, payload: unknown, other?: Record<string, unknown>): Promise<TelegramMessageLike> => {
      calls.push({ method, chatId, payload, other });
      return { message_id: 100 + calls.length };
    };
  return {
    calls,
    api: {
      sendMessage: record("sendMessage"),
      sendDocument: record("sendDocument"),
      sendVoice: record("sendVoice"),
      sendPhoto: record("sendPhoto"),
      sendAudio: record("sendAudio"),
      sendVideo: record("sendVideo"),
    },
  };
}

function hostFile(name: string): string {
  const path = join(mkdtempSync(join(tmpdir(), "telegram-send-")), name);
  writeFileSync(path, "bytes", "utf-8");
  return path;
}

test("parseTargetChatId accepts chat ids and consistent user/group prefixes", () => {
  assert.equal(parseTargetChatId("-100123"), -100123);
  assert.equal(parseTargetChatId("group:-100123"), -100123);
  assert.equal(parseTargetChatId("user:42"), 42);
  assert.equal(parseTargetChatId("bad-target"), null);
  assert.equal(parseTargetChatId("group:"), null);
  assert.equal(parseTargetChatId("user:-100123"), null);
  assert.equal(parseTargetChatId("group:42"), null);
  assert.equal(parseTargetChatId("0"), null);
});

test("sendTelegramMessage routes each attachment kind to its Bot API method", async () => {
  const { api, calls } = recordingApi();
  const send = async (kind: string, name: string, caption?: string) =>
    await sendTelegramMessage(api, "-100123", {
      kind,
      caption,
      source: { kind: "hostPath", hostPath: hostFile(name) },
    });

  const image = await send("image", "chart.png", "**Weekly** chart");
  await send("file", "report.pdf");
  await send("audio", "note.ogg");
  await send("audio", "song.mp3");
  await send("video", "clip.mp4");

  assert.deepEqual(image, { delivered: true, remoteMessageId: "101", remoteMediaId: null });
  assert.deepEqual(calls.map((call) => call.method), ["sendPhoto", "sendDocument", "sendVoice", "sendAudio", "sendVideo"]);
  assert.ok(calls.every((call) => call.chatId === -100123));
  assert.deepEqual(calls[0]?.other, { caption: "<b>Weekly</b> chart", parse_mode: "HTML" });
  assert.equal((calls[1]?.payload as InputFile).filename, "report.pdf");
});

test("sendTelegramMessage sends a caption over the platform limit as text before the media", async () => {
  const { api, calls } = recordingApi();
  const caption = "a".repeat(1100);

  const result = await sendTelegramMessage(api, "42", {
    kind: "file",
    fileName: "notes.txt",
    caption,
    source: { kind: "dataBase64", dataBase64: Buffer.from("notes").toString("base64") },
  });

  assert.equal(result.delivered, true);
  assert.equal(result.remoteMessageId, "102");
  assert.deepEqual(calls.map((call) => call.method), ["sendMessage", "sendDocument"]);
  assert.deepEqual(calls[1]?.other, {});
  assert.equal((calls[1]?.payload as InputFile).filename, "notes.txt");
});

test("sendTelegramMessage rejects unreachable targets and unknown kinds without sending", async () => {
  const { api, calls } = recordingApi();

  const badTarget = await sendTelegramMessage(api, "channel:general", { kind: "text", text: "hi" });
  const badKind = await sendTelegramMessage(api, "42", {
    kind: "sticker",
    source: { kind: "url", url: "https://example.com/s.webp" },
  });

  assert.equal(badTarget.delivered, false);
  assert.equal(badTarget.errorCode, "UnsupportedTarget");
  assert.equal(badKind.errorCode, "UnsupportedDeliveryKind");
  assert.equal(calls.length, 0);
});
