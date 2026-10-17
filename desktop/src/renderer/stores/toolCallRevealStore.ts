import { create } from 'zustand'
import type { ConversationItem, ConversationTurn } from '../types/conversation'
import { parseToolResultObject } from '../utils/toolCallDisplay'
import { resolveToolKind } from '../utils/toolKind'
import { parseWorkflowRunId } from '../components/workflow/WorkflowToolCard'

export type ToolCallRevealMatch =
  | { callId: string }
  | { terminalSessionId: string }
  | { agentPath: string }
  | { workflowRunId: string }

interface ToolCallRevealRequest {
  threadId: string
  match: ToolCallRevealMatch
  nonce: number
}

export interface ToolCallRevealTarget {
  threadId: string
  turnId: string
  callId: string
  nonce: number
}

interface ToolCallRevealState {
  request: ToolCallRevealRequest | null
  target: ToolCallRevealTarget | null
}

export const useToolCallRevealStore = create<ToolCallRevealState>(() => ({ request: null, target: null }))

let nextNonce = 1

export function requestToolCallReveal(threadId: string, match: ToolCallRevealMatch): void {
  useToolCallRevealStore.setState({ request: { threadId, match, nonce: nextNonce++ }, target: null })
}

export function finishToolCallReveal(nonce: number): void {
  const { request, target } = useToolCallRevealStore.getState()
  if (request?.nonce === nonce || target?.nonce === nonce) useToolCallRevealStore.setState({ request: null, target: null })
}

export function findToolCallRevealTarget(
  turns: readonly ConversationTurn[],
  request: ToolCallRevealRequest
): ToolCallRevealTarget | null {
  const { match } = request
  for (const turn of turns) {
    const item = turn.items.find((candidate) => candidate.toolCallId != null && matchesToolCall(candidate, match))
    if (item?.toolCallId) return { threadId: request.threadId, turnId: turn.id, callId: item.toolCallId, nonce: request.nonce }
  }
  return null
}

function matchesToolCall(item: ConversationItem, match: ToolCallRevealMatch): boolean {
  if ('callId' in match) return item.toolCallId === match.callId
  if ('terminalSessionId' in match) return item.terminalSessionId === match.terminalSessionId
  if ('agentPath' in match) {
    return resolveToolKind(item) === 'spawnAgent' && parseToolResultObject(item.result)?.agentPath === match.agentPath
  }
  return parseWorkflowRunId(item.toolName ?? '', item.result) === match.workflowRunId
}

export function useToolCallRevealTarget<T>(select: (target: ToolCallRevealTarget) => T | null): T | null {
  return useToolCallRevealStore((state) => state.target ? select(state.target) : null)
}
