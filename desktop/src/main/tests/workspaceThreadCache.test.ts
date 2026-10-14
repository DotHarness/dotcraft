import { describe, expect, it } from 'vitest'
import {
  applyWorkspaceThreadListRefreshFailure,
  applyWorkspaceThreadListRefreshSuccess,
  applyWorkspaceThreadListRequestResult,
  applyWorkspaceThreadNotificationToCache,
  refreshWorkspaceThreadListCache
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

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/archived', {
      threadId: 'parent'
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

  it.each([
    ['ephemeral', { ephemeral: true }],
    ['internal metadata', { metadata: { 'dotcraft.internal': 'agent-builder' } }],
    ['internal origin', { originChannel: 'dreams' }]
  ])('keeps a started %s thread out of the project cache', (_kind, hidden) => {
    const cache = [thread('visible'), thread('hidden')]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/started', {
      thread: thread('hidden', hidden)
    })

    expect(ids(result.threads)).toEqual(['visible'])
    expect(ids(applyWorkspaceThreadNotificationToCache([], 'thread/started', {
      thread: thread('fresh', hidden)
    }).threads)).toEqual([])
  })

  it('removes a deleted thread tree from the project cache', () => {
    const cache = [
      thread('parent'),
      subAgent('child', 'parent'),
      subAgent('grandchild', 'child'),
      thread('other')
    ]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/deleted', {
      threadId: 'parent'
    })

    expect(result.changed).toBe(true)
    expect(result.refreshThreadList).toBe(false)
    expect(ids(result.threads)).toEqual(['other'])

    for (const threadId of ['grandchild', 'child', 'parent']) {
      const repeated = applyWorkspaceThreadNotificationToCache(result.threads, 'thread/deleted', { threadId })
      expect(repeated.threads).toBe(result.threads)
      expect(repeated.changed).toBe(false)
      expect(repeated.refreshThreadList).toBe(false)
    }
  })

  it('requests a refresh when a previously archived thread is restored but missing from cache', () => {
    const cache = [thread('other')]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/unarchived', {
      threadId: 'restored'
    })

    expect(result.changed).toBe(false)
    expect(result.refreshThreadList).toBe(true)
    expect(result.threads).toBe(cache)
  })

  it('updates an existing restored thread without forcing a refresh', () => {
    const cache = [thread('restored', { status: 'archived' })]

    const result = applyWorkspaceThreadNotificationToCache(cache, 'thread/unarchived', {
      threadId: 'restored'
    })

    expect(result.changed).toBe(true)
    expect(result.refreshThreadList).toBe(false)
    expect((result.threads[0] as { status: string }).status).toBe('active')
  })
})

const workspaceListParams = {
  identity: { workspacePath: '/workspace' },
  scope: 'workspace',
  includeSubAgents: true
}

describe('workspace thread list refresh cache', () => {
  it('keeps recovered running threads and clears the error when a foreground list becomes the background cache', () => {
    const entry = { workspacePath: '/workspace', threads: [], errorMessage: 'thread/list timed out' }
    const running = thread('running', { runtime: { state: 'running' } })

    expect(applyWorkspaceThreadListRequestResult(entry, 'thread/list', workspaceListParams, { data: [running] })).toBe(true)
    expect(entry.threads).toEqual([running])
    expect(entry.errorMessage).toBeUndefined()
  })

  it.each([
    { query: 'search' },
    { limit: 20 },
    { cursor: 'next-page' },
    { channelName: 'cli' },
    { includeArchived: true },
    { includeInternal: true },
    { includeSubAgents: false },
    { scope: 'identity' },
    { identity: { workspacePath: '/other-project' } }
  ])('does not replace the sidebar cache with a different list selection: %j', (selection) => {
    const entry = { workspacePath: '/workspace', threads: [thread('running')], errorMessage: 'prior error' }
    const params = { ...workspaceListParams, ...selection }

    expect(applyWorkspaceThreadListRequestResult(entry, 'thread/list', params, { data: [] })).toBe(false)
    expect(ids(entry.threads)).toEqual(['running'])
    expect(entry.errorMessage).toBe('prior error')
  })

  it.each([
    ['thread/read', workspaceListParams, { data: [] }],
    ['thread/list', null, { data: [] }],
    ['thread/list', workspaceListParams, null],
    ['thread/list', workspaceListParams, {}],
    ['thread/list', workspaceListParams, { data: [], nextCursor: 'more' }]
  ])('ignores unrelated or incomplete results (%s)', (method, params, result) => {
    const entry = { workspacePath: '/workspace', threads: [thread('running')], errorMessage: 'prior error' }

    expect(applyWorkspaceThreadListRequestResult(entry, method as string, params, result)).toBe(false)
    expect(ids(entry.threads)).toEqual(['running'])
    expect(entry.errorMessage).toBe('prior error')
  })

  it('accepts an empty complete list and equivalent Windows workspace paths', () => {
    const entry = { workspacePath: 'E:/Projects/DN', threads: [thread('old')], errorMessage: 'prior error' }
    const params = { ...workspaceListParams, identity: { workspacePath: 'e:\\Projects\\DN\\' } }

    expect(applyWorkspaceThreadListRequestResult(entry, 'thread/list', params, { data: [], nextCursor: null })).toBe(true)
    expect(entry.threads).toEqual([])
    expect(entry.errorMessage).toBeUndefined()
  })

  it.each(['failure', 'success'])('ignores an older background %s after a successful foreground list', async (outcome) => {
    const entry = { workspacePath: '/workspace', threads: [], errorMessage: 'prior error' }
    let resolve!: (value: { data: unknown[] }) => void
    let reject!: (error: Error) => void
    const pending = new Promise<{ data: unknown[] }>((res, rej) => { resolve = res; reject = rej })
    const refresh = refreshWorkspaceThreadListCache(entry, () => pending)
    const running = thread('running')

    applyWorkspaceThreadListRequestResult(entry, 'thread/list', workspaceListParams, { data: [running] })
    if (outcome === 'failure') reject(new Error('old request timed out'))
    else resolve({ data: [thread('outdated')] })

    expect(await refresh).toBe(false)
    expect(entry.threads).toEqual([running])
    expect(entry.errorMessage).toBeUndefined()
  })

  it('uses the latest background refresh without letting an earlier request overwrite it', async () => {
    const entry = { threads: [thread('old')] }
    let resolve!: (value: { data: unknown[] }) => void
    const older = refreshWorkspaceThreadListCache(entry, () => new Promise((res) => { resolve = res }))

    expect(await refreshWorkspaceThreadListCache(entry, async () => ({ data: [thread('latest')] }))).toBe(true)
    resolve({ data: [thread('outdated')] })
    expect(await older).toBe(false)
    expect(ids(entry.threads)).toEqual(['latest'])
  })

  it('preserves loaded rows on failure and recovers after a later successful refresh', async () => {
    const entry = { threads: [thread('running')], errorMessage: undefined as string | undefined }

    expect(await refreshWorkspaceThreadListCache(entry, async () => { throw new Error('timeout') })).toBe(true)
    expect(ids(entry.threads)).toEqual(['running'])
    expect(entry.errorMessage).toBe('timeout')
    expect(await refreshWorkspaceThreadListCache(entry, async () => ({ data: [thread('fresh')] }))).toBe(true)
    expect(ids(entry.threads)).toEqual(['fresh'])
    expect(entry.errorMessage).toBeUndefined()
  })

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
