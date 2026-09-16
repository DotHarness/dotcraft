import type { CSSProperties } from 'react'
import { MessageSquarePlus } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ConversationColumn } from './ConversationColumn'
import { NoticeDivider } from './NoticeDivider'
import { UserMessageBlock } from './UserMessageBlock'

/** The conversation before its thread exists: the submitted message, already on screen. */
export function ThreadCreatingView({ text }: { text: string }): JSX.Element {
  const t = useT()
  const label = t('conversation.creatingThread')
  return (
    <div style={streamStyle}>
      <ConversationColumn style={columnStyle}>
        <UserMessageBlock text={text} />
        <NoticeDivider ariaLabel={label} title={label} icon={<MessageSquarePlus size={12} aria-hidden />} active />
      </ConversationColumn>
    </div>
  )
}

// Matches the message stream so the created conversation replaces this without moving anything.
const streamStyle: CSSProperties = {
  flex: 1,
  minHeight: 0,
  overflow: 'hidden',
  padding: '32px clamp(20px, 4vw, 40px)',
  display: 'flex',
  flexDirection: 'column',
  gap: 'var(--conversation-block-gap)'
}

const columnStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 'var(--conversation-block-gap)'
}
