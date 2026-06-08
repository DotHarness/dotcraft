import { create } from 'zustand'
import type { BinarySource, ConnectionStatusPayload } from '../../preload/api.d'

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error'
export type ConnectionErrorType = 'binary-not-found' | 'handshake-timeout' | 'crash' | 'remote-config-invalid'

export interface ServerInfo {
  name: string
  version: string
  protocolVersion?: string
}

export interface ServerCapabilities {
  threadManagement?: boolean
  threadFork?: boolean
  gitWorktrees?: boolean
  threadSubscriptions?: boolean
  approvalFlow?: boolean
  modeSwitch?: boolean
  configOverride?: boolean
  cronManagement?: boolean
  heartbeatManagement?: boolean
  skillsManagement?: boolean
  pluginManagement?: boolean
  skillVariants?: boolean
  commandManagement?: boolean
  modelCatalogManagement?: boolean
  workspaceConfigManagement?: boolean
  memoryManagement?: boolean
  dreams?: boolean
  mcpManagement?: boolean
  mcpServerOrigins?: boolean
  subAgentManagement?: boolean
  externalChannelManagement?: boolean
  mcpStatus?: boolean
  usageTelemetry?: boolean
  threadGoals?: boolean
  subAgentSessions?: boolean
  manualCompaction?: boolean
  manualMemoryConsolidation?: boolean
  appBinding?: boolean
  extensions?: Record<string, unknown>
  [key: string]: unknown
}

export interface ConnectionState {
  status: ConnectionStatus
  serverInfo: ServerInfo | null
  capabilities: ServerCapabilities | null
  /** DashBoard URL when AppServer reports it at initialize; null if unavailable. */
  dashboardUrl: string | null
  errorMessage: string | null
  errorType: ConnectionErrorType | null
  binarySource: BinarySource | null
  isExpectedRestart: boolean
  /** Increments for every Main Process connection status event, including connected -> connected promotions. */
  connectionEpoch: number
}

interface ConnectionStore extends ConnectionState {
  setStatus(payload: ConnectionStatusPayload): void
  setExpectedRestart(expected: boolean): void
  reset(): void
}

const initialState: ConnectionState = {
  status: 'connecting',
  serverInfo: null,
  capabilities: null,
  dashboardUrl: null,
  errorMessage: null,
  errorType: null,
  binarySource: null,
  isExpectedRestart: false,
  connectionEpoch: 0
}

export const useConnectionStore = create<ConnectionStore>((set) => ({
  ...initialState,

  setStatus(payload: ConnectionStatusPayload) {
    const connected = payload.status === 'connected'
    set((state) => ({
      status: payload.status,
      serverInfo: payload.serverInfo ?? null,
      capabilities: (payload.capabilities as ServerCapabilities) ?? null,
      dashboardUrl: connected ? (payload.dashboardUrl ?? null) : null,
      errorMessage: payload.errorMessage ?? null,
      errorType: (payload.errorType as ConnectionErrorType) ?? null,
      binarySource: payload.binarySource ?? null,
      isExpectedRestart: connected ? false : state.isExpectedRestart,
      connectionEpoch: state.connectionEpoch + 1
    }))
  },

  setExpectedRestart(expected: boolean) {
    set({ isExpectedRestart: expected })
  },

  reset() {
    set(initialState)
  }
}))

/**
 * Subscribe to connection status updates from Main Process.
 * Call this once at app initialization.
 * Returns an unsubscribe function.
 */
export function initConnectionStore(): () => void {
  const unsubscribe = window.api.appServer.onConnectionStatus((payload) => {
    useConnectionStore.getState().setStatus(payload)
  })
  void window.api.appServer
    .getConnectionStatus()
    .then((payload) => {
      useConnectionStore.getState().setStatus(payload)
    })
    .catch(() => {})
  return unsubscribe
}
