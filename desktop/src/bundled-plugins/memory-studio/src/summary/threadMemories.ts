import type { SessionItem, ThreadTurnsListResult } from '@dotcraft/sdk/contracts'
import { baseName, comparablePath } from '../data/hostPath'
import { isMemoryFile } from '../data/stores'
import type { Rpc } from '../data/rpc'

export interface MemoryStoreRef {
  id: string
  name: string
}

export interface ThreadMemory {
  fileName: string
  /** The path the tool call named, as the agent wrote it. */
  path: string
  /** Set for the personal store or an attached store, or null for the Thread's own store. */
  store: MemoryStoreRef | null
  lastUsedAt: string
}

/** The file tools whose calls Core marks as memory operations and that name one file. */
export const MEMORY_FILE_TOOLS = ['ReadFile', 'WriteFile', 'EditFile'] as const

const MARKED_PRESENTATIONS = new Set(['core.read-file', 'core.file-write'])
const MAX_PAGES = 20

type JsonRecord = Record<string, unknown>

function record(value: unknown): JsonRecord | null {
  return value != null && typeof value === 'object' && !Array.isArray(value) ? value as JsonRecord : null
}

function storeRef(value: unknown): MemoryStoreRef | null {
  const store = record(value)
  return store && typeof store.id === 'string' && typeof store.name === 'string' ? { id: store.id, name: store.name } : null
}

/** Memories a Thread read or wrote, most recent first, read only from Core's trusted memory marks. */
export function collectThreadMemories(items: readonly SessionItem[], workspacePath: string | null): ThreadMemory[] {
  const failedCalls = new Set<string>()
  for (const item of items) {
    const payload = record(item.payload)
    if (item.type === 'toolResult' && payload?.success === false && typeof payload.callId === 'string') failedCalls.add(payload.callId)
  }

  const memories = new Map<string, ThreadMemory>()
  for (const item of items) {
    if (item.type !== 'toolCall') continue
    const payload = record(item.payload)
    const presentation = record(payload?.presentation)
    const options = record(presentation?.options)
    if (record(payload?.source)?.kind !== 'CoreNative' || !MARKED_PRESENTATIONS.has(String(presentation?.presentationId))) continue
    const mark = options?.memory
    if (mark !== 'recall' && mark !== 'update') continue
    if (mark === 'update' && typeof payload?.callId === 'string' && failedCalls.has(payload.callId)) continue
    const path = record(payload?.arguments)?.path
    if (typeof path !== 'string' || !path.trim()) continue
    const fileName = baseName(path.trim())
    if (!isMemoryFile(fileName)) continue

    const store = storeRef(options?.memoryStore)
    const key = `${store?.id ?? ''}|${comparablePath(path.trim(), workspacePath)}`
    const memory = memories.get(key) ?? { fileName, path: path.trim(), store, lastUsedAt: item.createdAt }
    if (item.createdAt > memory.lastUsedAt) memory.lastUsedAt = item.createdAt
    memories.set(key, memory)
  }
  return [...memories.values()].sort((a, b) => b.lastUsedAt.localeCompare(a.lastUsedAt))
}

/** Reads the Thread's summary history, which carries the named file tool calls with their results. */
export async function readThreadMemoryItems(rpc: Rpc, threadId: string): Promise<SessionItem[]> {
  const items: SessionItem[] = []
  let cursor: string | null | undefined
  for (let page = 0; page < MAX_PAGES; page++) {
    const result: ThreadTurnsListResult = await rpc.request('thread/turns/list', {
      threadId,
      limit: 100,
      sortDirection: 'descending',
      itemsView: 'summary',
      summaryTools: MEMORY_FILE_TOOLS.map((name) => ({ name })),
      ...(cursor ? { cursor } : {})
    })
    for (const turn of result.data) items.push(...(turn.items ?? []))
    cursor = result.nextCursor
    if (!cursor) break
  }
  return items
}
