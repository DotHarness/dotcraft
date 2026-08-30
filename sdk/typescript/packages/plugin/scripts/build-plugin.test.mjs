import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdtempSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { tmpdir } from "node:os";
import { fileURLToPath, pathToFileURL } from "node:url";

const packageRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const project = mkdtempSync(join(tmpdir(), "dotcraft-plugin-"));

try {
  mkdirSync(join(project, "src"));
  writeFileSync(join(project, "src", "index.tsx"), `
    import { useState } from "react";
    import { Button } from "@dotcraft/plugin";
    import type { DesktopPluginActivate } from "@dotcraft/plugin";
    import logo from "./logo.svg";
    import "./style.css";

    export const logoUrl: string = logo;

    function View() {
      const [count, setCount] = useState(0);
      return <Button onClick={() => setCount(count + 1)}>{count}</Button>;
    }

    export const activate: DesktopPluginActivate = () => ({
      mainViews: [{
        id: "sample",
        label: { default: "Sample" },
        component: View,
      }],
    });
  `);
  writeFileSync(
    join(project, "src", "style.css"),
    ".sample { color: var(--text-primary); background-image: url(\"./logo.svg\"); }\n",
  );
  writeFileSync(
    join(project, "src", "logo.svg"),
    "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 8 8\"><rect width=\"8\" height=\"8\" /></svg>\n",
  );

  const successfulBuild = spawnSync(
    process.execPath,
    [join(packageRoot, "scripts", "build-plugin.mjs"), "build", project],
    { encoding: "utf8" },
  );
  assert.equal(successfulBuild.status, 0, successfulBuild.stderr);

  const output = readFileSync(join(project, "dist", "index.mjs"), "utf8");
  assert.doesNotMatch(output, /["']react(?:-dom)?(?:\/[^"']*)?["']/);
  assert.doesNotMatch(output, /__CLIENT_INTERNALS_DO_NOT_USE_OR_WARN_USERS_THEY_CANNOT_UPGRADE/);
  assert.match(output, /dotcraft\.desktop-plugin\.runtime/);
  assert.match(output, /"\.\/assets\/logo-[A-Z0-9]+\.svg"/);
  assert.match(output, /new URL\((?:"\.\/assets\/logo-[A-Z0-9]+\.svg"|[$\w]+),\s*import\.meta\.url\)\.href/);

  const styles = readFileSync(join(project, "dist", "index.css"), "utf8");
  assert.match(styles, /--text-primary/);
  // A stylesheet already resolves url() against its own address, so it keeps the plain path.
  assert.match(styles, /url\("?\.\/assets\/logo-[A-Z0-9]+\.svg"?\)/);

  const runtimeModule = await import(pathToFileURL(join(packageRoot, "dist", "runtime.js")));
  runtimeModule.installDesktopPluginRuntime({
    react: { useState: (value) => [value, () => {}] },
    jsxRuntime: {
      Fragment: Symbol("Fragment"),
      jsx: (type, props) => ({ type, props }),
      jsxs: (type, props) => ({ type, props }),
    },
    reactDom: { createPortal: (children) => children },
    ui: {
      PluginSurface: ({ children }) => children ?? null,
      Button: () => null,
      IconButton: () => null,
      Input: () => null,
      Textarea: () => null,
      Select: () => null,
      Checkbox: () => null,
      Spinner: () => null,
      Skeleton: () => null,
    },
  });
  const plugin = await import(`${pathToFileURL(join(project, "dist", "index.mjs"))}?test=1`);
  const activation = plugin.activate({});
  assert.equal(activation.mainViews[0].id, "sample");

  // An asset import must already be the URL of the emitted file, resolved against the bundle.
  assert.match(plugin.logoUrl, /^file:\/\/.*\/assets\/logo-[A-Z0-9]+\.svg$/);
  assert.equal(existsSync(fileURLToPath(plugin.logoUrl)), true);

  writeFileSync(join(project, "src", "index.tsx"), "export const activate = (");
  const failedBuild = spawnSync(
    process.execPath,
    [join(packageRoot, "scripts", "build-plugin.mjs"), "build", project],
    { encoding: "utf8" },
  );
  assert.equal(failedBuild.status, 1);
  assert.match(failedBuild.stderr, /src\/index\.tsx:1:/);
  assert.doesNotMatch(failedBuild.stderr, new RegExp(escapeRegExp(project), "i"));
  assert.doesNotMatch(failedBuild.stderr, /\n\s*at\s/);
  assert.equal(readFileSync(join(project, "dist", "index.mjs"), "utf8"), output);
} finally {
  rmSync(project, { recursive: true, force: true });
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}
