import { useMemo, type CSSProperties } from 'react'
import type { InputPart } from '../../types/conversation'
import { createOptimisticUserMessage } from '../../utils/inputPresentation'
import { ConversationColumn } from './ConversationColumn'
import { MESSAGE_STREAM_BOTTOM_BASE_PX } from './MessageStream'
import { UserMessageBlock } from './UserMessageBlock'

export function ThreadCreatingContent({
  text,
  inputParts,
  sentAsGoal = false
}: {
  text: string
  inputParts: InputPart[]
  sentAsGoal?: boolean
}): JSX.Element {
  const message = useMemo(
    () => createOptimisticUserMessage(inputParts, text, 'pending', sentAsGoal),
    [inputParts, sentAsGoal, text]
  )
  return (
    <div style={frameStyle}>
      <div className="dc-conversation-message-stream" style={{ paddingBottom: MESSAGE_STREAM_BOTTOM_BASE_PX }}>
        <ConversationColumn className="dc-conversation-column-stack">
          <div className="dc-conversation-turn-shell">
            <div style={turnStyle}>
              <UserMessageBlock
                messageId={message.id}
                text={message.text ?? ''}
                nativeInputParts={message.nativeInputParts}
                imageDataUrls={message.imageDataUrls}
                images={message.images}
                createdAt={message.createdAt}
                sentAsGoal={sentAsGoal}
              />
            </div>
          </div>
        </ConversationColumn>
      </div>
    </div>
  )
}

const frameStyle: CSSProperties = { position: 'relative', flex: 1, overflow: 'hidden' }
const turnStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 'var(--conversation-block-gap)' }
