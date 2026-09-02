import { beforeEach, describe, expect, it, vi } from 'vitest'
import { isSubAgentChildRunning, useSubAgentStore } from '../stores/subAgentStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { deferred, makeSubAgent } from './subAgentFixtures'

const request = vi.fn()
const store = useSubAgentStore.getState
const parent = 'parent-B'
const wire = (id = 'child-B', status = 'open') => ({ data: [{
  edge: { childThreadId: id, parentThreadId: parent, agentPath: '/root/review_core', status },
  thread: { id, runtime: { running: true }, displayName: id }
}] })

beforeEach(() => {
  request.mockReset()
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
    })
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

  it.each(['clear', 'reset'] as const)('ignores delayed preview responses after %s even when the same child is reloaded', async (action) => {
    store().setChildren(parent, [makeSubAgent()])
    const old = deferred<unknown>()
    request.mockImplementation((method: string) => {
      if (method === 'thread/turns/list') return old.promise
      if (method === 'thread/items/list') return Promise.resolve({ data: [{ turnId: 'old-turn', item: { id: 'message', type: 'agentMessage', status: 'completed', text: 'stale preview' } }], nextCursor: null })
      return Promise.resolve({ thread: { id: 'child-B', turns: [] } })
    })
    const preview = store().fetchPreviews(parent)
    if (action === 'clear') store().clearParent(parent)
    else store().reset()
    store().setChildren(parent, [makeSubAgent({ lastMessagePreview: 'fresh preview' })])
    old.resolve({ data: [{ id: 'old-turn', threadId: 'child-B', status: 'completed', startedAt: '' }], nextCursor: null })
    await preview
    expect(store().childrenByParent.get(parent)?.[0].lastMessagePreview).toBe('fresh preview')
    await store().fetchPreviews(parent, { force: true })
    expect(store().childrenByParent.get(parent)?.[0].lastMessagePreview).toBe('stale preview')
  })
})

describe('closed subagent history', () => {
  it('keeps closed records terminal across stale runtime, progress and list refreshes', () => {
    store().setChildren(parent, [makeSubAgent({ status: 'closed', isCompleted: true })])
    store().updateChildRuntime('child-B', { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false })
    store().updateProgress(parent, [{ label: 'Core B', currentTool: 'Exec', inputTokens: 3, outputTokens: 2, isCompleted: false }])
    store().setChildren(parent, [makeSubAgent()])
    expect(store().childrenByParent.get(parent)).toHaveLength(1)
    expect(isSubAgentChildRunning(store().childrenByParent.get(parent)![0])).toBe(false)
    store().setChildren(parent, [])
    expect(store().childrenByParent.get(parent)?.[0].status).toBe('closed')
  })

  it('does not let a closed row consume progress intended for a live child', () => {
    store().setChildren(parent, [
      makeSubAgent({ childThreadId: 'closed', nickname: 'Old', status: 'closed', isCompleted: true }),
      makeSubAgent()
    ])
    store().updateProgress(parent, [{ label: 'runtime label', currentTool: 'Exec', inputTokens: 11, outputTokens: 2, isCompleted: false }])
    const children = store().childrenByParent.get(parent)!
    expect(children).toHaveLength(2)
    expect(children[0].currentTool).toBeNull()
    expect(children[1].currentTool).toBe('Exec')
    expect(children[1].inputTokens).toBe(11)
  })
})
