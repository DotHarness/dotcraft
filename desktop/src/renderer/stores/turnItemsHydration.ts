import type { ConversationItem, ConversationTurn } from '../types/conversation'

function mergeItem(incoming: ConversationItem, existing: ConversationItem | undefined): ConversationItem {
  if (!existing) return incoming
  const merged = { ...existing, ...incoming }
  if (existing.type !== incoming.type) return merged
  if (existing.status === 'completed' && incoming.status !== 'completed') {
    return { ...incoming, ...existing }
  }
  const terminalFinished = existing.executionStatus != null && existing.executionStatus !== 'inProgress'
    && existing.terminalRunning !== true
  if (terminalFinished && (incoming.executionStatus == null || incoming.executionStatus === 'inProgress'
    || incoming.terminalRunning === true)) {
    return {
      ...merged,
      executionStatus: existing.executionStatus,
      terminalRunning: false,
      terminalSessionId: existing.terminalSessionId ?? incoming.terminalSessionId,
      aggregatedOutput: existing.aggregatedOutput ?? incoming.aggregatedOutput,
      exitCode: existing.exitCode ?? incoming.exitCode,
      duration: existing.duration ?? incoming.duration,
      result: existing.result ?? incoming.result,
      resultPreview: existing.resultPreview ?? incoming.resultPreview,
      terminalInputs: existing.terminalInputs ?? incoming.terminalInputs
    }
  }
  return merged
}

export function mergeTurnItemsPage(
  turn: ConversationTurn,
  page: ConversationItem[],
  nextCursor: string | null
): ConversationTurn {
  const byId = new Map(turn.items.map(item => [item.id, item]))
  const merged = page.map(item => mergeItem(item, byId.get(item.id)))
  const loaded = new Set(page.map(item => item.id))
  merged.push(...turn.items.filter(item => !loaded.has(item.id)))
  merged.sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt))
  return {
    ...turn,
    items: merged,
    summary: nextCursor == null ? undefined : {
      itemCount: Math.max(turn.summary?.itemCount ?? 0, merged.length + 1),
      nextCursor
    }
  }
}

export function mergeRestoredTurns(
  existing: readonly ConversationTurn[],
  incoming: readonly ConversationTurn[]
): ConversationTurn[] {
  const byId = new Map(existing.map(turn => [turn.id, turn]))
  return incoming.map(turn => {
    const previous = byId.get(turn.id)
    if (!previous || previous.threadId !== turn.threadId) return turn
    const oldItems = new Map(previous.items.map(item => [item.id, item]))
    const items = turn.items.map(item => mergeItem(item, oldItems.get(item.id)))
    if (turn.summary && turn.summary.itemCount >= previous.items.length) {
      const known = new Set(items.map(item => item.id))
      items.push(...previous.items.filter(item => !known.has(item.id)))
      items.sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt))
    }
    const terminal = previous.status === 'completed' || previous.status === 'failed' || previous.status === 'cancelled'
    const incomingLive = turn.status === 'running' || turn.status === 'waitingApproval' || turn.status === 'waitingInput'
    return {
      ...turn,
      ...(terminal && incomingLive ? {
        status: previous.status,
        completedAt: previous.completedAt,
        error: previous.error,
        providerError: previous.providerError,
        cancelReason: previous.cancelReason
      } : {}),
      items,
      summary: turn.summary && items.length < turn.summary.itemCount ? { ...turn.summary } : undefined
    }
  })
}

export function hydratedStreamingState(
  activeTurnId: string | null,
  activeItemId: string | null,
  turn: ConversationTurn,
  page: ConversationItem[]
): { activeItemId?: string; streamingMessage?: string; streamingReasoning?: string } {
  if (activeTurnId !== turn.id) return {}
  const active = page.find(item => item.id === activeItemId)
    ?? (activeItemId == null ? page.findLast(item => item.status !== 'completed'
      && (item.type === 'agentMessage' || item.type === 'reasoningContent')) : undefined)
  if (active?.status === 'completed') return {}
  if (active?.type === 'agentMessage') return { activeItemId: active.id, streamingMessage: active.text ?? '' }
  if (active?.type === 'reasoningContent') return { activeItemId: active.id, streamingReasoning: active.reasoning ?? '' }
  return {}
}
