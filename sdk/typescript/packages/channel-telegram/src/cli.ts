#!/usr/bin/env node

import { join, resolve } from "node:path";

import { type WorkspaceContext } from "@dotcraft/sdk/channel";

import { manifest } from "./manifest.js";
import { createModule } from "./module.js";

type ParsedArgs = {
  workspacePath?: string;
  configPath?: string;
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
  }
  return parsed;
}

function printLifecycleError(status: "configMissing" | "configInvalid", detail?: string): void {
  console.error(
    JSON.stringify({
      code: status,
      message:
        detail ?? (status === "configMissing" ? "Module config file not found." : "Module config is invalid."),
    }),
  );
}

function logInfo(message: string): void {
  if (process.env.DOTCRAFT_CHANNEL_TRANSPORT === "stdio") {
    console.error(message);
  } else {
    console.log(message);
  }
}

function waitForShutdownSignal(): Promise<NodeJS.Signals> {
  return new Promise((resolveSignal) => {
    const onSigint = () => resolveSignal("SIGINT");
    const onSigterm = () => resolveSignal("SIGTERM");
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
    logInfo(
      `[telegram] lifecycle=${status}` +
        (error?.code ? ` code=${error.code}` : "") +
        (error?.message ? ` message=${error.message}` : ""),
    );
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
    console.error(`[telegram] startup failed: ${err?.message ?? "unknown error"}`);
    process.exitCode = 1;
    return;
  }

  const signal = await waitForShutdownSignal();
  logInfo(`[telegram] shutdown signal: ${signal}`);
  await instance.stop();
}

export async function runFromCommandLine(): Promise<void> {
  const args = parseArgs(process.argv.slice(2));
  try {
    await runWorkspaceMode(args);
  } catch (error) {
    console.error("[telegram] startup failed:", error);
    process.exit(1);
  }
}

void runFromCommandLine();
