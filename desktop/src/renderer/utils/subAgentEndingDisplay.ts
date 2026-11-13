import type { ConversationItem } from '../types/conversation'
import { isSubAgentChildWorking, type SubAgentChild } from '../stores/subAgentStore'
import { findSubAgentChild, type SubAgentLookupSources } from './subAgentIdentity'
import { getTaskStopDisplay } from './taskStopDisplay'

export function subAgentEndingOutcome(status: string | undefined, child: SubAgentChild | null): 'finished' | 'interrupted' | null {
  if (status === 'failed' || status === 'cancelled') return 'interrupted'
  return status === 'completed' && child && isSubAgentChildWorking(child) ? null : 'finished'
}

export function subAgentEndingEntries(
  items: readonly ConversationItem[], lookup: SubAgentLookupSources, running: boolean, fallbackName: string
) {
  return items.flatMap(item => {
    const stop = getTaskStopDisplay(item, running)
    const path = stop?.taskId ?? item.triggerRefId ?? null
    const child = findSubAgentChild(lookup, null, path, stop ? 'tree' : 'children')
    const outcome = stop?.stopping ? 'stopping' : stop ? 'interrupted' : subAgentEndingOutcome(item.triggerStatus, child)
    if (outcome === null) return []
    const name = child?.nickname ?? (stop
      ? stop.label ?? path?.split('/').filter(Boolean).at(-1) ?? fallbackName
      : item.triggerLabel?.trim() ?? item.triggerRefId ?? '')
    return [{ id: item.id, callId: item.toolCallId, name, prompt: '', childThreadId: child?.childThreadId ?? null, outcome }]
  })
}
