import assert from "node:assert/strict";
import test from "node:test";

import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";

import { configDescriptors } from "./config-descriptors.js";
import { manifest } from "./manifest.js";
import { createModule } from "./module.js";

test("manifest matches qq module contract basics", () => {
  assert.equal(manifest.moduleId, "qq-standard");
  assert.equal(manifest.channelName, "qq");
  assert.equal(manifest.requiresInteractiveSetup, false);
  assert.equal(manifest.channelContractVersion, channelContractVersion);
});

test("createModule returns a full ModuleInstance shape", () => {
  const instance = createModule({
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: "qq",
    moduleId: "qq-standard",
  });

  assert.equal(typeof instance.start, "function");
  assert.equal(typeof instance.stop, "function");
  assert.equal(typeof instance.onStatusChange, "function");
  assert.equal(typeof instance.getStatus, "function");
  assert.equal(typeof instance.getError, "function");
});

test("config descriptors are non-empty", () => {
  assert.ok(configDescriptors.length > 0);
});
