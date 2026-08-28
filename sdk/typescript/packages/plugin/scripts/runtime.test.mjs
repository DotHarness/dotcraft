import assert from "node:assert/strict";
import test from "node:test";

import { installDesktopPluginRuntime, readDesktopPluginRuntime } from "../dist/runtime.js";

test("the Desktop installs the runtime consumed by plugin modules", () => {
  const PluginSurface = () => null;
  const runtime = {
    react: {},
    jsxRuntime: {},
    reactDom: { createPortal() {} },
    ui: { PluginSurface },
  };

  installDesktopPluginRuntime(runtime);

  assert.equal(readDesktopPluginRuntime(), runtime);
});

test("the public PluginSurface export uses the Desktop runtime component", async () => {
  const PluginSurface = () => null;
  installDesktopPluginRuntime({
    react: {},
    jsxRuntime: {},
    reactDom: { createPortal() {} },
    ui: { PluginSurface },
  });

  const publicApi = await import(`../dist/index.js?plugin-surface=${Date.now()}`);

  assert.equal(publicApi.PluginSurface, PluginSurface);
});
