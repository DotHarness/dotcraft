#!/usr/bin/env node

import { existsSync, mkdtempSync, readFileSync, readdirSync, renameSync, rmSync } from "node:fs";
import { dirname, isAbsolute, join, relative, resolve, sep } from "node:path";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const proxyEntries = new Map([
  ["@dotcraft/plugin", join(packageRoot, "dist", "index.js")],
  ["react", join(packageRoot, "dist", "react.js")],
  ["react/jsx-runtime", join(packageRoot, "dist", "jsx-runtime.js")],
  ["react-dom", join(packageRoot, "dist", "react-dom.js")],
]);

async function buildDesktopPlugin(projectRoot = process.cwd()) {
  const root = resolve(projectRoot);
  const entry = join(root, "src", "index.tsx");
  const outdir = join(root, "dist");
  if (!existsSync(entry)) {
    throw new Error("Desktop Plugin entry not found: src/index.tsx");
  }
  for (const proxy of proxyEntries.values()) {
    if (!existsSync(proxy)) {
      throw new Error("Build @dotcraft/plugin before building a Desktop Plugin.");
    }
  }

  const staging = mkdtempSync(join(root, ".desktop-plugin-dist-"));
  try {
    const result = await build({
      absWorkingDir: root,
      entryPoints: [entry],
      outdir: staging,
      entryNames: "index",
      chunkNames: "chunks/[name]-[hash]",
      assetNames: "assets/[name]-[hash]",
      outExtension: { ".js": ".mjs" },
      bundle: true,
      splitting: true,
      format: "esm",
      platform: "browser",
      target: "es2022",
      jsx: "automatic",
      minify: true,
      metafile: true,
      logLevel: "silent",
      loader: {
        ".gif": "file",
        ".jpg": "file",
        ".jpeg": "file",
        ".png": "file",
        ".svg": "file",
        ".webp": "file",
      },
      plugins: [runtimeProxyPlugin()],
    });

    const bundledReact = Object.keys(result.metafile.inputs).find((input) =>
      /(?:^|[/\\])node_modules[/\\]react(?:-dom)?[/\\]/.test(input),
    );
    if (bundledReact) {
      throw new Error("Desktop Plugin output contains a private React runtime.");
    }

    for (const output of walkFiles(staging)) {
      if (!output.endsWith(".mjs")) continue;
      const source = readFileSync(output, "utf8");
      if (/\b(?:from|import)\s*\(?\s*["']react(?:-dom)?(?:\/[^"']*)?["']/.test(source)) {
        throw new Error(`Desktop Plugin output contains a bare React import: ${relativeSourcePath(staging, output)}`);
      }
    }

    rmSync(outdir, { recursive: true, force: true });
    renameSync(staging, outdir);
  } finally {
    rmSync(staging, { recursive: true, force: true });
  }
}

function runtimeProxyPlugin() {
  return {
    name: "dotcraft-plugin-runtime",
    setup(buildContext) {
      for (const [specifier, proxy] of proxyEntries) {
        buildContext.onResolve({ filter: new RegExp(`^${escapeRegExp(specifier)}$`) }, () => ({ path: proxy }));
      }
      buildContext.onResolve({ filter: /^react(?:\/.*)?$|^react-dom(?:\/.*)?$/ }, (args) => ({
        errors: [{ text: `Unsupported React entry point '${args.path}' in a Desktop Plugin.` }],
      }));
    },
  };
}

function walkFiles(root) {
  const files = [];
  for (const entry of readdirSync(root, { withFileTypes: true })) {
    const path = join(root, entry.name);
    if (entry.isDirectory()) files.push(...walkFiles(path));
    else files.push(path);
  }
  return files;
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

function formatBuildFailure(error, projectRoot) {
  if (error && typeof error === "object" && Array.isArray(error.errors) && error.errors.length > 0) {
    return error.errors.map((diagnostic) => {
      const location = diagnostic.location;
      const prefix = location
        ? `${relativeSourcePath(projectRoot, location.file)}:${location.line}:${location.column + 1}: `
        : "";
      return `${prefix}${sanitizeMessage(diagnostic.text, projectRoot)}`;
    }).join("\n");
  }
  const message = error instanceof Error ? error.message : String(error);
  return sanitizeMessage(message, projectRoot);
}

function relativeSourcePath(projectRoot, file) {
  const absolute = isAbsolute(file) ? file : resolve(projectRoot, file);
  const projectRelative = relative(projectRoot, absolute);
  if (projectRelative && !projectRelative.startsWith(`..${sep}`) && projectRelative !== "..") {
    return projectRelative.replaceAll("\\", "/");
  }
  const packageRelative = relative(packageRoot, absolute);
  if (packageRelative && !packageRelative.startsWith(`..${sep}`) && packageRelative !== "..") {
    return `@dotcraft/plugin/${packageRelative.replaceAll("\\", "/")}`;
  }
  return "source";
}

function sanitizeMessage(message, projectRoot) {
  let result = message;
  for (const [path, replacement] of [[resolve(projectRoot), "."], [packageRoot, "@dotcraft/plugin"]]) {
    result = result.replaceAll(path, replacement).replaceAll(path.replaceAll("\\", "/"), replacement);
  }
  return result;
}

const args = process.argv.slice(2);
if (args[0] !== "build" || args.length > 2) {
  console.error("Usage: dotcraft-plugin build [project-root]");
  process.exitCode = 1;
} else {
  const projectRoot = resolve(args[1] ?? process.cwd());
  try {
    await buildDesktopPlugin(projectRoot);
  } catch (error) {
    console.error(formatBuildFailure(error, projectRoot));
    process.exitCode = 1;
  }
}
