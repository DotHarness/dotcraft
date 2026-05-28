import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";

import { resolveModuleStatePath } from "@dotcraft/sdk/channel";

import { WeixinAdapter } from "./weixin-adapter.js";
import type { WeixinCredentials } from "./state.js";

function createWorkspaceContext(rootName: string): {
  workspaceRoot: string;
  craftPath: string;
  channelName: string;
  moduleId: string;
} {
  const workspaceRoot = mkdtempSync(join(tmpdir(), `${rootName}-`));
  const craftPath = join(workspaceRoot, ".craft");
  mkdirSync(craftPath, { recursive: true });
  return {
    workspaceRoot,
    craftPath,
    channelName: "weixin",
    moduleId: "weixin-standard",
  };
}

function writeConfig(craftPath: string): void {
  const configPath = join(craftPath, "weixin.json");
  writeFileSync(
    configPath,
    JSON.stringify(
      {
        dotcraft: { wsUrl: "ws://127.0.0.1:9100/ws" },
        weixin: { apiBaseUrl: "https://ilinkai.weixin.qq.com" },
      },
      null,
      2,
    ),
    "utf-8",
  );
}

test("no saved credentials transitions to authRequired", async () => {
  const context = createWorkspaceContext("weixin-auth-none");
  writeConfig(context.craftPath);

  const adapter = new WeixinAdapter();
  let beginAuthFlowCalled = false;
  (adapter as unknown as { beginAuthFlow: (reason: "startup" | "expired") => void }).beginAuthFlow = () => {
    beginAuthFlowCalled = true;
  };

  await adapter.startWithContext(context);

  assert.equal(adapter.getStatus(), "authRequired");
  assert.equal(beginAuthFlowCalled, true);
});

test("valid saved credentials avoid authRequired on startup", async () => {
  const context = createWorkspaceContext("weixin-auth-existing");
  writeConfig(context.craftPath);

  const stateDir = resolveModuleStatePath(context);
  mkdirSync(stateDir, { recursive: true });
  const credentials: WeixinCredentials = {
    botToken: "bot-token",
    ilinkBotId: "bot-id",
    baseUrl: "https://ilinkai.weixin.qq.com",
    savedAt: new Date().toISOString(),
  };
  writeFileSync(join(stateDir, "credentials.json"), JSON.stringify(credentials, null, 2), "utf-8");

  const adapter = new WeixinAdapter();
  (adapter as unknown as { ensureDotCraftReady: () => Promise<void> }).ensureDotCraftReady = async () => {
    (adapter as unknown as { setStatus: (status: "ready") => void }).setStatus("ready");
  };
  (adapter as unknown as { startMonitor: () => void }).startMonitor = () => {};

  await adapter.startWithContext(context);

  assert.notEqual(adapter.getStatus(), "authRequired");
});

test("credentials are persisted under module-scoped state directory", () => {
  const context = createWorkspaceContext("weixin-auth-state-path");
  const stateDir = resolveModuleStatePath(context);
  mkdirSync(stateDir, { recursive: true });

  const credentialsPath = join(stateDir, "credentials.json");
  writeFileSync(
    credentialsPath,
    JSON.stringify(
      {
        botToken: "bot-token",
        ilinkBotId: "bot-id",
        baseUrl: "https://ilinkai.weixin.qq.com",
        savedAt: new Date().toISOString(),
      } satisfies WeixinCredentials,
      null,
      2,
    ),
    "utf-8",
  );

  assert.equal(credentialsPath.endsWith(join("state", "weixin-standard", "credentials.json")), true);
});
