import assert from "node:assert/strict";
import test from "node:test";

import { sdkContractVersion } from "@dotcraft/sdk";

import { configDescriptors } from "./config-descriptors.js";
import { manifest } from "./manifest.js";
import { createModule } from "./module.js";

test("manifest matches weixin module contract basics", () => {
  assert.equal(manifest.moduleId, "weixin-standard");
  assert.equal(manifest.channelName, "weixin");
  assert.equal(manifest.requiresInteractiveSetup, true);
  assert.equal(manifest.sdkContractVersion, sdkContractVersion);
});

test("createModule returns a full ModuleInstance shape", () => {
  const instance = createModule({
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: "weixin",
    moduleId: "weixin-standard",
  });

  assert.equal(typeof instance.start, "function");
  assert.equal(typeof instance.stop, "function");
  assert.equal(typeof instance.onStatusChange, "function");
  assert.equal(typeof instance.getStatus, "function");
  assert.equal(typeof instance.getError, "function");
});

test("module instance starts with stopped lifecycle status", () => {
  const instance = createModule({
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: "weixin",
    moduleId: "weixin-standard",
  });
  assert.equal(instance.getStatus(), "stopped");
});

test("config descriptors are non-empty", () => {
  assert.ok(configDescriptors.length > 0);
});
