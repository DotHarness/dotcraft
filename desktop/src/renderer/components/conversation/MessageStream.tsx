import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useConversationStore, type StreamRetrySignal } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { useSubAgentStore } from '../../stores/subAgentStore'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { useAutoScroll } from '../../hooks/useAutoScroll'
import { UserMessageBlock } from './UserMessageBlock'
import { AgentResponseBlock, type HistoricalToolContentMode } from './AgentResponseBlock'
import { ScrollToBottomButton } from './ScrollToBottomButton'
import { ConversationColumn } from './ConversationColumn'
import { wireTurnToConversationTurn } from '../../types/conversation'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import type { ContextUsageSnapshotWire, Thread, ThreadGoal } from '../../types/thread'
import { isAcceptPlanSentinel } from '../../utils/planAcceptSentinel'
import { getSpawnedFromThreadId } from '../../utils/subAgentThreads'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { estimateBackgroundActivityDockHeightPx } from './backgroundActivityDockLayout'

/** Module-level scroll position cache — ephemeral, not persisted to storage. */
const scrollPositionCache = new Map<string, number>()

const NEAR_BOTTOM_THRESHOLD = 50
const SCROLL_BUTTON_BASE_BOTTOM_PX = 10
const SCROLL_BUTTON_DOCK_GAP_PX = 10
/** Resting gap reserved below the last message so it never sits flush against the
 *  composer (and clears the dock's top edge when a dock is present). */
const MESSAGE_STREAM_BOTTOM_BASE_PX = 40
const FULL_HISTORY_TURN_COUNT = 3

interface InlineEditState {
  threadId: string
  turnId: string
  itemId: string
  draftText: string
  submitting: boolean
  rollbackPending: boolean
}

interface RollbackThreadResult {
  thread?: {
    turns?: Array<Record<string, unknown>>
    contextUsage?: ContextUsageSnapshotWire | null
    [key: string]: unknown
  }
}

function isTextOnlyEditableUserMessage(item: ConversationItem): boolean {
  if (item.type !== 'userMessage') return false
  if ((item.images?.length ?? 0) > 0 || (item.imageDataUrls?.length ?? 0) > 0) return false
  if (!item.nativeInputParts || item.nativeInputParts.length === 0) return true
  return item.nativeInputParts.every((part) => part.type === 'text')
}

function editableUserText(item: ConversationItem): string {
  if (item.nativeInputParts && item.nativeInputParts.length > 0) {
    return item.nativeInputParts
      .filter((part) => part.type === 'text')
      .map((part) => part.text)
      .join('')
  }
  return item.text ?? ''
}

function lastUserItem(turn: ConversationTurn): ConversationItem | undefined {
  return [...turn.items].reverse().find(isVisibleUserMessage)
}

function getSentAsGoalItemId(turns: ConversationTurn[], goal: ThreadGoal | null): string | null {
  const objective = goal?.objective.trim()
  if (!goal || !objective) return null

  for (const turn of turns) {
    const firstUser = turn.items.find((item) =>
      isVisibleUserMessage(item) &&
      !item.triggerKind
    )
    if (!firstUser) continue
    if ((firstUser.text ?? '').trim() !== objective) return null
    if (!isNearGoalCreation(firstUser.createdAt, goal.createdAt)) return null
    return firstUser.id
  }

  return null
}

function isVisibleUserMessage(item: ConversationItem): boolean {
  return (
    item.type === 'userMessage' &&
    item.deliveryMode !== 'guidance' &&
    item.deliveryMode !== 'subagentMailbox' &&
    item.triggerKind !== 'subagentMailbox' &&
    !isAcceptPlanSentinel(item.text ?? '')
  )
}

function isNearGoalCreation(messageCreatedAt: string | undefined, goalCreatedAt: string): boolean {
  if (!messageCreatedAt) return true
  const messageMs = Date.parse(messageCreatedAt)
  const goalMs = Date.parse(goalCreatedAt)
  if (!Number.isFinite(messageMs) || !Number.isFinite(goalMs)) return true
  const deltaMs = messageMs - goalMs
  return deltaMs >= -30_000 && deltaMs <= 5 * 60_000
}

/**
 * Scrollable container that renders the full turn history and live streaming content.
 * Spec §10.3.3. Handles scroll position restoration.
 */
export function MessageStream(): JSX.Element {
  const t = useT()
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const streamingMessage = useConversationStore((s) => s.streamingMessage)
  const streamingMessageLastDeltaAt = useConversationStore((s) => s.streamingMessageLastDeltaAt)
  const streamingReasoning = useConversationStore((s) => s.streamingReasoning)
  const systemLabel = useConversationStore((s) => s.systemLabel)
  const backgroundMemoryStatus = useConversationStore((s) => s.backgroundMemoryStatus)
  const streamRetrySignals = useConversationStore((s) => s.streamRetrySignals)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const activeThread = useThreadStore((s) => s.activeThread)
  const threadList = useThreadStore((s) => s.threadList)
  // Origin of a thread spawned by another thread (Desktop CreateThread). Drives the
  // "From another thread" pill on the first user message; null for normal threads.
  const threadOrigin = useMemo(() => {
    const self = threadList.find((t) => t.id === activeThreadId)
    const parentId = self ? getSpawnedFromThreadId(self) : null
    if (!parentId) return null
    const parent = threadList.find((t) => t.id === parentId)
    return { refId: parentId, label: parent?.displayName?.trim() || undefined }
  }, [threadList, activeThreadId])
  const currentGoal = useThreadStore((s) =>
    activeThreadId
      ? s.goalSnapshots.get(activeThreadId)
        ?? (s.activeThread?.id === activeThreadId ? (s.activeThread.goal ?? null) : null)
      : null
  )
  const queuedInputCount = useConversationStore((s) => s.queuedInputs.length)
  const subAgentChildCount = useSubAgentStore((s) =>
    activeThreadId ? (s.childrenByParent.get(activeThreadId)?.length ?? 0) : 0
  )
  const subAgentCollapsed = useSubAgentStore((s) =>
    activeThreadId ? s.collapsedByParent.get(activeThreadId) === true : false
  )
  const [editing, setEditing] = useState<InlineEditState | null>(null)
  const prevThreadIdRef = useRef<string | null>(null)
  const effectiveSystemLabel = systemLabel
    ?? (backgroundMemoryStatus === 'consolidating' ? 'systemStatus.consolidating' : null)

  // Use total character count + turn count as a proxy for content size changes
  const contentLength = turns.reduce((acc, t) => acc + t.items.length, 0) +
    streamingMessage.length +
    (showThinkingContent ? streamingReasoning.length : 0) +
    streamRetrySignals.reduce((acc, signal) => acc + signal.rawMessage.length, 0) +
    (turnStatus === 'running' && activeTurnId ? activeTurnId.length : 0) +
    (turnStatus === 'running' ? (streamingMessageLastDeltaAt ?? 0) : 0) +
    (effectiveSystemLabel?.length ?? 0)

  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)
  // The background-activity dock floats up from the composer's top edge, over the
  // bottom of the scroll region. Reserve its height (plus the resting gap) below
  // the last message so the composer/dock never covers it at the scroll bottom,
  // and lift the scroll-to-bottom button by the same dock height.
  const dockHeightPx = estimateBackgroundActivityDockHeightPx({
    queuedInputCount,
    subAgentChildCount,
    subAgentCollapsed
  })
  const scrollButtonBottomOffsetPx =
    SCROLL_BUTTON_BASE_BOTTOM_PX + (dockHeightPx > 0 ? dockHeightPx + SCROLL_BUTTON_DOCK_GAP_PX : 0)
  const bottomClearancePx = MESSAGE_STREAM_BOTTOM_BASE_PX + dockHeightPx
  const sentAsGoalItemId = getSentAsGoalItemId(turns, currentGoal)

  useEffect(() => {
    setEditing(null)
  }, [activeThreadId])

  const submitInlineEdit = useCallback(async (): Promise<void> => {
    const current = editing
    if (!current || current.submitting) return
    const draftText = current.draftText.trim()
    if (!draftText) return

    setEditing({ ...current, submitting: true })
    let rollbackPending = current.rollbackPending
    try {
      if (rollbackPending) {
        const state = useConversationStore.getState()
        const latestTurn = state.turns[state.turns.length - 1]
        const latestUser = latestTurn ? lastUserItem(latestTurn) : undefined
        if (!latestTurn || latestTurn.id !== current.turnId || latestUser?.id !== current.itemId) {
          setEditing(null)
          addToast(t('conversation.editStale'), 'warning')
          return
        }

        const rollbackResult = await window.api.appServer.sendRequest('thread/rollback', {
          threadId: current.threadId,
          numTurns: 1
        }) as RollbackThreadResult
        rollbackPending = false
        if (rollbackResult.thread) {
          useConversationStore.getState().setTurns((rollbackResult.thread.turns ?? []).map(wireTurnToConversationTurn))
          useConversationStore.getState().setContextUsage(rollbackResult.thread.contextUsage ?? null)
          useThreadStore.getState().setActiveThread(rollbackResult.thread as Thread)
        }
      }

      await startTurnWithOptimisticUI({
        threadId: current.threadId,
        workspacePath: workspacePath || activeThread?.workspacePath || '',
        identityWorkspacePath: activeThread?.workspacePath || workspacePath || '',
        text: draftText,
        fallbackThreadName: t('toast.imageMessage'),
        fileFallbackThreadName: t('toast.fileReferenceMessage'),
        attachmentFallbackThreadName: t('toast.attachmentMessage'),
        renameThreadFromText: false,
        throwOnStartError: true
      })
      setEditing(null)
    } catch (err) {
      console.error('inline edit retry failed:', err)
      setEditing((prev) =>
        prev && prev.turnId === current.turnId && prev.itemId === current.itemId
          ? { ...prev, submitting: false, rollbackPending }
          : prev
      )
      addToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }, [activeThread?.workspacePath, editing, t, workspacePath])

  // Save scroll position on thread switch; restore on switch-in
  useEffect(() => {
    const el = scrollRef.current
    const prev = prevThreadIdRef.current
    const curr = activeThreadId

    if (prev && prev !== curr && el) {
      // Save the departing thread's scroll position
      scrollPositionCache.set(prev, el.scrollTop)
    }

    if (curr && curr !== prev && el) {
      // Restore the arriving thread's scroll position (after content renders)
      requestAnimationFrame(() => {
        const saved = scrollPositionCache.get(curr)
        if (saved === undefined) {
          // Never visited: scroll to bottom
          el.scrollTop = el.scrollHeight
        } else {
          const atBottom = el.scrollHeight - saved - el.clientHeight <= NEAR_BOTTOM_THRESHOLD
          el.scrollTop = atBottom ? el.scrollHeight : saved
        }
      })
    }

    prevThreadIdRef.current = curr
  }, [activeThreadId, scrollRef])

  return (
    <div style={{ position: 'relative', flex: 1, overflow: 'hidden' }}>
      <div
        ref={scrollRef}
        data-testid="message-stream"
        aria-live="polite"
        aria-atomic="false"
        aria-label="Conversation messages"
        role="log"
        style={{
          height: '100%',
          overflowY: 'auto',
          padding: `32px clamp(20px, 4vw, 40px) ${bottomClearancePx}px`,
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--conversation-block-gap)'
        }}
      >
        <ConversationColumn
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--conversation-block-gap)'
          }}
        >
          {turns.map((turn, idx) => (
            <TurnBlock
              key={turn.id}
              turn={turn}
              historicalToolContentMode={getHistoricalToolContentMode({
                turn,
                index: idx,
                totalTurns: turns.length,
                activeTurnId
              })}
              streamingMessage={turn.id === activeTurnId ? streamingMessage : ''}
              streamingMessageLastDeltaAt={
                turn.id === activeTurnId ? streamingMessageLastDeltaAt : null
              }
              streamingReasoning={turn.id === activeTurnId ? streamingReasoning : ''}
              streamRetrySignals={
                turn.id === activeTurnId
                  ? streamRetrySignals.filter((signal) => signal.turnId === turn.id)
                  : []
              }
              isRunning={
                (turnStatus === 'running' || turnStatus === 'waitingInput' || turnStatus === 'waitingApproval') &&
                turn.id === activeTurnId
              }
              showIdleThinkingFallback={
                turnStatus === 'running' &&
                turn.id === activeTurnId &&
                !effectiveSystemLabel
              }
              isActiveTurn={turn.id === activeTurnId}
              isLastTurn={idx === turns.length - 1}
              isFirstTurn={idx === 0}
              threadOrigin={threadOrigin}
              isIdle={turnStatus === 'idle'}
              editing={editing}
              onStartEdit={(item) => {
                setEditing({
                  threadId: turn.threadId,
                  turnId: turn.id,
                  itemId: item.id,
                  draftText: editableUserText(item),
                  submitting: false,
                  rollbackPending: true
                })
              }}
              onDraftChange={(draftText) => {
                setEditing((prev) => prev ? { ...prev, draftText } : prev)
              }}
              sentAsGoalItemId={sentAsGoalItemId}
              onCancelEdit={() => {
                setEditing(null)
              }}
              onSubmitEdit={() => {
                void submitInlineEdit()
              }}
            />
          ))}

          {editing && !turns.some((turn) =>
            turn.id === editing.turnId && turn.items.some((item) => item.id === editing.itemId)
          ) && (
            <UserMessageBlock
              text={editing.draftText}
              editing
              editText={editing.draftText}
              editSubmitting={editing.submitting}
              editSubmitDisabled={
                turnStatus !== 'idle' || editing.submitting || editing.draftText.trim().length === 0
              }
              onEditTextChange={(draftText) => {
                setEditing((prev) => prev ? { ...prev, draftText } : prev)
              }}
              onCancelEdit={() => {
                setEditing(null)
              }}
              onSubmitEdit={() => {
                void submitInlineEdit()
              }}
            />
          )}

          {effectiveSystemLabel && <SystemStatusDivider labelKey={effectiveSystemLabel} />}

          {/* Bottom anchor for auto-scroll */}
          <div />
        </ConversationColumn>
      </div>

      {showScrollButton && (
        <ScrollToBottomButton
          onClick={scrollToBottom}
          bottomOffsetPx={scrollButtonBottomOffsetPx}
        />
      )}
    </div>
  )
}

function SystemStatusDivider({ labelKey }: { labelKey: string }): JSX.Element {
  const t = useT()
  const label = t(labelKey)

  return (
    <div
      role="status"
      aria-live="polite"
      aria-label={label}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 8,
        padding: '14px 4px',
        color: 'var(--text-secondary, #8a8a8a)',
        fontSize: 11,
        lineHeight: 1.4,
        userSelect: 'none'
      }}
    >
      <span
        aria-hidden
        style={{
          flex: 1,
          height: 1,
          background: 'var(--border-color, rgba(127,127,127,0.25))'
        }}
      />
      <span
        className="tool-running-gradient-text"
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: 6,
          fontWeight: 600,
          whiteSpace: 'nowrap'
        }}
      >
        {label}
      </span>
      <span
        aria-hidden
        style={{
          flex: 1,
          height: 1,
          background: 'var(--border-color, rgba(127,127,127,0.25))'
        }}
      />
    </div>
  )
}

// ---------------------------------------------------------------------------
// Single turn renderer
// ---------------------------------------------------------------------------

interface TurnBlockProps {
  turn: ConversationTurn
  historicalToolContentMode: HistoricalToolContentMode
  streamingMessage: string
  streamingMessageLastDeltaAt: number | null
  streamingReasoning: string
  streamRetrySignals: StreamRetrySignal[]
  isRunning: boolean
  showIdleThinkingFallback: boolean
  isActiveTurn: boolean
  isLastTurn: boolean
  isFirstTurn: boolean
  threadOrigin: { refId: string; label?: string } | null
  isIdle: boolean
  editing: InlineEditState | null
  onStartEdit: (item: ConversationItem) => void
  onDraftChange: (draftText: string) => void
  sentAsGoalItemId: string | null
  onCancelEdit: () => void
  onSubmitEdit: () => void
}

function TurnBlock({
  turn,
  historicalToolContentMode,
  streamingMessage,
  streamingMessageLastDeltaAt,
  streamingReasoning,
  streamRetrySignals,
  isRunning,
  showIdleThinkingFallback,
  isActiveTurn,
  isLastTurn,
  isFirstTurn,
  threadOrigin,
  isIdle,
  editing,
  onStartEdit,
  onDraftChange,
  sentAsGoalItemId,
  onCancelEdit,
  onSubmitEdit
}: TurnBlockProps): JSX.Element {
  // Separate user-input items from agent items
  const userItems = turn.items.filter(
    (i: ConversationItem) => isVisibleUserMessage(i)
  )
  const canEditUserMessage = isLastTurn && !isActiveTurn && userItems.length > 0

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--conversation-block-gap)' }}>
      {/* User messages */}
      {userItems.map((item: ConversationItem, idx) => {
        // The first user message of a thread spawned by another thread shows a
        // "From another thread" pill that jumps back to the originating thread.
        const showOrigin = isFirstTurn && idx === 0 && threadOrigin != null && !item.triggerKind
        return (
        <UserMessageBlock
          key={item.id}
          text={item.text ?? ''}
          nativeInputParts={item.nativeInputParts}
          imageDataUrls={item.imageDataUrls}
          images={item.images}
          createdAt={item.createdAt}
          deliveryMode={item.deliveryMode}
          triggerKind={showOrigin ? 'thread' : item.triggerKind}
          triggerLabel={showOrigin ? threadOrigin?.label : item.triggerLabel}
          triggerRefId={showOrigin ? threadOrigin?.refId : item.triggerRefId}
          sentAsGoal={item.id === sentAsGoalItemId}
          editable={canEditUserMessage && idx === userItems.length - 1 && isIdle && isTextOnlyEditableUserMessage(item)}
          onEdit={() => onStartEdit(item)}
          editing={editing?.turnId === turn.id && editing.itemId === item.id}
          editText={editing?.turnId === turn.id && editing.itemId === item.id ? editing.draftText : undefined}
          editSubmitting={editing?.turnId === turn.id && editing.itemId === item.id ? editing.submitting : false}
          editSubmitDisabled={
            !isIdle ||
            (editing?.turnId === turn.id && editing.itemId === item.id
              ? editing.submitting || editing.draftText.trim().length === 0
              : false)
          }
          onEditTextChange={onDraftChange}
          onCancelEdit={onCancelEdit}
          onSubmitEdit={onSubmitEdit}
        />
        )
      })}

      {/* Agent response */}
      <AgentResponseBlock
        turn={turn}
        streamingMessage={streamingMessage}
        streamingMessageLastDeltaAt={streamingMessageLastDeltaAt}
        streamingReasoning={streamingReasoning}
        streamRetrySignals={streamRetrySignals}
        isRunning={isRunning}
        showIdleThinkingFallback={showIdleThinkingFallback}
        isActiveTurn={isActiveTurn}
        isLastTurn={isLastTurn}
        historicalToolContentMode={historicalToolContentMode}
      />
    </div>
  )
}

function getHistoricalToolContentMode({
  turn,
  index,
  totalTurns,
  activeTurnId
}: {
  turn: ConversationTurn
  index: number
  totalTurns: number
  activeTurnId: string | null
}): HistoricalToolContentMode {
  const recentStartIndex = Math.max(0, totalTurns - FULL_HISTORY_TURN_COUNT)
  if (index >= recentStartIndex) return 'full'
  if (turn.id === activeTurnId) return 'full'
  if (turn.status === 'running' || turn.status === 'waitingApproval' || turn.status === 'waitingInput') {
    return 'full'
  }
  return 'trimmed'
}
