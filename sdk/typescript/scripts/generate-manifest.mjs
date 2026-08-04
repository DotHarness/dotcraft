import { writeFile } from "node:fs/promises";
import { join, resolve } from "node:path";
import { pathToFileURL } from "node:url";

const packageArg = process.argv[2];

if (!packageArg) {
  console.error("Usage: node scripts/generate-manifest.mjs <package-dir>");
  process.exit(1);
}

const packageDir = resolve(process.cwd(), packageArg);
const distIndexPath = join(packageDir, "dist", "index.js");
const distModuleUrl = pathToFileURL(distIndexPath).href;

const moduleExports = await import(distModuleUrl);
const { manifest, configDescriptors } = moduleExports;

if (!manifest || typeof manifest !== "object") {
  console.error(`Missing or invalid manifest export from ${distIndexPath}`);
  process.exit(1);
}

if (!Array.isArray(configDescriptors)) {
  console.error(`Missing or invalid configDescriptors export from ${distIndexPath}`);
  process.exit(1);
}

const outputPath = join(packageDir, "manifest.json");
const output = {
  ...manifest,
  configDescriptors,
};

const contents = `${JSON.stringify(output, null, 2)}\n`;
for (let attempt = 0; ; attempt += 1) {
  try {
    await writeFile(outputPath, contents, "utf-8");
    break;
  } catch (error) {
    if (attempt >= 9 || !["EBUSY", "EPERM", "UNKNOWN"].includes(error?.code)) {
      throw error;
    }
    await new Promise((resolveDelay) => setTimeout(resolveDelay, 50 * (attempt + 1)));
  }
}
console.log(`Wrote ${outputPath}`);
