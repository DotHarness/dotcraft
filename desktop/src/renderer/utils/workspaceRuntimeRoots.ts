import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useThreadStore } from '../stores/threadStore'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

export function runtimeWorkspaceRootsFor(identityWorkspacePath: string | undefined | null): string[] | undefined {
  const key = normalizeWorkspaceProjectKey(identityWorkspacePath)
  if (!key) return undefined

  const project = useWorkspaceProjectsStore.getState().projects.find((candidate) =>
    candidate.kind === 'local' && normalizeWorkspaceProjectKey(candidate.path) === key
  )
  return project ? [project.primaryFolder ?? project.path, ...(project.secondaryFolders ?? [])] : undefined
}

export function runtimeWorkspaceUpdateFor(
  identityWorkspacePath: string | undefined | null,
  cwd: string | undefined = identityWorkspacePath ?? undefined,
  executionWorkspaceOverride?: string | null
): { runtimeWorkspaceRoots?: string[]; cwd?: string } {
  const runtimeWorkspaceRoots = runtimeWorkspaceRootsFor(identityWorkspacePath)
  if (!runtimeWorkspaceRoots) return {}
  const cwdKey = normalizeWorkspaceProjectKey(cwd)
  const retainsCwd = runtimeWorkspaceRoots.some((root) => {
    const rootKey = normalizeWorkspaceProjectKey(root)
    return cwdKey === rootKey || (!executionWorkspaceOverride && cwdKey.startsWith(`${rootKey.replace(/\/$/u, '')}/`))
  })
  return { runtimeWorkspaceRoots, ...(!retainsCwd ? { cwd: runtimeWorkspaceRoots[0] } : {}) }
}

export function threadWorkspaceUpdateFor(threadId: string, identityWorkspacePath: string): ReturnType<typeof runtimeWorkspaceUpdateFor> {
  const { activeThread, threadList } = useThreadStore.getState()
  const thread = activeThread?.id === threadId ? activeThread : threadList.find((candidate) => candidate.id === threadId)
  const configuration = activeThread?.id === threadId ? activeThread.configuration : undefined
  const cwd = configuration?.cwd ?? thread?.worktree?.sourceWorkspacePath ?? thread?.workspacePath ?? identityWorkspacePath
  const executionWorkspaceOverride = configuration?.executionWorkspaceOverride ?? thread?.worktree?.path
  return runtimeWorkspaceUpdateFor(identityWorkspacePath, cwd, executionWorkspaceOverride)
}
