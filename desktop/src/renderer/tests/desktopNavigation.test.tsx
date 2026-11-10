import { beforeEach, describe, expect, it, vi } from 'vitest'
import { openThread } from '../utils/desktopNavigation'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}

beforeEach(() => {
  useThreadStore.getState().reset()
  useConnectionStore.getState().reset()
  useWorkspaceProjectsStore.getState().setPayload({
    foregroundWorkspacePath: '/a', foregroundProjectId: '/a', secondaryLimit: 8, projects: []
  })
  useUIStore.setState({ pendingProjectThreadOpen: null, activeMainView: 'conversation' })
  useThreadStore.getState().setActiveThreadId('thread-a')
})

describe('Desktop thread navigation', () => {
  it('does not let an older failed deep link clear a newer request for the same target', async () => {
    const first = deferred<void>()
    const second = deferred<void>()
    installDesktopApiMock({ workspace: {
      switch: vi.fn().mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    } })
    const older = openThread('thread-b', '/b')
    const failure = expect(older).rejects.toThrow('Old switch failed')
    const newer = openThread('thread-b', '/b')
    const latest = useUIStore.getState().pendingProjectThreadOpen!
    first.reject(new Error('Old switch failed'))
    await failure
    expect(useUIStore.getState().pendingProjectThreadOpen).toBe(latest)
    second.resolve()
    await newer
    expect(useUIStore.getState().pendingProjectThreadOpen).toBe(latest)
  })

  it('does not activate a late archived read after the user reselects the current thread', async () => {
    const read = deferred<{ thread: { id: string; status: string } }>()
    installDesktopApiMock({ appServer: { sendRequest: vi.fn().mockReturnValue(read.promise) } })
    const opening = openThread('archived', undefined, { includeArchived: true })
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    useThreadStore.getState().setActiveThreadId('thread-a')
    read.resolve({ thread: { id: 'archived', status: 'archived' } })
    await opening
    expect(useThreadStore.getState().activeThreadId).toBe('thread-a')
    expect(useThreadStore.getState().threadList).toHaveLength(0)
  })

  it('ignores an archived read after a foreground workspace round trip on retained connections', async () => {
    const read = deferred<{ thread: { id: string; status: string } }>()
    installDesktopApiMock({ appServer: { sendRequest: vi.fn().mockReturnValue(read.promise) } })
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: '/a', connectionId: 'connection-a' })
    const opening = openThread('archived', undefined, { includeArchived: true })
    for (const path of ['/b', '/a']) {
      useWorkspaceProjectsStore.getState().setPayload({
        foregroundWorkspacePath: path, foregroundProjectId: path, secondaryLimit: 8, projects: []
      })
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: path, connectionId: path === '/a' ? 'connection-a' : 'connection-b'
      })
      useThreadStore.getState().resetForeground()
    }
    read.resolve({ thread: { id: 'archived', status: 'archived' } })
    await opening
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useThreadStore.getState().threadList).toHaveLength(0)
  })

  it('selects a foreground thread without waiting for the projects payload', async () => {
    useWorkspaceProjectsStore.getState().reset()
    await openThread('thread-b')
    expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
  })
})
