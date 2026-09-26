import type { FileDiff } from '../../types/toolCall'
import { basename } from '../../utils/path'

export interface TurnArtifact {
  kind: 'markdown' | 'html'
  diff: FileDiff
}

export function toTurnArtifact(diff: FileDiff): TurnArtifact | null {
  if (isInlineVisualizationPath(diff.filePath)) return null
  const ext = extensionOf(diff.filePath)
  if (ext === '.md' || ext === '.markdown') return { kind: 'markdown', diff }
  if (ext === '.html' || ext === '.htm') return { kind: 'html', diff }
  return null
}

function isInlineVisualizationPath(filePath: string): boolean {
  const normalized = filePath.replace(/\\/g, '/').toLowerCase()
  return /(^|\/)\.craft\/visualizations(?:\/|$)/.test(normalized)
}

function extensionOf(filePath: string): string {
  const name = basename(filePath).toLowerCase()
  const dot = name.lastIndexOf('.')
  return dot >= 0 ? name.slice(dot) : ''
}
