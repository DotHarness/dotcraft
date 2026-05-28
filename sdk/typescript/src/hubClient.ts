import { spawn } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { homedir } from "node:os";
import { isIP } from "node:net";

import { DotCraftSdkError } from "./errors.js";

export interface HubClientOptions {
  dotcraftBin?: string;
  hubStartupTimeoutMs?: number;
  homeDir?: string;
}

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
  capabilities: Record<string, unknown>;
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
}

export interface HubEnsureAppServerOptions {
  client?: HubClientInfo;
  clientName?: string;
  clientVersion?: string;
  startIfMissing?: boolean;
  runtimeTools?: HubRuntimeToolsRequest;
}

export class HubClientError extends DotCraftSdkError {
  constructor(code: string, message: string, cause?: unknown) {
    super(code, message, { cause });
    this.name = "HubClientError";
  }
}

const DEFAULT_STARTUP_TIMEOUT_MS = 15_000;
const POLL_MS = 200;

export function hubLockPath(homeDir = homedir()): string {
  return join(homeDir, ".craft", "hub", "hub.lock");
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
    throw new HubClientError("invalidHubLock", `Invalid Hub URL: ${String(error)}`, error);
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

  async getStatus(): Promise<HubStatusResponse> {
    const hub = await this.ensureHub();
    const response = await fetch(`${hub.apiBaseUrl}/v1/status`);
    if (!response.ok) throw await this.toError(response);
    return (await response.json()) as HubStatusResponse;
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
    if (live) return live;

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
      return response.ok ? info : null;
    } catch {
      return null;
    }
  }

  private startHub(): void {
    const dotcraftBin = this.options.dotcraftBin?.trim() || "dotcraft";
    const isDll = dotcraftBin.toLowerCase().endsWith(".dll");
    const child = spawn(isDll ? "dotnet" : dotcraftBin, isDll ? [dotcraftBin, "hub"] : ["hub"], {
      detached: true,
      stdio: "ignore",
      windowsHide: true,
    });
    child.on("error", () => {
      // ensureHub reports startup failure after the readiness timeout.
    });
    child.unref();
  }

  private async requestJson<T>(hub: HubLockInfo, path: string, init: RequestInit): Promise<T> {
    const response = await fetch(`${hub.apiBaseUrl}${path}`, {
      ...init,
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${hub.token}`,
        ...(init.headers ?? {}),
      },
    });

    if (!response.ok) throw await this.toError(response);
    return (await response.json()) as T;
  }

  private async toError(response: Response): Promise<HubClientError> {
    try {
      const body = (await response.json()) as { error?: { code?: string; message?: string } };
      if (body.error?.code || body.error?.message) {
        return new HubClientError(
          body.error.code ?? "hubRequestFailed",
          body.error.message ?? `Hub request failed with HTTP ${response.status}.`,
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
}
