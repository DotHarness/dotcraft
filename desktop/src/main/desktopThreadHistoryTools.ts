import type { AppServerRequestClient, DynamicToolCallResult } from './desktopRuntimeThreadTools'
import {
  listThreadItemsArguments,
  readThreadItemArguments,
  searchThreadMessagesArguments,
  threadHistoryToolDefinitions
} from './desktopReadThreadContract'
import { historyJsonResult, invalidHistoryArguments, readThreadTool, type HistoryPage, type HistoryRecord } from './desktopReadThread'
import { projectReadThreadItem } from './desktopReadThreadProjection'

export { threadHistoryToolDefinitions } from './desktopReadThreadContract'
export const threadHistoryToolNames = new Set(threadHistoryToolDefinitions.map(tool => tool.name))
interface ItemEntry {
  turnId: string
  item: HistoryRecord
}

export async function handleDesktopThreadHistoryTool(
  client: AppServerRequestClient,
  tool: string,
  value: unknown
): Promise<DynamicToolCallResult> {
  switch (tool) {
    case 'ReadThread':
      return readThreadTool(client, value)
    case 'SearchThreadMessages': {
      const parsed = searchThreadMessagesArguments.safeParse(value)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      return historyJsonResult(await client.sendRequest('thread/searchOccurrences', parsed.data))
    }
    case 'ListThreadItems': {
      const parsed = listThreadItemsArguments.safeParse(value)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      const { threadId, turnId, cursor, limit, ...preview } = parsed.data
      const page = await client.sendRequest<HistoryPage<ItemEntry>>('thread/items/list', {
        threadId, turnId, ...(cursor === undefined ? {} : { cursor }), limit, sortDirection: 'ascending'
      })
      return historyJsonResult({
        threadId,
        turnId,
        page: { order: 'oldest_first', limit, nextCursor: page.nextCursor ?? null, hasMore: page.nextCursor != null },
        data: (page.data ?? []).map(entry => {
          if (entry.turnId !== turnId) throw new Error('History item belongs to an unexpected turn.')
          return { turnId, item: projectReadThreadItem(entry.item, preview) }
        })
      })
    }
    case 'ReadThreadItem': {
      const parsed = readThreadItemArguments.safeParse(value)
      if (!parsed.success) return invalidHistoryArguments(parsed.error)
      const { threadId, turnId, itemId } = parsed.data
      const page = await client.sendRequest<HistoryPage<ItemEntry>>('thread/items/read', {
        threadId, turnId, itemIds: [itemId]
      })
      const entry = page.data?.find(entry => entry.turnId === turnId && entry.item.id === itemId)
      if (!entry) {
        return { success: false, errorCode: 'ItemNotFound', errorMessage: `Item '${itemId}' was not found in Turn '${turnId}'.` }
      }
      return historyJsonResult({ threadId, turnId, item: projectReadThreadItem(entry.item) })
    }
    default:
      throw new Error(`Unknown history tool '${tool}'.`)
  }
}
