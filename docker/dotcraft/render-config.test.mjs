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
      DOTCRAFT_AUTH_METHOD: "apiKey",
      ENABLED_CHANNELS: "",
      ...overrides,
    },
  });
  assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
  return result;
}

async function readConfig(filePath) {
  return JSON.parse(await readFile(filePath, "utf8"));
}

test("writes a complete preference to global and workspace config", async (t) => {
  const fixture = await createFixture(t);

  runRenderer(fixture, {
    DOTCRAFT_PROVIDER: "openai",
    DOTCRAFT_MODEL: "gpt-test",
    DOTCRAFT_REASONING_EFFORT: "high",
    DOTCRAFT_REASONING_OUTPUT: "summary",
    DOTCRAFT_SPEED: "fast",
    DOTCRAFT_CONTEXT_WINDOW: "max",
  });

  const globalConfig = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  const workspaceConfig = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  const expected = {
    model: "gpt-test",
    reasoning: { enabled: true, effort: "high", output: "summary" },
    speed: "fast",
    contextWindow: { mode: "max" },
  };
  assert.deepEqual(globalConfig.ProviderPreferences.openai, expected);
  assert.deepEqual(workspaceConfig.ProviderPreferences.openai, expected);
});

test("updates provider case-insensitively and preserves unrelated preferences", async (t) => {
  const fixture = await createFixture(t);
  const existing = {
    ProviderId: "openai",
    ProviderPreferences: {
      anthropic: {
        model: "claude-test",
        reasoning: { enabled: true, effort: "high", output: "full" },
        speed: "standard",
        contextWindow: { mode: "default" },
      },
      OpenAI: {
        model: "gpt-old",
        reasoning: { enabled: false, effort: "medium", output: "full" },
        speed: "fast",
        contextWindow: { mode: "max" },
      },
    },
    Unrelated: { keep: true },
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
    assert.equal(config.ProviderPreferences.anthropic.model, "claude-test");
    assert.deepEqual(config.ProviderPreferences.OpenAI, {
      model: "gpt-new",
      reasoning: { enabled: false, effort: "medium", output: "full" },
      speed: "fast",
      contextWindow: { mode: "max" },
    });
    assert.deepEqual(config.Unrelated, { keep: true });
  }
});

test("does not create a preference when model and prior record are empty", async (t) => {
  const fixture = await createFixture(t);

  runRenderer(fixture, { DOTCRAFT_PROVIDER: "openai", DOTCRAFT_MODEL: "" });

  const globalConfig = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  const workspaceConfig = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  assert.deepEqual(globalConfig.ProviderPreferences, {});
  assert.deepEqual(workspaceConfig.ProviderPreferences, {});
});

test("preserves fields whose environment variables are absent", async (t) => {
  const fixture = await createFixture(t);
  const existing = {
    ProviderId: "openai",
    ProviderPreferences: {
      OPENAI: {
        model: "gpt-old",
        reasoning: { enabled: true, effort: "low", output: "none" },
        speed: "fast",
        contextWindow: { mode: "max" },
      },
    },
  };
  await writeFile(
    path.join(fixture.workspace, ".craft", "config.json"),
    `${JSON.stringify(existing)}\n`,
    "utf8",
  );

  runRenderer(fixture, {
    DOTCRAFT_PROVIDER: "openai",
    DOTCRAFT_MODEL: "gpt-new",
  });

  const config = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  assert.deepEqual(config.ProviderPreferences.OPENAI, {
    model: "gpt-new",
    reasoning: { enabled: true, effort: "low", output: "none" },
    speed: "fast",
    contextWindow: { mode: "max" },
  });
});

test("creates capability-safe fallback values without a catalog", async (t) => {
  const fixture = await createFixture(t);
  runRenderer(fixture, {
    DOTCRAFT_PROVIDER: "manual",
    DOTCRAFT_MODEL: "custom-model",
  });
  const config = await readConfig(path.join(fixture.workspace, ".craft", "config.json"));
  assert.deepEqual(config.ProviderPreferences.manual, {
    model: "custom-model",
    reasoning: { enabled: false, effort: "medium", output: "full" },
    speed: "standard",
    contextWindow: { mode: "default" },
  });
});

test("subscription mode removes API credentials and keeps the model preference", async (t) => {
  const fixture = await createFixture(t);
  await writeFile(
    path.join(fixture.home, ".craft", "config.json"),
    JSON.stringify({ Providers: { openai: { ApiKey: "old", EndPoint: "https://old.example" } } }),
  );
  runRenderer(fixture, {
    DOTCRAFT_PROVIDER: "openai",
    DOTCRAFT_AUTH_METHOD: "chatgptOAuth",
    DOTCRAFT_MODEL: "gpt-test",
    DOTCRAFT_API_KEY: "unused",
    DOTCRAFT_PROVIDER_ENDPOINT: "https://unused.example",
  });
  const config = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  assert.equal(config.Providers.openai.AuthMethod, "chatgptOAuth");
  assert.equal(config.Providers.openai.Protocol, "openai-responses");
  assert.equal(config.Providers.openai.ApiKey, undefined);
  assert.equal(config.Providers.openai.EndPoint, undefined);
  assert.equal(config.ProviderPreferences.openai.model, "gpt-test");
});

test("requires an explicit authentication method", async (t) => {
  const fixture = await createFixture(t);
  const result = spawnSync(process.execPath, [renderer], {
    encoding: "utf8",
    env: {
      HOME: fixture.home,
      DOTCRAFT_WORKSPACE: fixture.workspace,
      DOTCRAFT_PROVIDER: "openai",
      DOTCRAFT_MODEL: "gpt-test",
    },
  });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr, /DOTCRAFT_AUTH_METHOD must be apiKey or chatgptOAuth/);
});

for (const [name, value, allowed] of [
  ["DOTCRAFT_REASONING_EFFORT", "ultra", "off, low, medium, high, extraHigh"],
  ["DOTCRAFT_REASONING_OUTPUT", "verbose", "none, summary, full"],
  ["DOTCRAFT_SPEED", "turbo", "standard, fast"],
  ["DOTCRAFT_CONTEXT_WINDOW", "huge", "default, max"],
]) {
  test(`fails fast for invalid ${name}`, async (t) => {
    const fixture = await createFixture(t);
    const environment = Object.fromEntries(
      Object.entries(process.env).filter(([key]) => !key.startsWith("DOTCRAFT_")),
    );
    const result = spawnSync(process.execPath, [renderer], {
      encoding: "utf8",
      env: {
        ...environment,
        HOME: fixture.home,
        DOTCRAFT_WORKSPACE: fixture.workspace,
        DOTCRAFT_PROVIDER: "openai",
        DOTCRAFT_AUTH_METHOD: "apiKey",
        DOTCRAFT_MODEL: "gpt-test",
        ENABLED_CHANNELS: "",
        [name]: value,
      },
    });
    assert.notEqual(result.status, 0);
    assert.match(result.stderr, new RegExp(name));
    assert.match(result.stderr, new RegExp(allowed.replaceAll(", ", ".*")));
  });
}


test("remote mode renders client access and keeps model selection", async (t) => {
  const fixture = await createFixture(t);
  runRenderer(fixture, {
    DOTCRAFT_MODEL_MODE: "remote",
    DOTCRAFT_MODEL_SERVICE_URL: "http://models:8090/model-service",
    DOTCRAFT_MODEL_SERVICE_TOKEN: "client-test",
    DOTCRAFT_PROVIDER: "shared",
    DOTCRAFT_MODEL: "model-test",
  });
  const config = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  assert.deepEqual(config.ModelService, {
    Endpoint: "http://models:8090/model-service",
    Token: "$DOTCRAFT_MODEL_SERVICE_TOKEN",
  });
  assert.equal(config.ProviderId, "shared");
  assert.equal(config.ProviderPreferences.shared.model, "model-test");
  assert.equal(config.Providers, undefined);
  runRenderer(fixture, { DOTCRAFT_MODEL_MODE: "direct", DOTCRAFT_PROVIDER: "local", DOTCRAFT_API_KEY: "upstream-test" });
  const direct = await readConfig(path.join(fixture.home, ".craft", "config.json"));
  assert.equal(direct.ModelService, undefined);
  assert.equal(direct.Providers.local.ApiKey, "$DOTCRAFT_API_KEY");
});
