import { act, renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { newProjectChat, openProjectThread } from '../components/sidebar/projectConnectionActions'
import { useForegroundThreadList } from '../hooks/useForegroundThreadList'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const project: WorkspaceProjectSummary = {
  projectId: 'remote:device:computer-a:project-a', kind: 'remote', path: '/shared/path', name: 'project',
  state: 'secondary', running: true, loaded: true, threadCount: 1, threads: [], pinned: false,
  remote: { source: 'device', hostId: 'computer-a', remoteProjectId: 'project-a' }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}

beforeEach(() => {
  useThreadStore.getState().reset()
  useUIStore.setState({ pendingProjectThreadOpen: null, activeMainView: 'conversation' })
})

describe('device project connection actions', () => {
  it('opens a background thread only after the owning project list loads, even when another device has the same path and thread id', async () => {
    const openProject = vi.fn().mockResolvedValue({ ok: true, value: null })
    installDesktopApiMock({
      pairedComputers: { openProject },
      settings: { get: vi.fn().mockResolvedValue({}) },
      appServer: { onNotification: () => () => {}, sendRequest: vi.fn().mockResolvedValue({ data: [{ id: 'shared-thread', status: 'active' }] }) }
    })
    const activeProject = { current: 'remote:device:computer-b:project-b' }
    const { result } = renderHook(() => useForegroundThreadList(activeProject, { current: project.path }))

    await act(async () => { await openProjectThread(project, 'shared-thread') })
    expect(openProject).toHaveBeenCalledWith('computer-a', 'project-a')
    expect(useThreadStore.getState().activeThreadId).toBeNull()

    await act(async () => { await result.current.reloadThreadList() })
    expect(useUIStore.getState().pendingProjectThreadOpen?.projectKey).toBe(project.projectId)
    expect(useThreadStore.getState().activeThreadId).toBeNull()

    activeProject.current = project.projectId!
    await act(async () => { await result.current.reloadThreadList() })
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    expect(useThreadStore.getState().activeThreadId).toBe('shared-thread')
  })

  it('does not clear a newer request for the same project and thread when the first attempt fails', async () => {
    const first = deferred<{ ok: true; value: null }>()
    const second = deferred<{ ok: true; value: null }>()
    installDesktopApiMock({ pairedComputers: {
      openProject: vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    } })

    const oldOpening = openProjectThread(project, 'thread-b')
    const newOpening = openProjectThread(project, 'thread-b')
    const latest = useUIStore.getState().pendingProjectThreadOpen!
    first.reject(new Error('First attempt failed'))
    await oldOpening
    expect(useUIStore.getState().pendingProjectThreadOpen).toBe(latest)
    second.resolve({ ok: true, value: null })
    await newOpening
    expect(useUIStore.getState().pendingProjectThreadOpen).toBe(latest)
  })

  it('keeps a direct foreground selection after an earlier pending list finishes', async () => {
    const list = deferred<{ data: Array<{ id: string; status: string }> }>()
    installDesktopApiMock({
      settings: { get: vi.fn().mockResolvedValue({}) },
      appServer: { onNotification: () => () => {}, sendRequest: vi.fn().mockReturnValue(list.promise) }
    })
    useUIStore.getState().setPendingProjectThreadOpen({
      projectKey: project.projectId!, workspacePath: project.path, threadId: 'old-thread'
    })
    const activeProject = { current: project.projectId! }
    const { result } = renderHook(() => useForegroundThreadList(activeProject, { current: project.path }))
    let reload!: Promise<void>
    act(() => { reload = result.current.reloadThreadList() })

    await act(async () => { await openProjectThread({ ...project, state: 'foreground' }, 'new-thread') })
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    await act(async () => {
      list.resolve({ data: [{ id: 'old-thread', status: 'active' }, { id: 'new-thread', status: 'active' }] })
      await reload
    })
    expect(useThreadStore.getState().activeThreadId).toBe('new-thread')
  })

  it('keeps the current conversation until the device project is promoted before opening its new-chat draft', async () => {
    let resolveOpen!: (value: { ok: true; value: null }) => void
    const openProject = vi.fn(() => new Promise<{ ok: true; value: null }>(resolve => { resolveOpen = resolve }))
    installDesktopApiMock({ pairedComputers: { openProject } })
    useThreadStore.getState().setActiveThreadId('foreground-thread')

    const opening = newProjectChat(project, false)
    expect(useThreadStore.getState().activeThreadId).toBe('foreground-thread')
    resolveOpen({ ok: true, value: null })
    await opening

    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().welcomeDraftWorkspacePath).toBe(project.projectId)
  })

  it('does not clear the current conversation or draft when opening the device project fails', async () => {
    installDesktopApiMock({
      pairedComputers: { openProject: vi.fn().mockResolvedValue({ ok: false, code: 'failed', message: 'Open cancelled' }) }
    })
    useThreadStore.getState().setActiveThreadId('foreground-thread')
    useUIStore.setState({ welcomeDraftWorkspacePath: '/foreground' })

    await newProjectChat(project, false)

    expect(useThreadStore.getState().activeThreadId).toBe('foreground-thread')
    expect(useUIStore.getState().welcomeDraftWorkspacePath).toBe('/foreground')
  })
})
