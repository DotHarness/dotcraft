import { spawn } from "node:child_process";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join, resolve as resolvePath } from "node:path";
import { homedir } from "node:os";
import { isIP } from "node:net";

import { DotCraftError } from "./errors.js";

export interface HubClientOptions {
  executable?: string;
  expectedExecutable?: string;
  binaryMatchPolicy?: HubBinaryMatchPolicy;
  hubStartupTimeoutMs?: number;
  hubShutdownTimeoutMs?: number;
  homeDir?: string;
}

export type HubBinaryMatchPolicy = "ignore" | "restartIfMismatch" | "errorIfMismatch";

export interface HubLockInfo {
  pid: number;
  apiBaseUrl: string;
  token: string;
  startedAt?: string;
  version?: string;
  binaryPath?: string | null;
}

export interface HubClientInfo {
  name?: string;
  version?: string;
}

export interface HubServiceStatus {
  state: string;
  url?: string | null;
  reason?: string | null;
}

export interface HubAppServerResponse {
  workspacePath: string;
  canonicalWorkspacePath: string;
  state: string;
  pid?: number | null;
  endpoints: Record<string, string>;
  serviceStatus: Record<string, HubServiceStatus>;
  serverVersion?: string | null;
  startedByHub: boolean;
  exitCode?: number | null;
  lastError?: string | null;
  recentStderr?: string | null;
}

export interface HubStatusResponse {
  hubVersion: string;
  pid: number;
  startedAt: string;
  statePath: string;
  apiBaseUrl: string;
  binaryPath?: string | null;
  capabilities: HubCapabilities;
}

export interface HubCapabilities {
  appServerManagement: boolean;
  portManagement: boolean;
  events: boolean;
  notifications: boolean;
  tray: boolean;
  [key: string]: unknown;
}

export interface HubEvent {
  kind: string;
  at: string;
  workspacePath?: string | null;
  data?: unknown;
}

export interface HubRuntimeToolsRequest {
  ripgrepPath?: string;
  nodeBin?: string;
  nodeRunAsNode?: boolean;
  modulesDir?: string;
  builtInPluginRoots?: string;
  defaultPluginRegistryUrl?: string;
}

export interface HubEnsureAppServerOptions {
  client?: HubClientInfo;
  clientName?: string;
  clientVersion?: string;
  startIfMissing?: boolean;
  runtimeTools?: HubRuntimeToolsRequest;
}

export class HubClientError extends DotCraftError {
  readonly details?: unknown;

  constructor(code: string, message: string, details?: unknown, cause?: unknown) {
    super(code, message, { cause });
    this.name = "HubClientError";
    this.details = details;
  }
}

const DEFAULT_STARTUP_TIMEOUT_MS = 15_000;
const DEFAULT_SHUTDOWN_TIMEOUT_MS = 5_000;
const POLL_MS = 200;

export function hubLockPath(homeDir = homedir()): string {
  return join(homeDir, ".craft", "hub", "hub.lock");
}

export function defaultChatWorkspacePath(homeDir = homedir()): string {
  return join(homeDir, ".craft", "workspaces", "chats");
}

export function ensureDefaultChatWorkspace(homeDir = homedir()): string {
  const workspacePath = defaultChatWorkspacePath(homeDir);
  const craftPath = join(workspacePath, ".craft");
  mkdirSync(join(craftPath, "memory"), { recursive: true });
  mkdirSync(join(craftPath, "skills"), { recursive: true });
  mkdirSync(join(craftPath, "security"), { recursive: true });

  const configPath = join(craftPath, "config.json");
  if (!existsSync(configPath)) writeFileSync(configPath, "{}\n", "utf8");
  return workspacePath;
}

export function readHubLockFromPath(path: string): HubLockInfo | null {
  if (!existsSync(path)) return null;
  try {
    const parsed = JSON.parse(readFileSync(path, "utf8")) as Partial<HubLockInfo>;
    if (
      typeof parsed.pid === "number" &&
      typeof parsed.apiBaseUrl === "string" &&
      typeof parsed.token === "string"
    ) {
      return {
        pid: parsed.pid,
        apiBaseUrl: parsed.apiBaseUrl,
        token: parsed.token,
        startedAt: parsed.startedAt ?? "",
        version: parsed.version ?? "",
        binaryPath: typeof parsed.binaryPath === "string" ? parsed.binaryPath : null,
      };
    }
  } catch {
    // Ignore stale or partially written lock files.
  }
  return null;
}

export function isProcessAlive(pid: number): boolean {
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return true;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === "EPERM";
  }
}

export function isLoopbackHost(host: string): boolean {
  const normalized = host.trim().replace(/^\[/, "").replace(/\]$/, "");
  if (normalized.toLowerCase() === "localhost") return true;
  const ipVersion = isIP(normalized);
  if (ipVersion === 4) return normalized.startsWith("127.");
  if (ipVersion === 6) return normalized === "::1";
  return false;
}

export function parseHubBaseUrl(rawUrl: string): URL {
  let url: URL;
  try {
    url = new URL(rawUrl.trim());
  } catch (error) {
    throw new HubClientError("invalidHubLock", `Invalid Hub URL: ${String(error)}`, undefined, error);
  }

  if (url.protocol !== "http:") {
    throw new HubClientError("invalidHubLock", "Hub URL must use http://.");
  }
  if (!url.port) {
    throw new HubClientError("invalidHubLock", "Hub URL is missing a port.");
  }
  if (!isLoopbackHost(url.hostname)) {
    throw new HubClientError("invalidHubLock", "Hub URL must be loopback.");
  }
  if (url.pathname !== "/" || url.search || url.hash) {
    throw new HubClientError("invalidHubLock", "Hub URL must not include path, query, or fragment.");
  }

  url.pathname = "";
  return url;
}

export function findSseBoundary(buffer: string): { index: number; sequence: "\n\n" | "\r\n\r\n" } | null {
  const lf = buffer.indexOf("\n\n");
  const crlf = buffer.indexOf("\r\n\r\n");
  if (lf === -1 && crlf === -1) return null;
  if (lf === -1) return { index: crlf, sequence: "\r\n\r\n" };
  if (crlf === -1) return { index: lf, sequence: "\n\n" };
  return crlf < lf ? { index: crlf, sequence: "\r\n\r\n" } : { index: lf, sequence: "\n\n" };
}

async function sleep(ms: number): Promise<void> {
  await new Promise((resolve) => setTimeout(resolve, ms));
}

export class HubClient {
  constructor(private readonly options: HubClientOptions = {}) {}

  async ensureAppServer(
    workspacePath: string,
    options: HubEnsureAppServerOptions = {},
  ): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub();
    const client = options.client ?? {
      name: options.clientName ?? "dotcraft-sdk",
      version: options.clientVersion ?? "0.1.0",
    };
    return await this.requestJson<HubAppServerResponse>(hub, "/v1/appservers/ensure", {
      method: "POST",
      body: JSON.stringify({
        workspacePath,
        client,
        startIfMissing: options.startIfMissing ?? true,
        runtimeTools: options.runtimeTools,
      }),
    });
  }

  async getAppServerByWorkspace(workspacePath: string): Promise<HubAppServerResponse | null> {
    const hub = await this.tryGetLiveHub();
    if (!hub) return null;
    return await this.requestJson<HubAppServerResponse | null>(
      hub,
      `/v1/appservers/by-workspace?path=${encodeURIComponent(workspacePath)}`,
      { method: "GET" },
      true,
    );
  }

  async restartAppServer(
    workspacePath: string,
    runtimeTools?: HubRuntimeToolsRequest,
  ): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub();
    return await this.requestJson<HubAppServerResponse>(hub, "/v1/appservers/restart", {
      method: "POST",
      body: JSON.stringify({ workspacePath, runtimeTools }),
    });
  }

  async stopAppServer(workspacePath: string): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub();
    return await this.requestJson<HubAppServerResponse>(hub, "/v1/appservers/stop", {
      method: "POST",
      body: JSON.stringify({ workspacePath }),
    });
  }

  async listAppServers(): Promise<HubAppServerResponse[]> {
    const hub = await this.ensureHub();
    return await this.requestJson<HubAppServerResponse[]>(hub, "/v1/appservers", { method: "GET" });
  }

  async ensureDefaultChatAppServer(
    options: HubEnsureAppServerOptions = {},
  ): Promise<HubAppServerResponse> {
    const workspacePath = ensureDefaultChatWorkspace(this.options.homeDir);
    return await this.ensureAppServer(workspacePath, options);
  }

  async getStatus(): Promise<HubStatusResponse> {
    const hub = await this.ensureHub();
    const response = await fetch(`${hub.apiBaseUrl}/v1/status`);
    if (!response.ok) throw await this.toError(response);
    return (await response.json()) as HubStatusResponse;
  }

  async shutdownHub(): Promise<void> {
    const hub = await this.tryGetLiveHub();
    if (!hub) return;
    await this.requestJson<{ ok: boolean }>(hub, "/v1/shutdown", { method: "POST" });
  }

  async subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void> {
    const hub = await this.ensureHub();
    const response = await fetch(`${hub.apiBaseUrl}/v1/events`, {
      headers: { Authorization: `Bearer ${hub.token}` },
      signal,
    });
    if (!response.ok || !response.body) throw await this.toError(response);

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    while (!signal.aborted) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      let boundary = findSseBoundary(buffer);
      while (boundary) {
        const raw = buffer.slice(0, boundary.index);
        buffer = buffer.slice(boundary.index + boundary.sequence.length);
        const dataLine = raw.split(/\r?\n/).find((line) => line.startsWith("data:"));
        const data = dataLine?.slice("data:".length).trim();
        if (data) {
          try {
            onEvent(JSON.parse(data) as HubEvent);
          } catch {
            // Ignore malformed event frames by default.
          }
        }
        boundary = findSseBoundary(buffer);
      }
    }
  }

  async ensureHub(): Promise<HubLockInfo> {
    const live = await this.tryGetLiveHub();
    if (live) {
      const mismatch = this.getBinaryMismatch(live);
      if (!mismatch) return live;
      if (this.binaryMatchPolicy === "errorIfMismatch") {
        throw new HubClientError("hubBinaryMismatch", "Hub is running from a different executable.", mismatch);
      }
      if (this.binaryMatchPolicy === "restartIfMismatch") {
        await this.shutdownMismatchedHub(live, mismatch);
      } else {
        return live;
      }
    }

    this.startHub();

    const timeoutMs = this.options.hubStartupTimeoutMs ?? DEFAULT_STARTUP_TIMEOUT_MS;
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
      const info = await this.tryGetLiveHub();
      if (info) return info;
      await sleep(POLL_MS);
    }

    throw new HubClientError("hubUnavailable", "DotCraft Hub could not be started.");
  }

  async tryGetLiveHub(): Promise<HubLockInfo | null> {
    const info = readHubLockFromPath(hubLockPath(this.options.homeDir));
    if (!info || !isProcessAlive(info.pid)) return null;

    try {
      parseHubBaseUrl(info.apiBaseUrl);
      const response = await fetch(`${info.apiBaseUrl}/v1/status`);
      if (!response.ok) return null;
      let status: Partial<HubStatusResponse> = {};
      try {
        status = (await response.json()) as Partial<HubStatusResponse>;
      } catch {
        // A status response can be empty during startup races.
      }
      return {
        ...info,
        binaryPath: typeof status.binaryPath === "string" ? status.binaryPath : info.binaryPath,
      };
    } catch {
      return null;
    }
  }

  private startHub(): void {
    const executable = this.options.executable?.trim() || "dotcraft";
    const isDll = executable.toLowerCase().endsWith(".dll");
    const child = spawn(isDll ? "dotnet" : executable, isDll ? [executable, "hub"] : ["hub"], {
      detached: true,
      stdio: "ignore",
      windowsHide: true,
    });
    child.on("error", () => {
      // ensureHub reports startup failure after the readiness timeout.
    });
    child.unref();
  }

  private async requestJson<T>(
    hub: HubLockInfo,
    path: string,
    init: RequestInit,
    returnNullOnNotFound = false,
  ): Promise<T> {
    const response = await fetch(`${hub.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${hub.token}`,
        ...(init.headers ?? {}),
      },
    });

    if (returnNullOnNotFound && response.status === 404) return null as T;
    if (!response.ok) throw await this.toError(response);
    return (await response.json()) as T;
  }

  private async toError(response: Response): Promise<HubClientError> {
    try {
      const body = (await response.json()) as { error?: { code?: string; message?: string; details?: unknown } };
      if (body.error?.code || body.error?.message) {
        return new HubClientError(
          body.error.code ?? "hubRequestFailed",
          body.error.message ?? `Hub request failed with HTTP ${response.status}.`,
          body.error.details,
        );
      }
    } catch {
      // Fall through to status-based error.
    }
    return new HubClientError(
      response.status === 401 ? "unauthorized" : "hubRequestFailed",
      `Hub request failed with HTTP ${response.status}.`,
    );
  }

  private get binaryMatchPolicy(): HubBinaryMatchPolicy {
    return this.options.expectedExecutable?.trim()
      ? (this.options.binaryMatchPolicy ?? "ignore")
      : "ignore";
  }

  private getBinaryMismatch(hub: HubLockInfo): { expectedExecutable: string; actualExecutable: string | null } | null {
    const expected = this.options.expectedExecutable?.trim();
    if (!expected) return null;
    const actual = hub.binaryPath?.trim() || null;
    if (actual && pathsEqual(actual, expected)) return null;
    return { expectedExecutable: expected, actualExecutable: actual };
  }

  private async shutdownMismatchedHub(
    hub: HubLockInfo,
    details: { expectedExecutable: string; actualExecutable: string | null },
  ): Promise<void> {
    try {
      await this.requestJson<{ ok: boolean }>(hub, "/v1/shutdown", { method: "POST" });
    } catch (error) {
      throw new HubClientError(
        "hubMismatchShutdownFailed",
        "Hub uses a different executable and could not be stopped.",
        details,
        error,
      );
    }
    const timeoutMs = this.options.hubShutdownTimeoutMs ?? DEFAULT_SHUTDOWN_TIMEOUT_MS;
    const started = Date.now();
    while (Date.now() - started < timeoutMs) {
      if (!isProcessAlive(hub.pid)) return;
      await sleep(POLL_MS);
    }
    throw new HubClientError(
      "hubMismatchShutdownTimeout",
      "Hub uses a different executable and did not stop after shutdown.",
      details,
    );
  }
}

function normalizePathForCompare(path: string): string {
  const normalized = resolvePath(path);
  return process.platform === "win32" ? normalized.toLowerCase() : normalized;
}

function pathsEqual(left: string, right: string): boolean {
  return normalizePathForCompare(left) === normalizePathForCompare(right);
}
