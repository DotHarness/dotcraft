import type { Thread, Turn } from '../types/thread'

/** Turns per history page; small enough for a fast first paint, whole Turns either way. */
const HISTORY_TURN_PAGE_LIMIT = 5
/** Items per request while hydrating one Turn; equals the server's max page limit. */
const TURN_ITEM_PAGE_LIMIT = 500
/** How many Turns of a page hydrate their Items concurrently. */
const TURN_HYDRATION_CONCURRENCY = 5

interface HistoryPage<T> {
  data?: T[]
  nextCursor?: string | null
}

interface ThreadItemEntry {
  turnId: string
  item: Record<string, unknown>
}

export interface ThreadTurnsPage {
  /** Oldest first, every Turn carrying all of its Items. */
  turns: Turn[]
  nextCursor: string | null
}

export interface ThreadHistoryRead {
  thread: Thread
  turnCursor: string | null
}

type Request = (method: string, params: Record<string, unknown>) => Promise<unknown>

/** Reads every Item of one Turn, paging until the Turn-scoped cursor is exhausted. */
async function readTurnItems(
  request: Request,
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

/**
 * Reads one page of Turns (newest first on the wire) and hydrates each Turn with all
 * of its Items. Paging by Turn keeps a page from ever cutting a Turn in half — the
 * Item cursor only ever advances inside a single Turn.
 */
export async function readThreadTurnsPage(
  request: Request,
  threadId: string,
  cursor: string | null = null,
  limit = HISTORY_TURN_PAGE_LIMIT
): Promise<ThreadTurnsPage> {
  const page = await request('thread/turns/list', {
    threadId,
    cursor,
    limit,
    sortDirection: 'descending'
  }) as HistoryPage<Turn>

  const descending = page.data ?? []
  const hydrated = new Array<Turn>(descending.length)
  const hydrateFrom = async (index: number): Promise<void> => {
    const turn = descending[index]
    if (!turn) return
    hydrated[index] = { ...turn, items: await readTurnItems(request, threadId, turn.id) }
    await hydrateFrom(index + TURN_HYDRATION_CONCURRENCY)
  }
  await Promise.all(
    Array.from(
      { length: Math.min(descending.length, TURN_HYDRATION_CONCURRENCY) },
      (_unused, index) => hydrateFrom(index)
    )
  )

  return { turns: hydrated.reverse(), nextCursor: page.nextCursor ?? null }
}

/** Reads the Thread header plus its newest fully hydrated Turns. */
export async function readThreadHistoryHead(
  request: Request,
  threadId: string,
  turnLimit = HISTORY_TURN_PAGE_LIMIT
): Promise<ThreadHistoryRead> {
  const [turnsPage, readResult] = await Promise.all([
    readThreadTurnsPage(request, threadId, null, turnLimit),
    request('thread/read', { threadId }) as Promise<{ thread: Thread }>
  ])

  return {
    thread: { ...readResult.thread, turns: turnsPage.turns },
    turnCursor: turnsPage.nextCursor
  }
}
