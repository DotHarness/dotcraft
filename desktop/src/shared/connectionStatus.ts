import type { BinarySource } from './desktopSettings'

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error'

export type ConnectionErrorType =
  | 'binary-not-found'
  | 'handshake-timeout'
  | 'crash'
  | 'remote-config-invalid'

export interface ConnectionStatusPayload {
  status: ConnectionStatus
  serverInfo?: {
    name: string
    version: string
    protocolVersion?: string
  }
  capabilities?: Record<string, unknown>
  /** DashBoard URL when the server hosts it (initialize). */
  dashboardUrl?: string
  errorMessage?: string
  errorType?: ConnectionErrorType
  binarySource?: BinarySource
}

export interface RetryConnectionRequest {
  restartManaged?: boolean
}

export interface ResolvedBinaryPayload {
  source: BinarySource
  path: string | null
}
