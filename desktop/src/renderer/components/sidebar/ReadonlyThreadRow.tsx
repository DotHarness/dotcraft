import { useState, type CSSProperties } from 'react'
import { Archive, Copy, ExternalLink } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import type { ThreadSummary } from '../../types/thread'
import { archiveWorkspaceThread } from '../../utils/archiveWorkspaceThread'
import { formatRelativeTime } from '../../utils/relativeTime'
import { getSubAgentDepth, isSubAgentThread } from '../../utils/subAgentThreads'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { buildWorkspaceOpenDeepLink } from '../../../shared/desktopDeepLink'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
import { Spinner } from '../ui/Spinner'
import { PinIcon } from './ThreadEntry'
import { threadOriginBadge, useThreadEntryDetails } from './ThreadEntryDetails'
import { ThreadRowLayout } from './ThreadRowLayout'
import { SidebarEntryDetailsCard } from './SidebarEntryDetailsCard'
import { isRemoteProject, isThreadRunning, isThreadWaiting, projectIdentity } from './projectThreads'
import { useRememberRowSelection } from './threadRowSelection'

/**
 * Pin is a Desktop-local setting keyed by workspace path, so the whole
 * `pinnedThreadIdsByWorkspace[key]` list is persisted directly. The main process
 * re-pushes the workspace projects payload afterwards, which moves the row.
 */
function toggleWorkspacePin(
  workspacePath: string,
  threadId: string,
  currentPinnedIds: string[]
): void {
  const workspaceKey = normalizeWorkspaceProjectKey(workspacePath)
  const id = threadId.trim()
  if (!workspaceKey || !id) return
  const next = currentPinnedIds.includes(id)
    ? currentPinnedIds.filter((existing) => existing !== id)
    : [id, ...currentPinnedIds]
  void window.api?.settings
    ?.set({ pinnedThreadIdsByWorkspace: { [workspaceKey]: next } })
    .catch((err: unknown) =>
      console.error('settings:set pinnedThreadIdsByWorkspace failed:', err)
    )
}

export function ReadonlyThreadRow({
  thread,
  project,
  pinned = false
}: {
  thread: ThreadSummary
  project: WorkspaceProjectSummary
  pinned?: boolean
  variant?: 'project' | 'pinned'
}): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setPendingProjectThreadOpen = useUIStore((s) => s.setPendingProjectThreadOpen)
  const rememberRowSelection = useRememberRowSelection()
  const running = isThreadRunning(thread)
  const waiting = isThreadWaiting(thread)
  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)
  const subAgent = isSubAgentThread(thread)
  const threadDetails = useThreadEntryDetails({
    thread: { ...thread, displayName },
    project,
    projectName: project.name || project.path,
    relativeTime,
    origin: threadOriginBadge({ thread, isSubAgent: subAgent, t })
  })
  const rowProjectKey = projectIdentity(project)
  const subAgentDepth = getSubAgentDepth(thread)
  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  // Pin/archive route to the target workspace connection by path, so they only
  // apply to local secondary / Chats rows. Remote rows keep the static marker.
  const supportsLocalActions = !subAgent && !isRemoteProject(project)
  const isPinned = pinned
  const statusColumn = running
    ? '24px'
    : waiting
      ? 'minmax(74px, max-content)'
      : 'minmax(24px, max-content)'
  const statusSlotWidth = running ? '24px' : 'max-content'
  const statusSlotMinWidth = '24px'
  const statusSlotJustifySelf = running ? 'center' : 'end'
  // Center the time/badge within its (>=24px) slot so secondary-project rows line
  // up with the foreground ThreadEntry's centered status slot.
  const statusContentJustify = 'center'

  async function copySessionId(): Promise<void> {
    await navigator.clipboard.writeText(thread.id)
    addToast(t('toast.copied'), 'success')
  }

  async function copyDeepLink(): Promise<void> {
    if (isRemoteProject(project)) return
    await navigator.clipboard.writeText(buildWorkspaceOpenDeepLink(project.path, thread.id))
    addToast(t('toast.copied'), 'success')
  }

  async function openThread(): Promise<void> {
    rememberRowSelection(thread.id)
    if (!isRemoteProject(project)) {
      if (project.state !== 'foreground') {
        setPendingProjectThreadOpen({
          projectKey: rowProjectKey,
          workspacePath: project.path,
          threadId: thread.id
        })
        try {
          await window.api.workspace.switch(project.path)
        } catch (err) {
          useUIStore.getState().clearPendingProjectThreadOpen(rowProjectKey, thread.id)
          console.error('Failed to switch workspace for project thread:', err)
        }
        return
      }
    }
    setActiveMainView('conversation')
    useThreadStore.getState().setActiveThreadId(thread.id)
  }

  const statusContent = (
    <span
      className="dc-thread-row__status"
      style={{
        alignItems: 'center',
        justifyContent: statusContentJustify,
        width: running ? '100%' : 'auto',
        color: 'var(--text-dimmed)',
        fontSize: 'var(--type-secondary-size)',
        lineHeight: 'var(--type-secondary-line-height)',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'clip'
      }}
    >
      {running ? (
        <span className="dc-status-indicator">
          <Spinner
            label={t('threadEntry.turnRunning')}
            testId={`project-thread-running-indicator-${rowProjectKey}-${thread.id}`}
          />
        </span>
      ) : waiting ? (
        <span className="dc-status-badge" data-size="compact" data-tone="warning">
          <span className="dc-status-badge__label">{t('projectsRail.awaitingResponse')}</span>
        </span>
      ) : (
        relativeTime
      )}
    </span>
  )

  return (
    <>
    <SidebarEntryDetailsCard
      label={displayName}
      width={240}
      content={threadDetails.content}
      onOpen={threadDetails.onOpen}
      wrapperStyle={{ width: '100%' }}
    >
      <ThreadRowLayout
        isSubAgent={subAgent}
        subAgentDepth={subAgentDepth}
        canPin={!subAgent}
        subAgentLabel={t('threadEntry.subAgent')}
        rowTestId={`project-thread-entry-${rowProjectKey}-${thread.id}`}
        gridTestId={`project-thread-layout-${rowProjectKey}-${thread.id}`}
        statusTestId={`project-thread-status-${rowProjectKey}-${thread.id}`}
        leading={
          subAgent ? undefined : (
            <span
              data-testid={`project-thread-leading-${rowProjectKey}-${thread.id}`}
              style={readonlyLeadingSlotStyle}
            >
              {supportsLocalActions ? (
                <IconButton
                  icon={<PinIcon filled={isPinned} />}
                  label={isPinned ? t('threadEntry.unpin') : t('threadEntry.pin')}
                  tooltipLabel={isPinned ? t('threadEntry.unpin') : t('threadEntry.pin')}
                  tooltipPlacement="top"
                  size={22}
                  radius={6}
                  className="dc-thread-list-icon-button dc-thread-row__hover-action"
                  aria-pressed={isPinned}
                  data-pinned={isPinned ? 'true' : undefined}
                  data-testid={`project-thread-pin-${rowProjectKey}-${thread.id}`}
                  onClick={(e) => {
                    e.stopPropagation()
                    toggleWorkspacePin(project.path, thread.id, project.pinnedThreadIds ?? [])
                  }}
                  style={{ transition: 'opacity 120ms ease, color 120ms ease' }}
                />
              ) : (
                pinned && (
                  <ReadonlyPinnedIcon
                    label={t('threadGroup.pinned')}
                    testId={`project-thread-pinned-${rowProjectKey}-${thread.id}`}
                  />
                )
              )}
            </span>
          )
        }
        name={displayName}
        nameStyle={{ fontWeight: 'var(--type-ui-weight)' }}
        statusColumn={statusColumn}
        statusSlotWidth={statusSlotWidth}
        statusSlotMinWidth={statusSlotMinWidth}
        statusJustifySelf={statusSlotJustifySelf}
        status={statusContent}
        statusExtra={
          supportsLocalActions ? (
            <IconButton
              icon={<Archive size={14} strokeWidth={2} aria-hidden="true" />}
              label={t('threadEntry.archive')}
              tooltipLabel={t('threadEntry.archive')}
              tooltipPlacement="top"
              size={24}
              radius={8}
              className="dc-thread-list-icon-button dc-thread-row__hover-action dc-thread-row__archive"
              data-testid={`project-thread-archive-${rowProjectKey}-${thread.id}`}
              onClick={(e) => {
                e.stopPropagation()
                void archiveWorkspaceThread(project.path, thread, t)
              }}
              style={{
                borderRadius: 'var(--sidebar-icon-control-radius)',
                position: 'absolute',
                right: 0,
                top: '50%',
                transform: 'translateY(-50%)',
                transition: 'opacity 120ms ease, color 120ms ease',
                zIndex: 2
              }}
            />
          ) : undefined
        }
        hoverable
        containerStyle={{ cursor: 'pointer', textAlign: 'left' }}
        containerProps={{
          onClick: () => void openThread(),
          onContextMenu: (event) => {
            event.preventDefault()
            setContextMenu({ x: event.clientX, y: event.clientY })
          }
        }}
      />
    </SidebarEntryDetailsCard>
    {contextMenu && (
      <ContextMenu
        position={contextMenu}
        onClose={() => setContextMenu(null)}
        items={[
          {
            label: t('threadEntry.copySessionId'),
            icon: <Copy size={14} aria-hidden />,
            onClick: () => void copySessionId()
          },
          ...(!isRemoteProject(project)
            ? [
                {
                  label: t('threadEntry.copyDeepLink'),
                  icon: <ExternalLink size={14} aria-hidden />,
                  onClick: () => void copyDeepLink()
                }
              ]
            : [])
        ]}
      />
    )}
    </>
  )
}

const readonlyLeadingSlotStyle: CSSProperties = {
  width: '18px',
  minWidth: '18px',
  height: '24px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

function ReadonlyPinnedIcon({
  label,
  testId
}: {
  label: string
  testId: string
}): JSX.Element {
  return (
    <ActionTooltip label={label} placement="top">
      <span
        aria-label={label}
        data-testid={testId}
        style={{
          width: '18px',
          minWidth: '18px',
          height: '24px',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--text-secondary)',
          flexShrink: 0
        }}
      >
        <PinIcon filled />
      </span>
    </ActionTooltip>
  )
}
