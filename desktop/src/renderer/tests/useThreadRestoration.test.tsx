import { act, renderHook, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useThreadRestoration } from '../hooks/useThreadRestoration'
import { useConnectionStore } from '../stores/connectionStore'
import { getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { releaseThreadState } from '../stores/threadState'
import { finishThreadRestoration, stageThreadNotification, useThreadRestorationStore } from '../stores/threadRestoration'
import type { ThreadResumeResult } from '../stores/applyThreadRestoration'
import type { Thread } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

vi.mock('../utils/startPendingWelcomeTurn', () => ({ startPendingWelcomeTurn: vi.fn() }))

const props = { workspaceIdentity: 'workspace', workspaceKey: 'workspace', protocolWorkspacePath: 'workspace', locale: 'en' as const }
const requests: Array<{
  threadId: string
  apply(result: ThreadResumeResult): void
  resolve(result: ThreadResumeResult): void
  reject(error: unknown): void
}> = []
const sendRequest = vi.fn()
let preview: Promise<unknown>

function thread(id: string): Thread {
  return {
    id, userId: 'local', workspacePath: 'workspace', metadata: {}, displayName: id,
    originChannel: 'dotcraft-desktop', status: 'active', configuration: { mode: 'agent' },
    createdAt: '2026-11-07T00:00:00Z', lastActiveAt: '2026-11-07T00:00:00Z', turns: [],
    runtime: { running: false, waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: false }
  }
}

function snapshot(id: string, text = '', waiting = false): ThreadResumeResult {
  return {
    thread: { ...thread(id), runtime: { ...thread(id).runtime, running: Boolean(text), waitingOnInput: waiting } },
    instructionSources: [],
    currentState: {
      turnId: text ? 'turn-current' : undefined, turnStatus: text ? 'running' : undefined,
      itemCount: text ? 1 : 0, pendingRequests: []
    },
    initialTurnsPage: {
      data: text ? [{
        id: 'turn-current', threadId: id, status: 'running', startedAt: '2026-11-07T00:00:00Z',
        itemsView: 'summary', itemCount: 1,
        items: [{ id: 'message', type: 'agentMessage', status: 'streaming', payload: { text }, createdAt: '2026-11-07T00:00:00Z' }]
      }] : [], nextCursor: 'older'
    }
  } as unknown as ThreadResumeResult
}

function respond(id: string, result = snapshot(id)): void {
  const request = requests.find(request => request.threadId === id)
  if (!request) throw new Error('Missing restore')
  requests.splice(requests.indexOf(request), 1)
  request.apply(result)
  request.resolve(result)
}

beforeEach(() => {
  finishThreadRestoration()
  requests.length = 0
  for (let i = 0; i < 13; i++) releaseThreadState('thread-' + i)
  useThreadStore.getState().reset()
  useConversationStore.getState().reset()
  useConnectionStore.getState().reset()
  useUIStore.setState({ pendingWelcomeTurn: null, planApprovalDismissed: {} })
  preview = Promise.resolve({ data: [], nextCursor: null })
  sendRequest.mockReset().mockImplementation((method, params, _timeout, options) => {
    if (method === 'thread/resume') {
      return new Promise((resolve, reject) => requests.push({
        threadId: params.threadId, apply: options.onResponse, resolve, reject
      }))
    }
    if (method === 'thread/read') return Promise.resolve({ thread: thread(params.threadId) })
    if (method === 'thread/turns/list') return preview
    return Promise.resolve({})
  })
  installDesktopApiMock({ appServer: { sendRequest } })
  useConnectionStore.getState().setStatus({ status: 'connected' })
  useThreadStore.getState().setThreadList(Array.from({ length: 13 }, (_, i) => thread('thread-' + i)), 'workspace')
})

afterEach(() => finishThreadRestoration())

async function select(id: string): Promise<void> {
  await act(async () => useThreadStore.getState().setActiveThreadId(id))
}

describe('thread restoration', () => {
  it('shows a parallel read-only preview, replaces covered deltas and continues from the response text', async () => {
    let show!: (value: unknown) => void
    preview = new Promise(resolve => { show = resolve })
    renderHook(() => useThreadRestoration(props))
    await select('thread-0')
    expect(requests).toHaveLength(1)
    expect(sendRequest.mock.calls.some(call => call[0] === 'thread/turns/list')).toBe(true)
    await act(async () => show({
      data: [{ id: 'preview-turn', threadId: 'thread-0', status: 'completed', itemsView: 'summary', items: [], createdAt: '2026-11-07T00:00:00Z' }]
    }))
    expect(useConversationStore.getState().turns[0].id).toBe('preview-turn')
    expect(useThreadRestorationStore.getState().restoringThreadId).toBe('thread-0')
    const covered = vi.fn()
    expect(stageThreadNotification('thread-0', covered)).toBe(true)
    await act(async () => {
      respond('thread-0', snapshot('thread-0', 'authoritative'))
      expect(stageThreadNotification('thread-0', () => {})).toBe(false)
      useConversationStore.getState().onAgentMessageDelta(' suffix')
    })
    await waitFor(() => expect(useConversationStore.getState().streamingMessage).toBe('authoritative suffix'))
    expect(covered).not.toHaveBeenCalled()
    expect(useConversationStore.getState().turns.map(turn => turn.id)).toEqual(['turn-current'])
    expect(useThreadRestorationStore.getState().restoringThreadId).toBeNull()
  })

  it('retains matching logical input requests that arrived before the baseline and drops resolved ones', async () => {
    renderHook(() => useThreadRestoration(props))
    await select('thread-0')
    const input = {
      threadId: 'thread-0', turnId: 'turn-current', requestId: 'input-current', isBlocking: true,
      questions: [{ id: 'q', header: 'Choice', question: 'Continue?', options: [] }]
    }
    act(() => {
      useThreadStore.getState().parkUserInput('thread-0', { bridgeId: 'bridge-original', turnId: 'turn-current', rawParams: input })
      useThreadStore.getState().parkApproval('thread-0', {
        bridgeId: 'resolved', turnId: 'turn-current', rawParams: { requestId: 'already-resolved' }
      })
    })
    const result = snapshot('thread-0', 'waiting', true)
    result.currentState.pendingRequests = [{
      method: 'item/tool/requestUserInput', requestId: 'input-current', turnId: 'turn-current', itemId: 'question', isBlocking: true
    }]
    await act(async () => respond('thread-0', result))
    expect(useConversationStore.getState().pendingUserInput).toMatchObject({
      bridgeId: 'bridge-original', requestId: 'input-current', turnId: 'turn-current', isBlocking: true
    })
    expect(useConversationStore.getState().pendingApproval).toBeNull()
    expect(useConversationStore.getState().turnStatus).toBe('waitingInput')
  })

  it('reuses an attached warm view, including its live output, without another restore', async () => {
    renderHook(() => useThreadRestoration(props))
    await select('thread-0')
    await act(async () => respond('thread-0', snapshot('thread-0', 'warm')))
    await select('thread-1')
    await act(async () => respond('thread-1'))
    act(() => getThreadConversation('thread-0')!.setState({ streamingMessage: 'warm updated' }))
    sendRequest.mockClear()
    await select('thread-0')
    expect(useConversationStore.getState().streamingMessage).toBe('warm updated')
    expect(sendRequest).not.toHaveBeenCalled()
  })

  it('keeps a newer selection authoritative when an earlier restore finishes late', async () => {
    renderHook(() => useThreadRestoration(props))
    await select('thread-0')
    await select('thread-1')
    await act(async () => respond('thread-1'))
    await act(async () => respond('thread-0', snapshot('thread-0', 'late')))
    expect(useThreadStore.getState().activeThread?.id).toBe('thread-1')
    expect(useConversationStore.getState().turns).toHaveLength(0)
    expect(sendRequest).toHaveBeenCalledWith('thread/unsubscribe', { threadId: 'thread-0' })
  })

  it('does not let an older same-thread request release a newer selected lifetime', async () => {
    renderHook(() => useThreadRestoration(props))
    await select('thread-0')
    await select('thread-1')
    await act(async () => respond('thread-1'))
    await select('thread-0')
    await act(async () => respond('thread-0', snapshot('thread-0', 'old')))
    expect(useThreadRestorationStore.getState().restoringThreadId).toBe('thread-0')
    await act(async () => respond('thread-0', snapshot('thread-0', 'current')))
    expect(useConversationStore.getState().streamingMessage).toBe('current')
    expect(getThreadConversation('thread-0')).toBeDefined()
  })

  it('uses read-only history for an archived selection', async () => {
    useThreadStore.getState().setThreadList([{ ...thread('thread-0'), status: 'archived' }], 'workspace')
    renderHook(() => useThreadRestoration(props))
    await select('thread-0')
    await waitFor(() => expect(useThreadRestorationStore.getState().restoringThreadId).toBeNull())
    expect(requests).toHaveLength(0)
    expect(useThreadStore.getState().activeThread?.id).toBe('thread-0')
  })

  it('releases the oldest attachment when an eleventh warm view is left', async () => {
    renderHook(() => useThreadRestoration(props))
    for (let i = 0; i < 12; i++) {
      await select('thread-' + i)
      await act(async () => respond('thread-' + i))
    }
    expect(sendRequest).toHaveBeenCalledWith('thread/unsubscribe', { threadId: 'thread-0' })
    expect(getThreadConversation('thread-0')).toBeUndefined()
    expect(getThreadConversation('thread-1')).toBeDefined()
  })

  it('discards stale connection and workspace results even when their thread id matches', async () => {
    const { rerender } = renderHook(value => useThreadRestoration(value), { initialProps: props })
    await select('thread-0')
    act(() => useConnectionStore.getState().setStatus({ status: 'connected' }))
    await waitFor(() => expect(requests).toHaveLength(2))
    await act(async () => respond('thread-0', snapshot('thread-0', 'old connection')))
    expect(useThreadRestorationStore.getState().restoringThreadId).toBe('thread-0')
    await act(async () => respond('thread-0', snapshot('thread-0', 'new connection')))
    expect(useConversationStore.getState().streamingMessage).toBe('new connection')
    rerender({ ...props, workspaceIdentity: 'other', workspaceKey: 'other' })
    expect(useThreadStore.getState().activeThreadId).toBe('thread-0')
  })
})
