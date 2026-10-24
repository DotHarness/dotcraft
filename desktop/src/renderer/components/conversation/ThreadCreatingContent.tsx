import { useMemo, type CSSProperties, type ReactNode } from 'react'
import type { InputPart } from '../../types/conversation'
import { createOptimisticUserMessage } from '../../utils/inputPresentation'
import { ConversationColumn } from './ConversationColumn'
import { MESSAGE_STREAM_BOTTOM_BASE_PX } from './MessageStream'
import { UserMessageBlock } from './UserMessageBlock'

export function ThreadCreatingContent({
  text,
  inputParts,
  sentAsGoal = false,
  children
}: {
  text?: string
  inputParts?: InputPart[]
  sentAsGoal?: boolean
  children?: ReactNode
}): JSX.Element {
  const message = useMemo(
    () => inputParts ? createOptimisticUserMessage(inputParts, text ?? '', 'pending', sentAsGoal) : null,
    [inputParts, sentAsGoal, text]
  )
  return (
    <div style={frameStyle}>
      <div className="dc-conversation-message-stream" style={{ paddingBottom: MESSAGE_STREAM_BOTTOM_BASE_PX }}>
        <ConversationColumn className="dc-conversation-column-stack">
          <div className="dc-conversation-turn-shell">
            <div style={turnStyle}>
              {message ? (
                <UserMessageBlock
                  messageId={message.id}
                  text={message.text ?? ''}
                  nativeInputParts={message.nativeInputParts}
                  imageDataUrls={message.imageDataUrls}
                  images={message.images}
                  createdAt={message.createdAt}
                  sentAsGoal={sentAsGoal}
                />
              ) : null}
              {children}
            </div>
          </div>
        </ConversationColumn>
      </div>
    </div>
  )
}

const frameStyle: CSSProperties = { position: 'relative', flex: 1, overflow: 'hidden' }
const turnStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: 'var(--conversation-block-gap)' }
