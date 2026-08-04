/** Low-level AppServer JSON-RPC transport. Contracts live in `@dotcraft/sdk/contracts`. */

export { DotCraftWireClient } from "./client.js";
export type {
  DotCraftWireClientOptions,
  NotificationHandler,
  ServerRequestHandler,
  Unsubscribe,
  WireConnectionState,
} from "./client.js";
export {
  JsonRpcError,
  ReconnectQueueFullError,
  RequestTimeoutError,
} from "./errors.js";
export {
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
  JsonRpcMessage,
} from "./models.js";
export {
  StdioTransport,
  TransportClosed,
  TransportError,
  WebSocketTransport,
} from "./transport.js";
export type { Transport, WebSocketTransportOptions } from "./transport.js";
