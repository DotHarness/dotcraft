import type { FileDiff } from './toolCall'

export interface FileChangeEntry {
  path: string
  kind: 'add' | 'update'
  diff?: string
  additions: number
  deletions: number
  truncated?: boolean
}

export interface FileChangeStructuredContent {
  kind: 'fileChange'
  changes: FileChangeEntry[]
}

export interface TurnFileChange {
  /** `${turnId}::${itemId}` for history rows, `${turnId}::${path}` for live rows. */
  key: string
  turnId: string
  diff: FileDiff
  patchText: string
  truncated: boolean
}

export interface TurnDiff {
  turnId: string
  source: 'live' | 'history'
  files: TurnFileChange[]
}

export interface ThreadFileSummary {
  filePath: string
  additions: number
  deletions: number
  status: FileDiff['status']
}
