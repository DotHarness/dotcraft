import { derivePluginFunctionResultText, normalizePluginFunctionContentItems, normalizeToolSourceProvenance } from '../../../types/conversation'
import type { Source, SummaryItem } from './sources'

export interface SourceToolGroup {
  readonly name: string
  readonly calls: readonly SourceToolCall[]
}

export interface SourceToolCall {
  readonly turnId: string
  readonly callId: string
  readonly status: 'running' | 'completed' | 'failed' | 'cancelled'
  readonly input?: unknown
  readonly output?: unknown
}

interface NamedCall {
  readonly name: string
  readonly call: SourceToolCall
}

export interface McpCallCollector {
  observeCall(item: SummaryItem, callId: string): Source | undefined
  observeResult(item: SummaryItem, callId: string): void
  groups(server: string): SourceToolGroup[] | undefined
}

// Calls are keyed by Turn because call ids are only unique within one Turn.
export function collectMcpCalls(): McpCallCollector {
  const servers = new Map<string, Map<string, NamedCall>>()
  const results = new Map<string, Readonly<Record<string, unknown>>>()
  return {
    observeCall(item, callId) {
      const source = mcpSourceOf(item)
      if (source) recordCall(servers, source.id, item, callId)
      return source
    },
    observeResult(item, callId) {
      results.set(`${item.turnId}:${callId}`, item.payload)
    },
    groups(server) {
      const calls = servers.get(server)
      return calls ? groupCalls(calls, results) : undefined
    }
  }
}

function mcpSourceOf(item: SummaryItem): Source | undefined {
  const payload = item.payload
  const provenance = isRecord(payload.source) ? payload.source : undefined
  const source = normalizeToolSourceProvenance(provenance)
  const server = item.type === 'mcpToolCall'
    ? stringOf(payload.server) ?? stringOf(provenance?.sourceId)
    : source?.kind.toLowerCase() === 'mcp' ? source.sourceId : undefined
  const pluginId = stringOf(provenance?.pluginId)
  return server ? { kind: 'mcp', id: server, title: server, target: null, ...(pluginId ? { pluginId } : {}) } : undefined
}

function recordCall(servers: Map<string, Map<string, NamedCall>>, server: string, item: SummaryItem, callId: string): void {
  const key = `${item.turnId}:${callId}`
  const calls = servers.get(server) ?? new Map<string, NamedCall>()
  const previous = calls.get(key)
  const { payload } = item
  const name = stringOf(payload.toolName) ?? previous?.name ?? stringOf(payload.sourceToolId)
  if (!name) return

  const status = callStatus(item)
  const input = payload.arguments ?? previous?.call.input
  const output = item.type === 'mcpToolCall' ? callOutput(payload, true) ?? previous?.call.output : previous?.call.output
  calls.set(key, {
    name,
    call: {
      turnId: item.turnId,
      callId,
      status: status === 'running' ? previous?.call.status ?? status : status,
      ...(input !== undefined ? { input } : {}),
      ...(output !== undefined ? { output } : {})
    }
  })
  servers.set(server, calls)
}

function callStatus({ payload, status }: SummaryItem): SourceToolCall['status'] {
  if (payload.status === 'cancelled') return 'cancelled'
  if (payload.status === 'failed' || payload.success === false || payload.isError === true) return 'failed'
  if (payload.status === 'completed' || payload.success === true || (payload.status === undefined && status === 'completed')) return 'completed'
  return 'running'
}

function groupCalls(
  calls: ReadonlyMap<string, NamedCall>,
  results: ReadonlyMap<string, Readonly<Record<string, unknown>>>
): SourceToolGroup[] {
  const groups = new Map<string, SourceToolCall[]>()
  for (const [key, { name, call }] of calls) {
    const result = results.get(key)
    const group = groups.get(name) ?? []
    if (result) {
      const output = call.output ?? callOutput(result, false)
      const status = call.status === 'cancelled' || call.status === 'failed'
        ? call.status
        : result.success === false ? 'failed' : 'completed'
      group.push({ ...call, status, ...(output !== undefined ? { output } : {}) })
    } else {
      group.push(call)
    }
    groups.set(name, group)
  }
  return Array.from(groups, ([name, calls]) => ({ name, calls }))
}

function callOutput(payload: Readonly<Record<string, unknown>>, lifecycle: boolean): unknown {
  const recordedContent = lifecycle ? payload.content : payload.contentItems
  const content = Array.isArray(recordedContent) && recordedContent.length > 0 ? recordedContent : undefined
  const nonText = content?.filter((block) => !isRecord(block) || block.type !== 'text')
  const structured = payload.structuredContent
  if (structured != null) return nonText?.length ? { structuredContent: structured, content: nonText } : structured
  if (nonText?.length) return content
  return stringOf(payload.result) ?? derivePluginFunctionResultText(
    normalizePluginFunctionContentItems(content ?? payload.modelContentItems), undefined,
    stringOf(payload.errorMessage) ?? undefined
  )
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === 'object' && !Array.isArray(value)
}

function stringOf(value: unknown): string | null {
  return typeof value === 'string' && value !== '' ? value : null
}
