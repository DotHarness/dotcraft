import { memo, type ReactNode } from 'react'
import type { TranscriptLiveText } from '../../stores/subAgentTranscriptEvents'
import type { ConversationTurn } from '../../types/conversation'
import { isVisibleUserMessage } from '../../utils/visibleUserMessage'
import { AgentResponseBlock } from './AgentResponseBlock'
import { UserMessageBlock } from './UserMessageBlock'
import { TranscriptThreadContext } from './transcriptDiffs'
import styles from './TranscriptTurn.module.css'

export const TranscriptTurn = memo(function TranscriptTurn({
  threadId,
  turn,
  isLastTurn,
  running,
  live,
  renderTurnCompletion
}: {
  threadId: string
  turn: ConversationTurn
  isLastTurn: boolean
  running: boolean
  live: TranscriptLiveText | null
  renderTurnCompletion?: (turnId: string) => ReactNode
}): JSX.Element {
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
          triggerKind={item.triggerKind}
          triggerLabel={item.triggerLabel}
          triggerRefId={item.triggerRefId}
          sentAsGoal={item.sentAsGoal === true}
        />
      ))}
      <TranscriptThreadContext.Provider value={threadId}>
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
      </TranscriptThreadContext.Provider>
    </div>
  )
})
