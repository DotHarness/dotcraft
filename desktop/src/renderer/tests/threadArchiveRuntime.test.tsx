import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { archiveWorkspaceThread } from '../utils/archiveWorkspaceThread'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { archiveThreadWithUndo } from '../utils/threadArchive'
import { archiveProjectThreads } from '../utils/archiveProjectThreads'
import { requestConfirmDialog } from '../components/ui/ConfirmDialog'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../components/ui/ConfirmDialog', () => ({ requestConfirmDialog: vi.fn() }))

const idle: ThreadRuntimeSnapshot = {
  running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false
}
const running: ThreadRuntimeSnapshot = { ...idle, running: true, busy: true, activeTurnId: 'turn-active' }
const state = () => useThreadStore.getState()
const t = (key: string) => key

function thread(id: string, runtime = idle, parent?: string): ThreadSummary {
  return {
    id, displayName: id, status: 'active', originChannel: parent ? 'subagent' : 'dotcraft-desktop',
    createdAt: '2026-10-24T00:00:00Z', lastActiveAt: '2026-10-24T00:00:00Z', runtime,
    ...(parent ? { source: { kind: 'subagent', subAgent: { parentThreadId: parent, depth: 1 } } } : {})
  }
}

let sendRequest: ReturnType<typeof vi.fn>
let archiveWorkspace: ReturnType<typeof vi.fn>

beforeEach(() => {
  state().reset()
  useWorkspaceProjectsStore.getState().reset()
  useToastStore.setState({ toasts: [] })
  vi.mocked(requestConfirmDialog).mockReset()
  vi.mocked(requestConfirmDialog).mockReturnValue({ result: Promise.resolve(false), dismiss: vi.fn() })
  sendRequest = vi.fn().mockResolvedValue({ thread: { runtime: idle } })
  archiveWorkspace = vi.fn().mockResolvedValue(undefined)
  installDesktopApiMock({ appServer: { sendRequest }, workspace: { archiveThread: archiveWorkspace } })
})

describe('archive after runtime completion', () => {
  it.each(['parent', 'child', 'grandchild'])('archives an idle tree immediately after %s finishes', async (activeId) => {
    state().setThreadList([
      thread('parent', activeId === 'parent' ? running : idle),
      thread('child', activeId === 'child' ? running : idle, 'parent'),
      thread('grandchild', activeId === 'grandchild' ? running : idle, 'child')
    ])
    state().applyRuntimeSnapshot(activeId, idle, { isActive: activeId === 'parent', isDesktopOrigin: true })

    expect(await archiveThreadWithUndo({ threadId: 'parent', t })).toBe(true)
    expect(requestConfirmDialog).not.toHaveBeenCalled()
    expect(sendRequest.mock.calls.map(call => call[0])).toEqual(['thread/read', 'thread/archive'])
    expect(state().threadList).toEqual([])
  })

  it.each([
    running,
    { ...idle, waitingOnInput: true },
    { ...idle, waitingOnPlanConfirmation: true },
    { ...idle, maintenanceKind: 'compacting' }
  ])('requires confirmation while a descendant remains active: %j', async (runtime) => {
    state().setThreadList([thread('parent'), thread('child', idle, 'parent'), thread('grandchild', runtime, 'child')])
    expect(await archiveThreadWithUndo({ threadId: 'parent', t })).toBe(false)
    expect(requestConfirmDialog).toHaveBeenCalledOnce()
    expect(sendRequest).not.toHaveBeenCalled()
  })
})

describe('secondary workspace archive runtime', () => {
  function cache(threads: ThreadSummary[]): void {
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/foreground', secondaryLimit: 8,
      projects: [{
        path: '/secondary', name: 'secondary', kind: 'local', state: 'secondary',
        running: false, loaded: true, threadCount: threads.length, threads, pinned: false
      }]
    })
  }

  it('uses the cached idle summary rather than an old running row', async () => {
    cache([thread('parent')])
    expect(await archiveWorkspaceThread('/secondary', thread('parent', running), t)).toBe(true)
    expect(requestConfirmDialog).not.toHaveBeenCalled()
    expect(archiveWorkspace).toHaveBeenCalledWith('/secondary', 'parent')
  })

  it('checks running descendants in the owning workspace without touching foreground state', async () => {
    cache([thread('parent'), thread('child', running, 'parent')])
    state().setThreadList([thread('parent')])
    expect(await archiveWorkspaceThread('/secondary', thread('parent'), t)).toBe(false)
    expect(requestConfirmDialog).toHaveBeenCalledOnce()
    expect(archiveWorkspace).not.toHaveBeenCalled()
    expect(sendRequest).not.toHaveBeenCalled()
  })
})

describe('project archive runtime scope', () => {
  const archive = (threads: ThreadSummary[], live: boolean) =>
    archiveProjectThreads({ projectLabel: 'project', workspacePath: '/secondary', connectionKey: '/secondary', threads, live, t })

  it('uses the latest store state instead of the running summary captured by a menu', async () => {
    const captured = thread('parent', running)
    state().setThreadList([captured, thread('child', running, 'parent')])
    for (const id of ['parent', 'child']) state().applyRuntimeSnapshot(id, idle, { isActive: false, isDesktopOrigin: true })
    vi.mocked(requestConfirmDialog).mockReturnValue({ result: Promise.resolve(true), dismiss: vi.fn() })

    await archive([captured], true)

    expect(requestConfirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: false }))
    expect(archiveWorkspace).toHaveBeenCalledWith('/secondary', 'parent', '/secondary')
    expect(sendRequest).not.toHaveBeenCalled()
  })

  it('includes running descendants when the menu only contains their idle parent', async () => {
    const parent = thread('parent')
    state().setThreadList([parent, thread('child', running, 'parent')])

    await archive([parent], true)

    expect(requestConfirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
    expect(sendRequest).not.toHaveBeenCalled()
  })

  it('targets a device connection and its cached descendants instead of a local project at the same path', async () => {
    const key = 'remote:device:computer-a:project-a'
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/secondary', secondaryLimit: 8,
      projects: [
        { path: '/secondary', kind: 'local', name: 'local', state: 'foreground', running: true, loaded: true,
          threadCount: 1, threads: [thread('shared-id')], pinned: false },
        { projectId: key, path: '/secondary', kind: 'remote', name: 'remote', state: 'secondary', running: true,
          loaded: true, threadCount: 2, threads: [thread('shared-id'), thread('child', running, 'shared-id')], pinned: false }
      ]
    })
    state().setThreadList([thread('shared-id')], '/secondary')
    vi.mocked(requestConfirmDialog).mockReturnValue({ result: Promise.resolve(true), dismiss: vi.fn() })

    await archiveProjectThreads({
      projectLabel: 'remote', workspacePath: '/secondary', connectionKey: key,
      threads: [thread('shared-id')], live: false, t
    })

    expect(requestConfirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
    expect(archiveWorkspace).toHaveBeenCalledWith('/secondary', 'shared-id', key)
    expect(sendRequest).not.toHaveBeenCalled()
    expect(state().threadList.map(item => item.id)).toEqual(['shared-id'])
  })

  it('does not remove a newly selected foreground thread when a keyed archive confirmation resolves', async () => {
    let confirm!: (value: boolean) => void
    vi.mocked(requestConfirmDialog).mockReturnValue({
      result: new Promise<boolean>(resolve => { confirm = resolve }), dismiss: vi.fn()
    })
    const key = 'remote:device:computer-a:project-a'
    state().setThreadList([thread('shared-id')], key)
    const archiving = archiveProjectThreads({
      projectLabel: 'remote', workspacePath: '/secondary', connectionKey: key,
      threads: [thread('shared-id')], live: true, t
    })
    state().setThreadList([thread('shared-id')], '/foreground')
    state().setActiveThreadId('shared-id')

    confirm(true)
    await archiving

    expect(archiveWorkspace).toHaveBeenCalledWith('/secondary', 'shared-id', key)
    expect(sendRequest).not.toHaveBeenCalled()
    expect(state().activeThreadId).toBe('shared-id')
    expect(state().threadList.map(item => item.id)).toEqual(['shared-id'])
  })

  it('does not apply foreground activity to a secondary workspace with the same thread id', async () => {
    state().setThreadList([thread('shared-id', running)])
    vi.mocked(requestConfirmDialog).mockReturnValue({ result: Promise.resolve(true), dismiss: vi.fn() })

    await archive([thread('shared-id')], false)

    expect(requestConfirmDialog).toHaveBeenCalledWith(expect.objectContaining({ danger: false }))
    expect(archiveWorkspace).toHaveBeenCalledWith('/secondary', 'shared-id', '/secondary')
    expect(sendRequest).not.toHaveBeenCalled()
    expect(state().runningTurnThreadIds.has('shared-id')).toBe(true)
  })
})
