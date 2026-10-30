import type { ThreadItemsListResult } from '@dotcraft/sdk/contracts'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import type { TurnPageEntry } from './history'

const TURN_PAGE = 5
const TURN_ITEM_PAGE = 100
/** Tools whose results stay outside a folded Turn, so its summary keeps their calls. */
const SUMMARY_TOOLS = [
  { name: 'CreatePlan' },
  { name: 'Workflow' },
  { name: 'Automation' },
  { name: 'CreateThread', namespace: 'desktop' },
  { name: 'SendMessageToThread', namespace: 'desktop' },
]
const LIVE_TURN_STATUSES = new Set(['running', 'waitingApproval', 'waitingInput'])

export async function readTurnPage(
  client: DotCraftWireClient,
  threadId: string,
  cursor: string | null,
): Promise<{ turns: TurnPageEntry[]; nextCursor: string | null }> {
  const page = await client.request('thread/turns/list', {
    threadId,
    limit: TURN_PAGE,
    sortDirection: 'descending',
    itemsView: 'summary',
    summaryTools: SUMMARY_TOOLS,
    ...(cursor ? { cursor } : {}),
  })
  const turns = await Promise.all(
    [...page.data].reverse().map(async (turn) =>
      turn.itemsView === 'summary' && !LIVE_TURN_STATUSES.has(turn.status)
        ? { turn, items: turn.items ?? [] }
        : { turn: { ...turn, itemsView: null, itemCount: null }, items: await readTurnItems(client, threadId, turn.id) },
    ),
  )
  return { turns, nextCursor: page.nextCursor ?? null }
}

export async function readTurnItems(client: DotCraftWireClient, threadId: string, turnId: string): Promise<TurnPageEntry['items']> {
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
