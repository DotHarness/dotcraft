/**
 * Hub discovery, startup, management, and SSE helpers.
 */

export {
  HubClient,
  HubClientError,
  defaultChatWorkspacePath,
  ensureDefaultChatWorkspace,
  findSseBoundary,
  hubLockPath,
  isLoopbackHost,
  isProcessAlive,
  parseHubBaseUrl,
  readHubLockFromPath,
} from "./hubClient.js";
export type {
  HubAppServerResponse,
  HubBinaryMatchPolicy,
  HubCapabilities,
  HubClientInfo,
  HubClientOptions,
  HubCreateSatelliteInviteOptions,
  HubEnsureAppServerOptions,
  HubEvent,
  HubLockInfo,
  HubManagedServiceResponse,
  HubEnsureManagedServiceOptions,
  HubRuntimeToolsRequest,
  HubSatellite,
  HubSatelliteInvite,
  HubSatelliteWorkspace,
  HubServiceStatus,
  HubStatusResponse,
} from "./hubClient.js";
