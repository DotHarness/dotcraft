import type { ConversationItem, ConversationTurn } from '../../types/conversation'

export type TurnActivityStatus = 'working' | 'worked' | 'stopped'

export function getTurnActivityState(turn: ConversationTurn, items: ConversationItem[], isRunning: boolean) {
  let finalIndex = -1
  if (turn.status !== 'failed') {
    for (let index = items.length - 1; index >= 0; index--) {
      const item = items[index]
      if (item.type === 'agentMessage' && (item.phase === 'final' || (!isRunning && turn.status === 'completed' && item.phase == null))) {
        finalIndex = index
        break
      }
    }
  }
  const status: TurnActivityStatus | null = turn.status === 'cancelled' ? 'stopped'
    : finalIndex >= 0 || turn.status === 'completed' ? 'worked'
      : isRunning ? 'working' : null
  const finalStartedAt = finalIndex >= 0 ? items[finalIndex].createdAt : undefined
  return {
    status,
    finalIndex,
    completedAt: status === 'worked' ? finalStartedAt ?? turn.completedAt : turn.completedAt
  }
}

export function getActivityElapsedMs(startedAt: string | undefined, endedAt: string | undefined): number | undefined {
  if (!startedAt || !endedAt) return undefined
  const elapsed = Date.parse(endedAt) - Date.parse(startedAt)
  return Number.isFinite(elapsed) && elapsed >= 0 ? elapsed : undefined
}
