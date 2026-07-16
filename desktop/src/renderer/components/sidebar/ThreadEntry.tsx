import { useState, useRef, useCallback, useEffect } from 'react'
import type { ThreadSummary } from '../../types/thread'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useConnectionStore } from '../../stores/connectionStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { ContextMenu } from '../ui/ContextMenu'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ThreadRowLayout } from './ThreadRowLayout'
import { ChannelIconBadge } from '../ui/channelMeta'
import { Archive, ArrowRightLeft, Laptop, Pencil, Pin, Trash2 } from 'lucide-react'
import { AUTOMATION_TASK_DRAG_MIME } from '../automations/TaskCard'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useDragDropStore } from '../../stores/dragDropStore'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { getSubAgentDepth, isSubAgentThread } from '../../utils/subAgentThreads'
import { canForkThread, canForkWorktree, runThreadFork } from '../../utils/threadFork'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { SidebarEntryDetailsCard } from './SidebarEntryDetailsCard'
import { useThreadEntryDetails, workspacePathName } from './ThreadEntryDetails'

interface ThreadEntryProps {
  thread: ThreadSummary
}

/**
 * Single row in the thread list.
 * Layout: [Leading icons] [DisplayName ...] [PendingBadge] [StatusSlot]
 * Supports: click to select, right-click context menu, inline rename.
 * Spec 搂9.5
 */
export function ThreadEntry({ thread }: ThreadEntryProps): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const {
    activeThreadId,
    setActiveThreadId,
    renameThread,
    pinnedThreadIds,
    togglePinnedThread,
    runningTurnThreadIds,
    pendingApprovalThreadIds,
    pendingUserInputThreadIds,
    pendingPlanConfirmationThreadIds,
    unreadCompletedThreadIds
  } = useThreadStore()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const chat = useWorkspaceProjectsStore((s) => s.chat)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const isActive = activeThreadId === thread.id
  const isSubAgent = isSubAgentThread(thread)
  const subAgentDepth = getSubAgentDepth(thread)
  const hasRunningTurn = runningTurnThreadIds.has(thread.id)
  const hasPendingApproval = pendingApprovalThreadIds.has(thread.id)
  const hasPendingUserInput = pendingUserInputThreadIds.has(thread.id)
  const hasPendingPlanConfirmation = pendingPlanConfirmationThreadIds.has(thread.id)
  const hasUnreadCompleted = unreadCompletedThreadIds.has(thread.id)

  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  const [renaming, setRenaming] = useState(false)
  const [renameValue, setRenameValue] = useState(thread.displayName ?? '')
  const [hovered, setHovered] = useState(false)
  const [pinButtonFocused, setPinButtonFocused] = useState(false)
  const [archiveButtonFocused, setArchiveButtonFocused] = useState(false)
  const [dropActive, setDropActive] = useState(false)
  // `anim` drives the two transient post-drop animations. `success` plays
  // `dropSuccessPulse` on the row + `slideInBadge` on the inline bound icon;
  // `fail` plays `shake` on the row. Clears itself after the animation window.
  const [anim, setAnim] = useState<'success' | 'fail' | null>(null)
  const renameInputRef = useRef<HTMLInputElement>(null)
  const actionSlotRef = useRef<HTMLDivElement>(null)

  // Subscribe to the global drag session so we can dim archived threads and
  // mark the thread that's already the bound target of the dragged task.
  const dragActive = useDragDropStore((s) => s.active)
  const dragKind = dragActive?.kind ?? null
  const alreadyBound =
    dragKind === 'automation-task' &&
    dragActive!.alreadyBoundThreadId === thread.id
  const dimmedTarget =
    dragKind === 'automation-task' && thread.status !== 'active'

  useEffect(() => {
    if (!anim) return
    const timeout = anim === 'success' ? 700 : 360
    const t = setTimeout(() => setAnim(null), timeout)
    return () => clearTimeout(t)
  }, [anim])

  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)
  const identityPath = thread.worktree?.sourceWorkspacePath || thread.workspacePath || ''
  const ownsIdentity = (candidate: { projectId?: string; path: string; identityWorkspacePath?: string }): boolean =>
    [candidate.projectId, candidate.path, candidate.identityWorkspacePath]
      .some((value) => sameWorkspaceProjectKey(value, identityPath))
  const chatProject = chat && ownsIdentity(chat) ? chat : null
  const project = chatProject
    ?? projects.find(ownsIdentity)
    ?? projects.find((candidate) => sameWorkspaceProjectKey(candidate.projectId, foregroundProjectId))
    ?? null
  const projectName = chatProject
    ? t('chatsRail.title')
    : project?.name?.trim() || workspacePathName(identityPath)
  const threadDetails = useThreadEntryDetails({
    thread: { ...thread, displayName },
    project,
    projectName,
    relativeTime
  })
  const originPresentation = thread.originPresentation
  const showOriginBadge =
    !isSubAgent &&
    (Boolean(originPresentation || thread.originApp) || (
      thread.originChannel.length > 0 &&
      thread.originChannel.toLowerCase() !== 'dotcraft-desktop'
    ))
  // Hide the archive action during a drag session so the right side stays
  // clean while the drop-hint / already-bound pill is shown.
  const canPin = !isSubAgent && thread.status !== 'archived'
  const isPinned = canPin && pinnedThreadIds.includes(thread.id)
  const showPinAction =
    canPin && !renaming && !dragKind && (hovered || pinButtonFocused || isPinned)
  const showArchiveAction =
    !isSubAgent && !renaming && !dragKind && (hovered || archiveButtonFocused)
  const showPendingApprovalBadge = !isActive && hasPendingApproval
  const showPendingUserInputBadge = !isActive && !showPendingApprovalBadge && hasPendingUserInput
  const showPendingPlanBadge =
    !isActive && !showPendingApprovalBadge && !showPendingUserInputBadge && hasPendingPlanConfirmation
  const showPendingBadge =
    showPendingApprovalBadge || showPendingUserInputBadge || showPendingPlanBadge
  const hasBadgeContent = dropActive || alreadyBound || showPendingBadge || anim === 'success'
  const showStatusIcon = !isActive && thread.status !== 'active'
  const showUnreadCompletedDot =
    !isActive
    && !hasRunningTurn
    && !isSubAgent
    && thread.status === 'active'
    && hasUnreadCompleted
  const showRelativeTimeStatus = !hasRunningTurn && !showUnreadCompletedDot && !showStatusIcon
  const compactStatusColumn = '24px'
  const relativeTimeStatusColumn = 'minmax(24px, max-content)'
  // On hover the archive action replaces the status content in a compact 24px
  // slot; otherwise the relative-time slot may grow to fit its content.
  const showRelativeTimeSlot = !showArchiveAction && showRelativeTimeStatus
  const usesRelativeTimeColumn = showRelativeTimeSlot
  const statusColumn = usesRelativeTimeColumn ? relativeTimeStatusColumn : compactStatusColumn
  const statusSlotWidth = usesRelativeTimeColumn ? 'max-content' : compactStatusColumn
  const statusSlotMinWidth = compactStatusColumn
  const statusSlotJustifySelf = usesRelativeTimeColumn ? 'end' : 'center'
  // Center the relative time within its (>=24px) slot so it shares the same
  // horizontal center as the spinner / archive / status icons that replace it,
  // instead of hugging the right edge and sitting ~4px off from them.
  const statusContentJustify = 'center'

  const performArchiveThread = useCallback(async (): Promise<void> => {
    // One-click archive: archived threads are restorable anytime from
    // Settings → Archived Threads, so no extra confirmation is needed.
    try {
      await window.api.appServer.sendRequest('thread/archive', { threadId: thread.id })
    } catch {
      // Best-effort
    }
    if (activeThreadId === thread.id) setActiveThreadId(null)
    useThreadStore.getState().removeThreadTree(thread.id)
  }, [activeThreadId, setActiveThreadId, thread.id])

  const resetArchiveActionState = useCallback((): void => {
    setArchiveButtonFocused(false)
  }, [])

  function handleClick(): void {
    if (renaming) return
    setActiveThreadId(thread.id)
    setActiveMainView('conversation')
  }

  function handleContextMenu(e: React.MouseEvent): void {
    e.preventDefault()
    setContextMenu({ x: e.clientX, y: e.clientY })
  }

  function handleTogglePinned(e: React.MouseEvent<HTMLButtonElement>): void {
    e.stopPropagation()
    togglePinnedThread(thread.id)
  }

  function startRename(): void {
    setRenameValue(thread.displayName ?? '')
    setRenaming(true)
    setContextMenu(null)
    // Focus after render
    setTimeout(() => renameInputRef.current?.select(), 0)
  }

  function commitRename(): void {
    const trimmed = renameValue.trim()
    if (trimmed) {
      renameThread(thread.id, trimmed)
      void window.api.appServer
        .sendRequest('thread/rename', { threadId: thread.id, displayName: trimmed })
        .catch((err: unknown) => console.error('thread/rename failed:', err))
    }
    setRenaming(false)
  }

  function cancelRename(): void {
    setRenaming(false)
    setRenameValue(thread.displayName ?? '')
  }

  function handleRenameKeyDown(e: React.KeyboardEvent<HTMLInputElement>): void {
    if (e.key === 'Enter') commitRename()
    if (e.key === 'Escape') cancelRename()
  }
  function isAutomationDrag(e: React.DragEvent): boolean {
    const types = e.dataTransfer?.types
    if (!types) return false
    // DataTransferItemList doesn't implement Array.includes
    for (let i = 0; i < types.length; i++) {
      if (types[i] === AUTOMATION_TASK_DRAG_MIME) return true
    }
    return false
  }

  function handleDragOver(e: React.DragEvent): void {
    if (!isAutomationDrag(e)) return
    // Reject drops onto the already-bound thread (no-op) and onto threads that
    // can't host a bound automation run (archived, paused). This keeps the
    // drop ring from lighting up on non-actionable targets.
    if (alreadyBound || dimmedTarget) {
      e.dataTransfer.dropEffect = 'none'
      if (dropActive) setDropActive(false)
      return
    }
    e.preventDefault()
    e.dataTransfer.dropEffect = 'link'
    if (!dropActive) setDropActive(true)
  }

  function handleDragLeave(e: React.DragEvent): void {
    if (!isAutomationDrag(e)) return
    // Only clear when leaving the row, not when crossing child boundaries.
    const related = e.relatedTarget as Node | null
    if (related && (e.currentTarget as Node).contains(related)) return
    setDropActive(false)
  }

  async function handleDrop(e: React.DragEvent): Promise<void> {
    if (!isAutomationDrag(e)) return
    e.preventDefault()
    setDropActive(false)
    // Safety net: onDragEnd on the source fires slightly after drop in some
    // browsers. Clear the session eagerly so other rows stop dimming.
    useDragDropStore.getState().end()

    if (alreadyBound || dimmedTarget) return

    const raw = e.dataTransfer.getData(AUTOMATION_TASK_DRAG_MIME)
    const title = e.dataTransfer.getData('text/plain')
    const taskId = raw.trim()
    if (!taskId) return
    const state = useAutomationsStore.getState()
    const task = state.tasks.find((t) => t.id === taskId)
    if (!task) {
      addToast(t('auto.dnd.bindFailed', { error: taskId }), 'error')
      setAnim('fail')
      return
    }
    try {
      await state.updateBinding(task, { threadId: thread.id, mode: 'run-in-thread' })
      addToast(
        t('auto.dnd.bindSuccess', { task: title || task.title, thread: displayName }),
        'success'
      )
      setAnim('success')
    } catch (err: unknown) {
      addToast(
        t('auto.dnd.bindFailed', { error: err instanceof Error ? err.message : String(err) }),
        'error'
      )
      setAnim('fail')
    }
  }

  return (
    <>
      <SidebarEntryDetailsCard
        label={displayName}
        width={240}
        content={threadDetails.content}
        onOpen={threadDetails.onOpen}
        disabled={renaming || Boolean(dragKind)}
        wrapperStyle={{ width: '100%' }}
      >
      <ThreadRowLayout
        isSubAgent={isSubAgent}
        subAgentDepth={subAgentDepth}
        canPin={canPin}
        subAgentLabel={t('threadEntry.subAgent')}
        rowTestId={`thread-entry-${thread.id}`}
        gridTestId={`thread-layout-${thread.id}`}
        nameTestId={`thread-title-${thread.id}`}
        badgeTestId={`thread-badge-slot-${thread.id}`}
        statusTestId={`thread-status-slot-${thread.id}`}
        name={displayName}
        nameStyle={{ fontWeight: 'var(--type-ui-weight)' }}
        statusColumn={statusColumn}
        statusSlotWidth={statusSlotWidth}
        statusSlotMinWidth={statusSlotMinWidth}
        statusJustifySelf={statusSlotJustifySelf}
        statusContentJustify={statusContentJustify}
        statusSlotRef={actionSlotRef}
        statusSlotProps={{
          onBlurCapture: (e) => {
            const nextTarget = e.relatedTarget as Node | null
            if (nextTarget && actionSlotRef.current?.contains(nextTarget)) return
            resetArchiveActionState()
          }
        }}
        containerStyle={{
          cursor: dimmedTarget ? 'not-allowed' : 'pointer',
          backgroundColor: dropActive
            ? 'color-mix(in srgb, var(--accent) 14%, transparent)'
            : isActive
              ? 'var(--sidebar-control-active)'
              : hovered && !alreadyBound && !dragKind
                ? 'var(--sidebar-control-hover)'
                : 'transparent',
          // Single-effect drop/target ring replaces the older 3-effect combo
          // (left-border + tinted-bg + dashed-outline). dropActive = hovered
          // valid target; alreadyBound = inset outline marking the existing
          // binding; otherwise we defer to the success pulse keyframe.
          boxShadow: dropActive
            ? '0 0 0 2px color-mix(in srgb, var(--accent) 55%, transparent)'
            : alreadyBound
              ? 'inset 0 0 0 1px color-mix(in srgb, var(--accent) 40%, transparent)'
              : 'none',
          transform: dropActive ? 'scale(1.01)' : 'none',
          opacity: dimmedTarget ? 0.42 : 1,
          filter: dimmedTarget ? 'saturate(0.7)' : 'none',
          pointerEvents: dimmedTarget ? 'none' : 'auto',
          transition:
            'background-color 100ms ease, box-shadow 140ms ease, transform 140ms ease, opacity 140ms ease',
          animation:
            anim === 'success'
              ? 'dropSuccessPulse 700ms ease-out'
              : anim === 'fail'
                ? 'shake 320ms cubic-bezier(0.3, 0.7, 0.4, 1)'
                : undefined
        }}
        containerProps={{
          onClick: handleClick,
          onContextMenu: handleContextMenu,
          onDragOver: handleDragOver,
          onDragLeave: handleDragLeave,
          onDrop: (e) => void handleDrop(e),
          onMouseEnter: () => setHovered(true),
          onMouseLeave: () => {
            setHovered(false)
          }
        }}
        leading={
          <>
            {canPin && (
              <span
                style={{
                  width: '18px',
                  minWidth: '18px',
                  height: '24px',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  flexShrink: 0
                }}
              >
                <ActionTooltip
                  label={isPinned ? t('threadEntry.unpin') : t('threadEntry.pin')}
                  placement="right"
                >
                  <button
                    type="button"
                    aria-label={isPinned ? t('threadEntry.unpin') : t('threadEntry.pin')}
                    aria-pressed={isPinned}
                    data-testid={`thread-pin-${thread.id}`}
                    onClick={handleTogglePinned}
                    onFocus={() => setPinButtonFocused(true)}
                    onBlur={() => setPinButtonFocused(false)}
                    style={{
                      width: '22px',
                      height: '22px',
                      padding: 0,
                      border: 'none',
                      backgroundColor: 'transparent',
                      color: isPinned ? 'var(--text-secondary)' : 'var(--text-dimmed)',
                      display: 'inline-flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      cursor: showPinAction ? 'pointer' : 'default',
                      opacity: showPinAction ? 1 : 0,
                      pointerEvents: showPinAction ? 'auto' : 'none',
                      transition: 'opacity 120ms ease, color 120ms ease'
                    }}
                    onMouseEnter={(e) => {
                      e.currentTarget.style.color = 'var(--text-primary)'
                    }}
                    onMouseLeave={(e) => {
                      e.currentTarget.style.color = isPinned
                        ? 'var(--text-secondary)'
                        : 'var(--text-dimmed)'
                    }}
                  >
                    <PinIcon filled={isPinned} />
                  </button>
                </ActionTooltip>
              </span>
            )}
            {showOriginBadge && (
              <span
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  marginRight: '2px',
                  flexShrink: 0
                }}
              >
                {originPresentation ? (
                  <ChannelIconBadge
                    channelName={thread.originChannel}
                    iconSrc={originPresentation.icon ?? undefined}
                    label={originPresentation.displayName}
                    tooltip={t('threadEntry.originMember', { name: originPresentation.displayName })}
                    muted={!isActive}
                    size={18}
                  />
                ) : thread.originApp ? (
                  <ChannelIconBadge
                    channelName={thread.originChannel}
                    iconSrc={thread.originApp.icon ?? undefined}
                    label={thread.originApp.displayName}
                    tooltip={
                      thread.originApp.memberId
                        ? t('threadEntry.originMember', { name: thread.originApp.displayName })
                        : t('threadEntry.originApp', { app: thread.originApp.displayName })
                    }
                    muted={!isActive}
                    size={18}
                  />
                ) : (
                  <ChannelIconBadge
                    channelName={thread.originChannel}
                    tooltip={t('threadEntry.originChannel', { channel: thread.originChannel })}
                    muted={!isActive}
                    size={18}
                  />
                )}
              </span>
            )}
          </>
        }
        mainOverride={
          renaming ? (
            <input
              ref={renameInputRef}
              value={renameValue}
              onChange={(e) => setRenameValue(e.target.value)}
              onKeyDown={handleRenameKeyDown}
              onBlur={commitRename}
              autoFocus
              style={{
                flex: 1,
                fontSize: 'var(--type-ui-size)',
                lineHeight: 'var(--type-ui-line-height)',
                color: 'var(--text-primary)',
                backgroundColor: 'var(--sidebar-control-active)',
                border: '1px solid var(--border-active)',
                borderRadius: '4px',
                padding: '1px 4px',
                outline: 'none',
                minWidth: 0
              }}
              onClick={(e) => e.stopPropagation()}
            />
          ) : undefined
        }
        badge={
          hasBadgeContent ? (
            dropActive || alreadyBound ? (
              <span
                data-testid={
                  dropActive
                    ? `thread-drop-hint-${thread.id}`
                    : `thread-already-bound-${thread.id}`
                }
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  maxWidth: '150px',
                  minWidth: 0,
                  height: '18px',
                  padding: '2px 8px',
                  borderRadius: '999px',
                  border: '1px solid color-mix(in srgb, var(--accent) 40%, transparent)',
                  backgroundColor: dropActive
                    ? 'color-mix(in srgb, var(--accent) 22%, transparent)'
                    : 'color-mix(in srgb, var(--accent) 10%, transparent)',
                  color: 'var(--accent)',
                  fontSize: 'var(--type-secondary-size)',
                  lineHeight: 'var(--type-secondary-line-height)',
                  fontWeight: 'var(--type-ui-emphasis-weight)',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  flexShrink: 1
                }}
              >
                {dropActive ? t('auto.dnd.dropHere') : t('auto.dnd.alreadyBoundBadge')}
              </span>
            ) : showPendingBadge ? (
              <span
                data-testid={
                  showPendingApprovalBadge
                    ? `thread-pending-approval-${thread.id}`
                    : showPendingUserInputBadge
                      ? `thread-pending-input-${thread.id}`
                      : `thread-pending-confirmation-${thread.id}`
                }
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  maxWidth: '150px',
                  minWidth: 0,
                  height: '18px',
                  padding: '2px 8px',
                  borderRadius: '999px',
                  border: showPendingApprovalBadge || showPendingUserInputBadge
                    ? '1px solid color-mix(in srgb, #d4a33b 45%, transparent)'
                    : '1px solid color-mix(in srgb, var(--accent) 40%, transparent)',
                  backgroundColor: showPendingApprovalBadge || showPendingUserInputBadge
                    ? 'color-mix(in srgb, #d4a33b 18%, transparent)'
                    : 'color-mix(in srgb, var(--accent) 12%, transparent)',
                  color: showPendingApprovalBadge || showPendingUserInputBadge ? '#d4a33b' : 'var(--accent)',
                  fontSize: 'var(--type-secondary-size)',
                  lineHeight: 'var(--type-secondary-line-height)',
                  fontWeight: 'var(--type-ui-emphasis-weight)',
                  whiteSpace: 'nowrap',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  flexShrink: 1
                }}
              >
                {showPendingApprovalBadge
                  ? t('threadEntry.pendingApproval')
                  : showPendingUserInputBadge
                    ? t('threadEntry.pendingUserInput')
                    : t('threadEntry.pendingPlanConfirmation')}
              </span>
            ) : (
              <span
                aria-hidden="true"
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: 'var(--type-ui-size)',
                  lineHeight: 'var(--type-ui-line-height)',
                  color: 'var(--accent)',
                  animation: 'slideInBadge 450ms ease-out'
                }}
              >
                💬
              </span>
            )
          ) : undefined
        }
        status={
          <span
            aria-hidden={showArchiveAction}
            style={{
              fontSize: 'var(--type-secondary-size)',
              color: 'var(--text-dimmed)',
              lineHeight: 'var(--type-secondary-line-height)',
              whiteSpace: 'nowrap',
              display: showArchiveAction ? 'none' : 'inline-flex',
              alignItems: 'center',
              justifyContent: statusContentJustify,
              width: '100%',
              overflow: 'hidden',
              textOverflow: 'clip',
              opacity: showArchiveAction ? 0 : 1
            }}
          >
            {hasRunningTurn ? (
              <RunningSpinner
                label={t('threadEntry.turnRunning')}
                testId={`thread-running-indicator-${thread.id}`}
              />
            ) : showUnreadCompletedDot ? (
              <ActionTooltip label={t('threadEntry.unreadCompleted')}>
                <span
                  aria-label={t('threadEntry.unreadCompleted')}
                  data-testid={`thread-unread-completed-${thread.id}`}
                  style={{
                    width: '6px',
                    height: '6px',
                    borderRadius: '999px',
                    backgroundColor: 'var(--success)',
                    display: 'inline-block'
                  }}
                />
              </ActionTooltip>
            ) : showStatusIcon ? (
              <ActionTooltip label={thread.status}>
                <span
                  style={{ fontSize: '10px', color: 'var(--text-dimmed)', flexShrink: 0 }}
                  aria-label={thread.status}
                >
                  {thread.status === 'paused' ? '⏸' : '🗄'}
                </span>
              </ActionTooltip>
            ) : (
              relativeTime
            )}
          </span>
        }
        statusExtra={
          !isSubAgent ? (
            <ActionTooltip label={t('threadEntry.archive')} placement="right">
              <button
                className="dotcraft-sidebar-control-radius"
                type="button"
                aria-label={t('threadEntry.archive')}
                onClick={(e) => {
                  e.stopPropagation()
                  void performArchiveThread()
                }}
                onFocus={() => setArchiveButtonFocused(true)}
                style={{
                  width: '24px',
                  height: '24px',
                  padding: 0,
                  border: 'none',
                  borderRadius: 'var(--sidebar-icon-control-radius)',
                  backgroundColor: 'transparent',
                  color: isActive ? 'var(--text-secondary)' : 'var(--text-dimmed)',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  cursor: showArchiveAction ? 'pointer' : 'default',
                  position: 'absolute',
                  right: 0,
                  top: '50%',
                  transform: 'translateY(-50%)',
                  opacity: showArchiveAction ? 1 : 0,
                  pointerEvents: showArchiveAction ? 'auto' : 'none',
                  transition: 'opacity 120ms ease, background-color 120ms ease, color 120ms ease',
                  zIndex: 2
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.backgroundColor = 'var(--sidebar-control-hover)'
                  e.currentTarget.style.color = 'var(--error)'
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.backgroundColor = 'transparent'
                  e.currentTarget.style.color = isActive
                    ? 'var(--text-secondary)'
                    : 'var(--text-dimmed)'
                }}
              >
                <Archive size={14} strokeWidth={2} aria-hidden="true" />
              </button>
            </ActionTooltip>
          ) : undefined
        }
      />
      </SidebarEntryDetailsCard>

      {contextMenu && (
        <ThreadEntryContextMenu
          position={contextMenu}
          onClose={() => setContextMenu(null)}
          onRename={startRename}
          onArchive={performArchiveThread}
          thread={thread}
          allowLifecycleActions={!isSubAgent}
        />
      )}
    </>
  )
}

export function PinIcon({ filled }: { filled: boolean }): JSX.Element {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 16 16"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      aria-hidden="true"
    >
      <path
        d="M5.2 1.9h5.6l-.8 4 2.1 2.1v1.2H8.7l-.5 4.5H6.8l-.5-4.5H2.9V8l2.1-2.1-.8-4Z"
        fill={filled ? 'currentColor' : 'none'}
        stroke="currentColor"
        strokeWidth="1.35"
        strokeLinejoin="round"
      />
    </svg>
  )
}

interface ThreadEntryContextMenuProps {
  position: ContextMenuPosition
  onClose: () => void
  onRename: () => void
  onArchive: () => Promise<void>
  thread: ThreadSummary
  allowLifecycleActions: boolean
}

function ThreadEntryContextMenu({
  position,
  onClose,
  onRename,
  onArchive,
  thread,
  allowLifecycleActions
}: ThreadEntryContextMenuProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const capabilities = useConnectionStore((s) => s.capabilities)
  const canFork = canForkThread(capabilities)
  const canForkIntoWorktree = canForkWorktree(capabilities)
  const {
    removeThreadTree,
    activeThreadId,
    setActiveThreadId,
    pinnedThreadIds,
    togglePinnedThread
  } = useThreadStore()
  const threadId = thread.id
  const pinned = pinnedThreadIds.includes(threadId)

  async function handleDelete(): Promise<void> {
    onClose()
    const ok = await confirm({
      title: t('threadEntry.deleteTitle'),
      message: t('threadEntry.deleteMessage'),
      confirmLabel: t('threadEntry.delete'),
      danger: true
    })
    if (!ok) return
    try {
      await window.api.appServer.sendRequest('thread/delete', { threadId })
      if (activeThreadId === threadId) setActiveThreadId(null)
      removeThreadTree(threadId)
    } catch {
      // Keep local state unchanged when the backend delete fails.
    }
  }

  function handleTogglePinned(): void {
    togglePinnedThread(threadId)
  }

  function handleFork(mode: 'local' | 'worktree'): void {
    void runThreadFork({
      threadId,
      mode,
      t
    })
  }

  return (
    <ContextMenu
      position={position}
      onClose={onClose}
      items={[
        ...(allowLifecycleActions
          ? [
              {
                label: pinned ? t('threadEntry.unpin') : t('threadEntry.pin'),
                icon: <Pin size={14} aria-hidden />,
                onClick: handleTogglePinned
              }
            ]
          : []),
        {
          label: t('threadEntry.rename'),
          icon: <Pencil size={14} aria-hidden />,
          onClick: onRename
        },
        ...(allowLifecycleActions
          ? [
              {
                label: t('threadEntry.archive'),
                icon: <Archive size={14} aria-hidden />,
                onClick: async () => {
                  onClose()
                  await onArchive()
                }
              },
              ...(canFork
                ? [
                    {
                      type: 'separator' as const
                    },
                    {
                      label: t('fork.intoLocal'),
                      icon: <Laptop size={14} aria-hidden />,
                      onClick: () => handleFork('local')
                    },
                    ...(canForkIntoWorktree
                      ? [
                          {
                            label: t('fork.intoWorktree'),
                            icon: <ArrowRightLeft size={14} aria-hidden />,
                            onClick: () => handleFork('worktree')
                          }
                        ]
                      : [])
                  ]
                : []),
              { type: 'separator' as const },
              {
                label: t('threadEntry.delete'),
                icon: <Trash2 size={14} aria-hidden />,
                onClick: handleDelete,
                danger: true
              }
            ]
          : [])
      ]}
    />
  )
}
