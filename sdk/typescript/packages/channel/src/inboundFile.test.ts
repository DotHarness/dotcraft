import assert from "node:assert/strict";
import { mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { basename, dirname, join } from "node:path";
import test from "node:test";

import { saveInboundFile } from "./inboundFile.js";

test("saveInboundFile cannot be steered outside its folder by the file name", async () => {
  const craftPath = await mkdtemp(join(tmpdir(), "dotcraft-inbound-file-"));
  try {
    const first = String((await saveInboundFile(craftPath, Buffer.from("a"), "../../escape.txt") as { path: string }).path);
    const second = String((await saveInboundFile(craftPath, Buffer.from("b"), "..") as { path: string }).path);

    assert.equal(basename(first), "escape.txt");
    assert.equal(dirname(dirname(first)), join(craftPath, "attachments", "files"));
    assert.equal(basename(second), "attachment");
  } finally {
    await rm(craftPath, { recursive: true, force: true });
  }
});
