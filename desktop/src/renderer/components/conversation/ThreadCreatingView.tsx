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
    <div style={containerStyle}>
      <ConversationColumn>
        <UserMessageBlock text={text} />
        <NoticeDivider ariaLabel={label} title={label} icon={<MessageSquarePlus size={12} aria-hidden />} active />
      </ConversationColumn>
    </div>
  )
}

const containerStyle: CSSProperties = {
  display: 'flex',
  flex: 1,
  flexDirection: 'column',
  justifyContent: 'flex-end',
  paddingBottom: 24,
  overflow: 'hidden'
}
