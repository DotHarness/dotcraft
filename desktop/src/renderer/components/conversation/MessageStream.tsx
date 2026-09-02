import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useConversationStore, type StreamRetrySignal } from '../../stores/conversationStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { addToast } from '../../stores/toastStore'
import { useT } from '../../contexts/LocaleContext'
import { ConversationFindSurface } from '../../find/ConversationFindSurface'
import { useAutoScroll } from '../../hooks/useAutoScroll'
import { UserMessageBlock } from './UserMessageBlock'
import { AgentResponseBlock, type HistoricalToolContentMode } from './AgentResponseBlock'
import { ScrollToBottomButton } from './ScrollToBottomButton'
import { ConversationColumn } from './ConversationColumn'
import { wireTurnToConversationTurn } from '../../types/conversation'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import type { ContextUsageSnapshotWire, Thread } from '../../types/thread'
import { getSpawnedFromThreadId } from '../../utils/subAgentThreads'
import { startTurnWithOptimisticUI } from '../../utils/startTurn'
import { readThreadHistoryHead, readThreadTurnsPage } from '../../utils/threadHistory'
import { estimateQueuedInputDockHeightPx } from './queuedInputDockLayout'

/** Module-level scroll position cache — ephemeral, not persisted to storage. */
const scrollPositionCache = new Map<string, number>()

const NEAR_BOTTOM_THRESHOLD = 50
const SCROLL_BUTTON_BASE_BOTTOM_PX = 10
const SCROLL_BUTTON_DOCK_GAP_PX = 10
/** Resting gap reserved below the last message so it never sits flush against the
 *  composer (and clears the dock's top edge when a dock is present). */
const MESSAGE_STREAM_BOTTOM_BASE_PX = 40
const FULL_HISTORY_TURN_COUNT = 3
/** Distance from the top within which a scroll retries the pending history page. */
const LOAD_OLDER_TOP_THRESHOLD_PX = 80
const EMPTY_STREAM_RETRY_SIGNALS: StreamRetrySignal[] = []

const requestAppServer = (method: Parameters<typeof window.api.appServer.sendRequest>[0], params: any): Promise<any> =>
  window.api.appServer.sendRequest(method, params)

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

function isVisibleUserMessage(item: ConversationItem): boolean {
  return (
    item.type === 'userMessage' &&
    item.deliveryMode !== 'guidance' &&
    item.deliveryMode !== 'subagentMailbox' &&
    item.triggerKind !== 'subagentMailbox'
  )
}

/** Scrollable container for the turn history and live streaming content. Spec §10.3.3. */
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
  const historyTurnCursor = useThreadStore((s) =>
    s.activeHistoryCursors?.threadId === s.activeThreadId ? s.activeHistoryCursors.turnCursor : null
  )
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
  const queuedInputCount = useConversationStore((s) => s.queuedInputs.length)
  const [editing, setEditing] = useState<InlineEditState | null>(null)
  const prevThreadIdRef = useRef<string | null>(null)
  const effectiveSystemLabel = systemLabel
    ?? (backgroundMemoryStatus === 'consolidating' ? 'systemStatus.consolidating' : null)

  // Only the latest Turn changes during normal streaming, and ResizeObserver already
  // handles height-only changes, so do not walk the full history on every text delta.
  const latestTurnItemCount = turns[turns.length - 1]?.items.length ?? 0
  const contentLength = turns.length + latestTurnItemCount +
    streamingMessage.length +
    (showThinkingContent ? streamingReasoning.length : 0) +
    streamRetrySignals.reduce((acc, signal) => acc + signal.rawMessage.length, 0) +
    (turnStatus === 'running' && activeTurnId ? activeTurnId.length : 0) +
    (turnStatus === 'running' ? (streamingMessageLastDeltaAt ?? 0) : 0) +
    (effectiveSystemLabel?.length ?? 0)

  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)
  // The dock floats over the bottom of the scroll region, so its height is reserved
  // below the last message and the scroll-to-bottom button is lifted by the same amount.
  const dockHeightPx = estimateQueuedInputDockHeightPx(queuedInputCount)
  const scrollButtonBottomOffsetPx =
    SCROLL_BUTTON_BASE_BOTTOM_PX + (dockHeightPx > 0 ? dockHeightPx + SCROLL_BUTTON_DOCK_GAP_PX : 0)
  const bottomClearancePx = MESSAGE_STREAM_BOTTOM_BASE_PX + dockHeightPx

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
          const refreshed = await readThreadHistoryHead(requestAppServer, current.threadId)
          useConversationStore.getState().setTurns(
            (refreshed.thread.turns ?? []).map((turn) =>
              wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)
            )
          )
          useConversationStore.getState().setContextUsage(refreshed.thread.contextUsage ?? null)
          useThreadStore.getState().setActiveThread(refreshed.thread as Thread)
          useThreadStore.getState().setActiveHistoryCursors(current.threadId, refreshed.turnCursor)
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

  useEffect(() => {
    const el = scrollRef.current
    const prev = prevThreadIdRef.current
    const curr = activeThreadId

    if (prev && prev !== curr && el) {
      scrollPositionCache.set(prev, el.scrollTop)
    }

    if (curr && curr !== prev && el) {
      // rAF so the restore lands after the arriving thread's content renders.
      requestAnimationFrame(() => {
        const saved = scrollPositionCache.get(curr)
        if (saved === undefined) {
          el.scrollTop = el.scrollHeight
        } else {
          const atBottom = el.scrollHeight - saved - el.clientHeight <= NEAR_BOTTOM_THRESHOLD
          el.scrollTop = atBottom ? el.scrollHeight : saved
        }
      })
    }

    prevThreadIdRef.current = curr
  }, [activeThreadId, scrollRef])

  // History pages whole Turns, so a page can never render as a fragment of a Turn. On
  // first paint load only enough pages to make the viewport scrollable, never more.
  useEffect(() => {
    const el = scrollRef.current
    if (!el || !activeThreadId || !historyTurnCursor) return
    let cancelled = false
    let loading = false

    const loadOlderTurns = async (): Promise<void> => {
      if (loading) return
      loading = true
      const previousHeight = el.scrollHeight
      try {
        const page = await readThreadTurnsPage(requestAppServer, activeThreadId, historyTurnCursor)
        if (cancelled || useThreadStore.getState().activeThreadId !== activeThreadId) return
        const older = page.turns.map((turn) =>
          wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)
        )
        useConversationStore.getState().setTurns(
          [...older, ...useConversationStore.getState().turns],
          {
            preserveExistingRealtime: true,
            realtimeScopeThreadId: activeThreadId
          }
        )
        useThreadStore.getState().setActiveHistoryCursors(activeThreadId, page.nextCursor)
        // Hold the viewport on the same content now that the stream grew upwards.
        requestAnimationFrame(() => { el.scrollTop += el.scrollHeight - previousHeight })
      } catch (err) {
        console.error('thread history page load failed:', err)
      } finally {
        loading = false
      }
    }

    const loadOnScrollToTop = (): void => {
      if (el.scrollTop > LOAD_OLDER_TOP_THRESHOLD_PX) return
      void loadOlderTurns()
    }
    el.addEventListener('scroll', loadOnScrollToTop, { passive: true })

    const fillViewportFrame = requestAnimationFrame(() => {
      if (cancelled || el.clientHeight <= 0 || el.scrollHeight > el.clientHeight) return
      void loadOlderTurns()
    })
    return () => {
      cancelled = true
      cancelAnimationFrame(fillViewportFrame)
      el.removeEventListener('scroll', loadOnScrollToTop)
    }
  }, [activeThreadId, historyTurnCursor, scrollRef])

  return (
    <div style={{ position: 'relative', flex: 1, overflow: 'hidden' }}>
      <div
        ref={scrollRef}
        className="dc-conversation-message-stream"
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
        <ConversationFindSurface
          threadId={activeThreadId}
          getContainer={() => scrollRef.current}
          contentKey={contentLength}
        />
        <ConversationColumn
          style={{
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--conversation-block-gap)'
          }}
        >
          {turns.map((turn, idx) => {
            const isActiveTurn = turn.id === activeTurnId
            return (
              <div
                key={turn.id}
                className="dc-conversation-turn-shell"
                data-active={isActiveTurn ? 'true' : undefined}
              >
                <TurnBlock
                  turn={turn}
                  historicalToolContentMode={getHistoricalToolContentMode({
                    turn,
                    index: idx,
                    totalTurns: turns.length,
                    activeTurnId
                  })}
                  streamingMessage={isActiveTurn ? streamingMessage : ''}
                  streamingMessageLastDeltaAt={isActiveTurn ? streamingMessageLastDeltaAt : null}
                  streamingReasoning={isActiveTurn ? streamingReasoning : ''}
                  streamRetrySignals={
                    isActiveTurn
                      ? streamRetrySignals.filter((signal) => signal.turnId === turn.id)
                      : EMPTY_STREAM_RETRY_SIGNALS
                  }
                  isRunning={
                    (turnStatus === 'running' || turnStatus === 'waitingInput' || turnStatus === 'waitingApproval') &&
                    isActiveTurn
                  }
                  showIdleThinkingFallback={
                    turnStatus === 'running' &&
                    isActiveTurn &&
                    !effectiveSystemLabel
                  }
                  isActiveTurn={isActiveTurn}
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
                  onCancelEdit={() => {
                    setEditing(null)
                  }}
                  onSubmitEdit={() => {
                    void submitInlineEdit()
                  }}
                />
              </div>
            )
          })}

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
          background: 'var(--border-default)'
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
          background: 'var(--border-default)'
        }}
      />
    </div>
  )
}

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
  onCancelEdit: () => void
  onSubmitEdit: () => void
}

const TurnBlock = memo(function TurnBlock({
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
  onCancelEdit,
  onSubmitEdit
}: TurnBlockProps): JSX.Element {
  const userItems = turn.items.filter(
    (i: ConversationItem) => isVisibleUserMessage(i)
  )
  const canEditUserMessage = isLastTurn && !isActiveTurn && userItems.length > 0

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--conversation-block-gap)' }}>
      {userItems.map((item: ConversationItem, idx) => {
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
          sentAsGoal={item.sentAsGoal === true}
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
}, areTurnBlockPropsEqual)

function areTurnBlockPropsEqual(previous: TurnBlockProps, next: TurnBlockProps): boolean {
  return previous.turn === next.turn &&
    previous.historicalToolContentMode === next.historicalToolContentMode &&
    previous.streamingMessage === next.streamingMessage &&
    previous.streamingMessageLastDeltaAt === next.streamingMessageLastDeltaAt &&
    previous.streamingReasoning === next.streamingReasoning &&
    previous.streamRetrySignals === next.streamRetrySignals &&
    previous.isRunning === next.isRunning &&
    previous.showIdleThinkingFallback === next.showIdleThinkingFallback &&
    previous.isActiveTurn === next.isActiveTurn &&
    previous.isLastTurn === next.isLastTurn &&
    previous.isFirstTurn === next.isFirstTurn &&
    previous.threadOrigin === next.threadOrigin &&
    previous.isIdle === next.isIdle &&
    previous.editing === next.editing
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
