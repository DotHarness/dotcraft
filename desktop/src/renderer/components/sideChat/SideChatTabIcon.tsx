import { MessagesSquare } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { isSideChatRunning, sideChatNeedsResponse, useSideChatStore } from '../../stores/sideChatStore'
import { Spinner } from '../ui/Spinner'
import styles from './SideChat.module.css'

export function SideChatTabIcon({ sideChatKey }: { sideChatKey: string }): JSX.Element {
  const t = useT()
  const state = useSideChatStore((store) => {
    const chat = store.chats.get(sideChatKey)
    if (!chat) return 'idle'
    if (sideChatNeedsResponse(chat)) return 'needsResponse'
    if (isSideChatRunning(chat)) return 'running'
    return chat.unread ? 'unread' : 'idle'
  })
  if (state === 'running') return <Spinner size={14} label={t('sideChat.running')} />
  return (
    <span className={styles.tabIcon}>
      <MessagesSquare size={14} strokeWidth={2} aria-hidden style={{ display: 'block' }} />
      {state !== 'idle' && (
        <span
          className={styles.tabIndicator}
          data-state={state}
          role="img"
          aria-label={t(state === 'unread' ? 'sideChat.unread' : 'sideChat.needsResponse')}
        />
      )}
    </span>
  )
}
