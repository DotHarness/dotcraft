import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";
import { homedir } from "node:os";
import { isIP } from "node:net";

import { DotCraftError } from "./errors.js";
import { ensureLocalHub, isProcessAlive, probeLocalHub, type HubConnection } from "./hubBootstrap.js";
import { shareHubBootstrap } from "./hubBootstrapFlight.js";

export { isProcessAlive } from "./hubBootstrap.js";

import type {
  HubClientOptions, HubLockInfo, HubAppServerResponse, HubStatusResponse,
  HubManagedServiceResponse, HubEnsureManagedServiceOptions, HubEvent,
  HubSatellite, HubSatelliteInvite, HubCreateSatelliteInviteOptions,
  HubMobileState, HubMobilePairing, HubProject, HubRuntimeToolsRequest, HubEnsureAppServerOptions,
} from "./hubTypes.js";
export type * from "./hubTypes.js";

export class HubClientError extends DotCraftError {
  readonly details?: unknown;

  constructor(code: string, message: string, details?: unknown, cause?: unknown) {
    super(code, message, { cause });
    this.name = "HubClientError";
    this.details = details;
  }
}

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

export class HubClient {
  constructor(private readonly options: HubClientOptions = {}) {}

  async ensureAppServer(
    workspacePath: string,
    options: HubEnsureAppServerOptions = {},
    signal?: AbortSignal,
  ): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub(signal);
    const client = options.client ?? {
      name: options.clientName ?? "dotcraft-sdk",
      version: options.clientVersion ?? "0.1.0",
    };
    return await this.requestJson<HubAppServerResponse>(hub, "/v1/appservers/ensure", {
      method: "POST",
      signal,
      body: JSON.stringify({
        workspacePath,
        client,
        startIfMissing: options.startIfMissing ?? true,
        runtimeTools: options.runtimeTools,
      }),
    });
  }

  async getAppServerByWorkspace(workspacePath: string, signal?: AbortSignal): Promise<HubAppServerResponse | null> {
    const hub = await this.tryGetLiveHub(signal);
    if (!hub) return null;
    return await this.requestJson<HubAppServerResponse | null>(
      hub,
      `/v1/appservers/by-workspace?path=${encodeURIComponent(workspacePath)}`,
      { method: "GET", signal },
      true,
    );
  }

  async restartAppServer(
    workspacePath: string,
    runtimeTools?: HubRuntimeToolsRequest,
    signal?: AbortSignal,
  ): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubAppServerResponse>(hub, "/v1/appservers/restart", {
      method: "POST",
      signal,
      body: JSON.stringify({ workspacePath, runtimeTools }),
    });
  }

  async stopAppServer(workspacePath: string, signal?: AbortSignal): Promise<HubAppServerResponse> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubAppServerResponse>(hub, "/v1/appservers/stop", {
      method: "POST",
      signal,
      body: JSON.stringify({ workspacePath }),
    });
  }

  async listAppServers(signal?: AbortSignal): Promise<HubAppServerResponse[]> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubAppServerResponse[]>(hub, "/v1/appservers", { method: "GET", signal });
  }

  async ensureManagedService(
    serviceId: string,
    options: HubEnsureManagedServiceOptions = {},
    signal?: AbortSignal,
  ): Promise<HubManagedServiceResponse> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubManagedServiceResponse>(hub, "/v1/services/ensure", {
      method: "POST",
      signal,
      body: JSON.stringify({
        serviceId,
        executable: options.executable,
        startIfMissing: options.startIfMissing ?? true,
      }),
    });
  }

  async getManagedService(serviceId: string, signal?: AbortSignal): Promise<HubManagedServiceResponse> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubManagedServiceResponse>(
      hub,
      `/v1/services/by-id?id=${encodeURIComponent(serviceId)}`,
      { method: "GET", signal },
    );
  }

  async restartManagedService(serviceId: string, executable?: string, signal?: AbortSignal): Promise<HubManagedServiceResponse> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubManagedServiceResponse>(hub, "/v1/services/restart", {
      method: "POST",
      signal,
      body: JSON.stringify({ serviceId, executable }),
    });
  }

  async stopManagedService(serviceId: string, signal?: AbortSignal): Promise<HubManagedServiceResponse> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubManagedServiceResponse>(hub, "/v1/services/stop", {
      method: "POST",
      signal,
      body: JSON.stringify({ serviceId }),
    });
  }

  async listSatellites(signal?: AbortSignal): Promise<HubSatellite[]> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubSatellite[]>(hub, "/v1/satellites", { method: "GET", signal });
  }

  async createSatelliteInvite(
    options: HubCreateSatelliteInviteOptions = {},
    signal?: AbortSignal,
  ): Promise<HubSatelliteInvite> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubSatelliteInvite>(hub, "/v1/satellites/invites", {
      method: "POST",
      signal,
      body: JSON.stringify({
        name: options.name,
        host: options.host,
        ttlHours: options.ttlHours,
      }),
    });
  }

  async revokeSatellite(peerId: string, signal?: AbortSignal): Promise<void> {
    const hub = await this.ensureHub(signal);
    await this.requestJson<{ revoked: boolean }>(
      hub,
      `/v1/satellites/${encodeURIComponent(peerId)}`,
      { method: "DELETE", signal },
    );
  }

  async getMobile(signal?: AbortSignal): Promise<HubMobileState> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubMobileState>(hub, "/v1/mobile", { method: "GET", signal });
  }

  async enableMobile(signal?: AbortSignal): Promise<HubMobileState> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubMobileState>(hub, "/v1/mobile/enable", { method: "POST", body: "{}", signal });
  }

  async disableMobile(signal?: AbortSignal): Promise<HubMobileState> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubMobileState>(hub, "/v1/mobile/disable", { method: "POST", body: "{}", signal });
  }

  async createMobilePairing(signal?: AbortSignal): Promise<HubMobilePairing> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubMobilePairing>(hub, "/v1/mobile/pairings", { method: "POST", body: "{}", signal });
  }

  async allowMobilePairing(pairingId: string, signal?: AbortSignal): Promise<void> {
    const hub = await this.ensureHub(signal);
    await this.requestJson<void>(
      hub,
      `/v1/mobile/pairings/${encodeURIComponent(pairingId)}/allow`,
      { method: "POST", body: "{}", signal },
    );
  }

  async denyMobilePairing(pairingId: string, signal?: AbortSignal): Promise<void> {
    const hub = await this.ensureHub(signal);
    await this.requestJson<void>(
      hub,
      `/v1/mobile/pairings/${encodeURIComponent(pairingId)}/deny`,
      { method: "POST", body: "{}", signal },
    );
  }

  async revokeMobileDevice(deviceId: string, signal?: AbortSignal): Promise<void> {
    const hub = await this.ensureHub(signal);
    await this.requestJson<void>(
      hub,
      `/v1/mobile/devices/${encodeURIComponent(deviceId)}`,
      { method: "DELETE", signal },
    );
  }

  async setMobileRelay(url: string, token: string, signal?: AbortSignal): Promise<HubMobileState> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubMobileState>(hub, "/v1/mobile/relay", {
      method: "PUT",
      signal,
      body: JSON.stringify({ url, token }),
    });
  }

  async clearMobileRelay(signal?: AbortSignal): Promise<HubMobileState> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubMobileState>(hub, "/v1/mobile/relay", { method: "DELETE", signal });
  }

  async listProjects(signal?: AbortSignal): Promise<HubProject[]> {
    const hub = await this.ensureHub(signal);
    const response = await this.requestJson<{ projects: HubProject[] }>(hub, "/v1/projects", { method: "GET", signal });
    return response.projects;
  }

  async openProject(path: string, signal?: AbortSignal): Promise<HubProject> {
    const hub = await this.ensureHub(signal);
    return await this.requestJson<HubProject>(hub, "/v1/projects/open", {
      method: "POST",
      signal,
      body: JSON.stringify({ path }),
    });
  }

  async removeProject(path: string, signal?: AbortSignal): Promise<void> {
    const hub = await this.ensureHub(signal);
    await this.requestJson<unknown>(hub, "/v1/projects/remove", {
      method: "POST",
      signal,
      body: JSON.stringify({ path }),
    });
  }

  async ensureDefaultChatAppServer(
    options: HubEnsureAppServerOptions = {},
    signal?: AbortSignal,
  ): Promise<HubAppServerResponse> {
    this.cancellation(signal)?.throwIfAborted();
    const workspacePath = ensureDefaultChatWorkspace(this.options.homeDir);
    return await this.ensureAppServer(workspacePath, options, signal);
  }

  async getStatus(signal?: AbortSignal): Promise<HubStatusResponse> {
    const connection = await this.ensureConnection(signal);
    return connection.status;
  }

  async shutdownHub(signal?: AbortSignal): Promise<void> {
    const hub = await this.tryGetLiveHub(signal);
    if (!hub) return;
    await this.requestJson<{ ok: boolean }>(hub, "/v1/shutdown", { method: "POST", signal });
  }

  async subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void> {
    signal = this.cancellation(signal)!;
    const hub = await this.ensureHub(signal);
    signal.throwIfAborted();
    const response = await fetch(`${hub.apiBaseUrl}/v1/events`, {
      headers: { Authorization: `Bearer ${hub.token}` },
      signal,
    });
    if (!response.ok || !response.body) throw await this.toError(response);

    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = "";
    try {
      while (!signal.aborted) {
        const { done, value } = await reader.read();
        signal.throwIfAborted();
        if (done) break;
        buffer += decoder.decode(value, { stream: true });
        let boundary = findSseBoundary(buffer);
        while (boundary && !signal.aborted) {
          const raw = buffer.slice(0, boundary.index);
          buffer = buffer.slice(boundary.index + boundary.sequence.length);
          const dataLine = raw.split(/\r?\n/).find((line) => line.startsWith("data:"));
          const data = dataLine?.slice("data:".length).trim();
          if (data) {
            try {
              onEvent(JSON.parse(data) as HubEvent);
            } catch {}
          }
          boundary = findSseBoundary(buffer);
        }
      }
      signal.throwIfAborted();
    } finally {
      reader.releaseLock();
    }
  }

  async ensureHub(signal?: AbortSignal): Promise<HubLockInfo> {
    return (await this.ensureConnection(signal)).hub;
  }

  private ensureConnection(signal?: AbortSignal): Promise<HubConnection> {
    const lockPath = hubLockPath(this.options.homeDir);
    return shareHubBootstrap(this.options, lockPath, (sharedSignal) => ensureLocalHub(this.options, {
      lockPath,
      readLock: () => readHubLockFromPath(lockPath),
      validateBaseUrl: parseHubBaseUrl,
      shutdown: (hub, signal) => this.requestJson(hub, "/v1/shutdown", { method: "POST", signal }, false, false),
      error: (code, message, details) => new HubClientError(code, message, details),
    }, sharedSignal), this.cancellation(signal));
  }

  async tryGetLiveHub(signal?: AbortSignal): Promise<HubLockInfo | null> {
    signal = this.cancellation(signal);
    signal?.throwIfAborted();
    const info = readHubLockFromPath(hubLockPath(this.options.homeDir));
    if (!info || !isProcessAlive(info.pid)) return null;
    return (await probeLocalHub(info, parseHubBaseUrl, undefined, signal)).hub;
  }

  private cancellation(signal?: AbortSignal): AbortSignal | undefined {
    const lifetime = this.options.signal;
    return lifetime && signal && lifetime !== signal ? AbortSignal.any([lifetime, signal]) : signal ?? lifetime;
  }

  private async requestJson<T>(
    hub: HubLockInfo,
    path: string,
    init: RequestInit,
    returnNullOnNotFound = false,
    useClientSignal = true,
  ): Promise<T> {
    const signal = useClientSignal ? this.cancellation(init.signal ?? undefined) : init.signal;
    signal?.throwIfAborted();
    const response = await fetch(`${hub.apiBaseUrl}${path}`, {
      ...init,
      signal,
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${hub.token}`,
        ...(init.headers ?? {}),
      },
    });

    if (returnNullOnNotFound && response.status === 404) return null as T;
    if (!response.ok) throw await this.toError(response);
    if (response.status === 204) return undefined as T;
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
}
