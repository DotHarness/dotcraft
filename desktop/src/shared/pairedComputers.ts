export type PairedComputerStatus =
  | 'connecting'
  | 'connected'
  | 'offline'
  | 'accessOff'
  | 'updateRequired'
  | 'identityChanged'

export interface PairedComputerProject {
  projectId: string
  displayName: string
  path: string
  kind: 'project' | 'chats'
  saved: boolean
  unavailable: boolean
}

export interface PairedComputer {
  computerId: string
  name: string
  status: PairedComputerStatus
  version: string | null
  requiredVersion: string | null
  lastConnectedAt: string | null
  projects: PairedComputerProject[]
}

export type PairingFailureCode =
  | 'invalidLink'
  | 'ownComputer'
  | 'pairingDenied'
  | 'pairingCodeInvalid'
  | 'unreachable'
  | 'storageUnavailable'

export type AddFolderFailureCode = 'relativePath' | 'workspaceNotFound' | 'unreachable'

export type PairedComputerResult<T, Code extends string = string> =
  | { ok: true; value: T }
  | { ok: false; code: Code; message?: string }

export type PairedComputerEvent =
  | { kind: 'changed'; computers: PairedComputer[] }
  | { kind: 'pairingWaiting'; name: string }
  | { kind: 'revoked'; name: string }
  | { kind: 'pairingLink'; link: string }
