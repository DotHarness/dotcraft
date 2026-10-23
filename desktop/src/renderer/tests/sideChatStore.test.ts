import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useConnectionStore } from '../stores/connectionStore'
import { useSideChatStore } from '../stores/sideChatStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { parseSideSlashCommand, withoutSideCommand } from '../utils/sideChat'

type Notify = (payload: { method: string; params: unknown }) => void

let notify: Notify | null = null
let sendRequest: ReturnType<typeof vi.fn>
let sendConnectionRequest: ReturnType<typeof vi.fn>
let sendServerResponse: ReturnType<typeof vi.fn>

function emit(method: string, params: Record<string, unknown>): void {
  notify?.({ method, params })
}

async function openSideChat(prompt?: string): Promise<string> {
  const key = await useSideChatStore.getState().open('parent', {
    label: (ordinal) => `Side chat ${ordinal}`,
    ...(prompt ? { prompt: { text: prompt } } : {})
  })
  if (!key) throw new Error('side chat did not open')
  return key
}

function chat(key: string) {
  const value = useSideChatStore.getState().chats.get(key)
  if (!value) throw new Error('missing side chat')
  return value
}

beforeEach(() => {
  sendRequest = vi.fn(async (method: string) => {
    if (method === 'thread/fork') return { thread: { id: 'side-1' } }
    if (method === 'turn/start') return { turn: { id: 'turn-1' } }
    return {}
  })
  sendServerResponse = vi.fn(async () => {})
  sendConnectionRequest = vi.fn((_key: string, method: string, params: unknown) => sendRequest(method, params))
  vi.stubGlobal('window', {
    setTimeout,
    clearTimeout,
    api: {
      appServer: {
        sendRequest,
        sendConnectionRequest,
        getConnectionKey: async () => 'workspace-a',
        sendServerResponse,
        onNotification: (callback: Notify) => {
          notify = callback
          return () => {}
        }
      }
    }
  })
  useSideChatStore.getState().reset()
  useViewerTabStore.setState({ byThread: new Map(), currentThreadId: 'parent' })
  useConnectionStore.setState({ status: 'connected' })
})

afterEach(() => {
  useSideChatStore.getState().reset()
  useViewerTabStore.setState({ byThread: new Map(), currentThreadId: null })
  vi.unstubAllGlobals()
})

describe('side chats', () => {
  it('forks the whole thread, sends the first question, and keeps following its own events in the background', async () => {
    const key = await openSideChat('Where is Retry called?')

    expect(sendRequest).toHaveBeenCalledWith('thread/fork', { threadId: 'parent', ephemeral: true, sideChat: true })
    const turnStart = sendRequest.mock.calls.find(([method]) => method === 'turn/start')?.[1] as Record<string, unknown>
    expect(turnStart).toMatchObject({ threadId: 'side-1', input: [{ type: 'text', text: 'Where is Retry called?' }] })
    const tab = useViewerTabStore.getState().getThreadState('parent').tabs[0]
    expect(tab).toMatchObject({ kind: 'sideChat', sideChatKey: key, label: 'Side chat 1' })

    emit('turn/started', { turn: { id: 'turn-1', threadId: 'side-1', status: 'running' } })
    emit('item/completed', {
      threadId: 'side-1',
      turnId: 'turn-1',
      item: {
        id: 'user-1',
        type: 'userMessage',
        status: 'completed',
        createdAt: '2026-10-15T00:00:00.000Z',
        clientUserMessageId: turnStart.clientUserMessageId,
        payload: { text: 'Where is Retry called?' }
      }
    })
    emit('item/completed', {
      threadId: 'parent',
      turnId: 'main-turn',
      item: { id: 'main-item', type: 'agentMessage', status: 'completed', createdAt: '2026-10-15T00:00:01.000Z', payload: { text: 'main' } }
    })
    emit('item/completed', {
      threadId: 'side-1',
      turnId: 'turn-1',
      item: { id: 'agent-1', type: 'agentMessage', status: 'completed', createdAt: '2026-10-15T00:00:02.000Z', payload: { text: 'From Send.' } }
    })
    useViewerTabStore.getState().onThreadSwitched('another-thread')
    emit('turn/completed', { turn: { id: 'turn-1', threadId: 'side-1', status: 'completed' } })

    const { turns, unread } = chat(key)
    expect(turns.map((turn) => [turn.id, turn.status, turn.items.map((item) => item.id)])).toEqual([
      ['turn-1', 'completed', ['user-1', 'agent-1']]
    ])
    expect(unread).toBe(true)
  })

  it('guides the running side chat turn instead of starting another one', async () => {
    const key = await openSideChat('Where is Retry called?')
    emit('turn/started', { turn: { id: 'turn-1', threadId: 'side-1', status: 'running' } })

    await useSideChatStore.getState().send(key, {
      inputParts: [{ type: 'text', text: 'Only check the uploader.' }],
      visibleText: 'Only check the uploader.'
    })

    expect(sendRequest).toHaveBeenCalledWith('turn/steer', expect.objectContaining({
      threadId: 'side-1',
      expectedTurnId: 'turn-1',
      input: [{ type: 'text', text: 'Only check the uploader.' }]
    }))
    expect(sendRequest.mock.calls.filter(([method]) => method === 'turn/start')).toHaveLength(1)
  })

  it('keeps side chat approvals out of the main conversation and answers them', async () => {
    const key = await openSideChat()

    const handled = useSideChatStore.getState().handleServerRequest('item/approval/request', 'bridge-1', {
      threadId: 'side-1', turnId: 'turn-1', requestId: 'request-1', approvalType: 'shell', operation: 'run', target: 'git status'
    })
    const mainHandled = useSideChatStore.getState().handleServerRequest('item/approval/request', 'bridge-2', {
      threadId: 'parent', turnId: 'main-turn', requestId: 'request-2'
    })

    expect(handled).toBe(true)
    expect(mainHandled).toBe(false)
    expect(chat(key).pendingApprovals.map((approval) => approval.requestId)).toEqual(['request-1'])

    await useSideChatStore.getState().respondToApproval(key, 'bridge-1', 'accept')

    expect(sendServerResponse).toHaveBeenCalledWith('bridge-1', { decision: 'accept' })
    expect(chat(key).pendingApprovals).toEqual([])
  })

  it('deletes the fork and cancels its requests when its tab closes', async () => {
    const key = await openSideChat()
    useSideChatStore.getState().handleServerRequest('item/approval/request', 'bridge-1', {
      threadId: 'side-1', turnId: 'turn-1', requestId: 'request-1'
    })

    useViewerTabStore.getState().closeTab('parent', chat(key).tabId)

    expect(useSideChatStore.getState().chats.has(key)).toBe(false)
    expect(sendServerResponse).toHaveBeenCalledWith('bridge-1', { decision: 'cancel' })
    await vi.waitFor(() => expect(sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'side-1' }))
  })

  it('keeps using the connection it was opened on after the workspace switches', async () => {
    await openSideChat('Where is Retry called?')

    useViewerTabStore.getState().onWorkspaceSwitched('/workspace-b')

    expect(sendConnectionRequest).toHaveBeenCalledWith('workspace-a', 'thread/fork', expect.anything())
    expect(sendConnectionRequest).toHaveBeenCalledWith('workspace-a', 'turn/start', expect.anything())
    await vi.waitFor(() =>
      expect(sendConnectionRequest).toHaveBeenCalledWith('workspace-a', 'thread/delete', { threadId: 'side-1' }))
  })

  it('closes the tab when the server deletes the side chat', async () => {
    const key = await openSideChat()

    emit('thread/deleted', { threadId: 'side-1' })

    expect(useSideChatStore.getState().chats.has(key)).toBe(false)
    expect(useViewerTabStore.getState().getThreadState('parent').tabs).toEqual([])
  })

  it('expires open side chats when the connection is lost', async () => {
    const key = await openSideChat()

    useConnectionStore.setState({ status: 'disconnected' })

    expect(chat(key).status).toBe('expired')
  })

  it('closes the tab and reports the error when the fork fails', async () => {
    sendRequest.mockImplementation(async () => { throw new Error('boom') })

    await expect(useSideChatStore.getState().open('parent', { label: () => 'Side chat' })).rejects.toThrow('boom')

    expect(useViewerTabStore.getState().getThreadState('parent').tabs).toEqual([])
    expect(useSideChatStore.getState().chats.size).toBe(0)
  })
})

describe('side slash command', () => {
  it('reads the question after /side or /btw and ignores other text', () => {
    expect(parseSideSlashCommand('/side')).toEqual({ question: '' })
    expect(parseSideSlashCommand('/side  why is this slow? ')).toEqual({ question: 'why is this slow?' })
    expect(parseSideSlashCommand('/BTW what changed')).toEqual({ question: 'what changed' })
    for (const text of ['/sidebar', 'side', 'ask /side later']) {
      expect(parseSideSlashCommand(text)).toBeNull()
    }
  })

  it('removes the command but keeps the rest of the message', () => {
    expect(withoutSideCommand([
      { type: 'text', value: '/side explain ' },
      { type: 'file', relativePath: 'src/retry.ts' }
    ])).toEqual([
      { type: 'text', value: 'explain ' },
      { type: 'file', relativePath: 'src/retry.ts' }
    ])
  })
})
