import type { ConversationItem, ConversationTurn } from '../types/conversation'

export type SubAgentEndings = ReadonlyMap<string, readonly number[]>

function isSubAgentEndingNotice(item: ConversationItem): boolean {
  return item.type === 'userMessage' && item.triggerKind === 'subagent' && !!item.triggerRefId
}

export function isMidTurnSubAgentEnding(item: ConversationItem): boolean {
  return isSubAgentEndingNotice(item) && item.deliveryMode === 'guidance'
}

export function subAgentEndingInterrupted(item: ConversationItem): boolean {
  return item.triggerStatus === 'failed' || item.triggerStatus === 'cancelled'
}

export function collectSubAgentEndings(turns: readonly ConversationTurn[], threadId: string): SubAgentEndings {
  const endings = new Map<string, number[]>()
  for (const turn of turns) {
    if (turn.threadId !== threadId) continue
    for (const item of turn.items) {
      if (!isSubAgentEndingNotice(item)) continue
      const times = endings.get(item.triggerRefId!) ?? []
      times.push(item.createdAt ? Date.parse(item.createdAt) : Number.POSITIVE_INFINITY)
      endings.set(item.triggerRefId!, times)
    }
  }
  return endings
}

/** An ending without a usable time counts as later, since notifications only follow the spawn. */
export function hasEndingAfter(endings: SubAgentEndings, agentPath: string | null | undefined, since: string | undefined): boolean {
  const times = agentPath ? endings.get(agentPath) : undefined
  if (!times?.length) return false
  const start = since ? Date.parse(since) : Number.NaN
  return !Number.isFinite(start) || times.some((time) => !(time < start))
}
