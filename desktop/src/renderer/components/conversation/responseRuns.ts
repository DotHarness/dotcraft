import type { ConversationTurn } from '../../types/conversation'
import { isSubAgentMailbox, isVisibleUserMessage } from '../../utils/visibleUserMessage'
import { getActivityElapsedMs, getTaskWake, getTurnActivityState } from './turnActivityModel'

export interface ResponsePlacement {
  leading: readonly ConversationTurn[]
  absorbed: boolean
}

const SINGLE: ResponsePlacement = { leading: [], absorbed: false }

function continuesResponse(previous: ConversationTurn, turn: ConversationTurn): boolean {
  const input = turn.items.find((item) => item.type === 'userMessage')
  return previous.status === 'completed'
    && getTurnActivityState(previous, previous.items, false).finalIndex < 0
    && (getTaskWake(turn) != null || (input != null && input.deliveryMode !== 'guidance' && isSubAgentMailbox(input)))
    && !turn.items.some(isVisibleUserMessage)
}

export function placeResponses(
  turns: readonly ConversationTurn[],
  options: { shown(turn: ConversationTurn): boolean; separated(turn: ConversationTurn): boolean }
): Map<string, ResponsePlacement> {
  const placements = new Map<string, ResponsePlacement>()
  let previous: ConversationTurn | undefined
  let run: ConversationTurn[] = []
  const close = (): void => {
    run.forEach((turn, index) => placements.set(turn.id, index === run.length - 1
      ? run.length === 1 ? SINGLE : { leading: run.slice(0, -1), absorbed: false }
      : { leading: [], absorbed: true }))
    run = []
  }
  for (const turn of turns) {
    if (!previous || options.separated(turn) || !continuesResponse(previous, turn)) close()
    previous = turn
    if (options.shown(turn)) run.push(turn)
  }
  close()
  return placements
}

/** Start time that makes the last turn's own clock read the response's summed duration. */
export function responseStartedAt(leading: readonly ConversationTurn[], startedAt: string | undefined): string | undefined {
  const start = startedAt ? Date.parse(startedAt) : Number.NaN
  if (!Number.isFinite(start)) return startedAt
  const earlier = leading.reduce((sum, turn) => sum + (getActivityElapsedMs(turn.startedAt, turn.completedAt) ?? 0), 0)
  return earlier > 0 ? new Date(start - earlier).toISOString() : startedAt
}

export function sameTurns(a: readonly ConversationTurn[], b: readonly ConversationTurn[]): boolean {
  return a.length === b.length && a.every((turn, index) => turn === b[index])
}
