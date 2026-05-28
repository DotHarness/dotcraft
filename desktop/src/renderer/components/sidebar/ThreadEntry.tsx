import { useState, useRef, useCallback, useEffect } from 'react'
import type { ThreadSummary } from '../../types/thread'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import type { ContextMenuPosition } from '../ui/ContextMenu'
import { ContextMenu } from '../ui/ContextMenu'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ChannelIconBadge } from '../ui/channelMeta'
import { Archive, CornerDownRight } from 'lucide-react'
import { AUTOMATION_TASK_DRAG_MIME } from '../automations/TaskCard'
import { useAutomationsStore } from '../../stores/automationsStore'
import { useDragDropStore } from '../../stores/dragDropStore'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { getSubAgentDepth, isSubAgentThread } from '../../utils/subAgentThreads'

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
  const [archiveConfirming, setArchiveConfirming] = useState(false)
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
  const showOriginBadge =
    !isSubAgent &&
    thread.originChannel.length > 0 &&
    thread.originChannel.toLowerCase() !== 'dotcraft-desktop'
  // Hide the archive action during a drag session so the right side stays
  // clean while the drop-hint / already-bound pill is shown.
  const canPin = !isSubAgent && thread.status !== 'archived'
  const isPinned = canPin && pinnedThreadIds.includes(thread.id)
  const showPinAction =
    canPin && !renaming && !dragKind && (hovered || pinButtonFocused || isPinned)
  const showArchiveAction =
    !isSubAgent && !renaming && !dragKind && (hovered || archiveButtonFocused)
  const showArchiveConfirm = showArchiveAction && archiveConfirming
  const confirm = useConfirmDialog()
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
  const rowPaddingLeft = canPin ? 6 : 14 + subAgentDepth * 14
  const showRelativeTimeStatus = !hasRunningTurn && !showUnreadCompletedDot && !showStatusIcon
  const compactStatusColumn = '24px'
  const relativeTimeStatusColumn = 'minmax(24px, max-content)'
  const confirmStatusColumn = 'minmax(64px, max-content)'
  const showRelativeTimeSlot = !showArchiveAction && showRelativeTimeStatus
  const usesRelativeTimeColumn = showRelativeTimeSlot
  const statusColumn = showArchiveConfirm
    ? confirmStatusColumn
    : usesRelativeTimeColumn
      ? relativeTimeStatusColumn
      : compactStatusColumn
  const layoutColumns = hasBadgeContent
    ? `minmax(0, 1fr) minmax(74px, max-content) ${statusColumn}`
    : `minmax(0, 1fr) ${statusColumn}`
  const statusGridColumn = hasBadgeContent ? '3' : '2'
  const statusSlotWidth = showArchiveConfirm
    ? '100%'
    : usesRelativeTimeColumn
      ? 'max-content'
      : compactStatusColumn
  const statusSlotMinWidth = showArchiveConfirm
    ? '64px'
    : usesRelativeTimeColumn
      ? compactStatusColumn
      : compactStatusColumn
  const statusSlotJustifySelf = showArchiveConfirm ? 'stretch' : usesRelativeTimeColumn ? 'end' : 'center'
  const statusContentJustify = showRelativeTimeSlot ? 'flex-end' : 'center'

  const performArchiveThread = useCallback(async (): Promise<void> => {
    try {
      await window.api.appServer.sendRequest('thread/archive', { threadId: thread.id })
    } catch {
      // Best-effort
    }
    if (activeThreadId === thread.id) setActiveThreadId(null)
    useThreadStore.getState().removeThreadTree(thread.id)
  }, [activeThreadId, confirm, setActiveThreadId, t, thread.id])

  const archiveThreadWithDialog = useCallback(async (): Promise<void> => {
    const ok = await confirm({
      title: t('threadEntry.archiveTitle'),
      message: t('threadEntry.archiveMessage'),
      confirmLabel: t('threadEntry.archiveConfirm')
    })
    if (!ok) return
    await performArchiveThread()
  }, [confirm, performArchiveThread, t])

  const beginInlineArchiveConfirm = useCallback((): void => {
    setArchiveConfirming(true)
  }, [])

  const resetArchiveActionState = useCallback((): void => {
    setArchiveButtonFocused(false)
    setArchiveConfirming(false)
  }, [])

  function handleClick(): void {
    if (renaming) return
    setActiveThreadId(thread.id)
    setActiveMainView('conversation')
  }

  function handleContextMenu(e: React.MouseEvent): void {
    e.preventDefault()
    setArchiveConfirming(false)
    setContextMenu({ x: e.clientX, y: e.clientY })
  }

  function handleTogglePinned(e: React.MouseEvent<HTMLButtonElement>): void {
    e.stopPropagation()
    setArchiveConfirming(false)
    togglePinnedThread(thread.id)
  }

  function startRename(): void {
    setRenameValue(thread.displayName ?? '')
    setRenaming(true)
    setArchiveConfirming(false)
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

  const rowTooltipLabel = dimmedTarget
    ? t('auto.dnd.archivedCannotBind')
    : thread.displayName ?? displayName

  return (
    <>
      <ActionTooltip label={rowTooltipLabel} wrapperStyle={{ display: 'block', width: '100%' }}>
      <div
        className="dotcraft-sidebar-control-radius"
        onClick={handleClick}
        onContextMenu={handleContextMenu}
        onDragOver={handleDragOver}
        onDragLeave={handleDragLeave}
        onDrop={(e) => void handleDrop(e)}
        data-testid={`thread-entry-${thread.id}`}
        style={{
          display: 'flex',
          alignItems: 'center',
          position: 'relative',
          width: 'calc(100% - 20px)',
          margin: '2px 10px',
          padding: `6px 12px 6px ${rowPaddingLeft}px`,
          cursor: dimmedTarget ? 'not-allowed' : 'pointer',
          boxSizing: 'border-box',
          borderRadius: 'var(--sidebar-control-radius)',
          backgroundColor: dropActive
            ? 'color-mix(in srgb, var(--accent) 14%, transparent)'
            : isActive
              ? 'var(--sidebar-control-active)'
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
          gap: '6px',
          userSelect: 'none',
          transition:
            'background-color 100ms ease, box-shadow 140ms ease, transform 140ms ease, opacity 140ms ease',
          animation:
            anim === 'success'
              ? 'dropSuccessPulse 700ms ease-out'
              : anim === 'fail'
                ? 'shake 320ms cubic-bezier(0.3, 0.7, 0.4, 1)'
                : undefined
        }}
        onMouseEnter={(e) => {
          setHovered(true)
          if (!isActive && !dropActive && !alreadyBound && !dragKind) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--sidebar-control-hover)'
          }
        }}
        onMouseLeave={(e) => {
          setHovered(false)
          setArchiveConfirming(false)
          if (!isActive && !dropActive && !alreadyBound && !dragKind) {
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
          }
        }}
      >
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
                  transition:
                    'opacity 120ms ease, color 120ms ease'
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
        {isSubAgent && (
          <span
            title={t('threadEntry.subAgent')}
            style={{
              width: '16px',
              minWidth: '16px',
              display: 'inline-flex',
              alignItems: 'center',
              justifyContent: 'center',
              color: 'var(--text-dimmed)',
              flexShrink: 0
            }}
            aria-label={t('threadEntry.subAgent')}
          >
            <CornerDownRight size={12} strokeWidth={2} aria-hidden="true" />
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
            <ChannelIconBadge
              channelName={thread.originChannel}
              tooltip={t('threadEntry.originChannel', { channel: thread.originChannel })}
              muted={!isActive}
              size={18}
            />
          </span>
        )}

        {renaming ? (
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
        ) : (
          <div
            data-testid={`thread-layout-${thread.id}`}
            style={{
              flex: 1,
              minWidth: 0,
              display: 'grid',
              gridTemplateColumns: layoutColumns,
              columnGap: '7px',
              alignItems: 'center'
            }}
          >
            <span
              data-testid={`thread-title-${thread.id}`}
              style={{
                fontSize: 'var(--type-ui-size)',
                lineHeight: 'var(--type-ui-line-height)',
                fontWeight: 'var(--type-ui-weight)',
                color: 'var(--text-primary)',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                minWidth: 0
              }}
            >
              {displayName}
            </span>
            {hasBadgeContent && (
              <span
                data-testid={`thread-badge-slot-${thread.id}`}
                style={{
                  minWidth: 0,
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'flex-end',
                  justifySelf: 'stretch'
                }}
              >
                {(dropActive || alreadyBound) ? (
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
                      border:
                        '1px solid color-mix(in srgb, var(--accent) 40%, transparent)',
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
                    {dropActive
                      ? t('auto.dnd.dropHere')
                      : t('auto.dnd.alreadyBoundBadge')}
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
                )}
              </span>
            )}
            <div
              ref={actionSlotRef}
              data-testid={`thread-status-slot-${thread.id}`}
              style={{
                gridColumn: statusGridColumn,
                width: statusSlotWidth,
                minWidth: statusSlotMinWidth,
                justifySelf: statusSlotJustifySelf,
                height: '24px',
                position: 'relative',
                display: 'flex',
                alignItems: 'center',
                justifyContent: statusContentJustify
              }}
              onBlurCapture={(e) => {
                const nextTarget = e.relatedTarget as Node | null
                if (nextTarget && actionSlotRef.current?.contains(nextTarget)) return
                resetArchiveActionState()
              }}
            >
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
                    title={t('threadEntry.turnRunning')}
                    testId={`thread-running-indicator-${thread.id}`}
                  />
                ) : showUnreadCompletedDot ? (
                  <span
                    aria-label={t('threadEntry.unreadCompleted')}
                    title={t('threadEntry.unreadCompleted')}
                    data-testid={`thread-unread-completed-${thread.id}`}
                    style={{
                      width: '6px',
                      height: '6px',
                      borderRadius: '999px',
                      backgroundColor: 'var(--success)',
                      display: 'inline-block'
                    }}
                  />
                ) : showStatusIcon ? (
                  <span
                    title={thread.status}
                    style={{ fontSize: '10px', color: 'var(--text-dimmed)', flexShrink: 0 }}
                    aria-label={thread.status}
                  >
                    {thread.status === 'paused' ? '⏸' : '🗄'}
                  </span>
                ) : (
                  relativeTime
                )}
              </span>
              {!isSubAgent && (
                <>
                  <ActionTooltip label={t('threadEntry.archive')} placement="right">
                    <button
                      className="dotcraft-sidebar-control-radius"
                      type="button"
                      aria-label={t('threadEntry.archive')}
                      onClick={(e) => {
                        e.stopPropagation()
                        beginInlineArchiveConfirm()
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
                        cursor: showArchiveAction && !showArchiveConfirm ? 'pointer' : 'default',
                        position: 'absolute',
                        right: 0,
                        top: '50%',
                        transform: 'translateY(-50%)',
                        opacity: showArchiveAction && !showArchiveConfirm ? 1 : 0,
                        pointerEvents: showArchiveAction && !showArchiveConfirm ? 'auto' : 'none',
                        transition: 'opacity 120ms ease, background-color 120ms ease, color 120ms ease',
                        zIndex: 2
                      }}
                      onMouseEnter={(e) => {
                        e.currentTarget.style.backgroundColor = 'var(--bg-secondary)'
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
                  <ActionTooltip label={t('threadEntry.archiveConfirm')} placement="right">
                    <button
                      type="button"
                      tabIndex={showArchiveConfirm ? 0 : -1}
                      aria-label={t('threadEntry.archiveConfirm')}
                      onClick={(e) => {
                        e.stopPropagation()
                        void performArchiveThread()
                      }}
                      onFocus={() => setArchiveButtonFocused(true)}
                      style={{
                        height: '24px',
                        padding: '0 8px',
                        minWidth: '64px',
                        border: '1px solid color-mix(in srgb, var(--error) 42%, transparent)',
                        borderRadius: '999px',
                        backgroundColor: 'color-mix(in srgb, var(--error) 12%, transparent)',
                        color: 'var(--error)',
                        fontSize: 'var(--type-secondary-size)',
                        lineHeight: 'var(--type-secondary-line-height)',
                        fontWeight: 'var(--type-ui-emphasis-weight)',
                        display: 'inline-flex',
                        alignItems: 'center',
                        justifyContent: 'center',
                        cursor: showArchiveConfirm ? 'pointer' : 'default',
                        position: 'absolute',
                        right: 0,
                        top: '50%',
                        transform: 'translateY(-50%)',
                        opacity: showArchiveConfirm ? 1 : 0,
                        pointerEvents: showArchiveConfirm ? 'auto' : 'none',
                        transition: 'opacity 120ms ease, background-color 120ms ease',
                        zIndex: 2
                      }}
                      onMouseEnter={(e) => {
                        e.currentTarget.style.backgroundColor =
                          'color-mix(in srgb, var(--error) 18%, transparent)'
                      }}
                      onMouseLeave={(e) => {
                        e.currentTarget.style.backgroundColor =
                          'color-mix(in srgb, var(--error) 12%, transparent)'
                      }}
                    >
                      {t('threadEntry.archiveConfirm')}
                    </button>
                  </ActionTooltip>
                </>
              )}
            </div>
          </div>
        )}
      </div>
      </ActionTooltip>

      {contextMenu && (
        <ThreadEntryContextMenu
          position={contextMenu}
          onClose={() => setContextMenu(null)}
          onRename={startRename}
          onArchive={archiveThreadWithDialog}
          threadId={thread.id}
          allowLifecycleActions={!isSubAgent}
        />
      )}
    </>
  )
}

function PinIcon({ filled }: { filled: boolean }): JSX.Element {
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
  threadId: string
  allowLifecycleActions: boolean
}

function ThreadEntryContextMenu({
  position,
  onClose,
  onRename,
  onArchive,
  threadId,
  allowLifecycleActions
}: ThreadEntryContextMenuProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const { removeThreadTree, activeThreadId, setActiveThreadId } = useThreadStore()

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

  return (
    <ContextMenu
      position={position}
      onClose={onClose}
      items={[
        { label: t('threadEntry.rename'), onClick: onRename },
        ...(allowLifecycleActions
          ? [
              {
                label: t('threadEntry.archive'),
                onClick: async () => {
                  onClose()
                  await onArchive()
                }
              },
              { label: t('threadEntry.delete'), onClick: handleDelete, danger: true }
            ]
          : [])
      ]}
    />
  )
}
