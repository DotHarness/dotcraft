import type { TurnFileChange } from '../../types/turnDiff'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'

export interface TurnFileGroup {
  filePath: string
  rows: TurnFileChange[]
  additions: number
  deletions: number
}

export function groupTurnFileChanges(rows: readonly TurnFileChange[], workspacePath: string): TurnFileGroup[] {
  const groups = new Map<string, TurnFileGroup>()
  for (const row of rows) {
    const filePath = toWorkspaceRelativePath(workspacePath, row.diff.filePath)
    let group = groups.get(filePath)
    if (!group) {
      group = { filePath, rows: [], additions: 0, deletions: 0 }
      groups.set(filePath, group)
    }
    group.rows.push(row)
    group.additions += row.diff.additions
    group.deletions += row.diff.deletions
  }
  return [...groups.values()]
}
