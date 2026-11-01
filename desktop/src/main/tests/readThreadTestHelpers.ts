import { vi } from 'vitest'
import { handleDesktopRuntimeThreadToolCall, type AppServerRequestClient } from '../desktopRuntimeThreadTools'

export type RecordValue = Record<string, any>
export type RequestHandler = (method: string, params: RecordValue) => Promise<unknown>

export function item(id: string, type: string, payload: RecordValue): RecordValue {
  return { id, type, status: 'completed', payload }
}

export function historyClient(turns: RecordValue[], header: RecordValue = {}) {
  return clientFor(async (method, params) => {
    if (method === 'thread/read') return { thread: { id: 'thread-1', status: 'active', ...header } }
    if (method === 'thread/turns/list') {
      const offset = params.cursor ? Number(params.cursor.split(':')[1]) : 0
      const data = turns.slice(offset, offset + params.limit).map(({ items = [], summaryItems, ...turn }) => ({
        ...turn,
        itemsView: params.itemsView,
        ...(params.itemsView === 'summary' ? { itemCount: items.length } : {}),
        items: params.itemsView === 'none' ? [] : summaryItems ?? items
      }))
      return { data, nextCursor: offset + data.length < turns.length ? `turns:${offset + data.length}` : null }
    }
    if (method === 'thread/items/list' || method === 'thread/items/read') {
      const turn = turns.find(turn => turn.id === params.turnId)
      if (!turn) throw new Error('Unknown turn')
      if (method === 'thread/items/read') {
        return { data: turn.items.filter((item: RecordValue) => params.itemIds.includes(item.id))
          .map((item: RecordValue) => ({ turnId: turn.id, item })) }
      }
      const offset = params.cursor ? Number(params.cursor.split(':')[1]) : 0
      const data = turn.items.slice(offset, offset + params.limit).map((item: RecordValue) => ({ turnId: turn.id, item }))
      return { data, nextCursor: offset + data.length < turn.items.length ? `items:${offset + data.length}` : null }
    }
    throw new Error(`Unexpected request: ${method}`)
  })
}

export function clientFor(handler: RequestHandler) {
  const sendRequest = vi.fn(handler)
  return { client: { sendRequest } as unknown as AppServerRequestClient, sendRequest }
}

export async function callHistory(client: AppServerRequestClient, tool: string, args: unknown) {
  const response = await handleDesktopRuntimeThreadToolCall(client, {
    namespace: 'desktop', tool, arguments: args
  }, '/example-workspace')
  if (!response) throw new Error(`${tool} was not dispatched`)
  return { response, data: response.structuredContent as RecordValue }
}

export function read(client: AppServerRequestClient, args: unknown = { threadId: 'thread-1' }) {
  return callHistory(client, 'ReadThread', args)
}
