import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSubAgentTranscriptStore } from '../stores/subAgentTranscriptStore'
import { makeRestoreResult } from './threadRestorationFixtures'
import type { Thread } from '../types/thread'
import { useTurnItemsLoadStore } from '../stores/turnItemsLoadStore'

const request = vi.fn()
let notify: (payload: { method: string; params: unknown }) => void
let apply: (result: unknown) => void
let resolve: (result: unknown) => void
let archived = false
let itemPages: Record<string, { data: unknown[]; nextCursor: string | null }>

function thread(): Thread {
  return {
    id: 'child', userId: 'local', metadata: {}, turns: [], workspacePath: 'workspace',
    displayName: 'Child', originChannel: 'dotcraft', status: archived ? 'archived' : 'active',
    createdAt: '2026-11-07T00:00:00Z', lastActiveAt: '2026-11-07T00:00:00Z'
  }
}

async function settle(): Promise<void> {
  for (let i = 0; i < 8; i++) await Promise.resolve()
}

beforeEach(() => {
  vi.useFakeTimers()
  archived = false
  itemPages = {}
  useTurnItemsLoadStore.setState({ statuses: {} })
  request.mockReset().mockImplementation((_connection, method, params, _timeout, options) => {
    if (method === 'thread/read') return Promise.resolve({ thread: thread() })
    if (method === 'thread/resume') {
      apply = options.onResponse
      return new Promise(done => { resolve = done })
    }
    if (method === 'thread/turns/list') return Promise.resolve({ data: [], nextCursor: null })
    if (method === 'thread/items/list') {
      const page = itemPages[params.cursor ?? 'initial']
      options?.onResponse?.(page)
      return Promise.resolve(page)
    }
    return Promise.resolve({})
  })
  vi.stubGlobal('window', { api: { appServer: {
    getConnectionKey: async () => 'original-workspace',
    sendConnectionRequest: request,
    onNotification: (callback: typeof notify) => { notify = callback; return () => {} }
  } } })
  useSubAgentTranscriptStore.getState().reset()
})

afterEach(() => {
  useSubAgentTranscriptStore.getState().reset()
  vi.unstubAllGlobals()
  vi.useRealTimers()
})

function summarySnapshot(itemCount: number) {
  return makeRestoreResult({ ...thread(), turns: [{
    id: 'turn', threadId: 'child', status: 'completed', startedAt: '2026-11-07T00:00:00Z',
    itemsView: 'summary', itemCount,
    items: [{ id: 'reply', type: 'agentMessage', status: 'completed', createdAt: '2026-11-07T00:05:00Z', payload: { text: 'reply' } }]
  }] } as unknown as Thread)
}

function itemPage(ids: string[], nextCursor: string | null) {
  return { data: ids.map((id, index) => ({ turnId: 'turn', item: {
    id, type: 'toolCall', status: 'completed', createdAt: `2026-11-07T00:0${index + 1}:00Z`,
    payload: { toolName: 'Example', result: `loaded ${id}` }
  } })), nextCursor }
}

describe('child transcript observation', () => {
  it('keeps cached hydrated content visible during reopen and after a failed refresh', async () => {
    const store = useSubAgentTranscriptStore.getState()
    const stop = store.watchTranscript('child')
    await settle()
    const snapshot = summarySnapshot(2)
    apply(snapshot)
    resolve(snapshot)
    await settle()
    itemPages.initial = itemPage(['tool'], null)
    await store.loadTurnItems('child', 'turn')
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.turns[0].summary).toBeUndefined()
    stop()

    const reopen = store.watchTranscript('child')
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.status).toBe('ready')
    await settle()
    apply(snapshot)
    resolve(snapshot)
    await settle()
    const cached = useSubAgentTranscriptStore.getState().transcripts.get('child')!
    expect(cached.turns[0].items.map(item => item.id)).toEqual(['tool', 'reply'])
    expect(cached.turns[0].summary).toBeUndefined()
    reopen()

    request.mockRejectedValueOnce(new Error('refresh failed'))
    const failed = store.watchTranscript('child')
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.status).toBe('ready')
    await settle()
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')).toMatchObject({
      status: 'error', turns: cached.turns
    })
    failed()
  })

  it('fulfills cached detail demand after the reopened observation is ready', async () => {
    const store = useSubAgentTranscriptStore.getState()
    const stop = store.watchTranscript('child')
    await settle()
    const snapshot = summarySnapshot(2)
    apply(snapshot)
    resolve(snapshot)
    await settle()
    stop()
    const reopen = store.watchTranscript('child')
    await settle()
    itemPages.initial = itemPage(['tool'], null)
    const loading = store.loadTurnItems('child', 'turn')
    expect(request.mock.calls.filter(call => call[1] === 'thread/items/list')).toHaveLength(0)
    apply(snapshot)
    resolve(snapshot)
    await loading
    expect(request.mock.calls.filter(call => call[1] === 'thread/items/list')).toHaveLength(1)
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.turns[0].summary).toBeUndefined()
    reopen()
  })

  it('keeps a reopened viewer loading when an old viewer’s detail request finishes', async () => {
    const store = useSubAgentTranscriptStore.getState()
    const snapshot = summarySnapshot(2)
    const holdPage = () => {
      let finish!: () => void
      request.mockImplementationOnce((_connection, _method, _params, _timeout, options) => new Promise(done => {
        finish = () => {
          const page = itemPage(['tool'], null)
          options.onResponse(page)
          done(page)
        }
      }))
      return () => finish()
    }
    const stop = store.watchTranscript('child')
    await settle()
    apply(snapshot)
    resolve(snapshot)
    await settle()
    const finishOld = holdPage()
    const oldRead = store.loadTurnItems('child', 'turn')
    stop()

    const reopen = store.watchTranscript('child')
    await settle()
    apply(snapshot)
    resolve(snapshot)
    await settle()
    const finishNew = holdPage()
    const newRead = store.loadTurnItems('child', 'turn')
    finishOld()
    await oldRead
    expect(useTurnItemsLoadStore.getState().statuses.turn).toBe('loading')
    finishNew()
    await newRead
    expect(useTurnItemsLoadStore.getState().statuses.turn).toBeUndefined()
    reopen()
  })

  it('loads one detail page by default and drains remaining pages only for explicit expansion', async () => {
    const store = useSubAgentTranscriptStore.getState()
    const stop = store.watchTranscript('child')
    await settle()
    const snapshot = summarySnapshot(4)
    apply(snapshot)
    resolve(snapshot)
    await settle()
    itemPages.initial = itemPage(['first'], 'second')
    itemPages.second = itemPage(['second'], 'third')
    itemPages.third = itemPage(['third'], null)

    await store.loadTurnItems('child', 'turn')
    expect(request.mock.calls.filter(call => call[1] === 'thread/items/list')).toHaveLength(1)
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.turns[0].summary?.nextCursor).toBe('second')
    await store.loadTurnItems('child', 'turn', () => true, true)
    expect(request.mock.calls.filter(call => call[1] === 'thread/items/list').map(call => call[2].cursor)).toEqual([null, 'second', 'third'])
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.turns[0].summary).toBeUndefined()
    stop()
  })

  it('shares one watch, restores cumulative text before suffix events and releases only after the last viewer leaves', async () => {
    const store = useSubAgentTranscriptStore.getState()
    const first = store.watchTranscript('child')
    const second = store.watchTranscript('child')
    await settle()
    expect(request.mock.calls.filter(call => call[1] === 'thread/resume')).toHaveLength(1)
    notify({ method: 'item/agentMessage/delta', params: { threadId: 'child', itemId: 'message', delta: 'covered' } })
    const result = makeRestoreResult({
      ...thread(), turns: [{
        id: 'turn', threadId: 'child', status: 'running', createdAt: '2026-11-07T00:00:00Z',
        items: [{ id: 'message', type: 'agentMessage', status: 'started', payload: { text: 'baseline' } }]
      }]
    })
    apply(result)
    resolve(result)
    notify({ method: 'item/agentMessage/delta', params: { threadId: 'child', itemId: 'message', delta: ' suffix' } })
    vi.advanceTimersByTime(50)
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.live?.text).toBe('baseline suffix')
    first()
    expect(request.mock.calls.filter(call => call[1] === 'thread/unsubscribe')).toHaveLength(0)
    second()
    expect(request).toHaveBeenLastCalledWith(
      'original-workspace', 'thread/unsubscribe', { threadId: 'child' }, undefined,
      expect.objectContaining({ attachmentOwner: expect.stringMatching(/^transcript:child:/) })
    )
  })

  it('releases a late snapshot on its captured connection without activating an abandoned transcript', async () => {
    const stop = useSubAgentTranscriptStore.getState().watchTranscript('child')
    await settle()
    stop()
    const result = makeRestoreResult(thread())
    apply(result)
    resolve(result)
    await settle()
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.status).toBe('loading')
    expect(request).toHaveBeenLastCalledWith(
      'original-workspace', 'thread/unsubscribe', { threadId: 'child' }, undefined,
      expect.objectContaining({ attachmentOwner: expect.stringMatching(/^transcript:child:/) })
    )
  })

  it('renders archived history through read and summary pages only', async () => {
    archived = true
    const stop = useSubAgentTranscriptStore.getState().watchTranscript('child')
    await settle()
    expect(request.mock.calls.map(call => call[1])).toEqual(['thread/read', 'thread/turns/list'])
    expect(useSubAgentTranscriptStore.getState().transcripts.get('child')?.status).toBe('ready')
    stop()
  })
})
