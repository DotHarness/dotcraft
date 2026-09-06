import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

/**
 * `thisFolder` comes first so the thread's own working directory stays inside its
 * runtime roots; `undefined` lets the backend default them to `[cwd]`. Callers must not send `cwd` with
 * this, so an existing thread's cwd survives a change of the Project's primary folder.
 */
export function runtimeWorkspaceRootsFor(workspacePath: string | undefined | null): string[] | undefined {
  const key = normalizeWorkspaceProjectKey(workspacePath)
  if (!key) return undefined

  const { projects } = useWorkspaceProjectsStore.getState()
  for (const project of projects) {
    if (project.kind === 'remote') continue
    const folders = [project.path, ...(project.secondaryFolders ?? [])]
    const folderKeys = folders.map((folder) => normalizeWorkspaceProjectKey(folder))
    const matchIndex = folderKeys.indexOf(key)
    if (matchIndex === -1) continue
    // Single-folder project: nothing extra to add; let the backend default.
    if (folders.length <= 1) return undefined
    return [folders[matchIndex], ...folders.filter((_, index) => index !== matchIndex)]
  }
  return undefined
}
