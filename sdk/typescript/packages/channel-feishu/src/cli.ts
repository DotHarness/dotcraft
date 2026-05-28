#!/usr/bin/env node

import { existsSync, readFileSync } from "node:fs";
import { mkdir } from "node:fs/promises";
import { join, resolve } from "node:path";

import { type WorkspaceContext } from "@dotcraft/sdk/channel";

import { createFeishuEventHandlers } from "./event-handler.js";
import { FeishuAdapter, validateFeishuConfig } from "./feishu-adapter.js";
import { FeishuClient } from "./feishu-client.js";
import type { FeishuConfig } from "./feishu-types.js";
import { errorMessage, logError, logInfo, logWarn } from "./logging.js";
import { manifest } from "./manifest.js";
import { createModule } from "./module.js";

type ParsedArgs = {
  workspacePath?: string;
  configPath?: string;
  legacyConfigPath?: string;
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

function loadLegacyConfig(configPath: string): FeishuConfig {
  if (!existsSync(configPath)) {
    throw new Error(`Config not found: ${configPath}`);
  }
  const config = JSON.parse(readFileSync(configPath, "utf-8")) as unknown;
  validateFeishuConfig(config);
  return config;
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

async function runWorkspaceMode(args: ParsedArgs): Promise<void> {
  if (!args.workspacePath) {
    throw new Error("Missing value for --workspace.");
  }

  const workspaceRoot = resolve(args.workspacePath);
  const context: WorkspaceContext = {
    workspaceRoot,
    craftPath: join(workspaceRoot, ".craft"),
    channelName: manifest.channelName,
    moduleId: manifest.moduleId,
    configOverridePath: args.configPath ? resolve(args.configPath) : undefined,
  };

  const instance = createModule(context);
  instance.onStatusChange((status, error) => {
    logInfo("module.lifecycle", {
      status,
      errorCode: error?.code ?? "",
      errorMessage: error?.message ?? "",
    });
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
    console.error(`[feishu] startup failed: ${err?.message ?? "unknown error"}`);
    process.exitCode = 1;
    return;
  }

  const signal = await waitForShutdownSignal();
  logInfo("shutdown.signal_received", { signal });
  await instance.stop();
  logInfo("shutdown.cleanup_done");
}

async function runLegacyMode(args: ParsedArgs): Promise<void> {
  const configPath =
    args.legacyConfigPath || args.configPath || process.env.DOTCRAFT_FEISHU_CONFIG || join(process.cwd(), "adapter_config.json");
  console.error("[DEPRECATED] Legacy mode is deprecated. Use --workspace <path> [--config <path>] instead.");

  const config = loadLegacyConfig(configPath);
  logInfo("startup.config_loaded", {
    brand: config.feishu.brand ?? "feishu",
    groupMentionRequired: config.feishu.groupMentionRequired !== false,
    ackReactionEmoji: (config.feishu.ackReactionEmoji ?? "GLANCE").trim() || "GLANCE",
    hasDownloadDir: Boolean(config.feishu.downloadDir),
    hasDotcraftToken: Boolean(config.dotcraft.token),
    debugAdapterStream: config.feishu.debug?.adapterStream ?? false,
    debugTextMerge: config.feishu.debug?.textMerge ?? false,
  });
  if (config.feishu.downloadDir) {
    await mkdir(resolve(config.feishu.downloadDir), { recursive: true });
    logInfo("startup.download_dir_ready", {
      downloadDir: resolve(config.feishu.downloadDir),
    });
  }

  const feishuClient = new FeishuClient(config.feishu);
  const botInfo = await feishuClient.probeBot();
  logInfo("startup.bot_probe", {
    hasBotIdentity: botInfo.hasBotIdentity,
    appName: botInfo.appName || "(unnamed)",
    hasDiagnostic: Boolean(botInfo.diagnosticMessage),
    diagnosticTag: botInfo.diagnosticTag ?? "",
  });
  if (!botInfo.hasBotIdentity) {
    logWarn("startup.bot_probe_fallback", {
      reason: botInfo.diagnosticTag ?? "missing_bot_identity",
    });
    if (botInfo.diagnosticMessage) {
      logWarn("startup.bot_probe_diagnostic", {
        message: botInfo.diagnosticMessage,
      });
    }
    logWarn("startup.bot_probe_fallback_behavior", {
      dm: "allow",
      group:
        config.feishu.groupMentionRequired !== false
          ? "skip_when_mention_required"
          : "allow_when_mention_required_disabled",
    });
  } else {
    logInfo("startup.bot_ready", {
      appName: botInfo.appName || "(unnamed app)",
      openId: botInfo.openId,
      botName: botInfo.botName || "unknown",
    });
  }

  const adapter = new FeishuAdapter({
    wsUrl: config.dotcraft.wsUrl,
    dotcraftToken: config.dotcraft.token,
    approvalTimeoutMs: config.feishu.approvalTimeoutMs ?? 120000,
    feishu: feishuClient,
    debug: config.feishu.debug,
  });
  await adapter.start();
  logInfo("dotcraft.ws.connected");

  const handlers = createFeishuEventHandlers({
    adapter,
    client: feishuClient,
    bot: botInfo,
    config: config.feishu,
  });

  const abortController = new AbortController();
  process.on("SIGINT", () => {
    logInfo("shutdown.signal_received", { signal: "SIGINT" });
    abortController.abort();
  });
  process.on("SIGTERM", () => {
    logInfo("shutdown.signal_received", { signal: "SIGTERM" });
    abortController.abort();
  });

  try {
    await feishuClient.startEventStream(handlers, abortController.signal);
  } finally {
    feishuClient.stopEventStream();
    await adapter.stop();
    logInfo("shutdown.cleanup_done");
  }
}

export async function runFromCommandLine(): Promise<void> {
  const args = parseArgs(process.argv.slice(2));
  const useWorkspaceMode = Boolean(args.workspacePath);
  try {
    if (useWorkspaceMode) {
      await runWorkspaceMode(args);
    } else {
      await runLegacyMode(args);
    }
  } catch (error) {
    logError("startup.fatal", { message: errorMessage(error) });
    console.error(error);
    process.exit(1);
  }
}

void runFromCommandLine();
