import { useCallback, useState, type JSX, type ReactNode } from 'react'
import { Folder, GitBranch } from 'lucide-react'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import type { ThreadSummary } from '../../types/thread'
import { normalizeGitPathKey } from '../../stores/gitStore'
import { useGitHeadStore } from '../../stores/gitHeadStore'
import { useBranchPullRequest, useForgeRefStore, useOriginBranchPullRequest } from '../../stores/forgeRefStore'
import type { ForgePullSummary } from '../../../shared/forgeReferences'
import { importedThreadSourceLabel } from '../../utils/sessionImport'
import { ChannelIconBadge } from '../ui/channelMeta'
import { Skeleton } from '../ui/Skeleton'
import { ThreadPullRequestRow } from './ThreadPullRequest'

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

        muted
      />
    )
  }
  const importSource = importedThreadSourceLabel(thread)
  return (
    <ChannelIconBadge
      channelName={thread.originChannel}
      tooltip={
        importSource
          ? t('channel.sessionImportFrom', { source: importSource })
          : t('threadEntry.originChannel', { channel: thread.originChannel })
      }
      size={14}

      muted
    />
  )
}

export function useThreadEntryDetails({
  thread,
  project,
  projectName,
  relativeTime,
  origin,
  onScreen = false
}: {
  thread: ThreadSummary
  project?: WorkspaceProjectSummary | null
  projectName: string | null
  relativeTime: string
  origin?: ReactNode
  onScreen?: boolean
}): { content: JSX.Element; onOpen: () => void; pull: ForgePullSummary | null; rowPull: ForgePullSummary | null } {
  const recordedBranch = thread.gitInfo?.branch?.trim() || null
  const recordedOrigin = thread.gitInfo?.originUrl?.trim() || null
  const recorded = Boolean(recordedBranch && recordedOrigin)
  const worktreeBranch = thread.worktree?.branchName?.trim() || null
  const gitPath = (
    thread.effectiveWorkspacePath
    || thread.worktree?.path
    || thread.workspacePath
    || project?.path
    || ''
  ).trim()
  const localGit = Boolean(gitPath && project?.kind !== 'remote' && project?.kind !== 'chat')
  const canInspectGit = localGit && !worktreeBranch && !recordedBranch
  const gitPathKey = canInspectGit ? normalizeGitPathKey(gitPath) : ''
  const head = useGitHeadStore((state) => gitPathKey ? state.byPath[gitPathKey] : undefined)
  const pullBranch = worktreeBranch || (head?.inspection?.kind === 'branch' ? head.inspection.label : null)
  const [opened, setOpened] = useState(false)
  const originPull = useOriginBranchPullRequest(recordedOrigin, recordedBranch, recorded && (opened || onScreen))
  const pathPull = useBranchPullRequest(
    gitPath,
    pullBranch,
    !recordedOrigin && localGit && (opened || (Boolean(worktreeBranch) && onScreen))
  )
  const pull = recordedOrigin ? originPull : pathPull
  const ensureHead = useCallback(() => {
    setOpened(true)
    if (canInspectGit) void useGitHeadStore.getState().ensure(gitPath)
    if (recordedOrigin) {
      if (recordedBranch) void useForgeRefStore.getState().ensureOriginPull(recordedOrigin, recordedBranch)
    } else if (localGit && pullBranch) {
      void useForgeRefStore.getState().ensureBranchPull(gitPath, pullBranch)
    }
  }, [canInspectGit, gitPath, localGit, pullBranch, recordedBranch, recordedOrigin])
  const branchLabel = recordedBranch || worktreeBranch || (
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
    pull,
    // A row shows only the thread's own branch, never the workspace's current HEAD.
    rowPull: recordedOrigin ? originPull : worktreeBranch ? pathPull : null,
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
        {!checking && pull && <ThreadPullRequestRow pull={pull} />}
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
