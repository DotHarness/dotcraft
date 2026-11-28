import { useState, type JSX } from 'react'
import { MessageSquare, Unlink } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { unfollowLinkedChat, useLinkedChats } from '../../../stores/linkedChatsStore'
import type { LinkedChatStatus, LinkedChatSummary } from '../../../../shared/linkedChats'
import { openThread } from '../../sidebar/projectConnectionActions'
import { IconButton } from '../../ui/IconButton'
import { Spinner } from '../../ui/Spinner'
import type { SummaryServices } from './summaryServices'
import { GLYPH, Row, Section } from './SummaryRows'

const GROUPS = [
  { key: 'chats-needs-you', title: 'summary.chats.needsYou', statuses: ['waitingForInput'] },
  { key: 'chats-in-progress', title: 'summary.chats.inProgress', statuses: ['running', 'idle'] },
  { key: 'chats-done', title: 'summary.chats.done', statuses: ['completed', 'failed', 'stopped'] }
] as const satisfies ReadonlyArray<{ key: string; title: string; statuses: readonly LinkedChatStatus[] }>

const OUTCOME = {
  failed: 'threadActionCard.status.failed',
  stopped: 'threadActionCard.status.stopped'
} as const

const ACTION_GLYPH = { size: 14, strokeWidth: 1.75 } as const

export function LinkedChatsSection({ services, threadId }: { services: SummaryServices; threadId: string }): JSX.Element {
  const t = useT()
  const chats = useLinkedChats(threadId)
  const [unfollowing, setUnfollowing] = useState<string | null>(null)
  const sorted = [...chats].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const unfollow = async (chat: LinkedChatSummary): Promise<void> => {
    if (unfollowing) return
    setUnfollowing(chat.threadId)
    try {
      await unfollowLinkedChat(threadId, chat)
    } catch {
      services.ui.showError(t('summary.chats.unfollowFailed'))
    } finally {
      setUnfollowing(null)
    }
  }
  return (
    <>
      {GROUPS.map((group) => {
        const rows = sorted.filter((chat) => (group.statuses as readonly LinkedChatStatus[]).includes(chat.status))
        return rows.length ? (
          <Section key={group.key} sectionKey={group.key} title={t(group.title)} count={rows.length}>
            {rows.map((chat) => (
              <ChatRow
                key={`${chat.projectId}:${chat.threadId}`}
                chat={chat}
                busy={unfollowing === chat.threadId}
                disabled={unfollowing !== null}
                onUnfollow={() => void unfollow(chat)}
              />
            ))}
          </Section>
        ) : null
      })}
    </>
  )
}

function ChatRow({ chat, busy, disabled, onUnfollow }: {
  chat: LinkedChatSummary
  busy: boolean
  disabled: boolean
  onUnfollow: () => void
}): JSX.Element {
  const t = useT()
  const outcome = chat.status === 'failed' || chat.status === 'stopped' ? t(OUTCOME[chat.status]) : undefined
  const label = t('summary.chats.unfollow')
  return (
    <Row
      leading={<MessageSquare {...GLYPH} />}
      label={chat.title}
      inline={chat.projectName}
      meta={outcome}
      title={`${chat.title} · ${chat.projectName}`}
      onClick={() => void openThread(chat.threadId, chat.projectId)}
      trailing={chat.followed ? (
        <IconButton
          size={24}
          icon={busy ? <Spinner size={14} /> : <Unlink {...ACTION_GLYPH} />}
          label={label}
          tooltipLabel={label}
          disabled={disabled}
          aria-busy={busy || undefined}
          onClick={onUnfollow}
        />
      ) : undefined}
    />
  )
}
