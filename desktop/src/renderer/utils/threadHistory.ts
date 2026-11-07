import type { Thread, Turn } from '../types/thread'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'

export const THREAD_OPENING_PAGE = {
  limit: 5,
  itemsView: 'summary' as const,
  summaryTools: [
    { name: 'CreatePlan' },
    { name: 'Workflow' },
    { name: 'Automation' },
    { name: 'CreateThread', namespace: 'desktop' },
    { name: 'SendMessageToThread', namespace: 'desktop' }
  ]
}

const TURN_ITEM_PAGE_LIMIT = 100

export type TurnItemsView = 'none' | 'summary' | 'full'
export type TurnSortDirection = 'ascending' | 'descending'

interface HistoryPage<T> {
  data?: T[]
  nextCursor?: string | null
}

interface TurnsListResult extends HistoryPage<Turn> {
  backwardsCursor?: string | null
}

interface ThreadItemEntry {
  turnId: string
  item: Record<string, unknown>
}

export interface TurnItemsPage {
  items: Array<Record<string, unknown>>
  nextCursor: string | null
}

export interface ThreadTurnsListing {
  turns: Turn[]
  nextCursor: string | null
  backwardsCursor: string | null
}

export interface ThreadHistoryRead {
  thread: Thread
  turnCursor: string | null
}

export type HistoryRequest = (
  method: keyof ClientRequestMethods,
  // The generated API correlates method and params at each request boundary.
  params: any,
  options?: { onResponse?: (result: any) => void }
) => Promise<any>

export function createHistoryReadQueue(concurrency: number) {
  let active = 0
  const waiting: Array<() => void> = []
  return async <T>(read: () => Promise<T>, isCurrent = () => true): Promise<T> => {
    if (active >= concurrency) await new Promise<void>((resolve) => waiting.push(resolve))
    else active += 1
    try {
      if (!isCurrent()) throw new Error('History read is no longer needed')
      return await read()
    } finally {
      const next = waiting.shift()
      if (next) next()
      else active -= 1
    }
  }
}

export const readHistoryPreview = createHistoryReadQueue(2)

export async function readTurnItemsPage(
  request: HistoryRequest,
  threadId: string,
  turnId: string,
  cursor: string | null = null,
  onPage?: (page: TurnItemsPage) => void
): Promise<TurnItemsPage> {
  const parse = (page: HistoryPage<ThreadItemEntry>): TurnItemsPage => {
    const nextCursor = page.nextCursor ?? null
    if (nextCursor !== null && nextCursor === cursor) {
      throw new Error(`thread/items/list returned an unchanged cursor for turn ${turnId}`)
    }
    return { items: (page.data ?? []).map((entry) => entry.item), nextCursor }
  }
  const page = await request('thread/items/list', {
    threadId,
    turnId,
    cursor,
    limit: TURN_ITEM_PAGE_LIMIT,
    sortDirection: 'ascending'
  }, onPage ? { onResponse: (result) => onPage(parse(result)) } : undefined) as HistoryPage<ThreadItemEntry>
  return parse(page)
}

/** Only explicit complete expansion exhausts the Turn's Item cursor. */
export async function readAllTurnItems(
  request: HistoryRequest,
  threadId: string,
  turnId: string,
  isCurrent = () => true
): Promise<Array<Record<string, unknown>>> {
  const items = new Map<string, Record<string, unknown>>()
  let cursor: string | null = null
  do {
    if (!isCurrent()) return [...items.values()]
    const page = await readTurnItemsPage(request, threadId, turnId, cursor)
    for (const item of page.items) items.set(String(item.id), item)
    cursor = page.nextCursor
  } while (cursor !== null)
  return [...items.values()]
}

export async function listThreadTurns(
  request: HistoryRequest,
  threadId: string,
  cursor: string | null,
  sortDirection: TurnSortDirection,
  limit = THREAD_OPENING_PAGE.limit,
  itemsView: TurnItemsView = 'none'
): Promise<ThreadTurnsListing> {
  const page = await request('thread/turns/list', {
    threadId,
    cursor,
    limit,
    sortDirection,
    itemsView,
    ...(itemsView === 'summary' ? { summaryTools: THREAD_OPENING_PAGE.summaryTools } : {})
  }) as TurnsListResult
  return {
    turns: page.data ?? [],
    nextCursor: page.nextCursor ?? null,
    backwardsCursor: page.backwardsCursor ?? null
  }
}

export function readThreadHistoryHead(
  request: HistoryRequest,
  threadId: string,
  turnLimit = THREAD_OPENING_PAGE.limit,
  itemsView: TurnItemsView = 'summary',
  isCurrent = () => true
): Promise<ThreadHistoryRead> {
  return readHistoryPreview(async () => {
    const [page, readResult] = await Promise.all([
      listThreadTurns(request, threadId, null, 'descending', turnLimit, itemsView),
      request('thread/read', { threadId }) as Promise<{ thread: Thread }>
    ])
    return {
      thread: { ...readResult.thread, turns: [...page.turns].reverse() },
      turnCursor: page.nextCursor
    }
  }, isCurrent)
}
