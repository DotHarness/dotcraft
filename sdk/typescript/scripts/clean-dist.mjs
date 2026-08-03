import { rm } from "node:fs/promises";
import { basename, join, resolve } from "node:path";

const packageArg = process.argv[2] ?? ".";
const packageDir = resolve(process.cwd(), packageArg);
const distDir = resolve(join(packageDir, "dist"));

if (basename(distDir) !== "dist" || distDir === packageDir) {
  throw new Error(`Refusing to clean unexpected output path: ${distDir}`);
}

await rm(distDir, { recursive: true, force: true, maxRetries: 5, retryDelay: 50 });
await rm(join(packageDir, "tsconfig.tsbuildinfo"), { force: true, maxRetries: 5, retryDelay: 50 });
