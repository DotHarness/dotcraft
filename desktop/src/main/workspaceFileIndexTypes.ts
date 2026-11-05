export interface FileMatchWire {
  name: string
  relativePath: string
  dir: string
}

export interface FileIndexEntry {
  relativePath: string
  name: string
  dir: string
}

export type FileIndexStatus = 'empty' | 'building' | 'ready'
export type FileIndexSource = 'rg' | 'walk'

export interface FileListResultWire {
  files: FileMatchWire[]
  indexStatus: FileIndexStatus
  indexedCount: number
  stale: boolean
}

export interface FileIndexCacheWire {
  schemaVersion: number
  workspaceRoot: string
  generatedAt: string
  ignoreConfigVersion: string
  entries: FileIndexEntry[]
}

export interface WorkerSuccessMessage {
  type: 'success'
  entries: FileIndexEntry[]
  pathCount: number
  truncated?: boolean
  source?: FileIndexSource
  rgError?: string | null
}

export interface WorkerProgressMessage {
  type: 'progress'
  count: number
  source?: FileIndexSource
}

export interface WorkerErrorMessage {
  type: 'error'
  error: string
}

export type WorkerMessage = WorkerSuccessMessage | WorkerProgressMessage | WorkerErrorMessage

export interface BuildProgress {
  count: number
  source?: FileIndexSource
}
