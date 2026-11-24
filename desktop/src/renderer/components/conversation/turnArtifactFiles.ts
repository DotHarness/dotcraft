import type { FileDiff } from '../../types/toolCall'
import type { TurnFileChange } from '../../types/turnDiff'
import { basename } from '../../utils/path'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'

export interface TurnArtifact {
  kind: 'markdown' | 'html'
  diff: FileDiff
}

export function createdTurnArtifacts(rows: readonly TurnFileChange[], workspacePath: string): TurnArtifact[] {
  const artifacts = new Map<string, TurnArtifact>()
  for (const { diff } of rows) {
    if (!diff.isNewFile || diff.status !== 'written') continue
    const artifact = toTurnArtifact(diff)
    if (artifact) artifacts.set(toWorkspaceRelativePath(workspacePath, diff.filePath), artifact)
  }
  return [...artifacts.values()]
}

function toTurnArtifact(diff: FileDiff): TurnArtifact | null {
  if (isVisualizationPath(diff.filePath)) return null
  const ext = extensionOf(diff.filePath)
  if (ext === '.md' || ext === '.markdown') return { kind: 'markdown', diff }
  if (ext === '.html' || ext === '.htm') return { kind: 'html', diff }
  return null
}

function isVisualizationPath(filePath: string): boolean {
  const normalized = filePath.replace(/\\/g, '/').toLowerCase()
  return /(^|\/)\.craft\/visualizations(?:\/|$)/.test(normalized)
}

function extensionOf(filePath: string): string {
  const name = basename(filePath).toLowerCase()
  const dot = name.lastIndexOf('.')
  return dot >= 0 ? name.slice(dot) : ''
}
