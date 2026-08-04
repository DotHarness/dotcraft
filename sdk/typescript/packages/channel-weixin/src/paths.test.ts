import assert from "node:assert/strict";
import { join } from "node:path";
import test from "node:test";

import {
  resolveModuleStatePath,
  resolveModuleTempPath,
} from "@dotcraft/channel";

test("resolveModuleStatePath returns craftPath/state/weixin-standard", () => {
  const context = {
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: "weixin",
    moduleId: "weixin-standard",
  };
  assert.equal(resolveModuleStatePath(context), join("/workspace/demo/.craft", "state", "weixin-standard"));
});

test("resolveModuleTempPath returns craftPath/tmp/weixin-standard", () => {
  const context = {
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: "weixin",
    moduleId: "weixin-standard",
  };
  assert.equal(resolveModuleTempPath(context), join("/workspace/demo/.craft", "tmp", "weixin-standard"));
});
