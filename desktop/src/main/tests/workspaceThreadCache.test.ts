import { describe, expect, it } from 'vitest'
import {
  applyWorkspaceThreadListRefreshFailure,
  applyWorkspaceThreadListRefreshSuccess,
  applyWorkspaceThreadNotificationToCache
} from '../workspaceThreadCache'

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
  it('leaves the project cache unchanged for shell output notifications', () => {
    const cache = [thread('active')]

    for (const method of ['terminal/outputDelta', 'item/commandExecution/outputDelta']) {
      const result = applyWorkspaceThreadNotificationToCache(cache, method, {
        threadId: 'active',
        delta: 'many lines'
      })

      expect(result.changed).toBe(false)
      expect(result.refreshThreadList).toBe(false)
      expect(result.threads).toBe(cache)
    }
  })

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

  it('removes a started archived thread tree when status casing comes from the wire', () => {
    const cache = [
      thread('parent'),
      subAgent('child', 'parent'),
      thread('sibling')
    ]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/started', {
      thread: thread('parent', { status: 'Archived' })
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

describe('workspace thread list refresh cache', () => {
  it('clears a prior refresh error after a later successful thread list load', () => {
    const entry = {
      threads: [thread('stale')],
      errorMessage: 'thread/list failed'
    }

    applyWorkspaceThreadListRefreshFailure(entry, new Error('still failing'))
    expect(entry.errorMessage).toBe('still failing')

    applyWorkspaceThreadListRefreshSuccess(entry, [thread('fresh')])

    expect(entry.errorMessage).toBeUndefined()
    expect(ids(entry.threads)).toEqual(['fresh'])
  })
})
