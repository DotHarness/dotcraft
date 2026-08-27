import assert from "node:assert/strict";
import test from "node:test";

import { installDesktopPluginRuntime, readDesktopPluginRuntime } from "../dist/runtime.js";

test("the Desktop installs the runtime consumed by plugin modules", () => {
  const runtime = {
    react: {},
    jsxRuntime: {},
    reactDom: { createPortal() {} },
    ui: {},
  };

  installDesktopPluginRuntime(runtime);

  assert.equal(readDesktopPluginRuntime(), runtime);
});
