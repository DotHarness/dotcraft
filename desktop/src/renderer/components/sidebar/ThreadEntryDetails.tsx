import { useCallback, type JSX } from 'react'
import { Folder, GitBranch } from 'lucide-react'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import type { ThreadSummary } from '../../types/thread'
import { normalizeGitPathKey } from '../../stores/gitStore'
import { useGitHeadStore } from '../../stores/gitHeadStore'
import { Skeleton } from '../ui/Skeleton'

export function useThreadEntryDetails({
  thread,
  project,
  projectName,
  relativeTime
}: {
  thread: ThreadSummary
  project?: WorkspaceProjectSummary | null
  projectName: string | null
  relativeTime: string
}): { content: JSX.Element; onOpen: () => void } {
  const worktreeBranch = thread.worktree?.branchName?.trim() || null
  const gitPath = (
    thread.effectiveWorkspacePath
    || thread.worktree?.path
    || thread.workspacePath
    || project?.path
    || ''
  ).trim()
  const canInspectGit = Boolean(gitPath && !worktreeBranch && project?.kind !== 'remote' && project?.kind !== 'chat')
  const gitPathKey = canInspectGit ? normalizeGitPathKey(gitPath) : ''
  const head = useGitHeadStore((state) => gitPathKey ? state.byPath[gitPathKey] : undefined)
  const ensureHead = useCallback(() => {
    if (canInspectGit) void useGitHeadStore.getState().ensure(gitPath)
  }, [canInspectGit, gitPath])
  const branchLabel = worktreeBranch || (
    head?.inspection?.kind === 'branch'
      ? head.inspection.label
      : head?.inspection?.kind === 'detached'
        ? `HEAD ${head.inspection.label}`
        : null
  )
  const checking = canInspectGit && (!head || head.status === 'checking')
  const title = thread.displayName?.trim() || ''

  return {
    onOpen: ensureHead,
    content: (
      <>
        <div className="sidebar-entry-details-header">
          <span className="sidebar-entry-details-title" title={title}>{title}</span>
          <span className="sidebar-entry-details-meta">{relativeTime}</span>
        </div>
        {projectName && (
          <div className="sidebar-entry-details-row">
            <Folder size={14} strokeWidth={1.8} aria-hidden />
            <span>{projectName}</span>
          </div>
        )}
        {checking ? (
          <div className="sidebar-entry-details-row" aria-busy="true" aria-label="Git">
            <GitBranch size={14} strokeWidth={1.8} aria-hidden />
            <Skeleton width={72} height={10} />
          </div>
        ) : branchLabel ? (
          <div className="sidebar-entry-details-row">
            <GitBranch size={14} strokeWidth={1.8} aria-hidden />
            <span>{branchLabel}</span>
          </div>
        ) : null}
      </>
    )
  }
}

export function workspacePathName(path: string | null | undefined): string | null {
  const trimmed = path?.trim().replace(/[\\/]+$/, '')
  if (!trimmed) return null
  const parts = trimmed.split(/[\\/]/)
  return parts[parts.length - 1] || trimmed
}
