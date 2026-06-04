import { memo, type CSSProperties } from 'react'
import { MessageSquarePlus, MessageSquareText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadToolAction } from '../../utils/threadToolDisplay'

interface ThreadActionCardProps {
  action: ThreadToolAction
}

/**
 * Summary card for a Desktop CreateThread / SendMessageToThread tool call,
 * rendered before the agent footer (see TurnThreadActions). Mirrors the
 * file-artifact card chrome and offers a one-click jump to the target thread.
 */
export const ThreadActionCard = memo(function ThreadActionCard({ action }: ThreadActionCardProps): JSX.Element {
  const t = useT()
  const liveName = useThreadStore((s) =>
    s.threadList.find((thread) => thread.id === action.threadId)?.displayName
  )

  const name = (liveName ?? action.displayName)?.trim() || t('threadActionCard.untitled')
  const isCreated = action.kind === 'created'
  const title = isCreated ? t('threadActionCard.createdTitle') : t('threadActionCard.messagedTitle')
  const subtitle = action.queued ? t('threadActionCard.queuedSubtitle') : name

  function openChat(): void {
    useThreadStore.getState().setActiveThreadId(action.threadId)
    useUIStore.getState().setActiveMainView('conversation')
  }

  return (
    <div style={cardStyle}>
      <button
        type="button"
        aria-label={t('threadActionCard.openChatAria', { name })}
        onClick={openChat}
        style={bodyButtonStyle}
      >
        <span style={iconStyle}>
          {isCreated
            ? <MessageSquarePlus size={22} strokeWidth={1.8} aria-hidden />
            : <MessageSquareText size={22} strokeWidth={1.8} aria-hidden />}
        </span>
        <span style={textWrapStyle}>
          <span style={titleStyle}>{title}</span>
          <span style={subtitleStyle}>{subtitle}</span>
        </span>
      </button>
      <button
        type="button"
        onClick={openChat}
        style={openButtonStyle}
      >
        {t('threadActionCard.openChat')}
      </button>
    </div>
  )
})

const cardStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '0 14px 0 0',
  border: '1px solid var(--border-default)',
  borderRadius: '8px',
  background: 'var(--bg-primary)',
  boxShadow: '0 1px 2px rgba(0, 0, 0, 0.04)',
  overflow: 'hidden'
}

const bodyButtonStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  minHeight: '64px',
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  padding: '12px 0 12px 14px',
  border: 'none',
  background: 'transparent',
  color: 'inherit',
  cursor: 'pointer',
  textAlign: 'left',
  font: 'inherit'
}

const iconStyle: CSSProperties = {
  width: '44px',
  height: '44px',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

const textWrapStyle: CSSProperties = {
  minWidth: 0,
  flex: 1,
  display: 'block'
}

const titleStyle: CSSProperties = {
  display: 'block',
  fontSize: '14px',
  fontWeight: 600,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const subtitleStyle: CSSProperties = {
  display: 'block',
  marginTop: '2px',
  fontSize: '12px',
  color: 'var(--text-secondary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const openButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  minHeight: '32px',
  padding: '5px 14px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  background: 'transparent',
  color: 'var(--text-primary)',
  cursor: 'pointer',
  fontSize: '13px',
  fontWeight: 500,
  flexShrink: 0
}
