import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import test from "node:test";

import { saveInboundImage } from "./inboundImage.js";

test("saveInboundImage writes under the workspace attachments folder", async () => {
  const craftPath = await mkdtemp(join(tmpdir(), "dotcraft-inbound-image-"));
  try {
    const part = await saveInboundImage(craftPath, Buffer.from("png bytes"), "image/png");

    assert.equal(part.type, "localImage");
    const { path, mimeType, fileName } = part as { path: string; mimeType: string; fileName: string };
    assert.equal(dirname(path), join(craftPath, "attachments", "images"));
    assert.equal(mimeType, "image/png");
    assert.match(fileName, /^[0-9a-f-]{36}\.png$/);
    assert.equal(join(dirname(path), fileName), path);
    assert.equal(await readFile(path, "utf-8"), "png bytes");
  } finally {
    await rm(craftPath, { recursive: true, force: true });
  }
});
