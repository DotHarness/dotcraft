import { mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

import { bundleChannelEntry } from "./bundle-channel.mjs";

const sdkRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const modulesRootArgIndex = process.argv.indexOf("--modules-root");
const modulesRoot = modulesRootArgIndex >= 0
  ? resolve(process.argv[modulesRootArgIndex + 1] ?? "")
  : join(sdkRoot, "packages");
if (modulesRootArgIndex >= 0 && !process.argv[modulesRootArgIndex + 1]) {
  throw new Error("--modules-root requires a directory path.");
}
const channelPackages = [
  "channel-feishu",
  "channel-weixin",
  "channel-telegram",
  "channel-qq",
  "channel-wecom",
];

function runNode(script) {
  return spawnSync(process.execPath, [script], { encoding: "utf8" });
}

function outputOf(result) {
  return `${result.stderr ?? ""}\n${result.stdout ?? ""}`;
}

for (const packageName of channelPackages) {
  const bundle = join(modulesRoot, packageName, "dist", "cli.bundle.js");
  const result = runNode(bundle);
  const output = outputOf(result);
  if (result.status !== 1 || !output.includes("Missing value for --workspace.")) {
    throw new Error(
      `${packageName} bundle did not reach CLI argument validation (exit=${result.status}).\n${output}`,
    );
  }
}

const fixtureRoot = await mkdtemp(join(tmpdir(), "dotcraft-channel-bundle-"));
try {
  const entry = join(fixtureRoot, "globals.mjs");
  const bundle = join(fixtureRoot, "globals.bundle.mjs");
  await writeFile(
    entry,
    [
      'const fs = require("node:fs");',
      'if (!fs.existsSync(__filename)) throw new Error("missing __filename");',
      'if (typeof __dirname !== "string") throw new Error("missing __dirname");',
      'console.log("channel-bundle-globals-ok");',
    ].join("\n"),
    "utf8",
  );
  await bundleChannelEntry(entry, bundle);
  const result = runNode(bundle);
  if (result.status !== 0 || result.stdout.trim() !== "channel-bundle-globals-ok") {
    throw new Error(`Channel bundle CommonJS-global fixture failed.\n${outputOf(result)}`);
  }
} finally {
  await rm(fixtureRoot, { recursive: true, force: true, maxRetries: 5, retryDelay: 50 });
}

console.log(`Verified ${channelPackages.length} channel bundles and the CommonJS-global shim.`);
