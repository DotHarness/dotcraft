import { memo, useCallback, useEffect, useMemo } from 'react'
import { ChevronLeft } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useConversationStore } from '../../stores/conversationStore'
import { isSubAgentChildRunning, type SubAgentChild } from '../../stores/subAgentStore'
import { useSubAgentTranscriptStore, type SubAgentTranscript } from '../../stores/subAgentTranscriptStore'
import type { TranscriptLiveText } from '../../stores/subAgentTranscriptEvents'
import { useAutoScroll } from '../../hooks/useAutoScroll'
import type { TurnDiffSource } from '../../hooks/useTurnDiffActions'
import type { ConversationTurn } from '../../types/conversation'
import type { ThreadConfigurationWire } from '../../types/thread'
import type { TurnFileChange } from '../../types/turnDiff'
import { formatSubAgentMeta } from '../../utils/subAgentPresentation'
import { isVisibleUserMessage } from '../../utils/visibleUserMessage'
import { readReasoningObject } from '../conversation/modelReasoning'
import { AgentResponseBlock } from '../conversation/AgentResponseBlock'
import { UserMessageBlock } from '../conversation/UserMessageBlock'
import { collectConversationImages, ConversationImagesContext } from '../conversation/imagePreview/galleryImages'
import { ScrollToBottomButton } from '../conversation/ScrollToBottomButton'
import { TurnChangesCard } from '../conversation/TurnCompletionSummary'
import { RobotAvatar } from '../agents/RobotAvatar'
import { IconButton } from '../ui/IconButton'
import { Skeleton } from '../ui/Skeleton'
import styles from './SubagentDetail.module.css'

const TRANSCRIPT_POLL_MS = 2000
const NO_ROWS: TurnFileChange[] = []

type Translate = (key: string, vars?: Record<string, string | number>) => string

function isTurnLive(turn: ConversationTurn | undefined): boolean {
  return turn?.status === 'running' || turn?.status === 'waitingApproval' || turn?.status === 'waitingInput'
}

export function SubagentDetail({ child, onBack }: { child: SubAgentChild; onBack: () => void }): JSX.Element {
  const t = useT()
  const childThreadId = child.childThreadId
  const transcript = useSubAgentTranscriptStore((s) => s.transcripts.get(childThreadId))
  const fetchTranscript = useSubAgentTranscriptStore((s) => s.fetchTranscript)
  const watchTranscript = useSubAgentTranscriptStore((s) => s.watchTranscript)
  const connected = useConnectionStore((s) => s.status === 'connected')
  const running = isSubAgentChildRunning(child) || isTurnLive(transcript?.turns.at(-1))
  const pollFallback = running && transcript?.subscribed === false

  useEffect(() => {
    if (!connected) return
    return watchTranscript(childThreadId)
  }, [childThreadId, connected, watchTranscript])

  useEffect(() => {
    if (!pollFallback) return
    const timer = setInterval(() => {
      void fetchTranscript(childThreadId)
    }, TRANSCRIPT_POLL_MS)
    return () => clearInterval(timer)
  }, [childThreadId, pollFallback, fetchTranscript])

  const meta = formatSubAgentMeta({
    agentRole: child.agentRole,
    profileName: child.profileName,
    runtimeType: child.runtimeType
  })
  const modelLabel = formatModelLabel(transcript?.configuration ?? null, t)

  return (
    <div className={styles.detail}>
      <div className={styles.header}>
        <IconButton
          icon={<ChevronLeft size={16} strokeWidth={1.8} aria-hidden />}
          label={t('subagentsPanel.back')}
          tooltipLabel={t('subagentsPanel.back')}
          tooltipPlacement="bottom"
          size={24}
          onClick={onBack}
        />
        <span className={styles.avatar}>
          <RobotAvatar name={child.nickname} size={24} />
        </span>
        <span className={styles.nameGroup}>
          <span className={styles.name}>{child.nickname}</span>
          {meta && <span className={styles.meta}>({meta})</span>}
        </span>
        {modelLabel && <span className={styles.model}>{modelLabel}</span>}
      </div>
      <SubagentTranscript childThreadId={childThreadId} transcript={transcript} running={running} />
    </div>
  )
}

function SubagentTranscript({
  childThreadId,
  transcript,
  running
}: {
  childThreadId: string
  transcript: SubAgentTranscript | undefined
  running: boolean
}): JSX.Element {
  const t = useT()
  const turns = transcript?.turns ?? []
  const live = transcript?.live ?? null
  const lastTurn = turns[turns.length - 1]
  const contentLength = turns.length
    + (lastTurn?.items.length ?? 0)
    + (lastTurn?.items.reduce((total, item) => total + (item.text?.length ?? 0), 0) ?? 0)
    + (live?.text.length ?? 0)
  const { scrollRef, showScrollButton, scrollToBottom } = useAutoScroll(contentLength)
  const waiting = transcript == null
    || transcript.status === 'loading'
    || (transcript.status === 'ready' && turns.length === 0 && running)
  const unavailable = !waiting && turns.length === 0
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
  const getConversationImages = useCallback(
    () => collectConversationImages(transcript?.turns ?? [], { localFiles: !remoteWorkspaceActive }),
    [remoteWorkspaceActive, transcript?.turns]
  )

  return (
    <div className={styles.viewport}>
      <div ref={scrollRef} className={`${styles.transcript} dc-scrollbar-stable`}>
        {waiting ? (
          <TranscriptSkeleton label={t('subagentsPanel.transcriptLoading')} />
        ) : unavailable ? (
          <p className={styles.notice}>{t('subagentsPanel.transcriptUnavailable')}</p>
        ) : (
          <ConversationImagesContext.Provider value={getConversationImages}>
            {turns.map((turn, index) => {
              const isLastTurn = index === turns.length - 1
              return (
                <TranscriptTurn
                  key={turn.id}
                  childThreadId={childThreadId}
                  turn={turn}
                  isLastTurn={isLastTurn}
                  running={running && isLastTurn}
                  live={isLastTurn ? live : null}
                />
              )
            })}
          </ConversationImagesContext.Provider>
        )}
      </div>
      {showScrollButton && <ScrollToBottomButton onClick={scrollToBottom} />}
    </div>
  )
}

const TranscriptTurn = memo(function TranscriptTurn({
  childThreadId,
  turn,
  isLastTurn,
  running,
  live
}: {
  childThreadId: string
  turn: ConversationTurn
  isLastTurn: boolean
  running: boolean
  live: TranscriptLiveText | null
}): JSX.Element {
  const renderTurnCompletion = useCallback(
    (turnId: string) => <SubagentTurnChanges childThreadId={childThreadId} turnId={turnId} />,
    [childThreadId]
  )
  return (
    <div className={styles.turn}>
      {turn.items.filter(isVisibleUserMessage).map((item) => (
        <UserMessageBlock
          key={item.id}
          messageId={item.id}
          text={item.text ?? ''}
          nativeInputParts={item.nativeInputParts}
          imageDataUrls={item.imageDataUrls}
          images={item.images}
          createdAt={item.createdAt}
          deliveryMode={item.deliveryMode}
          triggerKind={item.triggerKind}
          triggerLabel={item.triggerLabel}
          triggerRefId={item.triggerRefId}
          sentAsGoal={item.sentAsGoal === true}
        />
      ))}
      <AgentResponseBlock
        turn={turn}
        streamingMessage={live?.kind === 'agentMessage' ? live.text : ''}
        streamingReasoning={live?.kind === 'reasoningContent' ? live.text : ''}
        isRunning={running}
        isLastTurn={isLastTurn}
        showIdleThinkingFallback={running && !live?.text}
        activeItemIdOverride={live?.itemId ?? null}
        shellRuntimeScope="none"
        readOnly
        renderTurnCompletion={renderTurnCompletion}
      />
    </div>
  )
})

function SubagentTurnChanges({ childThreadId, turnId }: { childThreadId: string; turnId: string }): JSX.Element | null {
  const rows = useSubAgentTranscriptStore((s) =>
    s.transcripts.get(childThreadId)?.turnDiffs.get(turnId)?.files ?? NO_ROWS
  )
  const childWorkspacePath = useSubAgentTranscriptStore((s) => s.transcripts.get(childThreadId)?.workspacePath)
  const parentWorkspacePath = useConversationStore((s) => s.workspacePath)
  const source = useMemo<TurnDiffSource>(() => ({
    rows: (id) => useSubAgentTranscriptStore.getState().transcripts.get(childThreadId)?.turnDiffs.get(id)?.files ?? [],
    setStatus: (id, key, status) =>
      useSubAgentTranscriptStore.getState().setTurnFileStatus(childThreadId, id, key, status)
  }), [childThreadId])
  return (
    <TurnChangesCard
      turnId={turnId}
      rows={rows}
      workspacePath={childWorkspacePath || parentWorkspacePath}
      source={source}
    />
  )
}

function TranscriptSkeleton({ label }: { label: string }): JSX.Element {
  return (
    <div role="status" aria-busy="true" aria-label={label} className={styles.skeleton}>
      <Skeleton width="64%" height={36} radius={16} style={{ alignSelf: 'flex-end' }} />
      <Skeleton width="92%" />
      <Skeleton width="86%" />
      <Skeleton width="54%" />
    </div>
  )
}

function formatModelLabel(configuration: ThreadConfigurationWire | null, t: Translate): string | null {
  const model = configuration?.model ?? configuration?.Model
  if (!model) return null
  const reasoning = readReasoningObject(configuration?.reasoning ?? configuration?.Reasoning)
  return reasoning?.enabled ? `${model} · ${t(`composer.reasoning.${reasoning.effort}`)}` : model
}
