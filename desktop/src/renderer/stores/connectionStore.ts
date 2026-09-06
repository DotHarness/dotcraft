import { create } from 'zustand'
import type { BinarySource } from '../../preload/api.d'
import type {
  ConnectionErrorType,
  ConnectionStatus,
  ConnectionStatusPayload
} from '../../shared/connectionStatus'

export type { ConnectionErrorType, ConnectionStatus } from '../../shared/connectionStatus'

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
  skillsManagement?: boolean
  toolCatalog?: boolean
  pluginManagement?: boolean
  pluginMarketplaces?: boolean
  skillVariants?: boolean
  commandManagement?: boolean
  modelCatalogManagement?: boolean
  workspaceConfigManagement?: boolean
  sourceControlManagement?: boolean
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
  remoteToolHost?: boolean
  appBindingVersion?: number
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
      errorType: payload.errorType ?? null,
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

/** Call this once at app initialization; returns an unsubscribe function. */
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
