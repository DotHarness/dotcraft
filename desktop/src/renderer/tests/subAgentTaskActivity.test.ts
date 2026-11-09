import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import { useConnectionStore } from '../stores/connectionStore'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import {
  isSubAgentChildFinished, isSubAgentChildRunning, isSubAgentChildWorking, useSubAgentStore
} from '../stores/subAgentStore'
import { syncSubAgentTaskActivity, useSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import { listDesktopPluginSubAgents } from '../plugins/desktopPluginSubAgents'
import { deferred, makeSubAgent } from './subAgentFixtures'

const parent = 'parent-B'
const child = 'child-B'
const listeners = new Set<(payload: RawNotificationPayload) => void>()
const request = vi.fn()
const source = { threadId: parent, connectionKey: 'A', connectionId: 'A1', workspaceKey: 'a', capabilities: { subAgentSessions: true } }
const state = useSubAgentStore.getState
const metadata = () => state().childrenByParent.get(parent)![0]
const terminal = (status: string) => ({ sessionId: 'term-owned', threadId: child, command: 'work', status })
let workflowStatus = 'succeeded'
let initialTerminals: Promise<{ terminals: ReturnType<typeof terminal>[] }> | null = null

function projectConnection(connectionId: string, loaded = true): void {
  useWorkspaceProjectsStore.getState().setPayload({
    foregroundWorkspacePath: 'B', secondaryLimit: 8,
    projects: [{ path: 'A', name: 'A', state: 'secondary', loaded, running: true,
      threadCount: 1, threads: [], pinned: false, connectionKey: 'A', connectionId }]
  })
}

function notify(method: string, params: unknown, connectionId = 'A1', connectionKey = 'A') {
  for (const listener of listeners) listener({ method, params, connectionId, connectionKey })
}

async function requestsSettled() {
  await Promise.all(request.mock.results.map(result => result.value))
}

beforeEach(() => {
  listeners.clear()
  request.mockReset()
  workflowStatus = 'succeeded'
  initialTerminals = null
  state().reset()
  useThreadStore.getState().reset()
  useConnectionStore.setState({ status: 'connected', connectionKey: 'B', connectionId: 'B1', capabilities: { subAgentSessions: true } })
  projectConnection('A1')
  registerThreadConnection(parent, source)
  vi.stubGlobal('window', { api: { appServer: {
    sendConnectionRequest: request,
    onNotificationRaw(listener: (payload: RawNotificationPayload) => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    }
  } } })
  request.mockImplementation(async (_connection, method) => {
    if (method === 'subagent/children/list') return { data: [{
      edge: { parentThreadId: parent, childThreadId: child, status: 'open' },
      thread: { id: child, runtime: { running: false } }
    }] }
    if (method === 'terminal/list') return initialTerminals ?? { terminals: [terminal('running')] }
    if (method === 'workflow/run/list') return { runs: [] }
    if (method === 'workflow/run/read') return { run: { runId: 'run-owned', threadId: child, name: 'work', status: workflowStatus } }
    return {}
  })
})

afterEach(() => {
  state().reset()
  releaseThreadConnection(parent)
  useWorkspaceProjectsStore.getState().reset()
  vi.unstubAllGlobals()
})

describe('subagent owned background activity', () => {
  it('keeps idle children working for their shell and workflow tasks without changing turn runtime', async () => {
    const discovery = deferred<{ terminals: ReturnType<typeof terminal>[] }>()
    initialTerminals = discovery.promise
    await state().fetchChildren(parent)
    expect(isSubAgentChildFinished(metadata())).toBe(false)
    const ready = new Promise<void>(resolve => {
      const stop = useSubAgentTaskActivity.subscribe(snapshot => {
        if (!snapshot.pendingThreadIds.has(child)) { stop(); resolve() }
      })
    })
    discovery.resolve({ terminals: [terminal('running')] })
    await ready
    expect(isSubAgentChildWorking(metadata())).toBe(true)
    expect(isSubAgentChildRunning(metadata())).toBe(false)
    expect(useThreadStore.getState().runtimeSnapshots.get(child)).toMatchObject({ running: false, busy: false })
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('working')
    for (const method of ['terminal/list', 'workflow/run/list']) {
      expect(request).toHaveBeenCalledWith('A', method, expect.objectContaining({ threadId: child }), undefined, { connectionId: 'A1' })
    }

    notify('terminal/completed', { terminal: terminal('completed') }, 'B1', 'B')
    expect(isSubAgentChildWorking(metadata())).toBe(true)
    notify('terminal/completed', { terminal: terminal('completed') })
    expect(isSubAgentChildFinished(metadata())).toBe(true)

    workflowStatus = 'running'
    notify('workflow/run/updated', { threadId: child, runId: 'run-owned', reason: 'created' })
    await requestsSettled()
    expect(isSubAgentChildWorking(metadata())).toBe(true)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('working')
    workflowStatus = 'succeeded'
    notify('workflow/run/updated', { threadId: child, runId: 'run-owned', reason: 'terminal' })
    await requestsSettled()
    expect(isSubAgentChildFinished(metadata())).toBe(true)

    useThreadStore.getState().applyRuntimeSnapshot(child, {
      running: true, activeTurnId: 'turn-woken', waitingOnApproval: false, waitingOnPlanConfirmation: false
    }, { isDesktopOrigin: false })
    expect(isSubAgentChildRunning(metadata())).toBe(true)
    expect(isSubAgentChildWorking(metadata())).toBe(true)
    state().setChildren(parent, [{ ...metadata(), status: 'closed' }])
    syncSubAgentTaskActivity(parent, [metadata()])
    expect(isSubAgentChildWorking(metadata())).toBe(false)
  })

  it('releases old connection observers and ignores a delayed old task snapshot', async () => {
    const old = deferred<{ terminals: ReturnType<typeof terminal>[] }>()
    request.mockImplementation(async (_connection, method, _params, _timeout, options) => {
      if (method === 'terminal/list') return options.connectionId === 'A1' ? old.promise : { terminals: [] }
      if (method === 'workflow/run/list') return { runs: [] }
      return {}
    })
    state().setChildren(parent, [makeSubAgent()])
    syncSubAgentTaskActivity(parent, [metadata()])
    projectConnection('A2')
    registerThreadConnection(parent, { ...source, connectionId: 'A2' })
    syncSubAgentTaskActivity(parent, [metadata()])
    old.resolve({ terminals: [terminal('running')] })
    await requestsSettled()
    expect(isSubAgentChildWorking(metadata())).toBe(false)
    notify('terminal/started', { terminal: terminal('running') }, 'A2')
    expect(isSubAgentChildWorking(metadata())).toBe(true)
    projectConnection('A2', false)
    expect(isSubAgentChildWorking(metadata())).toBe(false)
    notify('terminal/started', { terminal: { ...terminal('running'), sessionId: 'term-after-disconnect' } }, 'A2')
    expect(isSubAgentChildWorking(metadata())).toBe(false)
    state().clearParent(parent)
    expect(listeners.size).toBe(0)
  })
})
