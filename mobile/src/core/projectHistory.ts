import type { ThreadItemsListResult, ThreadTurnsListResult } from '@dotcraft/sdk/contracts'
import type { DotCraftWireClient } from '@dotcraft/sdk/wire'
import type { TurnPageEntry } from './history'
import { ReadQueue } from './readQueue'
import { chatKey, type ComputerAction, type ComputerState } from './state'
import type { Store } from './store'

export const OPENING_TURNS_PAGE = {
  limit: 5,
  itemsView: 'summary',
  summaryTools: [
    { name: 'CreatePlan' },
    { name: 'Workflow' },
    { name: 'Automation' },
    { name: 'CreateThread', namespace: 'desktop' },
    { name: 'SendMessageToThread', namespace: 'desktop' },
  ],
}

export function turnPage(page: ThreadTurnsListResult): { turns: TurnPageEntry[]; nextCursor: string | null } {
  return { turns: [...page.data].reverse().map((turn) => ({ turn, items: turn.items ?? [] })), nextCursor: page.nextCursor ?? null }
}

export async function readTurnPage(client: DotCraftWireClient, threadId: string, cursor: string | null) {
  return turnPage(await client.request('thread/turns/list', {
    threadId, ...OPENING_TURNS_PAGE, sortDirection: 'descending', ...(cursor ? { cursor } : {}),
  }))
}

function itemPage(page: ThreadItemsListResult) {
  return { items: page.data.map((entry) => entry.item), nextCursor: page.nextCursor ?? null }
}

export async function readTurnItems(
  client: DotCraftWireClient, threadId: string, turnId: string, cursor?: string | null,
  onResponse?: (page: ReturnType<typeof itemPage>) => void,
) {
  const page = await client.request('thread/items/list', {
    threadId, turnId, limit: 100, sortDirection: 'ascending', ...(cursor ? { cursor } : {}),
  }, undefined, { onResponse: (result) => onResponse?.(itemPage(result)) })
  return itemPage(page)
}

export class ProjectHistoryLoader {
  private readonly workers = new ReadQueue(4)
  private readonly items = new Map<string, Promise<void>>()
  private readonly older = new Set<string>()

  constructor(
    private readonly projectId: string,
    private readonly store: Store<ComputerState, ComputerAction>,
    private readonly client: () => DotCraftWireClient,
    private readonly beforeApply: (threadId: string) => void,
  ) {}

  loadTurnItems(threadId: string, turnId: string): Promise<void> {
    const slot = JSON.stringify([threadId, turnId])
    const pending = this.items.get(slot)
    if (pending) return pending
    const client = this.client()
    const loading = this.workers.run(async () => {
      if (this.client() !== client) return
      const key = chatKey(this.projectId, threadId)
      const turn = this.store.getState().details[key]?.history.turns.find((entry) => entry.id === turnId)
      if (!turn?.summary) return
      await readTurnItems(client, threadId, turnId, turn.itemsCursor, (page) => {
        if (this.client() !== client) return
        this.beforeApply(threadId)
        this.store.dispatch({ type: 'turnItemsLoaded', key, turnId, ...page })
      })
    }).finally(() => this.items.delete(slot))
    this.items.set(slot, loading)
    return loading
  }

  async loadOlder(threadId: string): Promise<void> {
    const client = this.client()
    const key = chatKey(this.projectId, threadId)
    const cursor = this.store.getState().details[key]?.history.olderCursor
    if (!cursor || this.older.has(threadId)) return
    this.older.add(threadId)
    this.store.dispatch({ type: 'olderLoading', key, value: true })
    try {
      const page = await readTurnPage(client, threadId, cursor)
      if (this.client() === client) this.store.dispatch({ type: 'olderLoaded', key, cursor, turns: page.turns, olderCursor: page.nextCursor })
    } finally {
      this.older.delete(threadId)
      this.store.dispatch({ type: 'olderLoading', key, value: false })
    }
  }
}
