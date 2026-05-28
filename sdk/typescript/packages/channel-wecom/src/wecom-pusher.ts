import { readFile } from "node:fs/promises";
import { createHash, randomUUID } from "node:crypto";

import {
  splitWeComMessage,
  WeComInterChunkDelayMs,
  WeComMarkdownMaxBytes,
  WeComTextMaxBytes,
} from "./message-splitter.js";

export interface WeComArticle {
  title?: string;
  description?: string;
  url?: string;
  picurl?: string;
}

export class WeComPusher {
  constructor(
    private readonly chatId: string,
    private readonly webhookUrl: string,
  ) {
    if (!chatId) throw new Error("chatId is required.");
    if (!webhookUrl) throw new Error("webhookUrl is required.");
  }

  getChatId(): string {
    return this.chatId;
  }

  async pushText(content: string, mentionedList?: string[], mentionedMobileList?: string[], visibleToUser?: string[]): Promise<void> {
    const chunks = splitWeComMessage(content, WeComTextMaxBytes);
    for (let i = 0; i < chunks.length; i += 1) {
      if (i > 0) await delay(WeComInterChunkDelayMs);
      await this.post({
        chatid: this.chatId,
        msgtype: "text",
        text: {
          content: chunks[i],
          mentioned_list: i === 0 ? mentionedList : undefined,
          mentioned_mobile_list: i === 0 ? mentionedMobileList : undefined,
        },
        visible_to_user: visibleToUser?.join("|"),
      });
    }
  }

  async pushMarkdown(content: string, visibleToUser?: string[]): Promise<void> {
    const chunks = splitWeComMessage(content, WeComMarkdownMaxBytes);
    for (let i = 0; i < chunks.length; i += 1) {
      if (i > 0) await delay(WeComInterChunkDelayMs);
      await this.post({
        chatid: this.chatId,
        msgtype: "markdown",
        markdown: { content: chunks[i] },
        visible_to_user: visibleToUser?.join("|"),
      });
    }
  }

  async pushImage(imageData: Buffer, visibleToUser?: string[]): Promise<void> {
    await this.post({
      chatid: this.chatId,
      msgtype: "image",
      image: {
        base64: imageData.toString("base64"),
        md5: createHash("md5").update(imageData).digest("hex"),
      },
      visible_to_user: visibleToUser?.join("|"),
    });
  }

  async pushNews(articles: WeComArticle[], visibleToUser?: string[]): Promise<void> {
    await this.post({
      chatid: this.chatId,
      msgtype: "news",
      news: { articles },
      visible_to_user: visibleToUser?.join("|"),
    });
  }

  async pushVoice(mediaId: string): Promise<void> {
    await this.post({
      chatid: this.chatId,
      msgtype: "voice",
      voice: { media_id: mediaId },
    });
  }

  async pushFile(mediaId: string): Promise<void> {
    await this.post({
      chatid: this.chatId,
      msgtype: "file",
      file: { media_id: mediaId },
    });
  }

  async uploadMediaFromPath(path: string, filename: string, type: "voice" | "file"): Promise<string> {
    return await this.uploadMedia(await readFile(path), filename, type);
  }

  async uploadMedia(fileBytes: Buffer, filename: string, type: "voice" | "file"): Promise<string> {
    const uploadUrl = this.buildUploadUrl(type);
    const boundary = `----DotCraft${randomUUID().replace(/-/g, "")}`;
    const header = Buffer.from(
      `--${boundary}\r\n` +
        `Content-Disposition: form-data; name="media"; filename="${escapeHeader(filename)}"; filelength=${fileBytes.length}\r\n` +
        "Content-Type: application/octet-stream\r\n" +
        "\r\n",
      "utf-8",
    );
    const footer = Buffer.from(`\r\n--${boundary}--\r\n`, "utf-8");
    const response = await fetch(uploadUrl, {
      method: "POST",
      headers: { "Content-Type": `multipart/form-data; boundary=${boundary}` },
      body: Buffer.concat([header, fileBytes, footer]),
    });
    const body = await response.text();
    if (!response.ok) throw new Error(`Upload media failed: HTTP ${response.status} ${body}`);
    const json = JSON.parse(body) as Record<string, unknown>;
    const errcode = Number(json.errcode ?? 0);
    if (errcode !== 0) throw new Error(`Upload media failed: ${String(json.errmsg ?? "unknown")}`);
    const mediaId = String(json.media_id ?? "");
    if (!mediaId) throw new Error("Upload media response does not contain media_id.");
    return mediaId;
  }

  async pushRaw(jsonData: string): Promise<void> {
    await this.postRaw(jsonData);
  }

  buildUploadUrl(type: "voice" | "file"): string {
    const url = new URL(this.webhookUrl);
    const key = url.searchParams.get("key") ?? "";
    if (!key) throw new Error("Cannot extract key parameter from WeCom webhook URL.");
    return `${url.protocol}//${url.host}/cgi-bin/webhook/upload_media?key=${encodeURIComponent(key)}&type=${type}`;
  }

  private async post(message: Record<string, unknown>): Promise<void> {
    const json = JSON.stringify(removeUndefined(message));
    await this.postRaw(json);
  }

  private async postRaw(json: string): Promise<void> {
    const response = await fetch(this.webhookUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json; charset=utf-8" },
      body: json,
    });
    if (!response.ok) {
      throw new Error(`Push message failed: HTTP ${response.status} ${await response.text()}`);
    }
  }
}

function removeUndefined(value: unknown): unknown {
  if (Array.isArray(value)) return value.map(removeUndefined);
  if (!value || typeof value !== "object") return value;
  return Object.fromEntries(
    Object.entries(value)
      .filter(([, item]) => item !== undefined)
      .map(([key, item]) => [key, removeUndefined(item)]),
  );
}

function escapeHeader(value: string): string {
  return value.replace(/["\r\n]/g, "_");
}

function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

