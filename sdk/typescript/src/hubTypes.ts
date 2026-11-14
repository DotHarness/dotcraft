import type { DevicePlatform } from "./gateway.js";

export interface HubClientOptions {
  executable?: string;
  expectedExecutable?: string;
  binaryMatchPolicy?: HubBinaryMatchPolicy;
  hubStartupTimeoutMs?: number;
  hubShutdownTimeoutMs?: number;
  homeDir?: string;
  /** Cancels this client\'s requests and releases its demand for shared bootstrap. */
  signal?: AbortSignal;
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
  managedServiceManagement?: boolean;
  portManagement: boolean;
  events: boolean;
  notifications: boolean;
  tray: boolean;
  satellites?: boolean;
  codexRemote: boolean;
  [key: string]: unknown;
}

export interface HubManagedServiceResponse {
  serviceId: string;
  state: string;
  pid?: number | null;
  endpoint?: string | null;
  accessToken?: string | null;
  version?: string | null;
  lastError?: string | null;
  recentStderr?: string | null;
}

export interface HubEnsureManagedServiceOptions {
  executable?: string;
  startIfMissing?: boolean;
}

export interface HubEvent {
  kind: string;
  at: string;
  workspacePath?: string | null;
  data?: unknown;
}

export interface HubSatelliteWorkspace {
  workspaceId: string;
  path: string;
  busy: boolean;
  busyOwner?: string | null;
  leaseExpiresAt?: string | null;
}

/** A paired machine; its peer id is also the AppServer `hostId`. */
export interface HubSatellite {
  peerId: string;
  displayName: string;
  online: boolean;
  machineName?: string;
  operatingSystem?: string;
  userName?: string;
  buildVersion?: string;
  workspaces: HubSatelliteWorkspace[];
  pairedAt?: string;
  lastSeenAt?: string | null;
}

export interface HubSatelliteInvite {
  inviteId: string;
  url: string;
  expiresAt: string;
}

export interface HubCreateSatelliteInviteOptions {
  name?: string;
  host?: string;
  ttlHours?: number;
}

export interface HubMobileDevice {
  deviceId: string;
  displayName: string;
  platform: DevicePlatform;
  osVersion: string;
  appVersion: string;
  pairedAt: string;
  lastSeenAt: string | null;
  connected: boolean;
}

export interface HubMobileRelay {
  url: string;
  state: "connecting" | "connected" | "failed";
}

export interface HubMobileState {
  state: "off" | "on" | "failed";
  failureCode?: string;
  failureMessage?: string;
  computerId: string | null;
  port: number;
  addresses: string[];
  devices: HubMobileDevice[];
  relay: HubMobileRelay | null;
}

export interface HubMobilePairing {
  pairingId: string;
  qrPayload: string;
  expiresAt: string;
}

export interface HubCodexRemoteStatus {
  state: "off" | "connecting" | "on" | "failed";
  signedIn: boolean;
  hostName: string;
  failureCode?: string;
  failureMessage?: string;
}

export interface HubCodexRemotePairing {
  pairingUrl: string;
  manualCode: string | null;
  expiresAt: string;
}

export interface HubProject {
  path: string;
  displayName: string;
  addedAt: string;
  lastOpenedAt: string;
  running: boolean;
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
