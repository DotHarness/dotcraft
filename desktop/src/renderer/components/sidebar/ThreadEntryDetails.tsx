import { useCallback, type JSX, type ReactNode } from 'react'
import { Folder, GitBranch } from 'lucide-react'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import type { ThreadSummary } from '../../types/thread'
import { normalizeGitPathKey } from '../../stores/gitStore'
import { useGitHeadStore } from '../../stores/gitHeadStore'
import { ChannelIconBadge } from '../ui/channelMeta'
import { Skeleton } from '../ui/Skeleton'

/** Threads started from Desktop itself carry no badge. */
export function threadOriginBadge({
  thread,
  isSubAgent,
  t
}: {
  thread: ThreadSummary
  isSubAgent: boolean
  t: (key: string, vars?: Record<string, string>) => string
}): ReactNode {
  const presentation = thread.originPresentation
  const visible =
    !isSubAgent &&
    (Boolean(presentation || thread.originApp) || (
      thread.originChannel.length > 0 &&
      thread.originChannel.toLowerCase() !== 'dotcraft-desktop'
    ))
  if (!visible) return null

  if (presentation) {
    return (
      <ChannelIconBadge
        channelName={thread.originChannel}
        iconSrc={presentation.icon ?? undefined}
        label={presentation.displayName}
        tooltip={t('threadEntry.originMember', { name: presentation.displayName })}
        size={14}
        framed={false}
        muted
      />
    )
  }
  if (thread.originApp) {
    return (
      <ChannelIconBadge
        channelName={thread.originChannel}
        iconSrc={thread.originApp.icon ?? undefined}
        label={thread.originApp.displayName}
        tooltip={
          thread.originApp.memberId
            ? t('threadEntry.originMember', { name: thread.originApp.displayName })
            : t('threadEntry.originApp', { app: thread.originApp.displayName })
        }
        size={14}
        framed={false}
        muted
      />
    )
  }
  return (
    <ChannelIconBadge
      channelName={thread.originChannel}
      tooltip={t('threadEntry.originChannel', { channel: thread.originChannel })}
      size={14}
      framed={false}
      muted
    />
  )
}

export function useThreadEntryDetails({
  thread,
  project,
  projectName,
  relativeTime,
  origin
}: {
  thread: ThreadSummary
  project?: WorkspaceProjectSummary | null
  projectName: string | null
  relativeTime: string
  origin?: ReactNode
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
        <div
          className={
            origin
              ? 'sidebar-entry-details-header sidebar-entry-details-header--with-origin'
              : 'sidebar-entry-details-header'
          }
        >
          <span className="sidebar-entry-details-title" title={title}>{title}</span>
          {origin && <span className="sidebar-entry-details-origin">{origin}</span>}
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
