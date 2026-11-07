import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { formatToolGroupLabel } from '../utils/toolGroupLabel'
import { getDesktopToolTargetThreadId, resolveDesktopToolOperation } from '../utils/desktopToolDisplay'

describe('resolveDesktopToolOperation', () => {
  it('recognizes Desktop tools, including a call still streaming without a namespace', () => {
    expect(resolveDesktopToolOperation({ toolName: 'ReadThread', pluginNamespace: 'desktop' }, undefined)).toBe('readChat')
    expect(resolveDesktopToolOperation({ toolName: 'ReadThread' }, undefined)).toBe('readChat')
  })

  it('never lends Desktop rendering to another source', () => {
    expect(resolveDesktopToolOperation({ toolName: 'ReadThread', pluginNamespace: 'acme' }, undefined)).toBeNull()
    expect(resolveDesktopToolOperation({ toolName: 'ReadThread', type: 'mcpToolCall' }, undefined)).toBeNull()
    expect(resolveDesktopToolOperation({ toolName: 'SomethingElse', pluginNamespace: 'desktop' }, undefined)).toBeNull()
  })

  it('follows the requested state for archive and pin', () => {
    const archive = { toolName: 'SetThreadArchived', pluginNamespace: 'desktop' }
    expect(resolveDesktopToolOperation(archive, { archived: true })).toBe('archiveChat')
    expect(resolveDesktopToolOperation(archive, { archived: false })).toBe('restoreChat')
    const pin = { toolName: 'SetThreadPinned', pluginNamespace: 'desktop' }
    expect(resolveDesktopToolOperation(pin, { pinned: true })).toBe('pinChat')
    expect(resolveDesktopToolOperation(pin, { pinned: false })).toBe('unpinChat')
  })
})

describe('getDesktopToolTargetThreadId', () => {
  it('opens a chat only for operations that act on one the user can visit', () => {
    expect(getDesktopToolTargetThreadId('readChat', { threadId: 'thread-1' })).toBe('thread-1')
    expect(getDesktopToolTargetThreadId('sendMessage', { threadId: 'thread-1' })).toBe('thread-1')
    expect(getDesktopToolTargetThreadId('readChat', {})).toBeNull()
    expect(getDesktopToolTargetThreadId('renameChat', { threadId: 'thread-1' })).toBeNull()
  })
})

describe('group summary', () => {
  function call(toolName: string, success = true): ConversationItem {
    return { id: toolName, type: 'dynamicToolCall', status: 'completed', toolName, pluginNamespace: 'desktop', success, createdAt: '' } as ConversationItem
  }

  it('merges paged reads into one phrase and names each kind of work', () => {
    const items = [call('ReadThread'), call('ReadThreadItem'), call('ListThreads')]
    expect(formatToolGroupLabel(items, 'en', new Map())).toBe('Read chat, listed chats')
  })

  it('reports failure only when every call of that kind failed', () => {
    expect(formatToolGroupLabel([call('ReadThread', false), call('ReadThreadItem')], 'en', new Map())).toBe('Read chat')
    expect(formatToolGroupLabel([call('ReadThread', false), call('ReadThreadItem', false)], 'en', new Map())).toBe("Couldn't read chat")
  })
})
