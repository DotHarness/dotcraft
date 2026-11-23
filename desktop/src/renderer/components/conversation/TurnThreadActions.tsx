import { memo } from 'react'
import { useConversationStore } from '../../stores/conversationStore'
import {
  isThreadActionToolItem,
  parseThreadToolAction,
  threadActionProjectKey,
  type ThreadToolAction
} from '../../utils/threadToolDisplay'
import { ThreadActionCard } from './ThreadActionCard'

interface TurnThreadActionsProps {
  turnId: string
}

export const TurnThreadActions = memo(function TurnThreadActions({ turnId }: TurnThreadActionsProps): JSX.Element | null {
  const turn = useConversationStore((s) => s.turns.find((t) => t.id === turnId))
  if (!turn) return null

  const actions = new Map<string, ThreadToolAction>()
  for (const item of turn.items) {
    if (!isThreadActionToolItem(item)) continue
    const action = parseThreadToolAction(item)
    if (!action) continue
    const key = JSON.stringify([action.kind, threadActionProjectKey(action), action.threadId])
    if (!actions.has(key)) actions.set(key, action)
  }

  if (actions.size === 0) return null

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginTop: '8px' }}>
      {Array.from(actions, ([key, action]) => (
        <ThreadActionCard key={key} action={action} />
      ))}
    </div>
  )
})
