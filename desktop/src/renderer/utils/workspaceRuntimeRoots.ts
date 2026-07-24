import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

/**
 * Ordered runtime workspace roots for a thread bound to `workspacePath`.
 *
 * Finds the local Project that contains this workspace among its folders and
 * returns `[thisFolder, ...the project's other folders]` with `thisFolder`
 * first, so the thread's own working directory always stays inside its runtime
 * roots. Returns `undefined` when the workspace is not part of a multi-folder
 * local Project (single folder / remote / chat / unknown) — callers then omit
 * the field so the backend defaults `runtimeWorkspaceRoots` to `[cwd]` (see
 * specs/features/multi-folder-projects.md §4).
 *
 * Only `runtimeWorkspaceRoots` is sent; `cwd` is left to default to the thread's
 * `WorkspacePath` (the primary snapshot), which keeps an existing thread's cwd
 * stable even after the Project's primary folder changes.
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
