import { memo } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import {
  isThreadActionToolItem,
  parseThreadToolAction,
  type ThreadToolAction
} from '../../utils/threadToolDisplay'
import { ThreadActionCard } from './ThreadActionCard'

interface TurnThreadActionsProps {
  turnId: string
}

/** "Chat created" / "Message sent" cards, shown before the agent footer alongside the file-artifact cards. */
export const TurnThreadActions = memo(function TurnThreadActions({ turnId }: TurnThreadActionsProps): JSX.Element | null {
  const turn = useConversationStore((s) => s.turns.find((t) => t.id === turnId))
  if (!turn) return null

  const actions: ThreadToolAction[] = []
  const seen = new Set<string>()
  for (const item of turn.items) {
    if (!isThreadActionToolItem(item)) continue
    const action = parseThreadToolAction(item)
    if (!action) continue
    const key = `${action.kind}:${action.threadId}`
    if (seen.has(key)) continue
    seen.add(key)
    actions.push(action)
  }

  if (actions.length === 0) return null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '8px' }}>
      {actions.map((action) => (
        <ThreadActionCard key={`${action.kind}:${action.threadId}`} action={action} />
      ))}
    </div>
  )
})
