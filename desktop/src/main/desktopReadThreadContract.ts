import { z } from 'zod'

const HISTORY_TURN_LIMIT = { default: 1, min: 1, max: 10 }
const HISTORY_PAGE_LIMIT = { default: 20, min: 1, max: 100 }
const HISTORY_TEXT_LIMIT = { default: 2_000, min: 1, max: 20_000 }
const HISTORY_OUTPUT_LIMIT = { default: 2_000, min: 0, max: 20_000 }

const threadId = z.string().trim().min(1).describe('Thread ID in the current server workspace.')
const turnId = z.string().trim().min(1).describe('Turn ID within the specified thread.')
const cursor = z.string().trim().min(1).optional()
  .describe('Opaque nextCursor from this tool for the same query. Omit for the first page.')
const pageLimit = z.number().int().min(HISTORY_PAGE_LIMIT.min).max(HISTORY_PAGE_LIMIT.max)
  .default(HISTORY_PAGE_LIMIT.default)
  .describe(`Results per page, ${HISTORY_PAGE_LIMIT.min}–${HISTORY_PAGE_LIMIT.max}; default ${HISTORY_PAGE_LIMIT.default}.`)
const previewArguments = {
  includeOutputs: z.boolean().default(false).describe('Include tool output and reasoning previews; default false.'),
  maxCharsPerItem: z.number().int().min(HISTORY_TEXT_LIMIT.min).max(HISTORY_TEXT_LIMIT.max)
    .default(HISTORY_TEXT_LIMIT.default)
    .describe(`Shared character allowance per Item for body, context, arguments and errors, ${HISTORY_TEXT_LIMIT.min}–${HISTORY_TEXT_LIMIT.max}; default ${HISTORY_TEXT_LIMIT.default}.`),
  maxOutputCharsPerItem: z.number().int().min(HISTORY_OUTPUT_LIMIT.min).max(HISTORY_OUTPUT_LIMIT.max)
    .default(HISTORY_OUTPUT_LIMIT.default)
    .describe(`Shared output character allowance per Item when includeOutputs is true, ${HISTORY_OUTPUT_LIMIT.min}–${HISTORY_OUTPUT_LIMIT.max}; default ${HISTORY_OUTPUT_LIMIT.default}.`)
}

const readThreadArguments = z.object({
  threadId,
  cursor: cursor.describe('Opaque nextCursor from ReadThread, or turnCursor from SearchThreadMessages. Omit for the newest page.'),
  turnLimit: z.number().int().min(HISTORY_TURN_LIMIT.min).max(HISTORY_TURN_LIMIT.max)
    .default(HISTORY_TURN_LIMIT.default)
    .describe(`Newest-first Turns per page, ${HISTORY_TURN_LIMIT.min}–${HISTORY_TURN_LIMIT.max}; default ${HISTORY_TURN_LIMIT.default}.`),
  includeItems: z.boolean().default(true).describe('Include server-selected summary Items; default true. False reads Turn metadata only.'),
  ...previewArguments
}).strict()

export const searchThreadMessagesArguments = z.object({
  threadId,
  searchTerm: z.string().trim().min(1).describe('Non-empty literal substring in stored user and assistant text, case-insensitive.'),
  cursor,
  limit: pageLimit
}).strict()

export const listThreadItemsArguments = z.object({
  threadId,
  turnId,
  cursor,
  limit: pageLimit,
  ...previewArguments
}).strict()

export const readThreadItemArguments = z.object({
  threadId,
  turnId,
  itemId: z.string().trim().min(1).describe('Exact visible Item ID within the specified Turn.')
}).strict()

export const readThreadToolDefinition = {
  namespace: 'desktop',
  name: 'ReadThread',
  description: 'Read a thread header and newest-first Turn summaries without opening it. Summary Items have bounded previews, itemsView, itemCount and truncation metadata; they are not complete Turns. Use includeItems false for metadata, SearchThreadMessages to locate text, ListThreadItems to discover Item IDs, and ReadThreadItem for complete model-safe content.',
  inputSchema: z.toJSONSchema(readThreadArguments, { io: 'input' })
}

export const threadHistoryToolDefinitions = [
  readThreadToolDefinition,
  {
    namespace: 'desktop',
    name: 'SearchThreadMessages',
    description: 'Find literal text occurrences in stored user and assistant messages without opening the thread. Returns data with turnId, itemId, snippet, snippetMatchRange (UTF-16, end-exclusive), turnCursor and nextCursor. Does not search tools or reasoning. Pass turnCursor to ReadThread or use ReadThreadItem for the exact message.',
    inputSchema: z.toJSONSchema(searchThreadMessagesArguments, { io: 'input' })
  },
  {
    namespace: 'desktop',
    name: 'ListThreadItems',
    description: 'Read one ascending Item page within a Turn without opening the thread. Returns Item IDs, kinds, call metadata and bounded previews with page.nextCursor. Use ReadThreadItem to read one Item completely.',
    inputSchema: z.toJSONSchema(listThreadItemsArguments, { io: 'input' })
  },
  {
    namespace: 'desktop',
    name: 'ReadThreadItem',
    description: 'Read one exact visible Item by threadId, turnId and itemId without opening the thread. Returns complete model-safe text, arguments and output without preview limits, excluding host metadata, private interaction responses and inline media. Missing Items fail with ItemNotFound. Oversized results fail under existing output limits; there is no character continuation.',
    inputSchema: z.toJSONSchema(readThreadItemArguments, { io: 'input' })
  }
]

export function parseReadThreadArguments(value: unknown) {
  return readThreadArguments.safeParse(value)
}
