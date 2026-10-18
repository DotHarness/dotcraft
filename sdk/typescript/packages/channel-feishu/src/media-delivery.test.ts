import assert from "node:assert/strict";
import test from "node:test";

import { FeishuChannelSender } from "./channel-send.js";
import type { FeishuSendResult } from "./feishu-types.js";
import { FeishuMediaDelivery } from "./media-delivery.js";

class FakeMediaRouter {
  readonly calls: Array<{ method: string; target: string; bytes?: string; messageType?: string }> = [];

  async sendCard(target: string, _card: Record<string, unknown>): Promise<FeishuSendResult> {
    this.calls.push({ method: "card", target });
    return { messageId: "om_card", chatId: target };
  }

  async sendFile(
    target: string,
    file: { data: Buffer },
    messageType = "file",
  ): Promise<FeishuSendResult & { fileKey: string }> {
    this.calls.push({ method: "file", target, bytes: file.data.toString("utf-8"), messageType });
    return { messageId: "om_file", chatId: target, fileKey: "fk" };
  }

  async sendImage(target: string, image: { data: Buffer }): Promise<FeishuSendResult & { imageKey: string }> {
    this.calls.push({ method: "image", target, bytes: image.data.toString("utf-8") });
    return { messageId: "om_image", chatId: target, imageKey: "ik" };
  }
}

test("FeishuMediaDelivery sends structured images as image messages", async () => {
  const router = new FakeMediaRouter();
  const delivery = new FeishuMediaDelivery(router as never);

  const result = await delivery.send("group:oc_1", {
    kind: "image",
    fileName: "ig_1.png",
    source: { kind: "dataBase64", dataBase64: Buffer.from("generated").toString("base64") },
  });

  assert.deepEqual(result, { delivered: true, remoteMessageId: "om_image", remoteMediaId: "ik" });
  assert.deepEqual(router.calls, [{ method: "image", target: "group:oc_1", bytes: "generated" }]);
});

test("FeishuMediaDelivery sends audio as a voice message and video as a media message", async () => {
  const router = new FakeMediaRouter();
  const delivery = new FeishuMediaDelivery(router as never);
  const source = (text: string) => ({ kind: "dataBase64", dataBase64: Buffer.from(text).toString("base64") });

  const audio = await delivery.send("dm:ou_1", { kind: "audio", fileName: "note.opus", source: source("opus") });
  const video = await delivery.send("dm:ou_1", { kind: "video", fileName: "clip.mp4", source: source("mp4") });

  assert.equal(audio.delivered, true);
  assert.equal(video.delivered, true);
  assert.deepEqual(router.calls, [
    { method: "file", target: "dm:ou_1", bytes: "opus", messageType: "audio" },
    { method: "file", target: "dm:ou_1", bytes: "mp4", messageType: "media" },
  ]);
});

test("FeishuChannelSender rejects targets outside the Feishu syntax before sending", async () => {
  const router = new FakeMediaRouter();
  const sender = new FeishuChannelSender({
    router: router as never,
    media: new FeishuMediaDelivery(router as never),
    cardTitle: () => "DotCraft",
  });

  const result = await sender.send("telegram:123", { kind: "text", text: "hi" });

  assert.equal(result.delivered, false);
  assert.equal(result.errorCode, "UnsupportedTarget");
  assert.deepEqual(router.calls, []);
});
