import type { Thread, Turn } from '../types/thread'

export interface ThreadHistoryPage<T> {
  data?: T[]
  nextCursor?: string | null
}

export interface ThreadItemEntry {
  turnId: string
  item: Record<string, unknown>
}

export interface ThreadHistoryRead {
  thread: Thread
  turnCursor: string | null
  itemCursor: string | null
}

function turnTime(turn: Turn): number {
  const value = (turn as Turn & { startedAt?: string }).startedAt ?? turn.createdAt
  const parsed = Date.parse(value)
  return Number.isFinite(parsed) ? parsed : 0
}

type Request = (method: string, params: Record<string, unknown>) => Promise<unknown>

/** Reads a bounded display-history head and merges separately paged turns and items. */
export async function readThreadHistoryHead(
  request: Request,
  threadId: string,
  turnLimit = 20,
  itemLimit = 100
): Promise<ThreadHistoryRead> {
  const [turnsResult, itemsResult, readResult] = await Promise.all([
    request('thread/turns/list', { threadId, limit: turnLimit, sortDirection: 'descending' }),
    request('thread/items/list', { threadId, limit: itemLimit, sortDirection: 'descending' }),
    request('thread/read', { threadId })
  ]) as [
    ThreadHistoryPage<Turn>,
    ThreadHistoryPage<ThreadItemEntry>,
    { thread: Thread }
  ]

  const itemsByTurn = new Map<string, Record<string, unknown>[]>()
  for (const entry of [...(itemsResult.data ?? [])].reverse()) {
    const items = itemsByTurn.get(entry.turnId) ?? []
    items.push(entry.item)
    itemsByTurn.set(entry.turnId, items)
  }
  const turns = [...(turnsResult.data ?? [])].reverse().map((turn) => ({
    ...turn,
    items: itemsByTurn.get(turn.id) ?? []
  }))
  const knownTurnIds = new Set(turns.map((turn) => turn.id))
  for (const [turnId, items] of itemsByTurn) {
    if (knownTurnIds.has(turnId)) continue
    const first = items[0]
    turns.push({
      id: turnId,
      threadId,
      status: 'completed',
      createdAt: typeof first?.createdAt === 'string' ? first.createdAt : new Date(0).toISOString(),
      items
    })
  }
  turns.sort((a, b) => turnTime(a) - turnTime(b))

  return {
    thread: { ...readResult.thread, turns },
    turnCursor: turnsResult.nextCursor ?? null,
    itemCursor: itemsResult.nextCursor ?? null
  }
}
