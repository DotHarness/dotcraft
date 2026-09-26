import type { AppServerRequestClient, DynamicToolCallResult } from './desktopRuntimeThreadTools'
import { parseReadThreadArguments } from './desktopReadThreadContract'
import { projectReadThreadHeader, projectReadThreadTurn } from './desktopReadThreadProjection'

type RecordValue = Record<string, unknown>
interface HistoryPage<T> {
  data?: T[]
  nextCursor?: string | null
}

const ITEM_PAGE_LIMIT = 500
const TURN_CONCURRENCY = 5

export async function readThreadTool(
  client: AppServerRequestClient,
  value: unknown
): Promise<DynamicToolCallResult> {
  const parsed = parseReadThreadArguments(value)
  if (!parsed.success) {
    return {
      success: false,
      errorCode: 'InvalidArguments',
      errorMessage: parsed.error.issues.map(issue => `${issue.path.join('.') || 'arguments'}: ${issue.message}`).join('; ')
    }
  }
  const args = parsed.data
  const [header, page] = await Promise.all([
    client.sendRequest<{ thread?: RecordValue }>('thread/read', { threadId: args.threadId }),
    client.sendRequest<HistoryPage<RecordValue>>('thread/turns/list', {
      threadId: args.threadId,
      cursor: args.cursor,
      limit: args.turnLimit,
      sortDirection: 'descending'
    })
  ])
  if (!header.thread) {
    return { success: false, errorCode: 'ThreadNotFound', errorMessage: `Thread '${args.threadId}' was not found.` }
  }
  if (page.nextCursor != null && page.nextCursor === args.cursor) {
    throw new Error('thread/turns/list returned an unchanged cursor.')
  }
  const turns = page.data ?? []
  const hydrated = new Array<RecordValue>(turns.length)
  const hydrate = async (start: number): Promise<void> => {
    for (let index = start; index < turns.length; index += TURN_CONCURRENCY) {
      const turn = turns[index]
      if (typeof turn.id !== 'string' || !turn.id) throw new Error('History turn is missing its id.')
      const items = await readTurnItems(client, args.threadId, turn.id)
      hydrated[index] = projectReadThreadTurn(turn, items, args.includeOutputs, args.maxOutputCharsPerItem)
    }
  }
  await Promise.all(Array.from({ length: Math.min(turns.length, TURN_CONCURRENCY) }, (_, index) => hydrate(index)))
  const result = {
    schemaVersion: 1,
    thread: projectReadThreadHeader(header.thread),
    page: {
      order: 'newest_first',
      limit: args.turnLimit,
      nextCursor: page.nextCursor ?? null,
      hasMore: page.nextCursor != null
    },
    turns: hydrated
  }
  return { success: true, contentItems: [{ type: 'text', text: JSON.stringify(result) }], structuredContent: result }
}

async function readTurnItems(client: AppServerRequestClient, threadId: string, turnId: string): Promise<RecordValue[]> {
  const items: RecordValue[] = []
  const seenCursors = new Set<string>()
  let cursor: string | null = null
  do {
    const page: HistoryPage<{ turnId: string; item: RecordValue }> = await client.sendRequest('thread/items/list', {
      threadId,
      turnId,
      cursor,
      limit: ITEM_PAGE_LIMIT,
      sortDirection: 'ascending'
    })
    for (const entry of page.data ?? []) {
      if (entry.turnId !== turnId) throw new Error('History item belongs to an unexpected turn.')
      items.push(entry.item)
    }
    cursor = page.nextCursor ?? null
    if (cursor !== null) {
      if (seenCursors.has(cursor)) throw new Error(`thread/items/list returned a repeated cursor for turn ${turnId}.`)
      seenCursors.add(cursor)
    }
  } while (cursor !== null)
  return items
}
