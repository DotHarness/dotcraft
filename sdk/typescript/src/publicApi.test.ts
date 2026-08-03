import assert from "node:assert/strict";
import test from "node:test";

test("documented SDK entry points are importable and curated", async () => {
  const [root, contracts, wire, hub, appBinding, dynamicTools, testing, meta] = await Promise.all([
    import("@dotcraft/sdk"),
    import("@dotcraft/sdk/contracts"),
    import("@dotcraft/sdk/wire"),
    import("@dotcraft/sdk/hub"),
    import("@dotcraft/sdk/app-binding"),
    import("@dotcraft/sdk/dynamic-tools"),
    import("@dotcraft/sdk/testing"),
    import("@dotcraft/sdk/meta"),
  ]);

  assert.equal(typeof root.DotCraft, "function");
  assert.equal("InternalAppServerClient" in root, false);
  assert.equal("DotCraftWireClient" in root, false);
  assert.equal(typeof contracts.CONTRACT_VERSION, "string");
  assert.equal("SessionThread" in wire, false);
  assert.equal("mergeReplyTextFromDeltaAndSnapshot" in wire, false);
  assert.equal(typeof wire.DotCraftWireClient, "function");
  assert.ok(hub && appBinding && dynamicTools && testing);
  assert.equal(typeof meta.SDK_VERSION, "string");
});
test("removed SDK subpaths are not exported", async () => {
  for (const specifier of ["@dotcraft/sdk/appserver", "@dotcraft/sdk/channel"]) {
    await assert.rejects(import(specifier), (error: unknown) =>
      error instanceof Error && "code" in error && error.code === "ERR_PACKAGE_PATH_NOT_EXPORTED");
  }
});
