/** Low-level AppServer JSON-RPC transport. Contracts live in `@dotcraft/sdk/contracts`. */

export * from "./wireCore.js";
export { StdioTransport, WebSocketTransport } from "./transport.js";
export type { WebSocketTransportOptions } from "./transport.js";
