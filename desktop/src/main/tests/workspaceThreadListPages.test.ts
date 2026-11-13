import { describe, expect, it } from 'vitest'
import {
  applyWorkspaceThreadListRequestResult,
  applyWorkspaceThreadNotificationToCache,
  beginWorkspaceThreadListRequest,
  noteWorkspaceThreadNotification,
  refreshWorkspaceThreadListCache,
  type WorkspaceThreadListRefreshState
} from '../workspaceThreadCache'

const params = { identity: { workspacePath: '/workspace' }, scope: 'workspace', includeSubAgents: true, limit: 50 }
const thread = (id: string) => ({ id, status: 'active', displayName: id })
const ids = (entry: WorkspaceThreadListRefreshState) => entry.threads.map(value => (value as { id: string }).id)
function page(entry: WorkspaceThreadListRefreshState & { workspacePath: string }, request: typeof params & { cursor?: string }, result: unknown) {
  const generation = beginWorkspaceThreadListRequest(entry, 'thread/list', request)
  return applyWorkspaceThreadListRequestResult(entry, 'thread/list', request, result, generation)
}

describe('workspace thread directory pages', () => {
  it('merges pages and removes stale rows only when the directory completes', () => {
    const entry = { workspacePath: '/workspace', threads: [thread('stale')], threadListLoaded: true }
    expect(page(entry, params, { data: [thread('first')], nextCursor: 'next' })).toBe(true)
    expect(ids(entry)).toEqual(['stale', 'first'])
    expect(entry.threadListLoaded).toBe(false)

    expect(page(entry, { ...params, cursor: 'next' }, { data: [thread('last')], nextCursor: null })).toBe(true)
    expect(ids(entry)).toEqual(['first', 'last'])
    expect(entry.threadListLoaded).toBe(true)
    expect(page(entry, params, { data: [], nextCursor: null })).toBe(true)
    expect(entry.threads).toEqual([])
    expect(entry.threadListLoaded).toBe(true)
  })

  it('does not let orphaned or skipped cursors replace the directory', () => {
    const entry = { workspacePath: '/workspace', threads: [thread('kept')] }
    expect(page(entry, { ...params, cursor: 'orphan' }, { data: [] })).toBe(false)
    page(entry, params, { data: [thread('first')], nextCursor: 'next' })
    expect(page(entry, { ...params, cursor: 'skipped' }, { data: [] })).toBe(false)
    expect(ids(entry)).toEqual(['kept', 'first'])
    expect(page(entry, { ...params, cursor: 'next' }, { data: [thread('last')] })).toBe(true)
    expect(ids(entry)).toEqual(['first', 'last'])
  })

  it.each(['foreground', 'background'])('preserves notifications during a %s directory load', async (source) => {
    const entry = { workspacePath: '/workspace', threads: [thread('old')] as unknown[], threadListLoaded: false }
    const listed = { firstUnread: { type: 'turn', turnId: 'turn' }, revision: 'old' }
    const notified = { firstUnread: null, revision: 'new' }
    page(entry, params, { data: [thread('first')], readStates: { first: listed }, nextCursor: 'next' })
    const request = { ...params, cursor: 'next' }
    const generation = beginWorkspaceThreadListRequest(entry, 'thread/list', request)
    let finish!: (value: { data: unknown[]; readStates: Record<string, unknown> }) => void
    const response = new Promise<{ data: unknown[]; readStates: Record<string, unknown> }>(resolve => { finish = resolve })
    const pending = source === 'background'
      ? refreshWorkspaceThreadListCache(entry, () => response)
      : response.then(result => applyWorkspaceThreadListRequestResult(entry, 'thread/list', request, result, generation))
    if (source === 'background') {
      expect(applyWorkspaceThreadListRequestResult(entry, 'thread/list', request, { data: [thread('outdated')] }, generation)).toBe(false)
    }
    const notify = (method: string, payload: unknown) => {
      entry.threads = applyWorkspaceThreadNotificationToCache(entry.threads, method, payload).threads
      noteWorkspaceThreadNotification(entry, method, payload)
    }
    notify('thread/started', { thread: thread('new') })
    notify('thread/renamed', { threadId: 'last', displayName: 'Updated' })
    notify('thread/status/changed', { threadId: 'last', runtime: { busy: true } })
    notify('thread/readState/changed', { threadId: 'first', readState: notified })
    notify('thread/readState/changed', { threadId: 'last', readState: notified })
    notify('thread/deleted', { threadId: 'deleted' })
    notify('thread/archived', { threadId: 'archived' })
    finish({
      data: [thread('first'), thread('last'), thread('deleted'), thread('archived'), {
        ...thread('child'), originChannel: 'subagent',
        source: { kind: 'subagent', subAgent: { parentThreadId: 'deleted' } }
      }],
      readStates: { first: listed, last: listed }
    })
    await expect(pending).resolves.toBe(true)
    expect(entry.threadListLoaded).toBe(true)
    expect(ids(entry)).toEqual(['new', 'first', 'last'])
    expect(entry.threads[1]).toMatchObject({ readState: notified })
    expect(entry.threads[2]).toMatchObject({ displayName: 'Updated', runtime: { busy: true }, readState: notified })
  })
})
