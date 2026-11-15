import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { syncSubAgentTaskActivity, useSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import { projectThreadActivity } from '../stores/subAgentActivity'
import { applyThreadNotification } from '../stores/threadNotifications'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { listDesktopPluginSubAgents } from '../plugins/desktopPluginSubAgents'
import { retainThreadTaskFeed } from '../stores/threadTaskFeed'
import { desktopWorkflowRunClient } from '../stores/workflowRunStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { deferred } from './subAgentFixtures'

const source = { connectionKey: 'workspace', connectionId: 'connection', workspaceKey: 'workspace',
  capabilities: { subAgentSessions: true } }
const listeners = new Set<(payload: RawNotificationPayload) => void>()
const terminal = { sessionId: 'terminal', threadId: 'child', turnId: 'turn', callId: 'exec',
  command: 'work', status: 'running', isBackground: true }
const request = vi.fn()
const context = { locale: 'en' as const, foreground: true, updateWarmThread() {}, updateWarmThreadMode() {},
  releaseWarmThread() {}, reloadThreadList: async () => {} }

function activity(threadId = 'root') {
  const work = useSubAgentTaskActivity.getState()
  return projectThreadActivity(threadId, { graph: useSubAgentStore.getState().childrenByParent,
    runtimes: useThreadStore.getState().runtimeSnapshots, tasks: work.tasksByThread, reconciling: work.reconcilingThreads })
}

function notify(method: string, params: unknown): void {
  const payload = { ...source, method, params }
  applyThreadNotification(payload, context)
  for (const listener of listeners) listener(payload)
}

async function discover(): Promise<void> {
  await useSubAgentStore.getState().ensureChildren('root')
  await syncSubAgentTaskActivity('root', useSubAgentStore.getState().childrenByParent.get('root') ?? [])
}

beforeEach(() => {
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  listeners.clear()
  request.mockReset()
  useConnectionStore.setState({ status: 'connected', ...source })
  registerThreadConnection('root', source)
  vi.stubGlobal('window', { api: { appServer: {
    sendConnectionRequest: request,
    onNotificationRaw(listener: (payload: RawNotificationPayload) => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    }
  } } })
  request.mockImplementation(async (_connection, method, params) => {
    if (method === 'subagent/children/list') return { data: params.parentThreadId === 'root' ? [{
      edge: { parentThreadId: 'root', childThreadId: 'child', status: 'open' },
      thread: { id: 'child', runtime: { running: true, activeTurnId: 'turn' } }
    }] : [] }
    if (method === 'terminal/list') return { terminals: [] }
    if (method === 'workflow/run/list') return { runs: [] }
    return {}
  })
})

afterEach(() => {
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  releaseThreadConnection('root')
  useConnectionStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  vi.unstubAllGlobals()
})

describe('retained conversation activity handoff', () => {
  it('does not query an unavailable connection or invent activity', async () => {
    useConnectionStore.getState().reset()
    const feed = retainThreadTaskFeed(desktopWorkflowRunClient, 'unowned')
    expect(await feed.ensure()).toBe(false)
    expect(request).not.toHaveBeenCalled()
    expect(activity('unowned').count).toBe(0)
    feed.release()
  })

  it('counts a child and its background task after the turn ends without opening any details', async () => {
    await discover()
    expect(activity().count).toBe(1)
    notify('terminal/started', { terminal: { ...terminal, isBackground: false } })
    expect(activity().count).toBe(1)
    notify('terminal/backgrounded', { terminal })
    expect(activity().count).toBe(2)
    expect(activity('child').count).toBe(1)
    request.mockImplementation(async (_connection, method, params) => ({
      terminals: method === 'terminal/list' && params.threadId === 'child' ? [terminal] : [], runs: []
    }))

    notify('thread/status/changed', { threadId: 'child', runtime: { running: false } })
    await vi.waitFor(() => expect(useSubAgentTaskActivity.getState().reconcilingThreads.size).toBe(0))
    expect(useThreadStore.getState().runtimeSnapshots.get('child')?.running).toBe(false)
    expect(activity().count).toBe(2)
    expect(listDesktopPluginSubAgents('root')[0].state).toBe('working')
    expect(request.mock.calls.some(([, method]) => method === 'thread/resume' || method === 'thread/items/list')).toBe(false)

    notify('terminal/completed', { terminal: { ...terminal, status: 'completed' } })
    expect(activity().count).toBe(0)
    expect(listDesktopPluginSubAgents('root')[0].state).toBe('done')
  })

  it('retains confirmed child activity when idle precedes handoff and an older snapshot arrives last', async () => {
    await discover()
    const response = deferred<{ terminals: typeof terminal[] }>()
    let reads = 0
    request.mockImplementation(async (_connection, method, params) => {
      if (method === 'terminal/list' && params.threadId === 'child') {
        return ++reads === 1 ? response.promise : { terminals: [terminal] }
      }
      return { terminals: [], runs: [] }
    })
    const counts: number[] = []
    const capture = (): void => { counts.push(activity().count) }
    const stopTasks = useSubAgentTaskActivity.subscribe(capture)
    const stopRuntime = useThreadStore.subscribe(capture)

    notify('thread/status/changed', { threadId: 'child', runtime: { running: false } })
    expect(activity().count).toBe(1)
    expect(listDesktopPluginSubAgents('root')[0].state).toBe('working')
    notify('terminal/backgrounded', { terminal })
    expect(activity().count).toBe(2)
    response.resolve({ terminals: [] })
    await vi.waitFor(() => expect(useSubAgentTaskActivity.getState().reconcilingThreads.size).toBe(0))
    expect(activity().count).toBe(2)
    expect(counts).not.toContain(0)
    stopTasks()
    stopRuntime()
  })

  it('retains handoff evidence received before child discovery completes', async () => {
    const children = deferred<unknown>()
    const background = deferred<{ terminals: typeof terminal[] }>()
    request.mockImplementation(async (_connection, method, params) => {
      if (method === 'subagent/children/list') return params.parentThreadId === 'root' ? children.promise : { data: [] }
      if (method === 'terminal/list') return params.threadId === 'child' ? background.promise : { terminals: [] }
      return { runs: [] }
    })
    const discovering = useSubAgentStore.getState().ensureChildren('root')
    notify('terminal/backgrounded', { terminal })
    children.resolve({ data: [{
      edge: { parentThreadId: 'root', childThreadId: 'child', status: 'open' },
      thread: { id: 'child', runtime: { running: false } }
    }] })
    await discovering
    expect(activity().count).toBe(2)
    expect(activity('child').count).toBe(1)
    expect(listDesktopPluginSubAgents('root')[0].state).toBe('working')
    background.resolve({ terminals: [terminal] })
    await vi.waitFor(() => expect(activity().count).toBe(2))
  })

  it('never revives an ended session from a delayed handoff or older list', async () => {
    await discover()
    const response = deferred<{ terminals: typeof terminal[] }>()
    request.mockImplementation(async (_connection, method, params) => {
      if (method === 'terminal/list' && params.threadId === 'child') return response.promise
      return { terminals: [], runs: [] }
    })
    notify('thread/status/changed', { threadId: 'child', runtime: { running: false } })
    notify('terminal/completed', { terminal: { ...terminal, status: 'completed' } })
    notify('terminal/backgrounded', { terminal })
    response.resolve({ terminals: [terminal] })
    await vi.waitFor(() => expect(useSubAgentTaskActivity.getState().reconcilingThreads.size).toBe(0))
    expect(activity().count).toBe(0)
    expect(activity('child').count).toBe(0)
    expect(listDesktopPluginSubAgents('root')[0].state).toBe('done')
  })

  it('keeps a shared feed attached to its original workspace after foreground navigation', async () => {
    const rootFeed = retainThreadTaskFeed(desktopWorkflowRunClient, 'root')
    await rootFeed.ensure()
    await discover()
    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: 'other', secondaryLimit: 8,
      projects: [{ path: 'workspace', name: 'workspace', state: 'secondary', loaded: true,
        running: true, threadCount: 1, threads: [], pinned: false, ...source }]
    })
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: 'other', connectionId: 'other-connection' })

    notify('terminal/backgrounded', { terminal })
    expect(activity().count).toBe(2)
    expect(activity('child').count).toBe(1)
    rootFeed.release()
    notify('terminal/completed', { terminal: { ...terminal, status: 'completed' } })
    expect(activity().count).toBe(1)
    expect(activity('child').count).toBe(0)
  })

  it('recursively counts each working agent and task once from snapshots alone', async () => {
    request.mockImplementation(async (_connection, method, params) => {
      if (method === 'subagent/children/list') {
        const child = params.parentThreadId === 'root' ? 'child' : params.parentThreadId === 'child' ? 'grandchild' : null
        return { data: child ? [{ edge: { parentThreadId: params.parentThreadId, childThreadId: child, status: 'open' },
          thread: { id: child, runtime: { running: false } } }] : [] }
      }
      if (method === 'terminal/list') return { terminals: params.threadId === 'grandchild' ? [{ ...terminal, threadId: 'grandchild' }] : [] }
      return { runs: [] }
    })
    await discover()
    await vi.waitFor(() => expect(activity().count).toBe(3))
    expect(activity('child').count).toBe(2)
    expect(activity('grandchild').count).toBe(1)
    expect(listDesktopPluginSubAgents('root')[0].state).toBe('working')
  })
})
