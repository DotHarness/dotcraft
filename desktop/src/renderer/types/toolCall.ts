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
  additions: number
  deletions: number
  diffHunks: DiffHunk[]
  status: 'written' | 'reverted'
  isNewFile: boolean
  /** Full file content before the edit, when known; lets the viewer highlight whole files. */
  originalContent?: string
  /** Full file content after the edit, when known */
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
