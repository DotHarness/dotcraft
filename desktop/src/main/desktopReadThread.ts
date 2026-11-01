import type { ZodError } from 'zod'
import type { AppServerRequestClient, DynamicToolCallResult } from './desktopRuntimeThreadTools'
import { parseReadThreadArguments } from './desktopReadThreadContract'
import { projectReadThreadHeader, projectReadThreadTurn } from './desktopReadThreadProjection'

export type HistoryRecord = Record<string, unknown>
export interface HistoryPage<T> {
  data?: T[]
  nextCursor?: string | null
}

export function historyJsonResult(result: unknown): DynamicToolCallResult {
  return { success: true, contentItems: [{ type: 'text', text: JSON.stringify(result) }], structuredContent: result }
}

export function invalidHistoryArguments(error: ZodError): DynamicToolCallResult {
  return {
    success: false,
    errorCode: 'InvalidArguments',
    errorMessage: error.issues.map(issue => `${issue.path.join('.') || 'arguments'}: ${issue.message}`).join('; ')
  }
}

export async function readThreadTool(
  client: AppServerRequestClient,
  value: unknown
): Promise<DynamicToolCallResult> {
  const parsed = parseReadThreadArguments(value)
  if (!parsed.success) return invalidHistoryArguments(parsed.error)
  const args = parsed.data
  const itemsView = args.includeItems ? 'summary' : 'none'
  const [header, page] = await Promise.all([
    client.sendRequest<{ thread?: HistoryRecord }>('thread/read', { threadId: args.threadId }),
    client.sendRequest<HistoryPage<HistoryRecord>>('thread/turns/list', {
      threadId: args.threadId,
      ...(args.cursor === undefined ? {} : { cursor: args.cursor }),
      limit: args.turnLimit,
      sortDirection: 'descending',
      itemsView
    })
  ])
  if (!header.thread) {
    return { success: false, errorCode: 'ThreadNotFound', errorMessage: `Thread '${args.threadId}' was not found.` }
  }
  return historyJsonResult({
    schemaVersion: 1,
    thread: projectReadThreadHeader(header.thread),
    page: {
      order: 'newest_first',
      limit: args.turnLimit,
      nextCursor: page.nextCursor ?? null,
      hasMore: page.nextCursor != null
    },
    turns: (page.data ?? []).map(turn => projectReadThreadTurn(turn, itemsView, args))
  })
}
