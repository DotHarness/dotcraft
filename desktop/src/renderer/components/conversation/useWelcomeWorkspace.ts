import { useCallback, useEffect, useState } from 'react'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import type { ComposerWorkspaceMode } from './ComposerWorkspaceFooter'

export function useWelcomeWorkspace(identityWorkspacePath: string) {
  const identityKey = normalizeWorkspaceProjectKey(identityWorkspacePath)
  const sourceWorkspacePath = useWorkspaceProjectsStore((state) => {
    const project = state.projects.find((candidate) =>
      candidate.kind === 'local' && normalizeWorkspaceProjectKey(candidate.path) === identityKey)
    return project?.primaryFolder ?? identityWorkspacePath
  })
  const [welcomeWorkspaceMode, setWelcomeWorkspaceMode] = useState<ComposerWorkspaceMode>('local')
  const [welcomeBaseRef, setWelcomeBaseRef] = useState<string | null>(null)
  const [welcomeWorktreeBranchName, setWelcomeWorktreeBranchName] = useState<string | null>(null)
  const [welcomeChangelist, setWelcomeChangelist] = useState('default')

  useEffect(() => {
    setWelcomeBaseRef(null)
    setWelcomeWorktreeBranchName(null)
    setWelcomeChangelist('default')
  }, [sourceWorkspacePath])

  const changeWelcomeWorkspaceMode = useCallback((nextMode: ComposerWorkspaceMode): void => {
    setWelcomeWorkspaceMode(nextMode)
    if (nextMode === 'local') setWelcomeWorktreeBranchName(null)
  }, [])

  return {
    sourceWorkspacePath,
    welcomeWorkspaceMode,
    changeWelcomeWorkspaceMode,
    welcomeBaseRef,
    setWelcomeBaseRef,
    welcomeWorktreeBranchName,
    setWelcomeWorktreeBranchName,
    welcomeChangelist,
    setWelcomeChangelist
  }
}
