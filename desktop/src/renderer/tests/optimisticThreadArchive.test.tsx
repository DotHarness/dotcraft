import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'
import type { WorkspaceProjectsPayload } from '../../shared/workspaceProjects'
import { archiveThreadWithUndo } from '../utils/threadArchive'
import { archiveWorkspaceThread } from '../utils/archiveWorkspaceThread'
import { installDesktopApiMock } from './desktopApiMock'

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (error: Error) => void
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no })
  return { promise, resolve, reject }
}

const t = (key: string) => key
const state = () => useThreadStore.getState()
const ids = () => state().threadList.map(thread => thread.id)
const undo = () => {
  const store = useToastStore.getState()
  store.settleToast(store.toasts.find(toast => toast.action)!.id, 'action')
}

function thread(id: string, parentThreadId?: string): ThreadSummary {
  return {
    id, displayName: id, status: 'active', originChannel: parentThreadId ? 'subagent' : 'dotcraft-desktop',
    createdAt: '2026-11-11T00:00:00Z', lastActiveAt: '2026-11-11T00:00:00Z',
    ...(parentThreadId ? { source: { kind: 'subagent', subAgent: { parentThreadId, depth: 1 } } } : {})
  }
}

function projectPayload(threads: ThreadSummary[]): WorkspaceProjectsPayload {
  return {
    foregroundWorkspacePath: '/foreground', secondaryLimit: 8,
    projects: [{
      path: '/secondary', connectionKey: '/secondary', connectionId: 'secondary-lifetime',
      name: 'secondary', state: 'secondary', running: true, loaded: true, pinned: false,
      threadCount: threads.length, threads
    }]
  }
}

beforeEach(() => {
  state().reset()
  useConnectionStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useToastStore.setState({ toasts: [] })
})

describe('optimistic thread archive', () => {
  it('hides the tree and offers Undo before cleanup, suppresses refreshes, and queues Undo after archive', async () => {
    const cleanup = deferred<unknown>()
    const started = deferred<void>()
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/archive') { started.resolve(); return cleanup.promise }
      return {}
    })
    installDesktopApiMock({ appServer: { sendRequest } })
    const tree = [thread('parent'), thread('child', 'parent'), thread('grandchild', 'child'), thread('other')]
    state().setThreadList(tree)
    state().setActiveThreadId('parent')

    const archiving = archiveThreadWithUndo({ threadId: 'parent', t })

    expect(ids()).toEqual(['other'])
    expect(state().activeThreadId).toBeNull()
    expect(useToastStore.getState().toasts[0].action).toBeDefined()
    state().setThreadList(tree)
    state().upsertThreads(tree)
    state().addThread(thread('child', 'parent'))
    expect(ids()).toEqual(['other'])
    await started.promise
    undo()
    expect(sendRequest).not.toHaveBeenCalledWith('thread/unarchive', expect.anything())

    cleanup.resolve({})
    expect(await archiving).toBe(true)
    await vi.waitFor(() => expect(ids()).toEqual(['parent', 'child', 'grandchild', 'other']))
    expect(sendRequest.mock.calls.map(([method]) => method)).toEqual(['thread/read', 'thread/archive', 'thread/unarchive'])
    expect(state().activeThreadId).toBe('parent')
  })

  it('discards Undo and restores rows on failure without overriding newer navigation or runtime', async () => {
    const cleanup = deferred<unknown>()
    const started = deferred<void>()
    const sendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/archive') { started.resolve(); return cleanup.promise }
      return {}
    })
    installDesktopApiMock({ appServer: { sendRequest } })
    state().setThreadList([thread('parent'), thread('other')])
    state().setActiveThreadId('parent')
    state().hydratePinnedThreadIds('/workspace', ['parent'])
    const archiving = archiveThreadWithUndo({ threadId: 'parent', t })
    state().prunePinnedThreadIds()
    expect(state().pinnedThreadIds).toEqual(['parent'])
    await started.promise
    undo()
    state().setActiveThreadId('other')
    state().applyRuntimeSnapshot('parent', {
      running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false
    }, { isDesktopOrigin: true })

    cleanup.reject(new Error('offline'))

    expect(await archiving).toBe(false)
    expect(ids()).toEqual(['parent', 'other'])
    expect(state().activeThreadId).toBe('other')
    expect(state().pinnedThreadIds).toEqual(['parent'])
    expect(state().threadList.find(thread => thread.id === 'parent')?.runtime?.running).toBe(false)
    expect(useToastStore.getState().toasts.map(({ type, action }) => ({ type, action })))
      .toEqual([{ type: 'error', action: undefined }])
    expect(sendRequest).not.toHaveBeenCalledWith('thread/unarchive', expect.anything())
  })

  it('keeps archive and Undo on their original connection when the foreground changes', async () => {
    const cleanup = deferred<unknown>()
    const started = deferred<void>()
    const sendConnectionRequest = vi.fn(async (_key: string, method: string) => {
      if (method === 'thread/archive') { started.resolve(); return cleanup.promise }
      return {}
    })
    installDesktopApiMock({ appServer: { sendConnectionRequest } })
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: '/secondary', connectionId: 'secondary-lifetime' })
    state().setThreadList([thread('same-id')], '/secondary')
    state().setActiveThreadId('same-id')
    useWorkspaceProjectsStore.getState().setPayload(projectPayload([thread('same-id')]))
    const archiving = archiveThreadWithUndo({ threadId: 'same-id', t })
    await started.promise
    state().setThreadList([thread('same-id')], '/foreground')
    state().setActiveThreadId('same-id')
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: '/foreground', connectionId: 'new-lifetime' })
    undo()

    cleanup.resolve({})
    expect(await archiving).toBe(true)
    await vi.waitFor(() => expect(useWorkspaceProjectsStore.getState().projects[0].threads).toHaveLength(1))
    expect(ids()).toEqual(['same-id'])
    expect(state().activeThreadId).toBe('same-id')
    expect(sendConnectionRequest.mock.calls.map(call => call.slice(0, 2))).toEqual([
      ['/secondary', 'thread/read'], ['/secondary', 'thread/archive'], ['/secondary', 'thread/unarchive']
    ])
    for (const call of vi.mocked(window.api.appServer.sendConnectionRequest).mock.calls) {
      expect(call[4]).toEqual({ connectionId: 'secondary-lifetime' })
    }
  })
})

describe('secondary workspace optimistic archive', () => {
  it('hides the cached subtree through refresh and restores on its own connection without touching foreground', async () => {
    const cleanup = deferred<void>()
    const archiveThread = vi.fn(() => cleanup.promise)
    const sendConnectionRequest = vi.fn().mockResolvedValue({})
    installDesktopApiMock({ workspace: { archiveThread }, appServer: { sendConnectionRequest } })
    state().setThreadList([thread('parent')], '/foreground')
    state().setActiveThreadId('parent')
    const payload = projectPayload([thread('parent'), thread('child', 'parent'), thread('other')])
    useWorkspaceProjectsStore.getState().setPayload(payload)

    const archiving = archiveWorkspaceThread('/secondary', thread('parent'), t)

    expect(useWorkspaceProjectsStore.getState().projects[0].threads).toEqual([thread('other')])
    useWorkspaceProjectsStore.getState().setPayload(payload)
    expect(useWorkspaceProjectsStore.getState().projects[0].threads).toEqual([thread('other')])
    expect(ids()).toEqual(['parent'])
    expect(state().activeThreadId).toBe('parent')
    undo()
    expect(sendConnectionRequest).not.toHaveBeenCalled()

    cleanup.resolve()
    expect(await archiving).toBe(true)
    await vi.waitFor(() => expect(new Set(useWorkspaceProjectsStore.getState().projects[0].threads))
      .toEqual(new Set(payload.projects[0].threads)))
    expect(sendConnectionRequest).toHaveBeenCalledWith('/secondary', 'thread/unarchive', { threadId: 'parent' }, undefined, {
      connectionId: 'secondary-lifetime'
    })
    expect(ids()).toEqual(['parent'])
  })

  it('rolls back only its own cached rows when archive fails', async () => {
    const cleanup = deferred<void>()
    installDesktopApiMock({ workspace: { archiveThread: () => cleanup.promise } })
    const payload = projectPayload([thread('parent'), thread('other')])
    useWorkspaceProjectsStore.getState().setPayload(payload)
    state().setThreadList([thread('parent')], '/foreground')
    const archiving = archiveWorkspaceThread('/secondary', thread('parent'), t)

    cleanup.reject(new Error('offline'))

    expect(await archiving).toBe(false)
    expect(new Set(useWorkspaceProjectsStore.getState().projects[0].threads)).toEqual(new Set(payload.projects[0].threads))
    expect(useWorkspaceProjectsStore.getState().projects[0].threadCount).toBe(2)
    expect(ids()).toEqual(['parent'])
    expect(useToastStore.getState().toasts.map(({ type, action }) => ({ type, action })))
      .toEqual([{ type: 'error', action: undefined }])
  })
})
