import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import { useConnectionStore } from '../stores/connectionStore'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import {
  isSubAgentChildRunning, isSubAgentChildWorking, useSubAgentStore
} from '../stores/subAgentStore'
import { syncSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import { listDesktopPluginSubAgents, onDesktopPluginSubAgentsChange } from '../plugins/desktopPluginSubAgents'
import { deferred, makeSubAgent } from './subAgentFixtures'

const parent = 'parent-B'
const child = 'child-B'
const listeners = new Set<(payload: RawNotificationPayload) => void>()
const request = vi.fn()
const source = { threadId: parent, connectionKey: 'A', connectionId: 'A1', workspaceKey: 'a', capabilities: { subAgentSessions: true } }
const state = useSubAgentStore.getState
const metadata = () => state().childrenByParent.get(parent)![0]
const terminal = (status: string) => ({ sessionId: 'term-owned', threadId: child, turnId: 'turn-1', callId: 'call-1', command: 'work', status })
const workflow = (status: string) => ({ runId: 'run-owned', threadId: child, name: 'work', status })
let terminals: ReturnType<typeof terminal>[]
let workflowRuns: ReturnType<typeof workflow>[]
let workflowStatus: string

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

function activity(working: boolean): void {
  expect(isSubAgentChildWorking(metadata())).toBe(working)
  expect(listDesktopPluginSubAgents(parent)[0].state).toBe(working ? 'working' : 'done')
}

function nextActivity(working: boolean): Promise<void> {
  return new Promise(resolve => {
    const stop = onDesktopPluginSubAgentsChange(parent, agents => {
      if (agents[0]?.state === (working ? 'working' : 'done')) {
        stop()
        resolve()
      }
    })
  })
}

beforeEach(() => {
  state().reset()
  listeners.clear()
  request.mockReset()
  terminals = [terminal('running')]
  workflowRuns = []
  workflowStatus = 'succeeded'
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
    if (method === 'terminal/list') return { terminals }
    if (method === 'workflow/run/list') return { runs: workflowRuns }
    if (method === 'workflow/run/read') return { run: workflow(workflowStatus) }
    return {}
  })
  state().setChildren(parent, [makeSubAgent()])
  useThreadStore.getState().applyRuntimeSnapshot(child, { running: false }, { isDesktopOrigin: false })
})

afterEach(() => {
  state().reset()
  releaseThreadConnection(parent)
  useWorkspaceProjectsStore.getState().reset()
  vi.unstubAllGlobals()
})

describe('subagent owned background activity', () => {
  it('does not manufacture activity during initial loading or failure and accepts a later successful read', async () => {
    const discovery = deferred<{ terminals: ReturnType<typeof terminal>[] }>()
    request.mockImplementation(async (_connection, method) => method === 'terminal/list' ? discovery.promise : { runs: [] })
    const syncing = syncSubAgentTaskActivity(parent, [metadata()])
    activity(false)

    discovery.reject(new Error('list unavailable'))
    await syncing
    activity(false)

    request.mockResolvedValue({ terminals: [terminal('running')], runs: [] })
    await syncSubAgentTaskActivity(parent, [metadata()])
    activity(true)
    expect(isSubAgentChildRunning(metadata())).toBe(false)
    expect(useThreadStore.getState().runtimeSnapshots.get(child)).toMatchObject({ running: false, busy: false })
    for (const method of ['terminal/list', 'workflow/run/list']) {
      expect(request).toHaveBeenCalledWith('A', method, expect.objectContaining({ threadId: child }), undefined, { connectionId: 'A1' })
    }
  })

  it('settles shell and workflow tasks from their scoped live notifications', async () => {
    await syncSubAgentTaskActivity(parent, [metadata()])
    activity(true)
    notify('terminal/completed', { terminal: terminal('completed') }, 'B1', 'B')
    activity(true)
    notify('terminal/completed', { terminal: terminal('completed') })
    activity(false)

    workflowStatus = 'running'
    const started = nextActivity(true)
    notify('workflow/run/updated', { threadId: child, runId: 'run-owned', reason: 'created' })
    await started
    activity(true)
    workflowStatus = 'succeeded'
    const ended = nextActivity(false)
    notify('workflow/run/updated', { threadId: child, runId: 'run-owned', reason: 'terminal' })
    await ended
    activity(false)
  })

  it('reconciles again when idle arrives during the initial terminal read', async () => {
    const old = deferred<{ terminals: ReturnType<typeof terminal>[] }>()
    let reads = 0
    request.mockImplementation(async (_connection, method) => {
      if (method === 'terminal/list') return ++reads === 1 ? old.promise : { terminals: [] }
      return { runs: [] }
    })
    const first = syncSubAgentTaskActivity(parent, [metadata()])
    notify('thread/status/changed', { threadId: child, runtime: { running: false } })
    old.resolve({ terminals: [terminal('running')] })
    await first
    activity(false)
    expect(reads).toBe(2)
  })

  it('settles a terminal from its correlated command execution completion item', async () => {
    await syncSubAgentTaskActivity(parent, [metadata()])
    const item = { type: 'commandExecution', payload: { sessionId: 'term-owned', callId: 'call-1', status: 'completed' } }
    notify('item/completed', { threadId: 'another-child', turnId: 'turn-1', item })
    activity(true)
    notify('item/completed', { threadId: child, turnId: 'another-turn', item })
    activity(true)
    notify('item/completed', { threadId: child, turnId: 'turn-1', item })
    activity(false)
    expect(request.mock.calls.filter(([, method]) => method === 'terminal/list')).toHaveLength(1)
  })

  it('keeps real background work active after idle reconciliation', async () => {
    await syncSubAgentTaskActivity(parent, [metadata()])
    const queried = deferred<void>()
    request.mockImplementation(async (_connection, method) => {
      if (method === 'terminal/list') {
        queried.resolve()
        return { terminals }
      }
      return { runs: [] }
    })
    notify('thread/status/changed', { threadId: child, runtime: { running: false } })
    activity(true)
    await queried.promise
    await syncSubAgentTaskActivity(parent, [metadata()])
    activity(true)
    expect(isSubAgentChildRunning(metadata())).toBe(false)
  })

  it('rereads known workflows when their completion notification was missed', async () => {
    terminals = []
    workflowRuns = [workflow('running')]
    workflowStatus = 'running'
    await syncSubAgentTaskActivity(parent, [metadata()])
    activity(true)
    workflowStatus = 'succeeded'
    const ended = nextActivity(false)
    notify('thread/status/changed', { threadId: child, runtime: { running: false } })
    await ended
    activity(false)
  })

  it('retains known work through failed reads and recovers on a later successful sync', async () => {
    await syncSubAgentTaskActivity(parent, [metadata()])
    request.mockRejectedValue(new Error('list unavailable'))
    await syncSubAgentTaskActivity(parent, [metadata()])
    activity(true)
    request.mockResolvedValue({ terminals: [], runs: [] })
    await syncSubAgentTaskActivity(parent, [metadata()])
    activity(false)
  })

  it('rediscovers task activity when the same connection recovers', async () => {
    await syncSubAgentTaskActivity(parent, [metadata()])
    projectConnection('A1', false)
    activity(false)
    const recovered = nextActivity(true)
    projectConnection('A1')
    await recovered
    activity(true)
  })

  it('releases old observers and ignores delayed snapshots from a replaced connection', async () => {
    const old = deferred<{ terminals: ReturnType<typeof terminal>[] }>()
    request.mockImplementation(async (_connection, method, _params, _timeout, options) => {
      if (method === 'terminal/list') return options.connectionId === 'A1' ? old.promise : { terminals: [] }
      return { runs: [] }
    })
    const first = syncSubAgentTaskActivity(parent, [metadata()])
    projectConnection('A2')
    registerThreadConnection(parent, { ...source, connectionId: 'A2' })
    await syncSubAgentTaskActivity(parent, [metadata()])
    old.resolve({ terminals: [terminal('running')] })
    await first
    activity(false)
    notify('terminal/started', { terminal: terminal('running') }, 'A1')
    activity(false)
    notify('terminal/started', { terminal: terminal('running') }, 'A2')
    activity(true)
    projectConnection('A2', false)
    activity(false)
    notify('terminal/started', { terminal: { ...terminal('running'), sessionId: 'term-after-disconnect' } }, 'A2')
    activity(false)
    state().clearParent(parent)
    expect(listeners.size).toBe(0)
  })

  it('does not publish an in-flight snapshot after its child closes', async () => {
    const old = deferred<{ terminals: ReturnType<typeof terminal>[] }>()
    request.mockImplementation(async (_connection, method) => method === 'terminal/list' ? old.promise : { runs: [] })
    const first = syncSubAgentTaskActivity(parent, [metadata()])
    state().setChildren(parent, [{ ...metadata(), status: 'closed' }])
    await syncSubAgentTaskActivity(parent, [metadata()])
    old.resolve({ terminals: [terminal('running')] })
    await first
    activity(false)
    notify('terminal/started', { terminal: terminal('running') })
    activity(false)
  })
})
