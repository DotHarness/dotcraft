import assert from "node:assert/strict";
import test from "node:test";
import { mkdtempSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { tmpdir } from "node:os";

import {
  WEIXIN_SEND_FILE_TOOL,
  WEIXIN_SEND_IMAGE_TOOL,
  WeixinMediaError,
  WeixinMediaTools,
  aesEcbPaddedSize,
  buildCdnUploadUrl,
  md5Hex,
  type WeixinMediaApi,
} from "./weixin-media-tools.js";
import type { GetUploadUrlReq, GetUploadUrlResp, SendMessageReq } from "./weixin-types.js";
import { MessageItemType } from "./weixin-types.js";
import { WeixinAdapter } from "./weixin-adapter.js";

class FakeWeixinApi implements WeixinMediaApi {
  readonly uploadBodies: GetUploadUrlReq[] = [];
  readonly sentBodies: SendMessageReq[] = [];

  constructor(private readonly uploadResponse: GetUploadUrlResp) {}

  async getUploadUrl(opts: { body: GetUploadUrlReq }): Promise<GetUploadUrlResp> {
    this.uploadBodies.push(opts.body);
    return this.uploadResponse;
  }

  async sendMessage(opts: { body: SendMessageReq }): Promise<void> {
    this.sentBodies.push(opts.body);
  }
}

class TestWeixinAdapter extends WeixinAdapter {
  exposeDeliveryCapabilities(): Record<string, unknown> | null {
    return this.getDeliveryCapabilities();
  }

  exposeChannelTools(): Record<string, unknown>[] | null {
    return this.getChannelTools();
  }

  async exposeSend(target: string, message: Record<string, unknown>): Promise<Record<string, unknown>> {
    return await this.onSend(target, message, {});
  }
}

function assertHex(value: unknown, length: number): asserts value is string {
  if (typeof value !== "string") {
    assert.fail(`Expected hex string, got ${typeof value}`);
  }
  assert.match(value, new RegExp(`^[0-9a-f]{${length}}$`));
}

function base64OfUtf8(value: string): string {
  return Buffer.from(value, "utf-8").toString("base64");
}

test("crypto helpers calculate AES padded size and md5 metadata", () => {
  assert.equal(aesEcbPaddedSize(0), 16);
  assert.equal(aesEcbPaddedSize(15), 16);
  assert.equal(aesEcbPaddedSize(16), 32);
  assert.equal(md5Hex(Buffer.from("abc", "utf-8")), "900150983cd24fb0d6963f7d28e17f72");
});

test("buildCdnUploadUrl prefers full URL and falls back to documented query shape", () => {
  assert.equal(
    buildCdnUploadUrl({
      cdnBaseUrl: "https://cdn.example/c2c",
      uploadFullUrl: "https://upload.example/full",
      uploadParam: "ignored",
      filekey: "file",
    }),
    "https://upload.example/full",
  );
  assert.equal(
    buildCdnUploadUrl({
      cdnBaseUrl: "https://cdn.example/c2c",
      uploadParam: "a+b",
      filekey: "file name.txt",
    }),
    "https://cdn.example/c2c/upload?encrypted_query_param=a%2Bb&filekey=file%20name.txt",
  );
});

test("sendStructuredMessage sends a file_item after encrypted CDN upload", async () => {
  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_full_url: "https://upload.example/file" });
  let uploadedBytes = 0;
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    uploadedBytes = Buffer.from((init?.body as Uint8Array) ?? []).length;
    return new Response("", { status: 200, headers: { "x-encrypted-param": "download-file" } });
  }) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api);
    const result = await tools.sendStructuredMessage({
      baseUrl: "https://ilink.example",
      token: "token",
      toUserId: "user@im.wechat",
      contextToken: "ctx",
      clientId: "client-1",
      message: {
        kind: "file",
        fileName: "report.txt",
        source: { kind: "dataBase64", dataBase64: Buffer.from("hello").toString("base64") },
      },
    });

    assert.equal(result.delivered, true);
    assert.equal(result.uploadStage, "completed");
    assert.equal(result.sendStage, "completed");
    assert.equal(result.mediaKind, "file");
    assert.equal(result.fileName, "report.txt");
    assert.equal(result.bytes, 5);
    assert.equal(uploadedBytes, 16);
    const uploadBody = api.uploadBodies[0];
    assert.equal(uploadBody?.media_type, 3);
    assertHex(uploadBody?.filekey, 32);
    assertHex(uploadBody?.aeskey, 32);
    assert.equal(uploadBody?.rawsize, 5);
    assert.equal(uploadBody?.filesize, 16);
    assert.equal(uploadBody?.no_need_thumb, true);
    const item = api.sentBodies[0]?.msg?.item_list?.[0];
    assert.equal(item?.type, MessageItemType.FILE);
    assert.equal(item?.file_item?.file_name, "report.txt");
    assert.equal(item?.file_item?.media?.encrypt_query_param, "download-file");
    assert.equal(item?.file_item?.media?.aes_key, base64OfUtf8(uploadBody?.aeskey ?? ""));
    assert.equal(item?.file_item?.media?.encrypt_type, 1);
    assert.equal(item?.file_item?.len, "5");
    assert.equal(Object.hasOwn(item?.file_item ?? {}, "md5"), false);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("sendStructuredMessage sends an image_item without thumbnail upload", async () => {
  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_param: "image-upload", thumb_upload_param: "thumb-upload" });
  const uploadedUrls: string[] = [];
  let uploadedBytes = 0;
  globalThis.fetch = (async (input: string | URL | Request, init?: RequestInit) => {
    uploadedUrls.push(String(input));
    uploadedBytes = Buffer.from((init?.body as Uint8Array) ?? []).length;
    return new Response("", { status: 200, headers: { "x-encrypted-param": "download-image" } });
  }) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api, "https://cdn.example/c2c");
    await tools.sendStructuredMessage({
      baseUrl: "https://ilink.example",
      token: "token",
      toUserId: "user@im.wechat",
      contextToken: "ctx",
      clientId: "client-1",
      message: {
        kind: "image",
        fileName: "image.jpg",
        source: { kind: "dataBase64", dataBase64: Buffer.from("image-bytes").toString("base64") },
      },
    });

    const uploadBody = api.uploadBodies[0];
    assert.equal(uploadBody?.media_type, 1);
    assertHex(uploadBody?.filekey, 32);
    assertHex(uploadBody?.aeskey, 32);
    assert.equal(uploadBody?.rawsize, 11);
    assert.equal(uploadBody?.filesize, 16);
    assert.equal(uploadBody?.no_need_thumb, true);
    assert.equal(Object.hasOwn(uploadBody ?? {}, "thumb_rawsize"), false);
    assert.equal(Object.hasOwn(uploadBody ?? {}, "thumb_filesize"), false);
    assert.equal(uploadedBytes, 16);
    assert.equal(uploadedUrls.length, 1);
    assert.equal(uploadedUrls[0]?.includes("encrypted_query_param=image-upload"), true);
    assert.equal(uploadedUrls[0]?.includes("thumb-upload"), false);
    const item = api.sentBodies[0]?.msg?.item_list?.[0];
    assert.equal(item?.type, MessageItemType.IMAGE);
    assert.equal(item?.image_item?.media?.encrypt_query_param, "download-image");
    assert.equal(item?.image_item?.media?.aes_key, base64OfUtf8(uploadBody?.aeskey ?? ""));
    assert.equal(item?.image_item?.media?.encrypt_type, 1);
    assert.equal(item?.image_item?.mid_size, 16);
    assert.equal(Object.hasOwn(item?.image_item ?? {}, "thumb_media"), false);
    assert.equal(Object.hasOwn(item?.image_item ?? {}, "aeskey"), false);
    assert.equal(Object.hasOwn(item?.image_item ?? {}, "hd_size"), false);
    assert.equal(Object.hasOwn(item?.image_item ?? {}, "thumb_size"), false);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("CDN upload retries 5xx responses and preserves x-error-message", async () => {
  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_full_url: "https://upload.example/file" });
  let attempts = 0;
  globalThis.fetch = (async () => {
    attempts += 1;
    return new Response("temporary body", {
      status: 500,
      headers: { "x-error-message": "cdn busy" },
    });
  }) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api);
    await assert.rejects(
      async () =>
        await tools.sendStructuredMessage({
          baseUrl: "https://ilink.example",
          token: "token",
          toUserId: "user@im.wechat",
          contextToken: "ctx",
          clientId: "client-1",
          message: {
            kind: "image",
            fileName: "image.jpg",
            source: { kind: "dataBase64", dataBase64: Buffer.from("image-bytes").toString("base64") },
          },
        }),
      (error: unknown) =>
        error instanceof WeixinMediaError &&
        error.code === "CdnUploadFailed" &&
        error.message.includes("HTTP 500") &&
        error.message.includes("cdn busy"),
    );
    assert.equal(attempts, 3);
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("media tools reject invalid sources", async () => {
  const tools = new WeixinMediaTools(new FakeWeixinApi({ upload_full_url: "https://upload.example/file" }));

  await assert.rejects(
    async () =>
      await tools.sendStructuredMessage({
        baseUrl: "https://ilink.example",
        toUserId: "user@im.wechat",
        clientId: "client",
        message: { kind: "file", source: { kind: "dataBase64", dataBase64: "%%%" } },
      }),
    (error: unknown) => error instanceof WeixinMediaError && error.code === "InvalidArguments",
  );

  await assert.rejects(
    async () =>
      await tools.sendStructuredMessage({
        baseUrl: "https://ilink.example",
        toUserId: "user@im.wechat",
        clientId: "client",
        message: { kind: "file", source: { kind: "url", url: "https://example.com/a.pdf" } },
      }),
    (error: unknown) => error instanceof WeixinMediaError && error.code === "UnsupportedMediaSource",
  );
});

test("tool call resolves host path file names and exposes approval metadata", async () => {
  const tempDir = mkdtempSync(join(tmpdir(), "weixin-file-"));
  const filePath = join(tempDir, "report.txt");
  writeFileSync(filePath, "hello", "utf-8");

  const originalFetch = globalThis.fetch;
  const api = new FakeWeixinApi({ upload_full_url: "https://upload.example/file" });
  globalThis.fetch = (async () =>
    new Response("", { status: 200, headers: { "x-encrypted-param": "download-file" } })) as typeof fetch;

  try {
    const tools = new WeixinMediaTools(api);
    const toolDescriptors = tools.getChannelTools();
    assert.equal(toolDescriptors[0]?.name, WEIXIN_SEND_FILE_TOOL);
    assert.deepEqual((toolDescriptors[0]?.approval as Record<string, unknown>).targetArgument, "filePath");
    assert.equal(toolDescriptors[1]?.name, WEIXIN_SEND_IMAGE_TOOL);
    assert.deepEqual((toolDescriptors[1]?.approval as Record<string, unknown>).targetArgument, "imagePath");

    const result = await tools.executeToolCall({
      baseUrl: "https://ilink.example",
      toUserId: "user@im.wechat",
      clientId: "client",
      toolName: WEIXIN_SEND_FILE_TOOL,
      args: { filePath },
    });
    assert.equal(result.success, true);
    assert.equal(api.sentBodies[0]?.msg?.item_list?.[0]?.file_item?.file_name, "report.txt");
  } finally {
    globalThis.fetch = originalFetch;
  }
});

test("adapter advertises real file and image media capabilities", () => {
  const adapter = new TestWeixinAdapter();
  const capabilities = adapter.exposeDeliveryCapabilities() as Record<string, unknown>;
  const media = capabilities.media as Record<string, Record<string, unknown>>;
  assert.equal(media.file?.supportsHostPath, true);
  assert.equal(media.file?.supportsBase64, true);
  assert.equal(media.image?.supportsHostPath, true);
  assert.equal(media.image?.supportsBase64, true);
});

test("adapter sends caption as normalized text before media delivery", async () => {
  const originalFetch = globalThis.fetch;
  const adapter = new TestWeixinAdapter();
  const order: string[] = [];
  const internals = adapter as unknown as {
    apiBaseUrl: string;
    botToken: string;
    contextTokens: Record<string, string>;
    mediaTools: {
      sendStructuredMessage(): Promise<Record<string, unknown>>;
      getDeliveryCapabilities(): Record<string, unknown>;
      getChannelTools(): Record<string, unknown>[];
    };
  };
  internals.apiBaseUrl = "https://ilink.example";
  internals.botToken = "token";
  internals.contextTokens = { "user@im.wechat": "ctx" };
  internals.mediaTools = {
    async sendStructuredMessage(): Promise<Record<string, unknown>> {
      order.push("media");
      return { delivered: true, remoteMediaId: "media-id" };
    },
    getDeliveryCapabilities(): Record<string, unknown> {
      return {};
    },
    getChannelTools(): Record<string, unknown>[] {
      return [];
    },
  };

  let followUpBody: Record<string, unknown> = {};
  globalThis.fetch = (async (_input: string | URL | Request, init?: RequestInit) => {
    order.push("caption");
    followUpBody = JSON.parse(String(init?.body)) as Record<string, unknown>;
    return new Response("", { status: 200 });
  }) as typeof fetch;

  try {
    const result = await adapter.exposeSend("user@im.wechat", {
      kind: "file",
      caption: "**hello** [site](https://example.com)",
      source: { kind: "dataBase64", dataBase64: Buffer.from("x").toString("base64") },
    });
    assert.equal(result.delivered, true);
    assert.deepEqual(order, ["caption", "media"]);
    const msg = followUpBody.msg as Record<string, unknown>;
    const item = (msg.item_list as Array<Record<string, { text?: string }>>)[0];
    assert.equal(item?.text_item?.text, "hello site");
    assert.equal(msg.context_token, "ctx");
  } finally {
    globalThis.fetch = originalFetch;
  }
});
