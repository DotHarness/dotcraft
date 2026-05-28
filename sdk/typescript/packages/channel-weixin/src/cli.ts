#!/usr/bin/env node

import { existsSync, readFileSync } from "node:fs";
import { join, resolve } from "node:path";

import { type WorkspaceContext } from "@dotcraft/sdk/channel";

import { waitForQrLogin, DEFAULT_BOT_TYPE } from "./auth.js";
import { runMonitorLoop } from "./monitor.js";
import { WeixinState } from "./state.js";
import type { WeixinConfig } from "./weixin-config.js";
import { manifest } from "./manifest.js";
import { createModule } from "./module.js";
import { WeixinAdapter, validateWeixinConfig } from "./weixin-adapter.js";

type ParsedArgs = {
  workspacePath?: string;
  configPath?: string;
  legacyConfigPath?: string;
};

type LegacyWeixinConfig = WeixinConfig & {
  weixin: WeixinConfig["weixin"] & {
    dataDir?: string;
  };
};

function parseArgs(argv: string[]): ParsedArgs {
  const parsed: ParsedArgs = {};
  for (let i = 0; i < argv.length; i += 1) {
    const token = argv[i];
    if (token === "--workspace") {
      parsed.workspacePath = argv[i + 1];
      i += 1;
      continue;
    }
    if (token === "--config") {
      parsed.configPath = argv[i + 1];
      i += 1;
      continue;
    }
    if (!token?.startsWith("-") && !parsed.legacyConfigPath) {
      parsed.legacyConfigPath = token;
    }
  }
  return parsed;
}

function loadLegacyConfig(configPath: string): LegacyWeixinConfig {
  if (!existsSync(configPath)) {
    throw new Error(`Config not found: ${configPath}`);
  }
  const raw = JSON.parse(readFileSync(configPath, "utf-8")) as unknown;
  validateWeixinConfig(raw);
  return raw as LegacyWeixinConfig;
}

function printLifecycleError(status: "configMissing" | "configInvalid", detail?: string): void {
  console.error(
    JSON.stringify({
      code: status,
      message: detail ?? (status === "configMissing" ? "Module config file not found." : "Module config is invalid."),
    }),
  );
}

function waitForShutdownSignal(): Promise<NodeJS.Signals> {
  return new Promise((resolve) => {
    const onSigint = () => resolve("SIGINT");
    const onSigterm = () => resolve("SIGTERM");
    process.once("SIGINT", onSigint);
    process.once("SIGTERM", onSigterm);
  });
}

function logInfo(message: string): void {
  if (process.env.DOTCRAFT_CHANNEL_TRANSPORT === "stdio") {
    console.error(message);
  } else {
    console.log(message);
  }
}

async function renderQrInTerminal(url: string): Promise<void> {
  logInfo(`\nScan this QR with WeChat:\n${url}\n`);
  if (process.env.DOTCRAFT_CHANNEL_TRANSPORT === "stdio") {
    return;
  }
  const qrcodeTerminal = await import("qrcode-terminal");
  try {
    qrcodeTerminal.default.generate(url, { small: true });
  } catch {
    // Ignore renderer errors.
  }
}

async function runWorkspaceMode(args: ParsedArgs): Promise<void> {
  const workspaceRoot = resolve(args.workspacePath ?? process.cwd());
  const context: WorkspaceContext = {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: manifest.channelName,
    moduleId: manifest.moduleId,
    configOverridePath: args.configPath ? resolve(args.configPath) : undefined,
  };

  const instance = createModule(context);
  let lastQrUrl = "";
  instance.onStatusChange((status, error) => {
    logInfo(
      `[weixin] lifecycle=${status}` +
        (error?.code ? ` code=${error.code}` : "") +
        (error?.message ? ` message=${error.message}` : ""),
    );
    if (status === "authRequired") {
      const qrUrl = String((error?.detail?.qrUrl as string | undefined) ?? "");
      if (qrUrl && qrUrl !== lastQrUrl) {
        lastQrUrl = qrUrl;
        void renderQrInTerminal(qrUrl);
      }
    }
  });

  await instance.start();
  const status = instance.getStatus();
  if (status === "configMissing" || status === "configInvalid") {
    printLifecycleError(status, instance.getError()?.message);
    process.exitCode = 1;
    return;
  }
  if (status === "stopped") {
    const err = instance.getError();
    console.error(`[weixin] startup failed: ${err?.message ?? "unknown error"}`);
    process.exitCode = 1;
    return;
  }

  const signal = await waitForShutdownSignal();
  logInfo(`[weixin] shutdown signal: ${signal}`);
  await instance.stop();
}

async function runLegacyMode(args: ParsedArgs): Promise<void> {
  const configPath =
    args.legacyConfigPath || process.env.DOTCRAFT_WEIXIN_CONFIG || join(process.cwd(), "adapter_config.json");
  console.error("[DEPRECATED] Legacy mode is deprecated. Use --workspace <path> [--config <path>] instead.");

  const config = loadLegacyConfig(configPath);
  const rawDataDir = config.weixin.dataDir ?? "./data";
  const dataDir = rawDataDir.startsWith(".") ? resolve(rawDataDir) : rawDataDir;
  const state = new WeixinState(dataDir);

  let creds = state.loadCredentials();
  if (!creds?.botToken || !creds.ilinkBotId) {
    logInfo("Starting Weixin QR login...");
    creds = await waitForQrLogin({
      apiBaseUrl: config.weixin.apiBaseUrl,
      botType: config.weixin.botType ?? DEFAULT_BOT_TYPE,
      onQrUrl: (url) => {
        void renderQrInTerminal(url);
      },
      deadlineMs: 480_000,
    });
    state.saveCredentials(creds);
  }

  const adapter = new WeixinAdapter({
    wsUrl: config.dotcraft.wsUrl,
    dotcraftToken: config.dotcraft.token,
    apiBaseUrl: config.weixin.apiBaseUrl,
    approvalTimeoutMs: config.weixin.approvalTimeoutMs ?? 120_000,
    state,
    credentials: creds,
  });

  await adapter.start();
  logInfo("Connected to DotCraft; starting Weixin monitor...");

  const ac = new AbortController();
  process.on("SIGINT", () => ac.abort());
  process.on("SIGTERM", () => ac.abort());

  await runMonitorLoop({
    baseUrl: creds.baseUrl || config.weixin.apiBaseUrl,
    token: creds.botToken,
    getInitialBuf: () => state.loadSyncBuf(),
    saveBuf: (buf) => state.saveSyncBuf(buf),
    longPollMs: config.weixin.pollTimeoutMs,
    abortSignal: ac.signal,
    callbacks: {
      onMessage: async (msg) => {
        await adapter.handleInboundUserMessage(msg);
      },
      onSessionExpired: async () => {
        logInfo("Session expired — run again to scan QR.");
      },
    },
  });

  await adapter.stop();
}

export async function runFromCommandLine(): Promise<void> {
  const args = parseArgs(process.argv.slice(2));
  const useWorkspaceMode = Boolean(args.workspacePath || args.configPath);
  try {
    if (useWorkspaceMode) {
      await runWorkspaceMode(args);
    } else {
      await runLegacyMode(args);
    }
  } catch (error) {
    console.error("[weixin] startup failed:", error);
    process.exit(1);
  }
}

void runFromCommandLine();
