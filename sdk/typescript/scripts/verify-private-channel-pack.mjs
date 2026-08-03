import { cp, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join, resolve } from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const scriptsDir = dirname(fileURLToPath(import.meta.url));
const sdkRoot = resolve(scriptsDir, "..");
const channelRoot = join(sdkRoot, "packages", "channel");
const temporaryRoot = await mkdtemp(join(tmpdir(), "dotcraft-channel-pack-"));

try {
  const sdkPackage = JSON.parse(await readFile(join(sdkRoot, "package.json"), "utf8"));
  const channelPackage = JSON.parse(await readFile(join(channelRoot, "package.json"), "utf8"));
  channelPackage.version = sdkPackage.version;

  await writeFile(join(temporaryRoot, "package.json"), `${JSON.stringify(channelPackage, null, 2)}\n`, "utf8");
  await cp(join(channelRoot, "dist"), join(temporaryRoot, "dist"), { recursive: true });

  const npmCommand = process.platform === "win32" ? process.env.ComSpec ?? "cmd.exe" : "npm";
  const npmArguments = process.platform === "win32"
    ? ["/d", "/s", "/c", "npm pack --dry-run"]
    : ["pack", "--dry-run"];
  const result = spawnSync(npmCommand, npmArguments, {
    cwd: temporaryRoot,
    encoding: "utf8",
  });
  process.stdout.write(result.stdout ?? "");
  process.stderr.write(result.stderr ?? "");
  if (result.status !== 0) process.exitCode = result.status ?? 1;
} finally {
  await rm(temporaryRoot, { recursive: true, force: true, maxRetries: 5, retryDelay: 50 });
}
