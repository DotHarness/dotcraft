import { describe, expect, it } from 'vitest'
import { applyWorkspaceThreadNotificationToCache } from '../workspaceThreadCache'

function thread(id: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    id,
    displayName: id,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    lastActiveAt: '2026-06-17T00:00:00.000Z',
    ...extra
  }
}

function subAgent(id: string, parentThreadId: string, extra: Record<string, unknown> = {}): Record<string, unknown> {
  return thread(id, {
    originChannel: 'subagent',
    source: {
      kind: 'subagent',
      subAgent: { parentThreadId }
    },
    ...extra
  })
}

function ids(threads: unknown[]): string[] {
  return threads.map((item) => (item as { id: string }).id)
}

describe('workspace thread cache notifications', () => {
  it('removes an archived thread tree from the project cache', () => {
    const cache = [
      thread('parent'),
      subAgent('child', 'parent'),
      subAgent('grandchild', 'child'),
      thread('sibling')
    ]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/statusChanged', {
      threadId: 'parent',
      previousStatus: 'active',
      newStatus: 'archived'
    })

    expect(result.changed).toBe(true)
    expect(result.refreshThreadList).toBe(false)
    expect(ids(result.threads)).toEqual(['sibling'])
  })

  it('removes a deleted thread tree from the project cache', () => {
    const cache = [
      thread('parent'),
      subAgent('child', 'parent'),
      thread('other')
    ]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/deleted', {
      threadId: 'parent'
    })

    expect(result.changed).toBe(true)
    expect(result.refreshThreadList).toBe(false)
    expect(ids(result.threads)).toEqual(['other'])
  })

  it('requests a refresh when a previously archived thread is restored but missing from cache', () => {
    const cache = [thread('other')]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/statusChanged', {
      threadId: 'restored',
      previousStatus: 'archived',
      newStatus: 'active'
    })

    expect(result.changed).toBe(false)
    expect(result.refreshThreadList).toBe(true)
    expect(result.threads).toBe(cache)
  })

  it('updates an existing restored thread without forcing a refresh', () => {
    const cache = [thread('restored', { status: 'paused' })]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/statusChanged', {
      threadId: 'restored',
      previousStatus: 'paused',
      newStatus: 'active'
    })

    expect(result.changed).toBe(true)
    expect(result.refreshThreadList).toBe(false)
    expect((result.threads[0] as { status: string }).status).toBe('active')
  })
})
