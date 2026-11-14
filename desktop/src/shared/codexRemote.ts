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

export type CodexRemoteFailure = MobileFailure
export type CodexRemoteResult<T> = MobileResult<T>

export type CodexRemoteEvent =
  | { kind: 'stateChanged'; status: CodexRemoteStatus }
  | { kind: 'paired' }

export interface CodexRemoteApi {
  status(): Promise<CodexRemoteResult<CodexRemoteStatus>>
  enable(): Promise<CodexRemoteResult<CodexRemoteStatus>>
  disable(): Promise<CodexRemoteResult<CodexRemoteStatus>>
  createPairing(): Promise<CodexRemoteResult<CodexRemotePairing>>
  onEvent(callback: (event: CodexRemoteEvent) => void): () => void
}
