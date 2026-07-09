import assert from "node:assert/strict";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import {
  MediaSourceError,
  mediaSourceFromToolPath,
  prepareMediaBytes,
  prepareMediaTempFile,
  prepareMediaUploadUri,
} from "./mediaSource.js";

test("prepareMediaBytes reads hostPath sources", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-media-source-"));
  try {
    const filePath = join(dir, "report.txt");
    await writeFile(filePath, "hello", "utf-8");

    const prepared = await prepareMediaBytes(mediaSourceFromToolPath(filePath));

    assert.equal(prepared.fileName, "report.txt");
    assert.equal(prepared.bytes.toString("utf-8"), "hello");
    assert.equal(prepared.byteLength, 5);
    assert.equal(prepared.mediaType, "text/plain");
    assert.equal(prepared.sourceKind, "hostPath");
    assert.match(prepared.md5, /^[0-9a-f]{32}$/);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("prepareMediaBytes decodes dataBase64 sources", async () => {
  const prepared = await prepareMediaBytes(
    { kind: "dataBase64", dataBase64: Buffer.from("abc", "utf-8").toString("base64") },
    { fallbackFileName: "payload.bin" },
  );

  assert.equal(prepared.fileName, "payload.bin");
  assert.equal(prepared.bytes.toString("utf-8"), "abc");
  assert.equal(prepared.sourceKind, "dataBase64");
});

test("prepareMediaBytes requires URL opt-in and fetches allowed URLs", async () => {
  await assert.rejects(
    async () =>
      await prepareMediaBytes({ kind: "url", url: "https://example.test/report.pdf" }),
    (error: unknown) => error instanceof MediaSourceError && error.code === "UnsupportedMediaSource",
  );

  const prepared = await prepareMediaBytes(
    { kind: "url", url: "https://example.test/report.pdf" },
    {
      allowUrl: true,
      fetch: (async () =>
        new Response(Buffer.from("pdf"), {
          status: 200,
          headers: { "content-type": "application/pdf", "content-length": "3" },
        })) as typeof fetch,
    },
  );

  assert.equal(prepared.fileName, "report.pdf");
  assert.equal(prepared.mediaType, "application/pdf");
  assert.equal(prepared.bytes.toString("utf-8"), "pdf");
});

test("prepareMediaBytes rejects invalid base64 and size overflows", async () => {
  await assert.rejects(
    async () => await prepareMediaBytes({ kind: "dataBase64", dataBase64: "%%%" }),
    (error: unknown) => error instanceof MediaSourceError && error.code === "InvalidArguments",
  );

  await assert.rejects(
    async () =>
      await prepareMediaBytes(
        { kind: "dataBase64", dataBase64: Buffer.from("too big").toString("base64") },
        { maxBytes: 3 },
      ),
    (error: unknown) => error instanceof MediaSourceError && error.message.includes("exceeding"),
  );
});

test("prepareMediaTempFile writes a cleanup-scoped file", async () => {
  const prepared = await prepareMediaTempFile(
    { kind: "dataBase64", dataBase64: Buffer.from("tmp").toString("base64") },
    { fileName: "a:b.txt" },
  );

  assert.equal(prepared.path.endsWith("a_b.txt"), true);
  await prepared.cleanup();
});

test("prepareMediaUploadUri converts hostPath to base64 URI by default", async () => {
  const dir = await mkdtemp(join(tmpdir(), "dotcraft-onebot-source-"));
  try {
    const filePath = join(dir, "napcat.txt");
    await writeFile(filePath, "napcat", "utf-8");

    const prepared = await prepareMediaUploadUri(mediaSourceFromToolPath(filePath));

    assert.equal(prepared.fileName, "napcat.txt");
    assert.equal(prepared.sourceKind, "hostPath");
    assert.equal(prepared.uri, `base64://${Buffer.from("napcat").toString("base64")}`);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("prepareMediaUploadUri passes URL sources through", async () => {
  const prepared = await prepareMediaUploadUri({ kind: "url", url: "https://example.test/a.zip" });

  assert.equal(prepared.uri, "https://example.test/a.zip");
  assert.equal(prepared.fileName, "a.zip");
  assert.equal(prepared.sourceKind, "url");
});
