import { beforeEach, describe, expect, it, vi } from 'vitest'
import { isSubAgentChildRunning, useSubAgentStore } from '../stores/subAgentStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { deferred, makeSubAgent } from './subAgentFixtures'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'

const request = vi.fn()
const store = useSubAgentStore.getState
const parent = 'parent-B'
const wire = (id = 'child-B', status = 'open') => ({ data: [{
  edge: { childThreadId: id, parentThreadId: parent, agentPath: '/root/review_core', status },
  thread: { id, runtime: { running: true }, displayName: id }
}] })

beforeEach(() => {
  request.mockReset()
  releaseThreadConnection(parent)
  store().reset()
  useThreadStore.getState().reset()
  useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
  vi.stubGlobal('window', { api: { appServer: { sendRequest: request } } })
})

describe('subagent discovery lifecycle', () => {
  it('shares the first complete-list request among multiple surfaces', async () => {
    const response = deferred<unknown>()
    request.mockReturnValue(response.promise)
    const first = store().ensureChildren(parent)
    const second = store().ensureChildren(parent)
    expect(second).toBe(first)
    expect(store().discoveryByParent.get(parent)).toEqual({ status: 'loading', discovered: false })
    expect(request).toHaveBeenCalledExactlyOnceWith('subagent/children/list', {
      parentThreadId: parent, includeClosed: true, includeThreads: true
    }, undefined, undefined)
    response.resolve(wire())
    await Promise.all([first, second])
    await store().ensureChildren(parent)
    expect(request).toHaveBeenCalledTimes(1)
    expect(store().discoveryByParent.get(parent)).toEqual({ status: 'ready', discovered: true })
  })

  it('queues one trailing refresh while sharing an in-flight request', async () => {
    const first = deferred<unknown>()
    const second = deferred<unknown>()
    request.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise)
    const load = store().ensureChildren(parent)
    const refresh = store().fetchChildren(parent, { authoritative: true })
    void store().fetchChildren(parent, { authoritative: true })
    expect(request).toHaveBeenCalledTimes(1)
    first.resolve(wire())
    await vi.waitFor(() => expect(request).toHaveBeenCalledTimes(2))
    expect(store().discoveryByParent.get(parent)).toEqual({ status: 'loading', discovered: true })
    second.resolve({ data: [] })
    await Promise.all([load, refresh])
    expect(store().childrenByParent.get(parent)).toEqual([])
    expect(store().discoveryByParent.get(parent)?.status).toBe('ready')
  })

  it('preserves live runtime across an older idle discovery response', async () => {
    const response = deferred<ReturnType<typeof wire>>()
    request.mockReturnValue(response.promise)
    const load = store().fetchChildren(parent)
    useThreadStore.getState().applyRuntimeSnapshot('child-B', {
      running: true, activeTurnId: 'turn-2', waitingOnApproval: false, waitingOnPlanConfirmation: false
    }, { isDesktopOrigin: false })
    response.resolve({ data: [{ ...wire().data[0], thread: { id: 'child-B', displayName: 'child-B', runtime: { running: false } } }] })
    await load
    expect(isSubAgentChildRunning(store().childrenByParent.get(parent)![0])).toBe(true)
    expect(useThreadStore.getState().runtimeSnapshots.get('child-B')?.activeTurnId).toBe('turn-2')
  })

  it('establishes a new discovery baseline on reconnect and ignores the old connection response', async () => {
    const old = deferred<ReturnType<typeof wire>>()
    const fresh = deferred<ReturnType<typeof wire>>()
    useConnectionStore.setState({ connectionKey: 'workspace', connectionId: 'old' })
    request.mockReturnValueOnce(old.promise).mockReturnValueOnce(fresh.promise)
    const first = store().fetchChildren(parent)
    useConnectionStore.setState({ connectionId: 'new' })
    const next = store().ensureChildren(parent)
    old.resolve(wire('stale'))
    await first
    expect(store().childrenByParent.has(parent)).toBe(false)
    fresh.resolve(wire('fresh'))
    await next
    expect(store().childrenByParent.get(parent)?.[0].childThreadId).toBe('fresh')
  })

  it('does not complete discovery on failure and allows a later refresh to recover', async () => {
    request.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce({ data: [] })
    await expect(store().ensureChildren(parent)).rejects.toThrow('offline')
    expect(store().discoveryByParent.get(parent)).toEqual({ status: 'error', discovered: false })
    await store().fetchChildren(parent)
    expect(store().discoveryByParent.get(parent)).toEqual({ status: 'ready', discovered: true })
  })

  it('retains reliable data and completed discovery after a refresh fails', async () => {
    request.mockResolvedValueOnce(wire('closed', 'closed')).mockRejectedValueOnce(new Error('offline'))
    await store().ensureChildren(parent)
    await expect(store().fetchChildren(parent)).rejects.toThrow('offline')
    expect(store().discoveryByParent.get(parent)).toEqual({ status: 'error', discovered: true })
    expect(store().childrenByParent.get(parent)?.[0].status).toBe('closed')
  })

  it.each(['clear', 'reset'] as const)('ignores stale list responses after %s, including thread upserts and discovery state', async (action) => {
    const old = deferred<unknown>()
    const fresh = deferred<unknown>()
    request.mockReturnValueOnce(old.promise).mockReturnValueOnce(fresh.promise)
    const first = store().fetchChildren(parent)
    if (action === 'clear') store().clearParent(parent)
    else store().reset()
    const next = store().ensureChildren(parent)
    old.resolve(wire('stale'))
    await first
    expect(store().childrenByParent.has(parent)).toBe(false)
    expect(useThreadStore.getState().threadList.some((thread) => thread.id === 'stale')).toBe(false)
    expect(store().discoveryByParent.get(parent)?.status).toBe('loading')
    fresh.resolve(wire('fresh'))
    await next
    expect(store().childrenByParent.get(parent)?.[0].childThreadId).toBe('fresh')
  })
})

describe('background subagent discovery', () => {
  const source = {
    threadId: parent, connectionKey: 'workspace-A', connectionId: 'connection-A',
    workspaceKey: 'workspace-A',
    capabilities: { subAgentSessions: true }
  }

  it('keeps a secondary parent child runtime across foreground list reloads without exposing its summary', async () => {
    registerThreadConnection(parent, source)
    const sendConnectionRequest = vi.fn().mockResolvedValue(wire())
    vi.stubGlobal('window', { api: { appServer: { sendRequest: request, onNotificationRaw: () => () => {}, sendConnectionRequest } } })
    useConnectionStore.setState({ capabilities: { subAgentSessions: false } })
    useThreadStore.getState().setThreadList([], 'workspace-B')

    await store().ensureChildren(parent)

    expect(request).not.toHaveBeenCalled()
    expect(sendConnectionRequest).toHaveBeenCalledWith(
      source.connectionKey, 'subagent/children/list',
      { parentThreadId: parent, includeClosed: true, includeThreads: true },
      undefined, expect.objectContaining({ connectionId: source.connectionId })
    )
    const child = store().childrenByParent.get(parent)![0]
    expect(child.childThreadId).toBe('child-B')
    expect(isSubAgentChildRunning(child)).toBe(true)
    expect(useThreadStore.getState().threadList).toEqual([])

    useThreadStore.getState().setThreadList([], 'workspace-B')
    expect(isSubAgentChildRunning(child)).toBe(true)
    expect(useThreadStore.getState().threadList).toEqual([])
  })

  it('does not borrow the foreground capability for a background parent', async () => {
    registerThreadConnection(parent, { ...source, capabilities: { subAgentSessions: false } })
    const sendConnectionRequest = vi.fn()
    vi.stubGlobal('window', { api: { appServer: { sendRequest: request, onNotificationRaw: () => () => {}, sendConnectionRequest } } })
    await store().ensureChildren(parent)
    expect(sendConnectionRequest).not.toHaveBeenCalled()
    expect(request).not.toHaveBeenCalled()
  })

  it('does not apply a response from a replaced parent connection', async () => {
    registerThreadConnection(parent, source)
    const response = deferred<unknown>()
    const sendConnectionRequest = vi.fn().mockReturnValue(response.promise)
    vi.stubGlobal('window', { api: { appServer: { sendRequest: request, onNotificationRaw: () => () => {}, sendConnectionRequest } } })
    const load = store().fetchChildren(parent)
    registerThreadConnection(parent, { ...source, connectionId: 'new-connection' })
    response.resolve(wire())
    await load
    expect(store().childrenByParent.has(parent)).toBe(false)
  })

  it('interrupts a child on its parent connection without using the foreground', async () => {
    registerThreadConnection(parent, source)
    const sendConnectionRequest = vi.fn().mockResolvedValue({})
    vi.stubGlobal('window', { api: { appServer: { sendRequest: request, onNotificationRaw: () => () => {}, sendConnectionRequest } } })
    useThreadStore.getState().applyRuntimeSnapshot('child-B', {
      running: true, activeTurnId: 'child-turn', waitingOnApproval: false, waitingOnPlanConfirmation: false
    }, { isDesktopOrigin: false })
    await store().interruptChild(makeSubAgent({ parentThreadId: parent }))
    expect(request).not.toHaveBeenCalled()
    expect(sendConnectionRequest).toHaveBeenCalledWith(
      source.connectionKey, 'turn/interrupt', { threadId: 'child-B', turnId: 'child-turn' },
      undefined, expect.objectContaining({ connectionId: source.connectionId })
    )
  })
})

describe('closed subagent history', () => {
  it('keeps closed records terminal across stale runtime, progress and list refreshes', () => {
    store().setChildren(parent, [makeSubAgent({ status: 'closed' })])
    useThreadStore.getState().applyRuntimeSnapshot('child-B', {
      running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false
    }, { isDesktopOrigin: false })
    store().updateProgress(parent, [{ label: 'Core B', currentTool: 'Exec', inputTokens: 3, outputTokens: 2, isCompleted: false }])
    store().setChildren(parent, [makeSubAgent()])
    expect(store().childrenByParent.get(parent)).toHaveLength(1)
    expect(isSubAgentChildRunning(store().childrenByParent.get(parent)![0])).toBe(false)
    store().setChildren(parent, [])
    expect(store().childrenByParent.get(parent)?.[0].status).toBe('closed')
  })

  it('does not let a closed row consume progress intended for a live child', () => {
    store().setChildren(parent, [
      makeSubAgent({ childThreadId: 'closed', nickname: 'Old', status: 'closed' }),
      makeSubAgent()
    ])
    store().updateProgress(parent, [{ label: 'runtime label', currentTool: 'Exec', inputTokens: 11, outputTokens: 2, isCompleted: false }])
    const children = store().childrenByParent.get(parent)!
    expect(children).toHaveLength(2)
    expect(children[0].currentTool).toBeNull()
    expect(children[1].currentTool).toBe('Exec')
  })
})
