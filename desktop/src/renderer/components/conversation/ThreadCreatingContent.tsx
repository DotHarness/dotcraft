import type { CSSProperties } from 'react'
import { MessageSquarePlus } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ConversationColumn } from './ConversationColumn'
import { NoticeDivider } from './NoticeDivider'
import { UserMessageBlock } from './UserMessageBlock'

/** The message area while the thread is being created: the submitted message, already on screen. */
export function ThreadCreatingContent({ text }: { text: string }): JSX.Element {
  const t = useT()
  const label = t('conversation.creatingThread')
  return (
    <div style={frameStyle}>
      <div className="dc-conversation-message-stream">
        <ConversationColumn className="dc-conversation-column-stack">
          <UserMessageBlock text={text} />
          <NoticeDivider ariaLabel={label} title={label} icon={<MessageSquarePlus size={14} aria-hidden />} active />
        </ConversationColumn>
      </div>
    </div>
  )
}

const frameStyle: CSSProperties = { position: 'relative', flex: 1, overflow: 'hidden' }
