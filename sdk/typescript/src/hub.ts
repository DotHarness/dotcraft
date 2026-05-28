/**
 * Hub discovery, startup, management, and SSE helpers.
 */

export {
  HubClient,
  HubClientError,
  findSseBoundary,
  hubLockPath,
  isLoopbackHost,
  isProcessAlive,
  parseHubBaseUrl,
  readHubLockFromPath,
} from "./hubClient.js";
export type {
  HubAppServerResponse,
  HubClientInfo,
  HubClientOptions,
  HubEnsureAppServerOptions,
  HubEvent,
  HubLockInfo,
  HubRuntimeToolsRequest,
  HubServiceStatus,
  HubStatusResponse,
} from "./hubClient.js";
