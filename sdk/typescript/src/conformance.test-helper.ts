import assert from "node:assert/strict";
import test from "node:test";

import type { ConfigDescriptor } from "./config.js";
import type { ModuleFactory, ModuleManifest, ModuleVariant, WorkspaceContext } from "./module.js";

const CurrentSdkContractVersion = "1.0.0";

export type ConformanceSuiteOptions = {
  expectedModuleId: string;
  expectedChannelName: string;
  expectedConfigFileName: string;
  expectedRequiresInteractiveSetup: boolean;
  expectedVariant: ModuleVariant;
  workspaceContextFixture: WorkspaceContext;
  validConfigFixture: unknown;
};

type ModuleExports = {
  manifest: ModuleManifest;
  createModule: ModuleFactory;
  configDescriptors?: ConfigDescriptor[];
};

function assertLocalizedStringMap(value: unknown, name: string): void {
  if (value === undefined) return;
  assert.equal(typeof value, "object", `${name} must be an object when present`);
  assert.notEqual(value, null, `${name} must not be null`);
  assert.equal(Array.isArray(value), false, `${name} must not be an array`);
  for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
    assert.ok(key === "en" || key === "zh-Hans", `${name} has unsupported locale '${key}'`);
    assert.equal(typeof item, "string", `${name}.${key} must be a string`);
  }
}

function assertOptionalString(value: unknown, name: string): void {
  if (value === undefined) return;
  assert.equal(typeof value, "string", `${name} must be a string when present`);
}

export function runModuleConformanceSuite(
  packageName: string,
  importModule: () => Promise<ModuleExports>,
  options: ConformanceSuiteOptions,
): void {
  test(`${packageName} manifest conformance`, async () => {
    const mod = await importModule();

    assert.ok(mod.manifest, "manifest must be exported");
    assert.equal(mod.manifest.moduleId, options.expectedModuleId);
    assert.equal(mod.manifest.channelName, options.expectedChannelName);
    assert.equal(mod.manifest.configFileName, options.expectedConfigFileName);
    assert.equal(mod.manifest.requiresInteractiveSetup, options.expectedRequiresInteractiveSetup);
    assert.equal(mod.manifest.variant, options.expectedVariant);
    assert.equal(mod.manifest.sdkContractVersion, CurrentSdkContractVersion);
    assert.ok(
      Array.isArray(mod.manifest.supportedProtocolVersions) && mod.manifest.supportedProtocolVersions.length > 0,
      "supportedProtocolVersions must be non-empty",
    );
    assert.equal(mod.manifest.launcher.supportsWorkspaceFlag, true);
    assert.equal(typeof mod.manifest.capabilitySummary, "object");
    assert.notEqual(mod.manifest.capabilitySummary, null);
    if (mod.manifest.interface !== undefined) {
      assert.equal(typeof mod.manifest.interface, "object");
      assert.notEqual(mod.manifest.interface, null);
      assert.equal(Array.isArray(mod.manifest.interface), false);
      assertOptionalString(mod.manifest.interface.shortDescription, "manifest.interface.shortDescription");
      assertOptionalString(mod.manifest.interface.longDescription, "manifest.interface.longDescription");
      assertOptionalString(mod.manifest.interface.previewPrompt, "manifest.interface.previewPrompt");
      assertLocalizedStringMap(
        mod.manifest.interface.localizedShortDescription,
        "manifest.interface.localizedShortDescription",
      );
      assertLocalizedStringMap(
        mod.manifest.interface.localizedLongDescription,
        "manifest.interface.localizedLongDescription",
      );
      assertLocalizedStringMap(
        mod.manifest.interface.localizedPreviewPrompt,
        "manifest.interface.localizedPreviewPrompt",
      );
    }
  });

  test(`${packageName} module entry conformance`, async () => {
    const mod = await importModule();

    assert.equal(typeof mod.createModule, "function");

    const instance = mod.createModule(options.workspaceContextFixture);
    assert.equal(typeof instance.start, "function");
    assert.equal(typeof instance.stop, "function");
    assert.equal(typeof instance.onStatusChange, "function");
    assert.equal(typeof instance.getStatus, "function");
    assert.equal(typeof instance.getError, "function");
    assert.equal(instance.getStatus(), "stopped");
  });

  test(`${packageName} config discovery conformance`, async () => {
    const mod = await importModule();
    const instance = mod.createModule(options.workspaceContextFixture);

    const statuses: string[] = [];
    instance.onStatusChange((status) => {
      statuses.push(status);
    });

    await instance.start();
    assert.equal(instance.getStatus(), "configMissing");
    assert.ok(statuses.includes("configMissing"), "status handlers should observe configMissing");
  });

  test(`${packageName} config descriptors conformance`, async () => {
    const mod = await importModule();
    if (!("configDescriptors" in mod) || mod.configDescriptors === undefined) {
      return;
    }

    assert.ok(Array.isArray(mod.configDescriptors), "configDescriptors must be an array");
    for (const descriptor of mod.configDescriptors) {
      assert.equal(typeof descriptor.key, "string");
      assert.ok(descriptor.key.length > 0, "descriptor.key must be non-empty");
      assert.equal(typeof descriptor.displayLabel, "string");
      assert.ok(descriptor.displayLabel.length > 0, "descriptor.displayLabel must be non-empty");
      assert.equal(typeof descriptor.dataKind, "string");
      assert.equal(typeof descriptor.required, "boolean");
      assert.equal(typeof descriptor.masked, "boolean");
      if (descriptor.dataKind === "secret") {
        assert.equal(descriptor.masked, true, `secret field '${descriptor.key}' must be masked`);
      }
    }
  });

  test(`${packageName} validConfigFixture is present`, () => {
    assert.notEqual(options.validConfigFixture, undefined);
    assert.notEqual(options.validConfigFixture, null);
  });
}
