import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

const norm = (value: string): string => value.replace(/\\/g, '/').replace(/\/+$/, '')

export function viewerRootsFor(workspacePath: string | null | undefined): string[] {
  const primary = workspacePath?.trim()
  if (!primary) return []
  const key = normalizeWorkspaceProjectKey(primary)
  const project = useWorkspaceProjectsStore.getState().projects.find((entry) =>
    entry.kind !== 'remote' &&
    [entry.path, ...(entry.secondaryFolders ?? [])].some((folder) => normalizeWorkspaceProjectKey(folder) === key)
  )
  const folders = project ? [project.path, ...(project.secondaryFolders ?? [])] : []
  const roots = [primary]
  const seen = new Set([key])
  for (const folder of folders) {
    const folderKey = normalizeWorkspaceProjectKey(folder)
    if (!folderKey || seen.has(folderKey)) continue
    seen.add(folderKey)
    roots.push(folder)
  }
  return roots
}

export function containingRoot(absolutePath: string, roots: readonly string[]): string | null {
  const target = norm(absolutePath).toLowerCase()
  let best: string | null = null
  let bestLength = -1
  for (const root of roots) {
    const candidate = norm(root).toLowerCase()
    if (!candidate) continue
    if ((target === candidate || target.startsWith(`${candidate}/`)) && candidate.length > bestLength) {
      best = root
      bestLength = candidate.length
    }
  }
  return best
}

export function relativeToRoot(absolutePath: string, root: string): string {
  const normalizedAbsolute = absolutePath.replace(/\\/g, '/')
  const normalizedRoot = norm(root)
  if (normalizedAbsolute.length <= normalizedRoot.length) {
    return normalizedAbsolute.split('/').pop() ?? normalizedAbsolute
  }
  return normalizedAbsolute.slice(normalizedRoot.length + 1)
}

export function rootLabel(root: string): string {
  const normalized = norm(root)
  return normalized.split('/').pop() || normalized
}
