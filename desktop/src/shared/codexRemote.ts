import type { MobileFailure, MobileResult } from './mobile'

export type CodexRemoteState = 'off' | 'connecting' | 'on' | 'failed'

export interface CodexRemoteStatus {
  state: CodexRemoteState
  signedIn: boolean
  hostName: string
  failureCode?: string
  failureMessage?: string
}

export interface CodexRemotePairing {
  pairingUrl: string
  manualCode: string | null
  expiresAt: string
}

export interface CodexRemoteClient {
  clientId: string
  displayName: string | null
  deviceType: string | null
  platform: string | null
  osVersion: string | null
  deviceModel: string | null
  appVersion: string | null
  lastSeenAt: string | null
}

export type CodexRemoteFailure = MobileFailure
export type CodexRemoteResult<T> = MobileResult<T>

export type CodexRemoteEvent =
  | { kind: 'stateChanged'; status: CodexRemoteStatus }
  | { kind: 'paired' }
  | { kind: 'clientsChanged' }

export interface CodexRemoteApi {
  status(): Promise<CodexRemoteResult<CodexRemoteStatus>>
  enable(): Promise<CodexRemoteResult<CodexRemoteStatus>>
  disable(): Promise<CodexRemoteResult<CodexRemoteStatus>>
  createPairing(): Promise<CodexRemoteResult<CodexRemotePairing>>
  listClients(): Promise<CodexRemoteResult<CodexRemoteClient[]>>
  revokeClient(clientId: string): Promise<CodexRemoteResult<void>>
  onEvent(callback: (event: CodexRemoteEvent) => void): () => void
}
