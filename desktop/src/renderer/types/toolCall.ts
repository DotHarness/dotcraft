/**
 * Tool call types: diffs, file changes, and SubAgent progress.
 */

export interface DiffLine {
  type: 'context' | 'add' | 'remove'
  content: string
}

export interface DiffHunk {
  oldStart: number
  oldLines: number
  newStart: number
  newLines: number
  lines: DiffLine[]
}

export interface FileDiff {
  filePath: string
  /** Last turn id that contributed an edit (same as last element of turnIds when present) */
  turnId: string
  /** All turn ids that contributed edits to this file (cross-turn accumulation) */
  turnIds: string[]
  additions: number
  deletions: number
  diffHunks: DiffHunk[]
  status: 'written' | 'reverted'
  isNewFile: boolean
  /** Full file content before any agent edit (when known — enables correct revert after multi-edit) */
  originalContent?: string
  /** Full file content after all accumulated edits (when known) */
  currentContent?: string
}

export interface SubAgentEntry {
  label: string
  currentTool: string | null
  currentToolDisplay: string | null
  inputTokens: number
  outputTokens: number
  isCompleted: boolean
}
