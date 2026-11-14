import { useAutomationRunReveal } from '../../hooks/useAutomationRunReveal'
import { useToolCallReveal } from '../../hooks/useToolCallReveal'
import { collectConversationImages, ConversationImagesContext, type GalleryImage } from './imagePreview/galleryImages'
import { collectConversationVisualizations, ConversationVisualizationsContext, type VisualizationEntry } from './visualizationGallery'
import { useHistoryGapLoading } from '../../hooks/useHistoryGapLoading'
import { Fragment, memo, useMemo, useRef } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import { runningTurnFiles } from '../../stores/turnDiffs'
import { useThreadStore } from '../../stores/threadStore'
import { useThreadHistoryStore, type HistoryGap } from '../../stores/threadHistoryStore'
import { useUIStore } from '../../stores/uiStore'
import { ConversationFindSurface } from '../../find/ConversationFindSurface'
import { useTranscriptViewport } from '../../hooks/useTranscriptViewport'
import { useScrollToLatestOnSend, type ComposerSendScrollRequest } from '../../hooks/useComposerSendScroll'
import { UserMessageBlock } from './UserMessageBlock'
import { AgentResponseBlock } from './AgentResponseBlock'
import { ScrollToBottomButton } from './ScrollToBottomButton'
import { RunningTurnChangesPill } from './RunningTurnChangesPill'
import { StreamRetryNotice } from './StreamRetryNotice'
import { ChannelBindingCodeCard } from './ChannelBindingCodeCard'
import { FeedbackSurvey } from './FeedbackSurvey'
import { SystemStatusDivider } from './SystemStatusDivider'
import { ConversationColumn } from './ConversationColumn'
import type { ConversationItem, ConversationTurn } from '../../types/conversation'
import { getSpawnedFromThreadId } from '../../utils/subAgentThreads'
import { findSupersededRetryTurnIds } from '../../utils/failedTurnResume'
import { isVisibleQueuedInput, isVisibleUserMessage } from '../../utils/visibleUserMessage'
import { isEmptyCompletedTurn } from '../../utils/turnPresentation'
import { estimateQueuedInputDockHeightPx } from './queuedInputDockLayout'
import { TurnNavigation } from './turnNavigation/TurnNavigation'
import { ConversationAsides } from './conversationAside/ConversationAside'
import { hasInlineEditContent, isInlineEditableUserMessage } from '../../utils/inlineMessageEdit'
import { useInlineMessageEdit, type InlineEditState } from './useInlineMessageEdit'

const SCROLL_BUTTON_BASE_BOTTOM_PX = 10
const SCROLL_BUTTON_DOCK_GAP_PX = 10
const CHANGES_PILL_CELL_PX = 32
const CHANGES_PILL_BOTTOM_GAP_PX = 4
export const MESSAGE_STREAM_BOTTOM_BASE_PX = 40
const NO_HISTORY_GAPS: HistoryGap[] = []

function getStoreConversationImages(): GalleryImage[] {
  const state = useConversationStore.getState()
  return collectConversationImages(state.turns, { localFiles: !state.remoteWorkspaceActive })
}

function getStoreConversationVisualizations(): VisualizationEntry[] {
  return collectConversationVisualizations(useConversationStore.getState().turns)
}

interface MessageStreamProps {
  sendScrollRequest?: ComposerSendScrollRequest | null
  viewportKey?: string
}

export function MessageStream({ sendScrollRequest = null, viewportKey }: MessageStreamProps): JSX.Element {
  return (
    <ConversationImagesContext.Provider value={getStoreConversationImages}>
      <ConversationVisualizationsContext.Provider value={getStoreConversationVisualizations}>
        <MessageStreamContent viewportKey={viewportKey} sendScrollRequest={sendScrollRequest} />
      </ConversationVisualizationsContext.Provider>
    </ConversationImagesContext.Provider>
  )
}

function MessageStreamContent({ sendScrollRequest = null, viewportKey }: MessageStreamProps): JSX.Element {
  const turns = useConversationStore((s) => s.turns)
  const turnStatus = useConversationStore((s) => s.turnStatus)
  const activeTurnId = useConversationStore((s) => s.activeTurnId)
  const streamingMessage = useConversationStore((s) => s.streamingMessage)
  const streamingMessageLastDeltaAt = useConversationStore((s) => s.streamingMessageLastDeltaAt)
  const streamingReasoning = useConversationStore((s) => s.streamingReasoning)
  const systemLabel = useConversationStore((s) => s.systemLabel)
  const streamRetry = useConversationStore((s) => s.streamRetry)
  const workspacePath = useConversationStore((s) => s.workspacePath)
  const showThinkingContent = useUIStore((s) => s.showThinkingContent)
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const historyGaps = useThreadHistoryStore((s) => s.threadId === activeThreadId ? s.gaps : NO_HISTORY_GAPS)
  const historyHeadLoaded = useThreadHistoryStore((s) => s.threadId === activeThreadId && s.headLoaded)
  const gapIdBeforeTurn = useMemo(
    () => new Map(historyGaps.map((gap) => [gap.followingTurnId, gap.id])),
    [historyGaps]
  )
  const threadStartLoaded = historyHeadLoaded && turns.length > 0 && !gapIdBeforeTurn.has(turns[0].id)
  const supersededRetryTurnIds = useMemo(() => findSupersededRetryTurnIds(turns), [turns])
  const turnDiffs = useConversationStore((s) => s.turnDiffs)
  const visibleTurns = useMemo(() => turns.filter((turn) => !supersededRetryTurnIds.has(turn.id)
    && (!isEmptyCompletedTurn(turn, (turnDiffs.get(turn.id)?.files.length ?? 0) > 0)
      || turn.items.some(isVisibleUserMessage))), [turns, turnDiffs, supersededRetryTurnIds])
  const visibleTurnIds = useMemo(() => new Set(visibleTurns.map((turn) => turn.id)), [visibleTurns])
  const threadList = useThreadStore((s) => s.threadList)
  const threadOrigin = useMemo(() => {
    const self = threadList.find((t) => t.id === activeThreadId)
    const parentId = self ? getSpawnedFromThreadId(self) : null
    if (!parentId) return null
    const parent = threadList.find((t) => t.id === parentId)
    return { refId: parentId, label: parent?.displayName?.trim() || undefined }
  }, [threadList, activeThreadId])
  const queuedInputCount = useConversationStore((s) => s.queuedInputs.filter(isVisibleQueuedInput).length)
  const changesPillVisible = useConversationStore((s) => runningTurnFiles(s).length > 0)
  const { editing, startEdit, changeDraft, cancelEdit, submitEdit } = useInlineMessageEdit()
  const columnRef = useRef<HTMLDivElement | null>(null)

  // Only the latest Turn changes during normal streaming, and ResizeObserver already
  // handles height-only changes, so do not walk the full history on every text delta.
  const latestTurnItemCount = turns[turns.length - 1]?.items.length ?? 0
  const contentLength = turns.length + latestTurnItemCount +
    streamingMessage.length +
    (showThinkingContent ? streamingReasoning.length : 0) +
    (streamRetry?.attempt ?? 0) +
    (turnStatus === 'running' && activeTurnId ? activeTurnId.length : 0) +
    (turnStatus === 'running' ? (streamingMessageLastDeltaAt ?? 0) : 0) +
    (systemLabel?.length ?? 0)

  const { scrollRef, showScrollButton, scrollToBottom } = useTranscriptViewport(
    contentLength, viewportKey ?? JSON.stringify([workspacePath, activeThreadId]), turns.length > 0
  )
  useAutomationRunReveal(scrollRef)
  useToolCallReveal(scrollRef)
  useHistoryGapLoading(scrollRef, historyGaps, turns.length)
  useScrollToLatestOnSend(sendScrollRequest, scrollToBottom)
  // The dock floats over the bottom of the scroll region, so its height is reserved
  // below the last message and the scroll-to-bottom button is lifted by the same amount.
  const dockHeightPx = estimateQueuedInputDockHeightPx(queuedInputCount)
  const scrollButtonBottomOffsetPx =
    SCROLL_BUTTON_BASE_BOTTOM_PX
    + (dockHeightPx > 0 ? dockHeightPx + SCROLL_BUTTON_DOCK_GAP_PX : 0)
    + (changesPillVisible ? CHANGES_PILL_CELL_PX : 0)
  const bottomClearancePx = MESSAGE_STREAM_BOTTOM_BASE_PX + dockHeightPx

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
        style={{ paddingBottom: bottomClearancePx }}
      >
        <ConversationFindSurface
          threadId={activeThreadId}
          getContainer={() => scrollRef.current}
          contentKey={contentLength}
        />
        <ConversationColumn ref={columnRef} className="dc-conversation-column-stack">
          {turns.map((turn, idx) => {
            const isActiveTurn = turn.id === activeTurnId
            const gapId = gapIdBeforeTurn.get(turn.id)
            return (
              <Fragment key={turn.id}>
                {gapId !== undefined && (
                  <div className="dc-conversation-history-gap" data-history-gap={gapId} aria-hidden="true" />
                )}
                {visibleTurnIds.has(turn.id) && <div
                  data-turn-id={turn.id}
                  className="dc-conversation-turn-shell"
                  data-active={isActiveTurn ? 'true' : undefined}
                >
                  <TurnBlock
                    turn={turn}
                    streamingMessage={isActiveTurn ? streamingMessage : ''}
                    streamingReasoning={isActiveTurn ? streamingReasoning : ''}
                    isRunning={
                      (turnStatus === 'running' || turnStatus === 'waitingInput' || turnStatus === 'waitingApproval') &&
                      isActiveTurn
                    }
                    showWorkingLine={
                      turnStatus === 'running' &&
                      isActiveTurn &&
                      !systemLabel
                    }
                    isActiveTurn={isActiveTurn}
                    isLastTurn={idx === turns.length - 1}
                    isLastVisibleTurn={turn === visibleTurns.at(-1)}
                    systemLabel={turn === visibleTurns.at(-1) ? systemLabel : null}
                    isFirstTurn={turn === visibleTurns[0] && threadStartLoaded}
                    threadOrigin={threadOrigin}
                    isIdle={turnStatus === 'idle'}
                    editing={editing}
                    onStartEdit={(item) => startEdit(turn, item)}
                    onDraftChange={changeDraft}
                    onCancelEdit={cancelEdit}
                    onSubmitEdit={() => { void submitEdit() }}
                  />
                </div>}
              </Fragment>
            )
          })}

          {editing && !turns.some((turn) =>
            turn.id === editing.turnId && turn.items.some((item) => item.id === editing.itemId)
          ) && (
            <UserMessageBlock
              text={editing.draftText}
              nativeInputParts={editing.inputParts}
              editing
              editText={editing.draftText}
              editSubmitting={editing.submitting}
              editSubmitDisabled={
                turnStatus !== 'idle' || editing.submitting || !hasInlineEditContent(editing.draftText, editing.inputParts)
              }
              onEditTextChange={changeDraft}
              onCancelEdit={cancelEdit}
              onSubmitEdit={() => { void submitEdit() }}
            />
          )}

          {systemLabel && visibleTurns.length === 0 && <SystemStatusDivider labelKey={systemLabel} />}

          {streamRetry && <StreamRetryNotice status={streamRetry} />}

          {activeThreadId && <ChannelBindingCodeCard threadId={activeThreadId} />}

          {activeThreadId && <FeedbackSurvey key={activeThreadId} threadId={activeThreadId} />}

          {/* Retains the stack's trailing gap above the bottom clearance. */}
          <div />
        </ConversationColumn>
      </div>

      <TurnNavigation scrollRef={scrollRef} columnRef={columnRef} />

      <ConversationAsides scrollRef={scrollRef} />

      <div className="dc-conversation-column-controls">
        <RunningTurnChangesPill bottomPx={dockHeightPx + CHANGES_PILL_BOTTOM_GAP_PX} />

        {showScrollButton && (
          <ScrollToBottomButton
            onClick={scrollToBottom}
            bottomOffsetPx={scrollButtonBottomOffsetPx}
            working={turnStatus === 'running'}
          />
        )}
      </div>
    </div>
  )
}

interface TurnBlockProps {
  turn: ConversationTurn
  streamingMessage: string
  streamingReasoning: string
  isRunning: boolean
  showWorkingLine: boolean
  isActiveTurn: boolean
  isLastTurn: boolean
  isLastVisibleTurn: boolean
  systemLabel: string | null
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
  streamingMessage,
  streamingReasoning,
  isRunning,
  showWorkingLine,
  isActiveTurn,
  isLastTurn,
  isLastVisibleTurn,
  systemLabel,
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
          messageId={item.id}
          text={item.text ?? ''}
          nativeInputParts={item.nativeInputParts}
          imageDataUrls={item.imageDataUrls}
          images={item.images}
          createdAt={item.createdAt}
          triggerKind={showOrigin ? 'thread' : item.triggerKind}
          triggerLabel={showOrigin ? threadOrigin?.label : item.triggerLabel}
          triggerRefId={showOrigin ? threadOrigin?.refId : item.triggerRefId}
          sentAsGoal={item.sentAsGoal === true}
          editable={canEditUserMessage && idx === userItems.length - 1 && isIdle && isInlineEditableUserMessage(item)}
          onEdit={() => onStartEdit(item)}
          editing={editing?.turnId === turn.id && editing.itemId === item.id}
          editText={editing?.turnId === turn.id && editing.itemId === item.id ? editing.draftText : undefined}
          editSubmitting={editing?.turnId === turn.id && editing.itemId === item.id ? editing.submitting : false}
          editSubmitDisabled={
            !isIdle ||
            (editing?.turnId === turn.id && editing.itemId === item.id
              ? editing.submitting || !hasInlineEditContent(editing.draftText, editing.inputParts)
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
        streamingReasoning={streamingReasoning}
        isRunning={isRunning}
        showWorkingLine={showWorkingLine}
        isLastTurn={isLastTurn}
        isLastVisibleTurn={isLastVisibleTurn}
        systemLabel={systemLabel}
      />
    </div>
  )
}, areTurnBlockPropsEqual)

function areTurnBlockPropsEqual(previous: TurnBlockProps, next: TurnBlockProps): boolean {
  return previous.turn === next.turn &&
    previous.streamingMessage === next.streamingMessage &&
    previous.streamingReasoning === next.streamingReasoning &&
    previous.isRunning === next.isRunning &&
    previous.showWorkingLine === next.showWorkingLine &&
    previous.isActiveTurn === next.isActiveTurn &&
    previous.isLastTurn === next.isLastTurn &&
    previous.isLastVisibleTurn === next.isLastVisibleTurn &&
    previous.systemLabel === next.systemLabel &&
    previous.isFirstTurn === next.isFirstTurn &&
    previous.threadOrigin === next.threadOrigin &&
    previous.isIdle === next.isIdle &&
    previous.editing === next.editing
}
