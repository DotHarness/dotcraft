import { build } from "esbuild";
import { fileURLToPath } from "node:url";
import { resolve } from "node:path";

export const channelBundleBanner = [
  "import{createRequire as __dotcraftCreateRequire}from'node:module';",
  "import{fileURLToPath as __dotcraftFileURLToPath}from'node:url';",
  "import{dirname as __dotcraftPathDirname}from'node:path';",
  "const require=__dotcraftCreateRequire(import.meta.url);",
  "const __filename=__dotcraftFileURLToPath(import.meta.url);",
  "const __dirname=__dotcraftPathDirname(__filename);",
].join("");

export async function bundleChannelEntry(entryPoint, outfile) {
  await build({
    entryPoints: [resolve(entryPoint)],
    outfile: resolve(outfile),
    bundle: true,
    platform: "node",
    format: "esm",
    external: ["node:*"],
    banner: { js: channelBundleBanner },
  });
}

export async function bundleChannelPackage(packageRoot) {
  const root = resolve(packageRoot);
  await bundleChannelEntry(resolve(root, "dist", "cli.js"), resolve(root, "dist", "cli.bundle.js"));
}

const invokedPath = process.argv[1] ? resolve(process.argv[1]) : null;
if (invokedPath === fileURLToPath(import.meta.url)) {
  const packageRoot = process.argv[2];
  if (!packageRoot) {
    console.error("Usage: node scripts/bundle-channel.mjs <channel-package-root>");
    process.exitCode = 1;
  } else {
    await bundleChannelPackage(packageRoot);
  }
}
