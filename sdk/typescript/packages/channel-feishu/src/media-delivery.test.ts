import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import type { FeishuSendResult } from "./feishu-types.js";
import { FEISHU_SEND_IMAGE_TOOL, FeishuMediaDelivery } from "./media-delivery.js";

class FakeMediaRouter {
  readonly calls: Array<{ method: string; target: string; bytes?: string }> = [];

  async sendCard(target: string, _card: Record<string, unknown>): Promise<FeishuSendResult> {
    this.calls.push({ method: "card", target });
    return { messageId: "om_card", chatId: target };
  }

  async sendFile(target: string, file: { data: Buffer }): Promise<FeishuSendResult & { fileKey: string }> {
    this.calls.push({ method: "file", target, bytes: file.data.toString("utf-8") });
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

test("FeishuMediaDelivery image tool sends a local image with its caption card", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-feishu-image-"));
  const imagePath = join(tempDir, "chart.png");
  writeFileSync(imagePath, "png bytes", "utf-8");
  const router = new FakeMediaRouter();
  const delivery = new FeishuMediaDelivery(router as never);

  const result = await delivery.executeToolCall(FEISHU_SEND_IMAGE_TOOL, "dm:ou_1", { imagePath, caption: "Q3" });

  assert.equal(result.success, true);
  assert.equal((result.structuredContent as Record<string, unknown>).imageKey, "ik");
  assert.deepEqual(router.calls.map((call) => call.method), ["image", "card"]);
});

test("FeishuMediaDelivery image tool reports the image as sent when only its caption card fails", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "dotcraft-feishu-image-"));
  const imagePath = join(tempDir, "chart.png");
  writeFileSync(imagePath, "png bytes", "utf-8");
  const router = new FakeMediaRouter();
  router.sendCard = async () => {
    throw new Error("card rejected");
  };
  const delivery = new FeishuMediaDelivery(router as never);

  const result = await delivery.executeToolCall(FEISHU_SEND_IMAGE_TOOL, "dm:ou_1", { imagePath, caption: "Q3" });

  assert.equal(result.success, true);
  assert.deepEqual(router.calls.map((call) => call.method), ["image"]);
});
