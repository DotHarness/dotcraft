import { memo, type ReactNode } from 'react'
import type { TranscriptLiveText } from '../../stores/subAgentTranscriptEvents'
import type { ConversationTurn } from '../../types/conversation'
import { getTurnContextHandoff } from '../../types/contextHandoff'
import { isVisibleUserMessage } from '../../utils/visibleUserMessage'
import { isEmptyCompletedTurn } from '../../utils/turnPresentation'
import { useSubAgentTranscriptStore } from '../../stores/subAgentTranscriptStore'
import { AgentResponseBlock } from './AgentResponseBlock'
import { UserMessageBlock } from './UserMessageBlock'
import { ContextHandoffNotice } from './ContextHandoffNotice'
import { TranscriptThreadContext } from './transcriptDiffs'
import styles from './TranscriptTurn.module.css'

export const TranscriptTurn = memo(function TranscriptTurn({
  threadId,
  turn,
  leadingTurns,
  absorbed = false,
  isLastTurn,
  running,
  live,
  renderTurnCompletion
}: {
  threadId: string
  turn: ConversationTurn
  leadingTurns?: readonly ConversationTurn[]
  absorbed?: boolean
  isLastTurn: boolean
  running: boolean
  live: TranscriptLiveText | null
  renderTurnCompletion?: (turnIds: readonly string[]) => ReactNode
}): JSX.Element | null {
  const loadTurnItems = useSubAgentTranscriptStore((s) => s.loadTurnItems)
  const hasFileChanges = useSubAgentTranscriptStore((s) =>
    (s.transcripts.get(threadId)?.turnDiffs.get(turn.id)?.files.length ?? 0) > 0)
  const hasUserMessage = turn.items.some(isVisibleUserMessage)
  const handoff = getTurnContextHandoff(turn)
  if ((absorbed || isEmptyCompletedTurn(turn, hasFileChanges)) && !hasUserMessage && !handoff) return null

  return (
    <div className={styles.turn}>
      {handoff && <ContextHandoffNotice handoff={handoff} />}
      {turn.items.filter(isVisibleUserMessage).map((item) => (
        <UserMessageBlock
          key={item.id}
          messageId={item.id}
          text={item.text ?? ''}
          nativeInputParts={item.nativeInputParts}
          imageDataUrls={item.imageDataUrls}
          images={item.images}
          createdAt={item.createdAt}
          triggerKind={item.triggerKind}
          triggerLabel={item.triggerLabel}
          triggerRefId={item.triggerRefId}
          sentAsGoal={item.sentAsGoal === true}
        />
      ))}
      {!absorbed && <TranscriptThreadContext.Provider value={threadId}>
        <AgentResponseBlock
          turn={turn}
          leadingTurns={leadingTurns}
          streamingMessage={live?.kind === 'agentMessage' ? live.text : ''}
          streamingReasoning={live?.kind === 'reasoningContent' ? live.text : ''}
          isRunning={running}
          isLastTurn={isLastTurn}
          isLastVisibleTurn={isLastTurn}
          showWorkingLine={running}
          activeItemIdOverride={live?.itemId ?? null}
          shellRuntimeScope="none"
          readOnly
          renderTurnCompletion={renderTurnCompletion}
          loadTurnItems={loadTurnItems}
        />
      </TranscriptThreadContext.Provider>}
    </div>
  )
})
