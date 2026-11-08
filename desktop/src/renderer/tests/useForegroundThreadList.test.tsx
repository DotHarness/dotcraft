import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useForegroundThreadList } from '../hooks/useForegroundThreadList'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

describe('foreground thread list loading', () => {
  beforeEach(() => {
    useThreadStore.getState().reset()
    useUIStore.setState({ pendingProjectThreadOpen: null })
  })

  it('starts one list request before settings resolve and hydrates pins after both complete', async () => {
    const settings = deferred<{ pinnedThreadIdsByWorkspace: Record<string, string[]> }>()
    const get = vi.fn(() => settings.promise)
    const sendRequest = vi.fn().mockResolvedValue({ data: [{ id: 'a', status: 'idle' }] })
    installDesktopApiMock({ settings: { get }, appServer: { sendRequest } })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(sendRequest).toHaveBeenCalledWith('thread/list', expect.objectContaining({ scope: 'workspace', includeSubAgents: true }))
    expect(get).toHaveBeenCalledTimes(1)
    await act(async () => {
      settings.resolve({ pinnedThreadIdsByWorkspace: { '/a': ['a'] } })
      await load
    })
    expect(useThreadStore.getState().threadList.map((t) => t.id)).toEqual(['a'])
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['a'])
    expect(useThreadStore.getState().loading).toBe(false)
  })

  it('opens an archived ranking target without restoring it or adding archives to the ordinary list', async () => {
    const target = { projectKey: '/b', workspacePath: '/b', threadId: 'archived', includeArchived: true }
    useUIStore.getState().setPendingProjectThreadOpen(target)
    const sendRequest = vi.fn().mockResolvedValue({ data: [
      { id: 'active', status: 'idle' }, { id: 'archived', status: 'archived' }
    ] })
    installDesktopApiMock({ settings: { get: async () => ({}) }, appServer: { sendRequest } })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/b' }, { current: '/b' }))
    await act(async () => { await result.current.reloadThreadList() })
    expect(sendRequest).toHaveBeenCalledExactlyOnceWith('thread/list', expect.objectContaining({ includeArchived: true }))
    expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['active'])
    expect(useThreadStore.getState().activeThreadId).toBe('archived')
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
  })
})
