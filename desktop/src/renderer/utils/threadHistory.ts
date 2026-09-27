import type { Thread, Turn } from '../types/thread'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'

/** Turns per history page; small enough for a fast first paint, whole Turns either way. */
const HISTORY_TURN_PAGE_LIMIT = 5
/** Items per request while hydrating one Turn; equals the server's max page limit. */
const TURN_ITEM_PAGE_LIMIT = 500
/** How many Turns of a page hydrate their Items concurrently. */
const TURN_HYDRATION_CONCURRENCY = 5

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
  // Each caller forwards the correlated method/params pair directly to the
  // generated AppServer request API at this dynamic adapter boundary.
  params: any
) => Promise<any>

/** Reads every Item of one Turn, paging until the Turn-scoped cursor is exhausted. */
export async function readTurnItems(
  request: HistoryRequest,
  threadId: string,
  turnId: string
): Promise<Array<Record<string, unknown>>> {
  const items: Array<Record<string, unknown>> = []
  let cursor: string | null = null
  do {
    const page = await request('thread/items/list', {
      threadId,
      turnId,
      cursor,
      limit: TURN_ITEM_PAGE_LIMIT,
      sortDirection: 'ascending'
    }) as HistoryPage<ThreadItemEntry>
    for (const entry of page.data ?? []) items.push(entry.item)
    const next = page.nextCursor ?? null
    if (next !== null && next === cursor) {
      throw new Error(`thread/items/list returned an unchanged cursor for turn ${turnId}`)
    }
    cursor = next
  } while (cursor !== null)
  return items
}

export async function listThreadTurns(
  request: HistoryRequest,
  threadId: string,
  cursor: string | null,
  sortDirection: TurnSortDirection,
  limit = HISTORY_TURN_PAGE_LIMIT
): Promise<ThreadTurnsListing> {
  const page = await request('thread/turns/list', {
    threadId,
    cursor,
    limit,
    sortDirection
  }) as TurnsListResult
  return {
    turns: page.data ?? [],
    nextCursor: page.nextCursor ?? null,
    backwardsCursor: page.backwardsCursor ?? null
  }
}

/** Paging by Turn keeps a page from ever cutting a Turn in half; the Item cursor only advances inside one Turn. */
export async function hydrateTurns(
  request: HistoryRequest,
  threadId: string,
  turns: readonly Turn[]
): Promise<Turn[]> {
  const hydrated = new Array<Turn>(turns.length)
  const hydrateFrom = async (index: number): Promise<void> => {
    const turn = turns[index]
    if (!turn) return
    hydrated[index] = { ...turn, items: await readTurnItems(request, threadId, turn.id) }
    await hydrateFrom(index + TURN_HYDRATION_CONCURRENCY)
  }
  await Promise.all(
    Array.from(
      { length: Math.min(turns.length, TURN_HYDRATION_CONCURRENCY) },
      (_unused, index) => hydrateFrom(index)
    )
  )
  return hydrated
}

/** Reads the Thread header plus its newest fully hydrated Turns. */
export async function readThreadHistoryHead(
  request: HistoryRequest,
  threadId: string,
  turnLimit = HISTORY_TURN_PAGE_LIMIT
): Promise<ThreadHistoryRead> {
  const readTurns = async (): Promise<{ turns: Turn[]; nextCursor: string | null }> => {
    const page = await listThreadTurns(request, threadId, null, 'descending', turnLimit)
    const turns = await hydrateTurns(request, threadId, page.turns)
    return { turns: turns.reverse(), nextCursor: page.nextCursor }
  }
  const [turnsPage, readResult] = await Promise.all([
    readTurns(),
    request('thread/read', { threadId }) as Promise<{ thread: Thread }>
  ])

  return {
    thread: { ...readResult.thread, turns: turnsPage.turns },
    turnCursor: turnsPage.nextCursor
  }
}
