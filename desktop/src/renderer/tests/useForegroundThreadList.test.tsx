import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useForegroundThreadList } from '../hooks/useForegroundThreadList'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import type { ThreadReadState, ThreadSummary } from '../types/thread'
import type { KnownNotificationPayload } from '../../shared/appServerBoundary'
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
type ThreadList = { data: ThreadSummary[]; nextCursor?: string | null; readStates?: Record<string, ThreadReadState> }

function thread(id: string): ThreadSummary {
  return { id, displayName: id, workspacePath: '/a', status: 'active', originChannel: 'dotcraft-desktop',
    createdAt: '2026-11-10T00:00:00Z', lastActiveAt: '2026-11-10T00:00:00Z' }
}

let settings: ReturnType<typeof deferred<Settings>>
let list: ReturnType<typeof deferred<ThreadList>>
let settingsSet: ReturnType<typeof vi.fn>
let sendRequest: ReturnType<typeof vi.fn>
let notifications: Set<(payload: KnownNotificationPayload) => void>
const onNotification = (listener: (payload: KnownNotificationPayload) => void) => {
  notifications.add(listener)
  return () => { notifications.delete(listener) }
}

beforeEach(() => {
  notifications = new Set()
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
    appServer: { sendRequest, onNotification }
  })
})

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('foreground thread list loading', () => {
  it('makes the first page ready while the remaining metadata is still loading', async () => {
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => { list.resolve({ data: [thread('first')], nextCursor: 'page-2' }); await load })
    expect(sendRequest).toHaveBeenNthCalledWith(2, 'thread/list', expect.objectContaining({ limit: 50, cursor: 'page-2' }))
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['first'])
    expect(useThreadStore.getState().loading).toBe(false)
    expect(result.current.loadedProjectKey).toBe('/a')
    await act(async () => { next.resolve({ data: [thread('older'), thread('first')] }) })
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['older', 'first'])
    expect(notifications.size).toBe(0)
  })

  it('keeps pins and a pending navigation until their later page arrives', async () => {
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/a', workspacePath: '/a', threadId: 'older' })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => {
      settings.resolve({ pinnedThreadIdsByWorkspace: { '/a': ['older', 'missing'] } })
      list.resolve({ data: [thread('first')], nextCursor: 'page-2' })
      await load
    })
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['older', 'missing'])
    expect(useUIStore.getState().pendingProjectThreadOpen?.threadId).toBe('older')
    expect(settingsSet).not.toHaveBeenCalled()
    await act(async () => { next.resolve({ data: [thread('older')] }) })
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['older'])
    expect(useThreadStore.getState().activeThreadId).toBe('older')
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
  })

  it.each(['project', 'connection', 'reload'] as const)('discards a late page and stops its chain after %s changes', async (change) => {
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    const project = { current: '/a' }
    const path = { current: '/a' }
    const { result } = renderHook(() => useForegroundThreadList(project, path))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => { list.resolve({ data: [thread('first')], nextCursor: 'page-2' }); await load })
    if (change === 'reload') {
      sendRequest.mockResolvedValueOnce({ data: [thread('fresh')] })
      await act(async () => { await result.current.reloadThreadList() })
    } else {
      act(() => {
        if (change === 'project') { project.current = '/b'; path.current = '/b' }
        else useConnectionStore.getState().setStatus({ status: 'connected', connectionId: 'new' })
      })
    }
    const calls = sendRequest.mock.calls.length
    await act(async () => { next.resolve({ data: [thread('stale')], nextCursor: 'page-3' }) })
    expect(sendRequest).toHaveBeenCalledTimes(calls)
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(change === 'reload' ? ['fresh'] : ['first'])
  })

  it('keeps notification updates and does not resurrect deleted or archived rows from a later page', async () => {
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => { list.resolve({ data: [thread('first'), thread('deleted')] , nextCursor: 'page-2' }); await load })
    const readState: ThreadReadState = { revision: 'new', firstUnread: null }
    act(() => {
      useThreadStore.getState().renameThread('first', 'New name')
      useThreadStore.getState().setThreadReadState('first', readState)
      useThreadStore.getState().removeThreadTree('deleted')
      for (const listener of notifications) {
        listener({ method: 'thread/deleted', params: { threadId: 'deleted' } })
        listener({ method: 'thread/archived', params: { threadId: 'unseen-archived' } })
        listener({ method: 'thread/readState/changed', params: { threadId: 'older', readState } })
        listener({ method: 'thread/renamed', params: { threadId: 'older', displayName: 'Older renamed' } })
      }
    })
    await act(async () => { next.resolve({ data: [thread('first'), thread('deleted'), thread('unseen-archived'), thread('older')],
      readStates: { first: { revision: 'old', firstUnread: { type: 'threadStart' } }, older: { revision: 'old', firstUnread: { type: 'threadStart' } } } }) })
    const threads = useThreadStore.getState().threadList
    expect(threads.map((entry) => entry.id)).toEqual(['older', 'first'])
    expect(threads.find((entry) => entry.id === 'first')).toMatchObject({ displayName: 'New name', readState })
    expect(threads.find((entry) => entry.id === 'older')).toMatchObject({ displayName: 'Older renamed', readState })
  })

  it('preserves a selected archived thread and newer first-page state during refresh', async () => {
    const archived = { ...thread('archived'), status: 'archived' as const }
    useThreadStore.getState().setThreadList([archived, thread('first'), thread('deleted')], '/a')
    useThreadStore.getState().setActiveThreadId('archived')
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    const readState: ThreadReadState = { revision: 'new', firstUnread: null }
    act(() => {
      useThreadStore.getState().setThreadReadState('first', readState)
      useThreadStore.getState().renameThread('first', 'Renamed while waiting')
      useThreadStore.getState().removeThreadTree('deleted')
      useThreadStore.getState().addThread(thread('new'))
    })
    await act(async () => {
      list.resolve({ data: [thread('first'), thread('deleted')], readStates: { first: { revision: 'old', firstUnread: { type: 'threadStart' } } } })
      await load
    })
    const threads = useThreadStore.getState().threadList
    expect(threads.map((entry) => entry.id)).toEqual(['first', 'new', 'archived'])
    expect(threads.find((entry) => entry.id === 'first')).toMatchObject({ displayName: 'Renamed while waiting', readState })
    expect(useThreadStore.getState().activeThreadId).toBe('archived')
  })

  it('retains off-page active and running threads with their approval and goal state', async () => {
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    const runtime = { running: true, activeTurnId: 'turn-running', busy: true, waitingOnApproval: true }
    useThreadStore.getState().setThreadList([thread('selected'), { ...thread('running'), runtime }, thread('old-static')], '/a')
    useThreadStore.getState().setActiveThreadId('selected')
    useThreadStore.getState().parkApproval('running', { bridgeId: 'approval', turnId: 'turn-running', rawParams: {} })
    const goal = { threadId: 'running', objective: 'Finish', status: 'active' as const, tokensUsed: 0, timeUsedSeconds: 0, createdAt: 1, updatedAt: 1 }
    useThreadStore.getState().setThreadGoal(goal)
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => { list.resolve({ data: [thread('first')], nextCursor: 'page-2' }); await load })
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['first', 'selected', 'running'])
    expect(useThreadStore.getState().activeThreadId).toBe('selected')
    expect(useThreadStore.getState().runtimeSnapshots.get('running')).toMatchObject(runtime)
    expect(useThreadStore.getState().parkedApprovals.get('running')?.[0].bridgeId).toBe('approval')
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('running')).toBe(true)
    expect(useThreadStore.getState().goalSnapshots.get('running')).toEqual(goal)
    await act(async () => { next.resolve({ data: [thread('selected'), thread('running'), thread('old-static')] }) })
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['old-static', 'first', 'selected', 'running'])
    expect(useThreadStore.getState().goalSnapshots.get('running')).toEqual(goal)
    expect(useThreadStore.getState().runtimeSnapshots.get('running')).toMatchObject(runtime)
    expect(useThreadStore.getState().parkedApprovals.get('running')?.[0].bridgeId).toBe('approval')
  })

  it('preserves a runtime notification that arrives while the first page is loading', async () => {
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    useThreadStore.getState().setThreadList([thread('running')], '/a')
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    const runtime = { running: true, activeTurnId: 'turn-new', busy: true, waitingOnApproval: false }
    act(() => { useThreadStore.getState().applyRuntimeSnapshot('running', runtime, { isDesktopOrigin: true }) })
    await act(async () => { list.resolve({ data: [thread('first')], nextCursor: 'page-2' }); await load })
    expect(useThreadStore.getState().threadList.find((entry) => entry.id === 'running')?.runtime).toMatchObject(runtime)
    await act(async () => { next.resolve({ data: [thread('running')] }) })
    expect(useThreadStore.getState().runningTurnThreadIds.has('running')).toBe(true)
    expect(useThreadStore.getState().runtimeSnapshots.get('running')).toMatchObject(runtime)
  })

  it('keeps the first page, pins, and pending navigation when a background page fails', async () => {
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const next = deferred<ThreadList>()
    sendRequest.mockReturnValueOnce(list.promise).mockReturnValueOnce(next.promise)
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/a', workspacePath: '/a', threadId: 'older' })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    await act(async () => {
      settings.resolve({ pinnedThreadIdsByWorkspace: { '/a': ['older'] } })
      list.resolve({ data: [thread('first')], nextCursor: 'page-2' })
      await load
    })
    await act(async () => { next.reject(new Error('page unavailable')) })
    expect(result.current.loadedProjectKey).toBe('/a')
    expect(useThreadStore.getState().loading).toBe(false)
    expect(useThreadStore.getState().threadList.map((entry) => entry.id)).toEqual(['first'])
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['older'])
    expect(useUIStore.getState().pendingProjectThreadOpen?.threadId).toBe('older')
    expect(settingsSet).not.toHaveBeenCalled()
    expect(notifications.size).toBe(0)
  })

  it('applies the list and opens the pending thread before settings resolve', async () => {
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey: '/a', workspacePath: '/a', threadId: 'a' })
    const { result } = renderHook(() => useForegroundThreadList({ current: '/a' }, { current: '/a' }))
    let load!: Promise<void>
    act(() => { load = result.current.reloadThreadList() })
    expect(sendRequest).toHaveBeenCalledTimes(1)
    expect(sendRequest).toHaveBeenCalledWith('thread/list', expect.objectContaining({ scope: 'workspace', includeSubAgents: true, limit: 50 }))
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
    installDesktopApiMock({ settings: { get, set: settingsSet }, appServer: { sendRequest, onNotification } })
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
