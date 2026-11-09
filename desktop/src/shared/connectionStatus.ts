import type { BinarySource } from './desktopSettings'

export type ConnectionStatus = 'connecting' | 'connected' | 'disconnected' | 'error'

export type ConnectionErrorType =
  | 'binary-not-found'
  | 'handshake-timeout'
  | 'hub-startup-failed'
  | 'crash'
  | 'remote-config-invalid'

export interface ConnectionStatusPayload {
  status: ConnectionStatus
  connectionKey?: string
  connectionId?: string
  serverInfo?: {
    name: string
    version: string
    protocolVersion?: string
  }
  capabilities?: Record<string, unknown>
  dashboardUrl?: string
  errorMessage?: string
  errorDetails?: string
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
