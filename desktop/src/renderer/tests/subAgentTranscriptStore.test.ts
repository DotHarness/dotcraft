import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useSubAgentTranscriptStore } from '../stores/subAgentTranscriptStore'
import { makeRestoreResult } from './threadRestorationFixtures'
import type { Thread } from '../types/thread'

const request = vi.fn()
let notify: (payload: { method: string; params: unknown }) => void
let apply: (result: unknown) => void
let resolve: (result: unknown) => void
let archived = false

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
  request.mockReset().mockImplementation((_connection, method, _params, _timeout, options) => {
    if (method === 'thread/read') return Promise.resolve({ thread: thread() })
    if (method === 'thread/resume') {
      apply = options.onResponse
      return new Promise(done => { resolve = done })
    }
    if (method === 'thread/turns/list') return Promise.resolve({ data: [], nextCursor: null })
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

describe('child transcript observation', () => {
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
