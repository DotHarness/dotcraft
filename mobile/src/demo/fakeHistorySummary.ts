interface FakeItem {
  turnId: string
  type: string
  status: string
  payload?: Record<string, unknown>
}

interface SummaryTool {
  name: string
  namespace?: string | null
}

const ALWAYS = new Set(['userMessage', 'agentMessage', 'error', 'systemNotice', 'webSearch'])

/** Adds summary Items to a Turn page the way the AppServer does for `itemsView: "summary"`. */
export function withSummaries<T extends { id: string }, I extends FakeItem>(turns: T[], items: I[], params: Record<string, unknown>): T[] {
  if (params.itemsView !== 'summary') return turns
  const tools = (params.summaryTools as SummaryTool[] | undefined) ?? []
  return turns.map((turn) => {
    const own = items.filter((item) => item.turnId === turn.id)
    return { ...turn, itemsView: 'summary', itemCount: own.length, items: summaryItems(own, tools) }
  })
}

function summaryItems<I extends FakeItem>(items: I[], tools: SummaryTool[]): I[] {
  const named = (payload: Record<string, unknown>) =>
    tools.some((tool) => tool.name === payload.toolName && (tool.namespace ?? '') === ((payload.namespace as string | undefined) ?? ''))
  const kept = new Set<unknown>()
  for (const { type, payload = {} } of items) {
    const content = payload.structuredContent as { kind?: unknown } | undefined
    if ((type === 'toolResult' && content?.kind === 'fileChange') || (type === 'toolCall' && named(payload))) kept.add(payload.callId)
  }
  return items.filter(({ type, status, payload = {} }) => {
    if (ALWAYS.has(type)) return true
    if (type === 'imageGeneration') return payload.status !== 'inProgress'
    if (type === 'mcpToolCall') return status === 'completed' && String(payload.mcpAppResourceUri ?? '').startsWith('ui://')
    if (type === 'dynamicToolCall') return named(payload)
    if (type === 'toolCall' || type === 'toolResult') return kept.has(payload.callId)
    return false
  })
}
