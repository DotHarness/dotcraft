import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { stageChannelSeeds } from "./stage-channel-seeds.mjs";

test("stages all five plugin seeds offline only after verifying the pinned archive", async (t) => {
  const root = await mkdtemp(join(tmpdir(), "dotcraft-seeds-"));
  t.after(() => rm(root, { force: true, recursive: true }));
  const ids = ["feishu", "qq", "telegram", "wecom", "weixin"].map((name) => `channel-${name}`);
  const entry = "./channel/dist/cli.bundle.mjs";
  const manifest = "./channel/manifest.json";
  for (const id of ids) {
    await mkdir(join(root, id, ".craft-plugin"), { recursive: true });
    await mkdir(join(root, id, "channel", "dist"), { recursive: true });
    await writeFile(join(root, id, ".craft-plugin", "plugin.json"), JSON.stringify({
      id, interface: { category: "Channels" }, channel: { manifest, entry },
    }));
    await writeFile(join(root, id, manifest), JSON.stringify({ channelName: id.slice("channel-".length) }));
    await writeFile(join(root, id, entry), "export const bundle = true;");
  }

  const archive = join(root, "channel-seeds.tar.gz");
  const tar = spawnSync("tar", ["-czf", archive, "-C", root, ...ids], { encoding: "utf8" });
  assert.equal(tar.status, 0, tar.stderr);
  const destination = join(root, "output");
  const sha256 = createHash("sha256").update(await readFile(archive)).digest("hex");
  await assert.rejects(stageChannelSeeds(archive, "a".repeat(64), destination), /checksum mismatch/);
  await stageChannelSeeds(archive, sha256, destination);
  const launchers = await Promise.all(ids.map((id) => readFile(join(destination, id, entry), "utf8")));
  assert.deepEqual(launchers, ids.map(() => "export const bundle = true;"));
});
