import type { JSX } from 'react'
import type { ThreadTask } from '../../../../shared/threadTasks'
import { useThreadTasks } from '../../../stores/threadTasksStore'
import type { ConversationItem } from '../../../types/conversation'
import { ThreadTaskOfferCard, ThreadTaskProposalCard } from './ThreadTaskCard'

export function TurnThreadTaskCards({ threadId, items }: { threadId: string; items: ConversationItem[] }): JSX.Element | null {
  const snapshot = useThreadTasks(threadId)
  if (!snapshot) return null
  const order = items.map((item) => item.toolCallId)
  const tasks = snapshot.tasks
    .filter((task) => task.toolCallId !== undefined && order.includes(task.toolCallId))
    .sort((a, b) => order.indexOf(a.toolCallId) - order.indexOf(b.toolCallId))
  const offers = tasks.filter((task) => task.kind === 'offer')
  const proposals = tasks.filter((task: ThreadTask) => task.kind === 'proposal')
  if (tasks.length === 0) return null
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px' }}>
      {offers.map((task) => <ThreadTaskOfferCard key={task.id} task={task} />)}
      {proposals.length > 0 ? <ThreadTaskProposalCard tasks={proposals} /> : null}
    </div>
  )
}
