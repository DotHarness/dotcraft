/**
 * Low-level AppServer JSON-RPC client and wire DTOs.
 */

export { DotCraftWireClient } from "./client.js";
export type { NotificationHandler, ServerRequestHandler, Unsubscribe } from "./client.js";
export {
  DotCraftError,
  DotCraftSdkError,
  ApprovalTimeoutError,
  InitializationError,
  ThreadNotActiveError,
  ThreadNotFoundError,
  TurnCancelledError,
  TurnFailedError,
  TurnInProgressError,
} from "./errors.js";
export {
  DECISION_ACCEPT,
  DECISION_ACCEPT_ALWAYS,
  DECISION_ACCEPT_FOR_SESSION,
  DECISION_CANCEL,
  DECISION_DECLINE,
  ERR_ALREADY_INITIALIZED,
  ERR_APPROVAL_TIMEOUT,
  ERR_CHANNEL_REJECTED,
  ERR_CRON_NOT_FOUND,
  ERR_NOT_INITIALIZED,
  ERR_THREAD_NOT_ACTIVE,
  ERR_THREAD_NOT_FOUND,
  ERR_TURN_IN_PROGRESS,
  ERR_TURN_NOT_FOUND,
  ERR_TURN_NOT_RUNNING,
  InitializeResult,
  JsonRpcMessage,
  ServerCapabilities,
  ServerInfo,
  Thread,
  Turn,
  commandRefPart,
  fileRefPart,
  imageUrlPart,
  localImagePart,
  skillRefPart,
  textPart,
} from "./models.js";
export type { SessionIdentityWire } from "./models.js";
export {
  StdioTransport,
  TransportClosed,
  TransportError,
  WebSocketTransport,
} from "./transport.js";
export type { Transport, WebSocketTransportOptions } from "./transport.js";
export {
  configureTextMergeDebug,
  extractAgentReplyTextFromTurnCompletedParams,
  extractAgentReplyTextsFromTurnCompletedParams,
  mergeReplyTextFromDeltaAndSnapshot,
} from "./turnReply.js";
