import assert from "node:assert/strict";
import test from "node:test";

import type { ModuleManifest, WorkspaceContext } from "./module.js";

function selectModuleById(
  manifests: ModuleManifest[],
  moduleId: string,
): ModuleManifest | undefined {
  return manifests.find((manifest) => manifest.moduleId === moduleId);
}

test("variant substitution keeps channel identity while switching moduleId", async () => {
  const dynamicImport = new Function(
    "modulePath",
    "return import(modulePath);",
  ) as (modulePath: string) => Promise<unknown>;
  const { manifest: feishuStandardManifest } = (await dynamicImport(
    "@dotcraft/channel-feishu",
  )) as { manifest: ModuleManifest };

  const feishuEnterpriseManifest: ModuleManifest = {
    ...feishuStandardManifest,
    moduleId: "feishu-enterprise",
    variant: "enterprise",
    displayName: "Feishu Enterprise",
  };

  const manifests = [feishuStandardManifest, feishuEnterpriseManifest];
  const selectedStandard = selectModuleById(manifests, "feishu-standard");
  const selectedEnterprise = selectModuleById(manifests, "feishu-enterprise");

  assert.ok(selectedStandard, "standard variant should be selectable by moduleId");
  assert.ok(selectedEnterprise, "enterprise variant should be selectable by moduleId");
  assert.equal(selectedStandard.channelName, "feishu");
  assert.equal(selectedEnterprise.channelName, "feishu");
  assert.equal(selectedStandard.configFileName, selectedEnterprise.configFileName);

  const standardContext: WorkspaceContext = {
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: selectedStandard.channelName,
    moduleId: selectedStandard.moduleId,
  };
  const enterpriseContext: WorkspaceContext = {
    workspaceRoot: "/workspace/demo",
    craftPath: "/workspace/demo/.craft",
    channelName: selectedEnterprise.channelName,
    moduleId: selectedEnterprise.moduleId,
  };

  assert.equal(
    standardContext.channelName,
    enterpriseContext.channelName,
    "host context channelName must stay stable when swapping module variants",
  );
  assert.notEqual(standardContext.moduleId, enterpriseContext.moduleId);
});
