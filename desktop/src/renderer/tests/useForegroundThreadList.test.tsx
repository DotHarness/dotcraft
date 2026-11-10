import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useForegroundThreadList } from '../hooks/useForegroundThreadList'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import type { ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}

type Settings = {
  pinnedThreadIdsByWorkspace?: Record<string, string[]>
  turnBookmarksByThread?: Record<string, string[]>
}
type ThreadList = { data: ThreadSummary[] }

function thread(id: string): ThreadSummary {
  return { id, displayName: id, workspacePath: '/a', status: 'active', originChannel: 'dotcraft-desktop',
    createdAt: '2026-11-10T00:00:00Z', lastActiveAt: '2026-11-10T00:00:00Z' }
}

let settings: ReturnType<typeof deferred<Settings>>
let list: ReturnType<typeof deferred<ThreadList>>
let settingsSet: ReturnType<typeof vi.fn>
let sendRequest: ReturnType<typeof vi.fn>

beforeEach(() => {
  useThreadStore.getState().reset()
  useUIStore.setState({ pendingProjectThreadOpen: null })
  useConnectionStore.getState().reset()
  useTurnBookmarkStore.setState({ loaded: false, byThread: {} })
  settings = deferred<Settings>()
  list = deferred<ThreadList>()
  settingsSet = vi.fn().mockResolvedValue(undefined)
  sendRequest = vi.fn(() => list.promise)
  installDesktopApiMock({
    settings: { get: vi.fn(() => settings.promise), set: settingsSet },
    appServer: { sendRequest }
  })
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('foreground thread list loading', () => {
  it('applies the list and opens the pending thread before settings resolve', async () => {
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/a', workspacePath: '/a', threadId: 'a' })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(sendRequest).toHaveBeenCalledWith('thread/list', expect.objectContaining({ scope: 'workspace', includeSubAgents: true }))
    await act(async () => { list.resolve({ data: [thread('a')] }); await load })
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['a'])
    expect(useThreadStore.getState().activeThreadId).toBe('a')
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    expect(useThreadStore.getState().loading).toBe(false)
    expect(result.current.loadedProjectKey).toBe('/a')
    await act(async () => { settings.resolve({ pinnedThreadIdsByWorkspace: { '/a': ['a'] } }) })
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['a'])
  })

  it('hydrates settings independently and prunes pins only after a complete list arrives', async () => {
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => { settings.resolve({
      pinnedThreadIdsByWorkspace: { '/a': ['a', 'missing'] }, turnBookmarksByThread: { '/a::a': ['turn-a'] }
    }) })
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['a', 'missing'])
    expect(useTurnBookmarkStore.getState().byThread).toEqual({ '/a::a': ['turn-a'] })
    expect(settingsSet).not.toHaveBeenCalled()
    await act(async () => { list.resolve({ data: [thread('a')] }); await load })
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['a'])
    expect(settingsSet).toHaveBeenCalledWith({ pinnedThreadIdsByWorkspace: { '/a': ['a'] } })
  })

  it('keeps a successful list and pending selection when settings fail', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/a', workspacePath: '/a', threadId: 'a' })
    useThreadStore.getState().hydratePinnedThreadIds('/a', ['kept'])
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => { settings.reject(new Error('settings unavailable')) })
    await act(async () => { list.resolve({ data: [thread('a')] }); await load })
    expect(useThreadStore.getState().activeThreadId).toBe('a')
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['a'])
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['kept'])
    expect(settingsSet).not.toHaveBeenCalled()
    expect(result.current.loadedProjectKey).toBe('/a')
  })

  it.each(['connection', 'project'] as const)('ignores late list and settings results after the %s changes', async (change) => {
    const project = { current: 'remote:device:a:project' }
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: project.current, connectionId: 'one' })
    const path = { current: '/shared' }
    const { result } = renderHook(() => useForegroundThreadList(project, path))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    act(() => {
      if (change === 'connection') {
        useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: project.current, connectionId: 'two' })
      } else {
        project.current = 'remote:device:b:project'
      }
    })
    await act(async () => {
      settings.resolve({ pinnedThreadIdsByWorkspace: { 'remote:device:a:project': ['a'] }, turnBookmarksByThread: { old: ['turn'] } })
      list.resolve({ data: [thread('a')] })
      await load
    })
    expect(useThreadStore.getState().threadList).toEqual([])
    expect(useThreadStore.getState().pinnedThreadIds).toEqual([])
    expect(useTurnBookmarkStore.getState().loaded).toBe(false)
    expect(result.current.loadedProjectKey).toBeNull()
  })

  it('ignores an older response and its settings after a newer load has completed', async () => {
    const get = vi.fn(() => settings.promise)
    installDesktopApiMock({ settings: { get, set: settingsSet }, appServer: { sendRequest } })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let oldLoad!: Promise<void>
    act(() => { oldLoad = result.current.reloadThreadList() })
    get.mockResolvedValue({ pinnedThreadIdsByWorkspace: { '/a': ['current'] } })
    sendRequest.mockResolvedValue({ data: [thread('current')] })
    await act(async () => { await result.current.reloadThreadList() })
    await act(async () => {
      settings.resolve({ pinnedThreadIdsByWorkspace: { '/a': ['stale'] } })
      list.resolve({ data: [thread('stale')] })
      await oldLoad
    })
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['current'])
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['current'])
  })

  it('does not let an older request consume a new navigation to the same thread', async () => {
    const target = { projectKey: '/a', workspacePath: '/a', threadId: 'a' }
    useUIStore.getState().setPendingProjectThreadOpen(target)
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    act(() => { useUIStore.getState().setPendingProjectThreadOpen(target) })
    const latest = useUIStore.getState().pendingProjectThreadOpen
    await act(async () => { list.resolve({ data: [thread('a')] }); await load })
    expect(useUIStore.getState().pendingProjectThreadOpen).toBe(latest)
    expect(useThreadStore.getState().activeThreadId).toBeNull()
  })

  it('keeps the existing view and pins when the list fails and cancels a retry on invalidation', async () => {
    vi.useFakeTimers()
    vi.spyOn(console, 'error').mockImplementation(() => {})
    useThreadStore.getState().setThreadList([thread('warm')], '/a')
    useThreadStore.getState().setActiveThreadId('warm')
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => {
      settings.resolve({ pinnedThreadIdsByWorkspace: { '/a': ['warm', 'not-listed-yet'] } })
      list.reject(new Error('list unavailable'))
      await load
    })
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['warm'])
    expect(useThreadStore.getState().activeThreadId).toBe('warm')
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['warm', 'not-listed-yet'])
    expect(settingsSet).not.toHaveBeenCalled()
    act(() => { result.current.invalidateThreadList() })
    await act(async () => { await vi.runAllTimersAsync() })
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(useThreadStore.getState().loading).toBe(false)
  })

  it('does not let a retry chain clear a navigation created after it started', async () => {
    vi.useFakeTimers()
    vi.spyOn(console, 'error').mockImplementation(() => {})
    sendRequest.mockRejectedValue(new Error('list unavailable'))
    const target = { projectKey: '/a', workspacePath: '/a', threadId: 'a' }
    useUIStore.getState().setPendingProjectThreadOpen(target)
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    await act(async () => { await result.current.reloadThreadList() })
    act(() => { useUIStore.getState().setPendingProjectThreadOpen(target) })
    const latest = useUIStore.getState().pendingProjectThreadOpen
    await act(async () => { await vi.runAllTimersAsync() })
    expect(sendRequest).toHaveBeenCalledTimes(4)
    expect(useUIStore.getState().pendingProjectThreadOpen).toBe(latest)
  })

  it('limits retries and clears its pending navigation when they are exhausted', async () => {
    vi.useFakeTimers()
    vi.spyOn(console, 'error').mockImplementation(() => {})
    sendRequest.mockRejectedValue(new Error('list unavailable'))
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/a', workspacePath: '/a', threadId: 'a' })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    await act(async () => { await result.current.reloadThreadList() })
    await act(async () => { await vi.runAllTimersAsync() })
    expect(sendRequest).toHaveBeenCalledTimes(4)
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
  })
})
