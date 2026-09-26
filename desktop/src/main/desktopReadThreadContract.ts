import { z } from 'zod'

const readThreadArguments = z.object({
  threadId: z.string().trim().min(1),
  cursor: z.string().trim().min(1).optional(),
  turnLimit: z.number().int().min(1).max(10).default(1),
  includeOutputs: z.boolean().default(false),
  maxOutputCharsPerItem: z.number().int().min(0).max(20_000).default(2_000)
}).strict()

export const readThreadToolDefinition = {
  namespace: 'desktop',
  name: 'ReadThread',
  description: 'Read status and the latest complete turn of a DotCraft thread without opening it. Use cursor to read older turns. User and assistant text is preserved in full; optional tool outputs may be truncated.',
  inputSchema: z.toJSONSchema(readThreadArguments, { io: 'input' }),
  outputSchema: {
    type: 'object',
    properties: {
      schemaVersion: { type: 'integer', const: 1 },
      thread: { type: 'object' },
      page: {
        type: 'object',
        properties: {
          order: { type: 'string', const: 'newest_first' },
          limit: { type: 'integer' },
          nextCursor: { type: ['string', 'null'] },
          hasMore: { type: 'boolean' }
        },
        required: ['order', 'limit', 'nextCursor', 'hasMore']
      },
      turns: { type: 'array', items: { type: 'object' } }
    },
    required: ['schemaVersion', 'thread', 'page', 'turns']
  },
  display: { title: 'Read thread', subtitle: 'Desktop' }
}

export function parseReadThreadArguments(value: unknown) {
  return readThreadArguments.safeParse(value)
}
