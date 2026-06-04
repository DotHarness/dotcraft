import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { parseThreadToolAction, isThreadActionToolItem } from '../utils/threadToolDisplay'
import { getSpawnedFromThreadId } from '../utils/subAgentThreads'
import type { ThreadSummary } from '../types/thread'

function item(partial: Partial<ConversationItem>): ConversationItem {
  return {
    id: 'item-1',
    type: 'dynamicToolCall',
    status: 'completed',
    createdAt: '2026-06-04T00:00:00Z',
    ...partial
  } as ConversationItem
}

describe('parseThreadToolAction', () => {
  it('parses a successful CreateThread result', () => {
    const action = parseThreadToolAction(item({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: { thread: { id: 'thread-9', displayName: 'Research' }, started: true }
    }))
    expect(action).toEqual({ kind: 'created', threadId: 'thread-9', displayName: 'Research', started: true, queued: false })
  })

  it('parses a queued SendMessageToThread result', () => {
    const action = parseThreadToolAction(item({
      toolName: 'SendMessageToThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: { threadId: 'thread-3', started: false, queued: true }
    }))
    expect(action).toEqual({ kind: 'messaged', threadId: 'thread-3', displayName: undefined, started: false, queued: true })
  })

  it('returns null for a failed call', () => {
    expect(parseThreadToolAction(item({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: false,
      structuredResult: { thread: { id: 'thread-9' } }
    }))).toBeNull()
  })

  it('returns null when the result has no thread id', () => {
    expect(parseThreadToolAction(item({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: { started: true }
    }))).toBeNull()
  })

  it('ignores unrelated tools', () => {
    expect(isThreadActionToolItem(item({ toolName: 'WriteFile' }))).toBe(false)
    expect(isThreadActionToolItem(item({ toolName: 'CreateThread', pluginNamespace: 'desktop' }))).toBe(true)
  })
})

describe('getSpawnedFromThreadId', () => {
  it('reads the origin from thread metadata', () => {
    const thread = { metadata: { spawnedFromThreadId: 'thread-parent' } } as unknown as ThreadSummary
    expect(getSpawnedFromThreadId(thread)).toBe('thread-parent')
  })

  it('returns null when absent or blank', () => {
    expect(getSpawnedFromThreadId({ metadata: {} } as unknown as ThreadSummary)).toBeNull()
    expect(getSpawnedFromThreadId({ metadata: { spawnedFromThreadId: '  ' } } as unknown as ThreadSummary)).toBeNull()
    expect(getSpawnedFromThreadId({} as unknown as ThreadSummary)).toBeNull()
  })
})
