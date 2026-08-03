import assert from "node:assert/strict";
import test from "node:test";

import { CHANNEL_CONTRACT_VERSION as channelContractVersion } from "@dotcraft/channel/meta";

import { configDescriptors } from "./config-descriptors.js";
import { manifest } from "./manifest.js";
import { createModule } from "./module.js";

test("manifest matches module contract basics", () => {
  assert.equal(manifest.moduleId, "feishu-standard");
  assert.equal(manifest.channelName, "feishu");
  assert.equal(manifest.channelContractVersion, channelContractVersion);
});

test("createModule returns a full ModuleInstance shape", () => {
  const instance = createModule({
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: "feishu",
    moduleId: "feishu-standard",
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
    channelName: "feishu",
    moduleId: "feishu-standard",
  });

  assert.equal(instance.getStatus(), "stopped");
});

test("config descriptors are non-empty and have required fields", () => {
  assert.ok(configDescriptors.length > 0);
  for (const descriptor of configDescriptors) {
    assert.ok(descriptor.key.length > 0);
    assert.ok(descriptor.displayLabel.length > 0);
    assert.ok(descriptor.dataKind.length > 0);
    assert.equal(typeof descriptor.required, "boolean");
    assert.equal(typeof descriptor.masked, "boolean");
  }
});

test("config descriptors expose the Feishu docx tool toggle", () => {
  const descriptor = configDescriptors.find((item) => item.key === "feishu.tools.docs.enabled");
  assert.ok(descriptor);
  assert.equal(descriptor?.dataKind, "boolean");
  assert.equal(descriptor?.required, false);
  assert.equal(descriptor?.advanced ?? false, false);
});

test("config descriptors expose the card title setting", () => {
  const descriptor = configDescriptors.find((item) => item.key === "feishu.cardTitle");
  assert.ok(descriptor);
  assert.equal(descriptor?.dataKind, "string");
  assert.equal(descriptor?.required, false);
  assert.equal(descriptor?.advanced ?? false, true);
  assert.equal(descriptor?.defaultValue, "DotCraft");
});
