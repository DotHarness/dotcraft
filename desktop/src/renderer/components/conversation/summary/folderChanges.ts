import { containingRoot } from '../../../utils/viewerRoots'
import { sumChanges, type ChangeTotals, type FileChanges } from './sources'

const isAbsolute = (path: string): boolean => /^(?:[a-zA-Z]:)?[\\/]/.test(path)

export function changesByFolder(
  changes: FileChanges,
  workspacePath: string,
  roots: readonly string[]
): Map<string, ChangeTotals> {
  const byRoot = new Map<string, ChangeTotals[]>()
  for (const [path, totals] of changes) {
    const absolute = isAbsolute(path) ? path : `${workspacePath.replace(/[\\/]+$/, '')}/${path}`
    const root = containingRoot(absolute, roots) ?? roots[0]
    byRoot.set(root, [...(byRoot.get(root) ?? []), totals])
  }
  return new Map([...byRoot].map(([root, entries]) => [root, sumChanges(entries)]))
}
