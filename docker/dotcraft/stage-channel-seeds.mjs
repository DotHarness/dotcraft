import { execFile } from "node:child_process";
import { createHash } from "node:crypto";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { promisify } from "node:util";

const tar = promisify(execFile);
const ids = ["channel-feishu", "channel-qq", "channel-telegram", "channel-wecom", "channel-weixin"];

export async function stageChannelSeeds(archive, sha256, destination) {
  if (!/^[0-9a-f]{64}$/.test(sha256) ||
      createHash("sha256").update(await readFile(archive)).digest("hex") !== sha256) {
    throw new Error("Channel seed archive checksum mismatch.");
  }
  const { stdout } = await tar("tar", ["-tzf", archive]);
  const entries = stdout.trim().split(/\r?\n/).map((path) => path.replace(/\/$/, ""));
  if (entries.some((path) => {
    const segments = path.split("/");
    return !ids.includes(segments[0]) ||
      segments.some((segment) => !/^[a-zA-Z0-9._-]+$/.test(segment) || [".", "..", "node_modules"].includes(segment));
  })) {
    throw new Error("Channel seed archive contains an unexpected path.");
  }
  const { stdout: details } = await tar("tar", ["-tvzf", archive]);
  if (details.trim().split(/\r?\n/).some((line) => !/^[-d]/.test(line))) {
    throw new Error("Channel seed archive contains a link or special file.");
  }
  await mkdir(destination, { recursive: true });
  await tar("tar", ["-xzf", archive, "-C", destination]);
  for (const id of ids) {
    const channel = JSON.parse(await readFile(join(destination, id, ".craft-plugin", "plugin.json"), "utf8"));
    if (channel.id !== id || channel.interface?.category !== "Channels" ||
        channel.channel?.manifest !== "./channel/manifest.json" || channel.channel?.entry !== "./channel/dist/cli.bundle.mjs") {
      throw new Error(`Channel seed ${id} is missing its contribution metadata.`);
    }
    const descriptor = JSON.parse(await readFile(join(destination, id, channel.channel.manifest), "utf8"));
    if (descriptor.channelName !== id.slice("channel-".length)) throw new Error(`Channel seed ${id} does not match its platform.`);
    await readFile(join(destination, id, channel.channel.entry));
  }
}

async function main() {
  const release = process.env.CHANNEL_SEEDS_RELEASE ?? "";
  const sha256 = process.env.CHANNEL_SEEDS_SHA256 ?? "";
  const destination = process.argv[2] || "/out/channel-seeds";
  await mkdir(destination, { recursive: true });
  if (!release && !sha256) return;
  if (!/^v[0-9]+\.[0-9]+\.[0-9]+$/.test(release) || !/^[0-9a-f]{64}$/.test(sha256)) {
    throw new Error("Supply both CHANNEL_SEEDS_RELEASE (vX.Y.Z) and CHANNEL_SEEDS_SHA256.");
  }

  const staging = await mkdtemp(join(tmpdir(), "dotcraft-channel-seeds-"));
  try {
    const response = await fetch(
      `https://github.com/DotHarness/dotcraft-channels/releases/download/${release}/channel-seeds.tar.gz`,
      { signal: AbortSignal.timeout(120_000) },
    );
    if (!response.ok) throw new Error(`Channel seed download returned HTTP ${response.status}.`);
    const archive = join(staging, "channel-seeds.tar.gz");
    await writeFile(archive, Buffer.from(await response.arrayBuffer()));
    await stageChannelSeeds(archive, sha256, destination);
  } finally {
    await rm(staging, { recursive: true, force: true });
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) await main();
