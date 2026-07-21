import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";

const renderer = fileURLToPath(new URL("./render-config.mjs", import.meta.url));

async function createFixture(t) {
  const root = await mkdtemp(path.join(tmpdir(), "dotcraft-render-config-"));
  const home = path.join(root, "home");
  const workspace = path.join(root, "workspace");
  await mkdir(path.join(home, ".craft"), { recursive: true });
  await mkdir(path.join(workspace, ".craft"), { recursive: true });
  t.after(() => rm(root, { recursive: true, force: true }));
  return { home, workspace };
}

function runRenderer(fixture, overrides = {}) {
  const environment = Object.fromEntries(
    Object.entries(process.env).filter(([key]) =>
      !key.startsWith("DOTCRAFT_") &&
      !key.endsWith("_TOKEN") &&
      !key.endsWith("_SECRET") &&
      !key.endsWith("_API_KEY"),
    ),
  );
  const result = spawnSync(process.execPath, [renderer], {
    encoding: "utf8",
    env: {
      ...environment,
      HOME: fixture.home,
      DOTCRAFT_WORKSPACE: fixture.workspace,
      ENABLED_CHANNELS: "",
      ...overrides,
    },
  });
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
}

async function readConfig(filePath) {
  return JSON.parse(await readFile(filePath, "utf8"));
}

test("writes the selected model to global and workspace provider maps", async (t) => {
  const fixture = await createFixture(t);

  runRenderer(fixture, {
    DOTCRAFT_PROVIDER: "openai",
    DOTCRAFT_MODEL: "gpt-test",
  });

  const globalConfig = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  const workspaceConfig = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  assert.equal(globalConfig.ProviderModels.openai, "gpt-test");
  assert.equal(workspaceConfig.ProviderModels.openai, "gpt-test");
  assert.equal("Model" in globalConfig, false);
  assert.equal("Model" in workspaceConfig, false);
});

test("updates the selected provider without removing other models or legacy fields", async (t) => {
  const fixture = await createFixture(t);
  const existing = {
    ProviderId: "openai",
    ProviderModels: { anthropic: "claude-test", OpenAI: "gpt-old" },
    Model: "legacy-model",
  };
  await writeFile(
    path.join(fixture.home, ".craft", "config.json"),
    `${JSON.stringify(existing)}\n`,
    "utf8",
  );
  await writeFile(
    path.join(fixture.workspace, ".craft", "config.json"),
    `${JSON.stringify(existing)}\n`,
    "utf8",
  );

  runRenderer(fixture, { DOTCRAFT_MODEL: "gpt-new" });

  const globalConfig = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  const workspaceConfig = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  for (const config of [globalConfig, workspaceConfig]) {
    assert.deepEqual(config.ProviderModels, {
      anthropic: "claude-test",
      OpenAI: "gpt-new",
    });
    assert.equal(config.Model, "legacy-model");
  }
});

test("does not create a provider model entry when the model is empty", async (t) => {
  const fixture = await createFixture(t);

  runRenderer(fixture, { DOTCRAFT_PROVIDER: "openai", DOTCRAFT_MODEL: "" });

  const globalConfig = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  const workspaceConfig = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  assert.equal("ProviderModels" in globalConfig, false);
  assert.equal("ProviderModels" in workspaceConfig, false);
});
