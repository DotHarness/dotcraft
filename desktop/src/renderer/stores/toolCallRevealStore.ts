import { create } from 'zustand'
import type { ConversationTurn } from '../types/conversation'

type ToolCallRevealMatch = { callId: string } | { terminalSessionId: string }

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
    const item = turn.items.find((candidate) => candidate.toolCallId != null && ('callId' in match
      ? candidate.toolCallId === match.callId
      : candidate.terminalSessionId === match.terminalSessionId))
    if (item?.toolCallId) return { threadId: request.threadId, turnId: turn.id, callId: item.toolCallId, nonce: request.nonce }
  }
  return null
}

export function useToolCallRevealTarget<T>(select: (target: ToolCallRevealTarget) => T | null): T | null {
  return useToolCallRevealStore((state) => state.target ? select(state.target) : null)
}
