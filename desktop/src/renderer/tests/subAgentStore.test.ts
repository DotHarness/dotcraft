import { beforeEach, describe, expect, it, vi } from 'vitest'
import {
  isSubAgentChildFinished, isSubAgentChildRunning, isSubAgentChildWorking, subAgentChildRuntime,
  useSubAgentStore, type SubAgentChild
} from '../stores/subAgentStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { listDesktopPluginSubAgents, onDesktopPluginSubAgentsChange } from '../plugins/desktopPluginSubAgents'
import { makeSubAgent } from './subAgentFixtures'

const request = vi.fn()
const parent = 'parent-B'
const childId = 'child-B'
const store = useSubAgentStore.getState
const threads = useThreadStore.getState
const child = (): SubAgentChild => store().childrenByParent.get(parent)![0]
const runtime = (running: boolean, turnId: string | null = null) => ({
  running, activeTurnId: turnId, waitingOnApproval: false, waitingOnPlanConfirmation: false
})

function progress(label: string, isCompleted = false) {
  return { label, isCompleted, currentTool: isCompleted ? null : 'ReadFile', currentToolDisplay: 'Reading atlas', inputTokens: 1, outputTokens: 2 }
}

beforeEach(() => {
  vi.clearAllMocks()
  store().reset()
  useConnectionStore.getState().reset()
  useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
  threads().reset()
  vi.stubGlobal('window', { api: { appServer: { sendRequest: request } } })
})

describe('subagent metadata and shared runtime', () => {
  it('does not request children without the server capability', async () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: false } })
    await store().fetchChildren(parent)
    expect(request).not.toHaveBeenCalled()
  })

  it('keeps progress independent from runtime and shares subsequent turns with the Summary', () => {
    store().setChildren(parent, [makeSubAgent()])
    const observed: string[][] = []
    const stop = onDesktopPluginSubAgentsChange(parent, agents => observed.push(agents.map(agent => agent.state)))
    threads().applyRuntimeSnapshot(childId, runtime(true, 'turn-1'), { isDesktopOrigin: false })
    expect(isSubAgentChildRunning(child())).toBe(true)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('working')

    threads().applyRuntimeSnapshot(childId, runtime(false), { isDesktopOrigin: false })
    store().updateProgress(parent, [progress('Core B')])
    expect(isSubAgentChildFinished(child())).toBe(true)
    expect(child().currentTool).toBeNull()

    threads().applyRuntimeSnapshot(childId, runtime(true, 'turn-2'), { isDesktopOrigin: false })
    expect(isSubAgentChildRunning(child())).toBe(true)
    expect(subAgentChildRuntime(child())?.activeTurnId).toBe('turn-2')
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('working')
    expect(observed).toEqual([['working'], ['done'], ['working']])
    stop()
  })

  it('derives parent working from a discovered active descendant without changing the idle parent runtime', () => {
    store().setChildren(parent, [makeSubAgent()])
    threads().applyRuntimeSnapshot(childId, runtime(false), { isDesktopOrigin: false })
    store().setChildren(childId, [makeSubAgent({ childThreadId: 'grandchild', parentThreadId: childId })])
    threads().applyRuntimeSnapshot('grandchild', runtime(true, 'nested-turn'), { isDesktopOrigin: false })
    expect(isSubAgentChildWorking(child())).toBe(true)
    expect(isSubAgentChildRunning(child())).toBe(false)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('working')
    expect(threads().runtimeSnapshots.get(childId)).toMatchObject({ running: false, busy: false })
    store().setChildren(childId, [makeSubAgent({ childThreadId: 'grandchild', parentThreadId: childId, status: 'closed' })])
    expect(isSubAgentChildFinished(child())).toBe(true)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('done')
  })

  it('hydrates placeholder identity and progress while storing runtime in the thread store', async () => {
    store().updateProgress(parent, [progress('Core B')])
    request.mockResolvedValue({ data: [{
      edge: { childThreadId: childId, parentThreadId: parent, agentNickname: 'Core B', agentRole: 'explorer', status: 'open' },
      thread: { id: childId, displayName: 'Atlas review', runtime: runtime(true, 'turn-1') }
    }] })
    await store().fetchChildren(parent)
    expect(request).toHaveBeenCalledWith('subagent/children/list', {
      parentThreadId: parent, includeClosed: true, includeThreads: true
    }, undefined, undefined)
    expect(child()).toMatchObject({ childThreadId: childId, nickname: 'Atlas review', agentRole: 'explorer',
      lastToolDisplay: 'Reading atlas', currentTool: 'ReadFile', isPlaceholder: false })
    expect(isSubAgentChildRunning(child())).toBe(true)
    expect(threads().runtimeSnapshots.get(childId)?.activeTurnId).toBe('turn-1')
  })

  it('does not turn missing runtime into completed activity or infer it from an open edge', () => {
    store().setChildren(parent, [makeSubAgent()])
    expect(isSubAgentChildRunning(child())).toBe(false)
    expect(isSubAgentChildFinished(child())).toBe(false)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('waiting')
  })

  it('treats an open historical edge with an idle runtime as finished', async () => {
    request.mockResolvedValue({ data: [{
      edge: { childThreadId: childId, parentThreadId: parent, status: 'open' },
      thread: { id: childId, runtime: runtime(false) }
    }] })
    await store().fetchChildren(parent)
    expect(child().status).toBe('open')
    expect(isSubAgentChildRunning(child())).toBe(false)
    expect(isSubAgentChildFinished(child())).toBe(true)
  })

  it('preserves only live placeholders through an empty discovery result', async () => {
    store().updateProgress(parent, [progress('Finished', true), progress('Working')])
    request.mockResolvedValue({ data: [] })
    await store().fetchChildren(parent)
    const children = store().childrenByParent.get(parent)!
    expect(children.map(entry => entry.nickname)).toEqual(['Working'])
    expect(isSubAgentChildRunning(children[0])).toBe(true)
  })

  it('clears authoritative empty discovery and blocks stale progress placeholders', async () => {
    store().setChildren(parent, [makeSubAgent()])
    request.mockResolvedValue({ data: [] })
    await store().fetchChildren(parent, { authoritative: true })
    store().updateProgress(parent, [progress('Core B')])
    expect(store().childrenByParent.get(parent)).toEqual([])
  })
})
