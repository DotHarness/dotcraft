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
  HubEnsureAppServerOptions,
  HubEvent,
  HubLockInfo,
  HubRuntimeToolsRequest,
  HubServiceStatus,
  HubStatusResponse,
} from "./hubClient.js";
