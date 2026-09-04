import { spawn } from "node:child_process";
import { constants } from "node:fs";
import { access, readFile } from "node:fs/promises";
import { isAbsolute, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";

import type { FeishuConfig } from "./feishu-types.js";
import { FeishuUserIdentityError } from "./feishu-user-identity.js";
import { logInfo, logWarn } from "./logging.js";

const DEFAULT_TIMEOUT_MS = 15_000;
const DEFAULT_OUTPUT_LIMIT = 512 * 1024;
const MAX_ARGUMENTS = 256;
const MAX_ARGUMENT_LENGTH = 32 * 1024;
const LOCAL_COMMANDS = new Set(["skills", "schema"]);
const FORBIDDEN_COMMANDS = new Set([
  "api",
  "completion",
  "extension",
  "extensions",
  "mcp",
  "self-update",
  "update",
]);
const FORBIDDEN_FLAGS = [
  "--as",
  "--profile",
  "--yes",
];
const PATH_FLAGS = new Set([
  "--attachment",
  "--body-file",
  "--data",
  "--directory",
  "--dir",
  "--file",
  "--file-path",
  "--image",
  "--input",
  "--media",
  "--out",
  "--output",
  "--params",
  "--path",
]);

export type FeishuCliRisk = "read" | "write" | "high-risk-write";
export type FeishuCliIdentity = "bot" | "user";

type FeishuCliClassification = {
  risk: FeishuCliRisk;
  operation: string;
};

type ShortcutCatalog = {
  version: string;
  commands: Record<string, FeishuCliRisk>;
};

export type FeishuCliProcessRequest = {
  executable: string;
  args: string[];
  cwd: string;
  env: NodeJS.ProcessEnv;
  timeoutMs: number;
  outputLimit: number;
  signal?: AbortSignal;
};

export type FeishuCliProcessResult = {
  exitCode: number | null;
  stdout: string;
  stderr: string;
  timedOut?: boolean;
  cancelled?: boolean;
  outputExceeded?: boolean;
};

export type FeishuCliProcessExecutor = (
  request: FeishuCliProcessRequest,
) => Promise<FeishuCliProcessResult>;

export type FeishuCliRunResult = {
  risk: FeishuCliRisk;
  contentItems: Array<{ type: "text"; text: string }>;
  structuredResult?: unknown;
};

type FeishuCliErrorResult = {
  type: string;
  subtype?: string;
  message: string;
  hint?: string;
  identity?: string;
};

export class FeishuCliRunnerError extends Error {
  constructor(
    public readonly code: string,
    message: string,
    public readonly structuredResult?: FeishuCliErrorResult,
  ) {
    super(message);
    this.name = "FeishuCliRunnerError";
  }
}

export type FeishuCliRunOptions = {
  identity?: FeishuCliIdentity;
  signal?: AbortSignal;
};

export type FeishuCliRunnerOptions = {
  executable: string;
  shortcutCatalog: ShortcutCatalog;
  workspaceRoot: string;
  appId: string;
  brand: "feishu" | "lark";
  version: string;
  getTenantAccessToken: () => Promise<string>;
  getUserAccessToken: () => Promise<string>;
  timeoutMs?: number;
  outputLimit?: number;
  execute?: FeishuCliProcessExecutor;
};

export class FeishuCliRunner {
  private readonly execute: FeishuCliProcessExecutor;
  private readonly timeoutMs: number;
  private readonly outputLimit: number;

  constructor(private readonly options: FeishuCliRunnerOptions) {
    this.execute = options.execute ?? executeFeishuCliProcess;
    this.timeoutMs = options.timeoutMs ?? DEFAULT_TIMEOUT_MS;
    this.outputLimit = options.outputLimit ?? DEFAULT_OUTPUT_LIMIT;
    if (options.shortcutCatalog.version !== options.version) {
      throw new FeishuCliRunnerError(
        "FeishuCliArtifactMismatch",
        "The Feishu CLI command catalog does not match the pinned executable version.",
      );
    }
  }

  static async fromModule(
    workspaceRoot: string,
    config: FeishuConfig["feishu"],
    getTenantAccessToken: () => Promise<string>,
    getUserAccessToken: () => Promise<string>,
  ): Promise<FeishuCliRunner> {
    const moduleRoot = fileURLToPath(new URL("../", import.meta.url));
    const executable = resolve(
      moduleRoot,
      "vendor",
      process.platform === "win32" ? "lark-cli.exe" : "lark-cli",
    );
    const catalogPath = resolve(moduleRoot, "vendor", "lark-cli-shortcuts.json");
    let catalog: ShortcutCatalog;
    try {
      await access(executable, process.platform === "win32" ? constants.R_OK : constants.R_OK | constants.X_OK);
      catalog = JSON.parse(await readFile(catalogPath, "utf8")) as ShortcutCatalog;
    } catch {
      throw new FeishuCliRunnerError(
        "FeishuCliArtifactUnavailable",
        "The bundled Feishu CLI command catalog is unavailable.",
      );
    }
    if (!catalog || typeof catalog.version !== "string" || !catalog.commands) {
      throw new FeishuCliRunnerError(
        "FeishuCliArtifactMismatch",
        "The bundled Feishu CLI command catalog is invalid.",
      );
    }
    return new FeishuCliRunner({
      executable,
      shortcutCatalog: catalog,
      workspaceRoot,
      appId: config.appId,
      brand: config.brand ?? "feishu",
      version: "1.0.87",
      getTenantAccessToken,
      getUserAccessToken,
    });
  }

  async run(command: string, args: string[], options: FeishuCliRunOptions = {}): Promise<FeishuCliRunResult> {
    const identity = options.identity ?? "bot";
    const { signal } = options;
    const normalizedCommand = validateCommand(command);
    const normalizedArgs = validateArguments(args, this.options.workspaceRoot);
    const startedAt = Date.now();
    const classification = await this.classify(normalizedCommand, normalizedArgs, signal);
    const { risk } = classification;
    if (identity === "user" && risk !== "read") {
      throw new FeishuCliRunnerError(
        "FeishuCliUserWriteRejected",
        "User identity is limited to read-only commands. Use the default Bot identity to make changes.",
      );
    }
    const invocationArgs = [normalizedCommand, ...normalizedArgs];
    if (risk === "high-risk-write") invocationArgs.push("--yes");

    let accessToken: string | undefined;
    const isHelp = normalizedArgs.includes("--help");
    if (!LOCAL_COMMANDS.has(normalizedCommand) && !isHelp) {
      accessToken = identity === "user"
        ? await this.resolveUserAccessToken()
        : await this.resolveTenantAccessToken();
    }

    const result = await this.execute({
      executable: this.options.executable,
      args: invocationArgs,
      cwd: this.options.workspaceRoot,
      env: createChildEnvironment(this.options, identity, accessToken),
      timeoutMs: this.timeoutMs,
      outputLimit: this.outputLimit,
      signal,
    });
    logInfo("cli.invocation.completed", {
      command: classification.operation,
      identity,
      risk,
      durationMs: Date.now() - startedAt,
      exitCode: result.exitCode ?? -1,
      outcome: processOutcome(result),
    });
    throwForProcessFailure(result);
    return parseCliResult(result.stdout, result.stderr, risk, normalizedCommand, normalizedArgs);
  }

  private async resolveTenantAccessToken(): Promise<string> {
    try {
      return await this.options.getTenantAccessToken();
    } catch {
      throw new FeishuCliRunnerError(
        "FeishuCliAuthenticationFailed",
        "Unable to obtain an adapter-managed Feishu tenant access token.",
        {
          type: "authentication",
          subtype: "token_unavailable",
          message: "Unable to obtain an adapter-managed Feishu tenant access token.",
          identity: "bot",
        },
      );
    }
  }

  private async resolveUserAccessToken(): Promise<string> {
    try {
      return await this.options.getUserAccessToken();
    } catch (error) {
      const code = error instanceof FeishuUserIdentityError ? error.code : "authorization_failed";
      if (code === "not_configured") {
        throw new FeishuCliRunnerError(
          "FeishuCliUserIdentityUnavailable",
          "User identity is not enabled for this Channel. Use the default Bot identity.",
        );
      }
      const message = code === "authorization_failed"
        ? "The authorized Feishu account could not be used right now. Try again shortly."
        : "No authorized Feishu account is available. "
          + "Ask the operator to send /feishu-auth to this bot in a direct message.";
      throw new FeishuCliRunnerError("FeishuCliUserAuthorizationRequired", message, {
        type: "authentication",
        subtype: "user_authorization_required",
        message,
        identity: "user",
      });
    }
  }

  private async classify(
    command: string,
    args: string[],
    signal?: AbortSignal,
  ): Promise<FeishuCliClassification> {
    if (command === "skills") {
      if (args[0] !== "list" && args[0] !== "read") {
        throw new FeishuCliRunnerError(
          "FeishuCliCommandRejected",
          "Only skills list and skills read are allowed.",
        );
      }
      return { risk: "read", operation: `skills.${args[0]}` };
    }
    if (command === "schema") {
      if (!args[0] || args[0].startsWith("-")) {
        throw new FeishuCliRunnerError("FeishuCliInputInvalid", "schema requires a command id.");
      }
      return { risk: "read", operation: "schema" };
    }
    if (command === "whoami") return { risk: "read", operation: command };
    if (args.includes("--help")) return { risk: "read", operation: `${command}.help` };

    const shortcut = args[0]?.startsWith("+") ? `${command} ${args[0]}` : undefined;
    if (shortcut) {
      const risk = this.options.shortcutCatalog.commands[shortcut];
      if (!risk) {
        throw new FeishuCliRunnerError(
          "FeishuCliCommandRejected",
          "The requested Feishu CLI shortcut is not in the pinned command catalog. Read the relevant official Skill and reference before choosing a shortcut.",
        );
      }
      return { risk, operation: shortcut.replace(" ", ".") };
    }

    const schemaId = generatedCommandId(command, args);
    const schemaResult = await this.execute({
      executable: this.options.executable,
      args: ["schema", schemaId, "--format", "json"],
      cwd: this.options.workspaceRoot,
      env: createChildEnvironment(this.options, "bot"),
      timeoutMs: this.timeoutMs,
      outputLimit: this.outputLimit,
      signal,
    });
    throwForProcessFailure(schemaResult, "FeishuCliCommandRejected");
    try {
      const parsed = JSON.parse(schemaResult.stdout) as { _meta?: { risk?: unknown } };
      const risk = parsed._meta?.risk;
      if (risk === "read" || risk === "write" || risk === "high-risk-write") {
        return { risk, operation: schemaId };
      }
    } catch {
      // Classified below as a stable command rejection.
    }
    throw new FeishuCliRunnerError(
      "FeishuCliCommandRejected",
      "The requested Feishu CLI command could not be classified.",
    );
  }
}

function validateCommand(command: string): string {
  const normalized = command.trim();
  if (normalized === "auth" || normalized === "config") {
    throw new FeishuCliRunnerError(
      "FeishuCliCommandRejected",
      `The ${normalized} command is unavailable because this Channel uses adapter-managed external credentials.`,
    );
  }
  if (normalized === "profile") {
    throw new FeishuCliRunnerError(
      "FeishuCliCommandRejected",
      "CLI profile access is unavailable because this Channel does not use host-local Feishu CLI profiles.",
    );
  }
  if (!/^[a-z][a-z0-9-]*$/.test(normalized)
      || FORBIDDEN_COMMANDS.has(normalized)
      || (!LOCAL_COMMANDS.has(normalized) && normalized.length > 64)) {
    throw new FeishuCliRunnerError(
      "FeishuCliCommandRejected",
      "The requested Feishu CLI command is not allowed.",
    );
  }
  return normalized;
}

function validateArguments(args: string[], workspaceRoot: string): string[] {
  if (!Array.isArray(args) || args.length > MAX_ARGUMENTS || args.some((arg) => typeof arg !== "string")) {
    throw new FeishuCliRunnerError("FeishuCliInputInvalid", "args must be a bounded string array.");
  }
  const normalized = args.map((arg) => {
    if (arg.length > MAX_ARGUMENT_LENGTH || arg.includes("\0")) {
      throw new FeishuCliRunnerError("FeishuCliInputInvalid", "A Feishu CLI argument is invalid.");
    }
    const flag = arg.split("=", 1)[0]?.toLowerCase() ?? "";
    if (FORBIDDEN_FLAGS.includes(flag)) {
      throw new FeishuCliRunnerError("FeishuCliCommandRejected", forbiddenFlagMessage(flag));
    }
    return arg;
  });

  for (let index = 0; index < normalized.length; index += 1) {
    const arg = normalized[index]!;
    const [flag, inlineValue] = splitFlag(arg);
    if (arg.startsWith("@") && arg.length > 1) assertWorkspacePath(arg.slice(1), workspaceRoot);
    if (flag && PATH_FLAGS.has(flag)) {
      const value = inlineValue ?? normalized[index + 1];
      if (value && !value.startsWith("-")) assertWorkspacePath(value.replace(/^@/, ""), workspaceRoot);
    }
    if (isAbsolute(arg) || /^(?:\.\.?[\\/])/.test(arg)) assertWorkspacePath(arg, workspaceRoot);
  }
  return normalized;
}

function splitFlag(arg: string): [string | undefined, string | undefined] {
  if (!arg.startsWith("--")) return [undefined, undefined];
  const separator = arg.indexOf("=");
  return separator < 0
    ? [arg.toLowerCase(), undefined]
    : [arg.slice(0, separator).toLowerCase(), arg.slice(separator + 1)];
}

function assertWorkspacePath(value: string, workspaceRoot: string): void {
  if (/^[a-z][a-z0-9+.-]*:\/\//i.test(value)) return;
  const root = resolve(workspaceRoot);
  const candidate = resolve(root, value);
  const rel = relative(root, candidate);
  if (rel === ".." || rel.startsWith(`..${process.platform === "win32" ? "\\" : "/"}`) || isAbsolute(rel)) {
    throw new FeishuCliRunnerError(
      "FeishuCliPathRejected",
      "Feishu CLI file arguments must stay inside the workspace.",
    );
  }
}

function generatedCommandId(command: string, args: string[]): string {
  const path = [command];
  for (const arg of args) {
    if (arg.startsWith("-")) break;
    path.push(arg);
  }
  if (path.length < 2) {
    throw new FeishuCliRunnerError(
      "FeishuCliCommandRejected",
      "The requested Feishu CLI command has no classifiable operation.",
    );
  }
  return path.join(".");
}

function forbiddenFlagMessage(flag: string): string {
  switch (flag) {
    case "--yes":
      return "Caller-supplied confirmation is unavailable; DotCraft controls confirmation after risk classification.";
    case "--as":
      return "Identity is selected with the identity input, not with --as.";
    default:
      return "Host-local Feishu CLI profile selection is unavailable.";
  }
}

/** The two identities are mutually exclusive: one token and one locked strict mode per run. */
function createChildEnvironment(
  options: FeishuCliRunnerOptions,
  identity: FeishuCliIdentity,
  accessToken?: string,
): NodeJS.ProcessEnv {
  const env = { ...process.env };
  for (const key of Object.keys(env)) {
    if (/^LARKSUITE_CLI_/i.test(key)) delete env[key];
  }
  env.LARKSUITE_CLI_APP_ID = options.appId;
  if (accessToken) {
    if (identity === "user") env.LARKSUITE_CLI_USER_ACCESS_TOKEN = accessToken;
    else env.LARKSUITE_CLI_TENANT_ACCESS_TOKEN = accessToken;
  }
  env.LARKSUITE_CLI_BRAND = options.brand;
  env.LARKSUITE_CLI_DEFAULT_AS = identity;
  env.LARKSUITE_CLI_STRICT_MODE = identity;
  env.LARKSUITE_CLI_NO_UPDATE_NOTIFIER = "1";
  return env;
}

function throwForProcessFailure(result: FeishuCliProcessResult, code = "FeishuCliExecutionFailed"): void {
  if (result.cancelled) throw new FeishuCliRunnerError("FeishuCliCancelled", "Feishu CLI execution was cancelled.");
  if (result.timedOut) throw new FeishuCliRunnerError("FeishuCliTimeout", "Feishu CLI execution timed out.");
  if (result.outputExceeded) {
    throw new FeishuCliRunnerError("FeishuCliOutputLimitExceeded", "Feishu CLI output exceeded the allowed limit.");
  }
  if (result.exitCode !== 0) {
    const officialError = parseOfficialError(result.stdout, result.stderr);
    if (officialError) {
      throw new FeishuCliRunnerError(
        code === "FeishuCliExecutionFailed"
          ? errorCodeForOfficialType(officialError.type)
          : code,
        officialError.message,
        officialError,
      );
    }
    throw new FeishuCliRunnerError(code, "Feishu CLI execution failed.");
  }
}

function parseCliResult(
  stdout: string,
  stderr: string,
  risk: FeishuCliRisk,
  command: string,
  args: string[],
): FeishuCliRunResult {
  const text = stdout.trim();
  if (args.includes("--help")) {
    const helpText = text || stderr.trim();
    return {
      risk,
      contentItems: [{ type: "text", text: helpText || "Feishu CLI help completed without output." }],
    };
  }
  if (command === "skills" && args[0] === "read") {
    return { risk, contentItems: [{ type: "text", text }] };
  }
  try {
    const structuredResult = JSON.parse(text) as unknown;
    return {
      risk,
      structuredResult,
      contentItems: [{ type: "text", text: text || "Feishu CLI completed successfully." }],
    };
  } catch {
    throw new FeishuCliRunnerError(
      "FeishuCliInvalidOutput",
      "Feishu CLI returned an invalid result envelope. Use --format json for business commands.",
    );
  }
}

function parseOfficialError(stdout: string, stderr: string): FeishuCliErrorResult | undefined {
  for (const candidate of [stdout, stderr]) {
    const text = candidate.trim();
    if (!text) continue;
    try {
      const envelope = JSON.parse(text) as Record<string, unknown>;
      const rawError = envelope.error;
      if (!rawError || typeof rawError !== "object") continue;
      const error = rawError as Record<string, unknown>;
      if (typeof error.type !== "string" || typeof error.message !== "string") continue;
      return {
        type: error.type,
        ...(typeof error.subtype === "string" ? { subtype: error.subtype } : {}),
        message: error.message,
        ...(typeof error.hint === "string" ? { hint: error.hint } : {}),
        ...(typeof envelope.identity === "string" ? { identity: envelope.identity } : {}),
      };
    } catch {
      // Try the other output stream before falling back to the generic process error.
    }
  }
  return undefined;
}

function errorCodeForOfficialType(type: string): string {
  switch (type) {
    case "authentication": return "FeishuCliAuthenticationFailed";
    case "authorization":
    case "permission": return "FeishuCliAuthorizationFailed";
    case "validation": return "FeishuCliValidationFailed";
    case "configuration":
    case "config": return "FeishuCliConfigurationFailed";
    case "confirmation": return "FeishuCliConfirmationRequired";
    case "rate_limit": return "FeishuCliRateLimited";
    case "network": return "FeishuCliNetworkFailed";
    case "api": return "FeishuCliApiFailed";
    default: return "FeishuCliExecutionFailed";
  }
}

function processOutcome(result: FeishuCliProcessResult): string {
  if (result.cancelled) return "cancelled";
  if (result.timedOut) return "timeout";
  if (result.outputExceeded) return "outputExceeded";
  return result.exitCode === 0 ? "success" : "failed";
}

export async function executeFeishuCliProcess(
  request: FeishuCliProcessRequest,
): Promise<FeishuCliProcessResult> {
  return await new Promise((resolveResult) => {
    const child = spawn(request.executable, request.args, {
      cwd: request.cwd,
      env: request.env,
      shell: false,
      windowsHide: true,
      stdio: ["ignore", "pipe", "pipe"],
    });
    const stdout: Buffer[] = [];
    const stderr: Buffer[] = [];
    let outputBytes = 0;
    let timedOut = false;
    let cancelled = false;
    let outputExceeded = false;
    let settled = false;
    const terminate = () => {
      if (!child.killed) child.kill();
    };
    const onData = (target: Buffer[]) => (chunk: Buffer) => {
      outputBytes += chunk.byteLength;
      if (outputBytes > request.outputLimit) {
        outputExceeded = true;
        terminate();
        return;
      }
      target.push(chunk);
    };
    child.stdout.on("data", onData(stdout));
    child.stderr.on("data", onData(stderr));
    const timer = setTimeout(() => {
      timedOut = true;
      terminate();
    }, request.timeoutMs);
    const onAbort = () => {
      cancelled = true;
      terminate();
    };
    request.signal?.addEventListener("abort", onAbort, { once: true });
    if (request.signal?.aborted) onAbort();
    const finish = (exitCode: number | null) => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      request.signal?.removeEventListener("abort", onAbort);
      resolveResult({
        exitCode,
        stdout: Buffer.concat(stdout).toString("utf8"),
        stderr: Buffer.concat(stderr).toString("utf8"),
        timedOut,
        cancelled,
        outputExceeded,
      });
    };
    child.once("error", () => finish(null));
    child.once("close", finish);
  });
}

export function logFeishuCliFailure(error: unknown): void {
  const code = error instanceof FeishuCliRunnerError ? error.code : "FeishuCliExecutionFailed";
  logWarn("cli.invocation.failed", { code });
}
