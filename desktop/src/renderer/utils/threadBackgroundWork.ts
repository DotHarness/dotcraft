import type { ConversationTurn } from '../types/conversation'
import { isSubAgentChildRunning, useSubAgentStore } from '../stores/subAgentStore'
import { useWorkflowRunStore } from '../stores/workflowRunStore'
import { resolveShellBackgroundState } from './toolCallAggregation'

export function hasRunningBackgroundWork(threadId: string, turns: ConversationTurn[]): boolean {
  const terminalRunning = turns.some((turn) =>
    turn.items.some((item) => item.type === 'toolCall' && resolveShellBackgroundState(item) === 'backgroundRunning')
  )
  if (terminalRunning) return true
  if ((useSubAgentStore.getState().childrenByParent.get(threadId) ?? []).some(isSubAgentChildRunning)) return true
  for (const entry of useWorkflowRunStore.getState().entries.values()) {
    if (entry.run?.threadId === threadId && entry.run.status === 'running') return true
  }
  return false
}
