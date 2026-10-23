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
  it('parses successful CreateThread and queued SendMessageToThread results', () => {
    expect(parseThreadToolAction(item({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: { thread: { id: 'thread-9', displayName: 'Research' }, started: true }
    }))).toEqual({ kind: 'created', threadId: 'thread-9', displayName: 'Research', started: true, queued: false })

    expect(parseThreadToolAction(item({
      toolName: 'SendMessageToThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: { threadId: 'thread-3', started: false, queued: true }
    }))).toEqual({ kind: 'messaged', threadId: 'thread-3', displayName: undefined, started: false, queued: true })
  })

  it('returns null for failed calls, results without a thread id, and unrelated tools', () => {
    expect(parseThreadToolAction(item({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: false,
      structuredResult: { thread: { id: 'thread-9' } }
    }))).toBeNull()
    expect(parseThreadToolAction(item({
      toolName: 'CreateThread',
      pluginNamespace: 'desktop',
      success: true,
      structuredResult: { started: true }
    }))).toBeNull()
    expect(isThreadActionToolItem(item({ toolName: 'WriteFile' }))).toBe(false)
    expect(isThreadActionToolItem(item({ toolName: 'CreateThread', pluginNamespace: 'desktop' }))).toBe(true)
  })
})

describe('getSpawnedFromThreadId', () => {
  it('reads the origin from thread metadata and ignores absent or blank values', () => {
    expect(getSpawnedFromThreadId({ metadata: { spawnedFromThreadId: 'thread-parent' } } as unknown as ThreadSummary)).toBe('thread-parent')
    expect(getSpawnedFromThreadId({ metadata: { spawnedFromThreadId: '  ' } } as unknown as ThreadSummary)).toBeNull()
    expect(getSpawnedFromThreadId({} as unknown as ThreadSummary)).toBeNull()
  })
})
