import type { ThreadItemsListResult } from '@dotcraft/sdk/contracts'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import type { TurnPageEntry } from './history'

const TURN_PAGE = 5
const TURN_ITEM_PAGE = 100

export async function readTurnPage(
  client: DotCraftWireClient,
  threadId: string,
  cursor: string | null,
): Promise<{ turns: TurnPageEntry[]; nextCursor: string | null }> {
  const page = await client.request('thread/turns/list', { threadId, limit: TURN_PAGE, sortDirection: 'descending', ...(cursor ? { cursor } : {}) })
  const turns = await Promise.all(
    [...page.data].reverse().map(async (turn) => ({ turn, items: await readTurnItems(client, threadId, turn.id) })),
  )
  return { turns, nextCursor: page.nextCursor ?? null }
}

async function readTurnItems(client: DotCraftWireClient, threadId: string, turnId: string): Promise<TurnPageEntry['items']> {
  const items: TurnPageEntry['items'] = []
  let cursor: string | null = null
  do {
    const page: ThreadItemsListResult = await client.request('thread/items/list', {
      threadId,
      turnId,
      limit: TURN_ITEM_PAGE,
      sortDirection: 'ascending',
      ...(cursor ? { cursor } : {}),
    })
    items.push(...page.data.map((entry) => entry.item))
    const next: string | null = page.nextCursor ?? null
    if (next !== null && next === cursor) throw new Error(`thread/items/list returned an unchanged cursor for turn ${turnId}`)
    cursor = next
  } while (cursor !== null)
  return items
}
