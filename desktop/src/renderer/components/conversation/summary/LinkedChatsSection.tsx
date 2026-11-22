import { MessageSquare } from 'lucide-react'
import { useT } from '../../../contexts/LocaleContext'
import { useLinkedChats } from '../../../stores/linkedChatsStore'
import type { LinkedChatStatus, LinkedChatSummary } from '../../../../shared/linkedChats'
import { openThread } from '../../sidebar/projectConnectionActions'
import { GLYPH, Row, Section } from './SummaryRows'

const GROUPS = [
  { title: 'summary.chats.needsYou', statuses: ['waitingForInput'] },
  { title: 'summary.chats.inProgress', statuses: ['running', 'idle'] },
  { title: 'summary.chats.done', statuses: ['completed', 'failed', 'stopped'] }
] as const satisfies ReadonlyArray<{ title: string; statuses: readonly LinkedChatStatus[] }>

const OUTCOME = {
  failed: 'threadActionCard.status.failed',
  stopped: 'threadActionCard.status.stopped'
} as const

export function LinkedChatsSection({ threadId }: { threadId: string }): JSX.Element {
  const t = useT()
  const chats = useLinkedChats(threadId)
  const sorted = [...chats].sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  return (
    <>
      {GROUPS.map((group) => {
        const rows = sorted.filter((chat) => (group.statuses as readonly LinkedChatStatus[]).includes(chat.status))
        return rows.length ? (
          <Section key={group.title} title={t(group.title)}>
            {rows.map((chat) => <ChatRow key={chat.threadId} chat={chat} />)}
          </Section>
        ) : null
      })}
    </>
  )
}

function ChatRow({ chat }: { chat: LinkedChatSummary }): JSX.Element {
  const t = useT()
  const outcome = chat.status === 'failed' || chat.status === 'stopped' ? t(OUTCOME[chat.status]) : undefined
  return (
    <Row
      leading={<MessageSquare {...GLYPH} />}
      label={chat.title}
      inline={chat.projectName}
      meta={outcome}
      title={`${chat.title} · ${chat.projectName}`}
      onClick={() => void openThread(chat.threadId, chat.projectId)}
    />
  )
}
