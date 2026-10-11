export const MCP_APP_SANDBOX_SCHEME = 'dotcraft-mcp-sandbox'
export const MCP_APP_SANDBOX_PROXY_URL = `${MCP_APP_SANDBOX_SCHEME}://proxy/index.html`

export const MCP_APP_MAX_BRIDGE_MESSAGE_BYTES = 256 * 1024
export const MCP_APP_MAX_RESOURCE_BYTES = 2 * 1024 * 1024
export const VISUALIZATION_MAX_DOCUMENT_BYTES = MCP_APP_MAX_RESOURCE_BYTES + 1024 * 1024

export const MCP_APP_SANDBOX_PROXY_READY_METHOD = 'ui/notifications/sandbox-proxy-ready'
export const MCP_APP_SANDBOX_RESOURCE_READY_METHOD = 'ui/notifications/sandbox-resource-ready'
export const VISUALIZATION_SANDBOX_RESOURCE_READY_METHOD = 'visualization/notifications/sandbox-resource-ready'
export const MCP_APP_SANDBOX_BRIDGE_VIOLATION_METHOD = 'ui/notifications/sandbox-bridge-violation'

export const VISUALIZATION_DOWNLOAD_METHOD = 'visualization/download'
export const VISUALIZATION_MAX_DOWNLOAD_BYTES = 2 * 1024 * 1024
export const VISUALIZATION_MAX_DOWNLOAD_MESSAGE_BYTES = Math.ceil(VISUALIZATION_MAX_DOWNLOAD_BYTES / 3) * 4 + 64 * 1024
