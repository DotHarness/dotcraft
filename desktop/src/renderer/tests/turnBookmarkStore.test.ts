import { beforeEach, describe, expect, it, vi } from 'vitest'
import { turnBookmarkKey, useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadSummary } from '../types/thread'

const settingsSet = vi.fn()
const KEY = 'c:/fixtures/ws::thread-1'

function thread(id: string): ThreadSummary {
  return {
    id,
    workspacePath: 'C:\\fixtures\\ws',
    displayName: id,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActiveAt: '2026-01-01T00:00:00.000Z'
  }
}

describe('turn bookmark store', () => {
  beforeEach(() => {
    settingsSet.mockReset()
    settingsSet.mockResolvedValue(undefined)
    Object.defineProperty(globalThis, 'window', {
      configurable: true,
      value: { api: { settings: { set: settingsSet } } }
    })
    useTurnBookmarkStore.setState({ loaded: false, byThread: {} })
    useThreadStore.getState().reset()
  })

  it('keys bookmarks by the normalized workspace and thread', () => {
    expect(turnBookmarkKey('C:\\fixtures\\ws\\', 'thread-1')).toBe(KEY)
    expect(turnBookmarkKey('  ', 'thread-1')).toBeNull()
  })

  it('persists each toggle for its thread and prunes the thread when its last bookmark goes', () => {
    const { toggle } = useTurnBookmarkStore.getState()

    toggle(KEY, 'turn-1:user-1')
    toggle(KEY, 'turn-2:user-2')
    toggle(KEY, 'turn-1:user-1')
    toggle(KEY, 'turn-2:user-2')

    expect(settingsSet.mock.calls.map(([partial]) => partial)).toEqual([
      { turnBookmarksByThread: { [KEY]: ['turn-1:user-1'] } },
      { turnBookmarksByThread: { [KEY]: ['turn-1:user-1', 'turn-2:user-2'] } },
      { turnBookmarksByThread: { [KEY]: ['turn-2:user-2'] } },
      { turnBookmarksByThread: { [KEY]: [] } }
    ])
    expect(useTurnBookmarkStore.getState().byThread).toEqual({})
  })

  it('loads the persisted bookmarks once', () => {
    const { hydrate } = useTurnBookmarkStore.getState()

    hydrate({ [KEY]: ['turn-1:user-1'] })
    hydrate({ [KEY]: ['stale'] })

    expect(useTurnBookmarkStore.getState().byThread).toEqual({ [KEY]: ['turn-1:user-1'] })
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('removes a deleted thread\'s bookmarks in every workspace', () => {
    useTurnBookmarkStore.getState().hydrate({
      [KEY]: ['turn-1:user-1'],
      'remote:servers:host-1:stack-1::thread-1': ['turn-9:user-9'],
      'c:/fixtures/ws::thread-2': ['turn-3:user-3']
    })

    useTurnBookmarkStore.getState().forgetThread('thread-1')
    useTurnBookmarkStore.getState().forgetThread('thread-unknown')

    expect(settingsSet).toHaveBeenCalledTimes(1)
    expect(settingsSet).toHaveBeenCalledWith({
      turnBookmarksByThread: { [KEY]: [], 'remote:servers:host-1:stack-1::thread-1': [] }
    })
    expect(useTurnBookmarkStore.getState().byThread).toEqual({ 'c:/fixtures/ws::thread-2': ['turn-3:user-3'] })
  })

  it('keeps bookmarks when an archived thread leaves the thread list', () => {
    useThreadStore.getState().setThreadList([thread('thread-1')])
    useTurnBookmarkStore.getState().hydrate({ [KEY]: ['turn-1:user-1'] })

    useThreadStore.getState().removeThreadTree('thread-1')

    expect(useTurnBookmarkStore.getState().byThread).toEqual({ [KEY]: ['turn-1:user-1'] })
    expect(settingsSet).not.toHaveBeenCalledWith(expect.objectContaining({ turnBookmarksByThread: expect.anything() }))
  })
})
