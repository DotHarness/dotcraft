import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

export function runtimeWorkspaceRootsFor(identityWorkspacePath: string | undefined | null): string[] | undefined {
  const key = normalizeWorkspaceProjectKey(identityWorkspacePath)
  if (!key) return undefined

  const project = useWorkspaceProjectsStore.getState().projects.find((candidate) =>
    candidate.kind === 'local' && normalizeWorkspaceProjectKey(candidate.path) === key
  )
  return project ? [project.path, ...(project.secondaryFolders ?? [])] : undefined
}
