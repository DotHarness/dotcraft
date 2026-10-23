import assert from "node:assert/strict";
import test from "node:test";

import { installDesktopPluginRuntime, readDesktopPluginRuntime } from "../dist/runtime.js";

test("plugin modules read the Desktop-installed runtime and its PluginSurface", async () => {
  const PluginSurface = () => null;
  const runtime = {
    react: {},
    jsxRuntime: {},
    reactDom: { createPortal() {} },
    ui: { PluginSurface },
  };
  installDesktopPluginRuntime(runtime);
  assert.equal(readDesktopPluginRuntime(), runtime);

  const publicApi = await import(`../dist/index.js?plugin-surface=${Date.now()}`);
  assert.equal(publicApi.PluginSurface, PluginSurface);
});
