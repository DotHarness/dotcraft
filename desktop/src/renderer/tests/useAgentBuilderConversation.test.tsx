import { useEffect } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { render, screen, waitFor } from '@testing-library/react'
import { useAgentBuilderConversation } from '../components/agents/useAgentBuilderConversation'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()
const appServerOnNotification = vi.fn()
type NotificationPayload = { method: string; params?: unknown }
let notificationHandlers: Array<(payload: NotificationPayload) => void> = []

function emitNotification(payload: NotificationPayload): void {
  for (const handler of notificationHandlers) handler(payload)
}

function Harness({
  onResult = vi.fn(),
  onEditingField
}: {
  onResult?: ReturnType<typeof vi.fn>
  onEditingField?: ReturnType<typeof vi.fn>
}): JSX.Element {
  const result = useAgentBuilderConversation({
    active: true,
    onResult,
    onEditingField
  })

  useEffect(() => {
    void result.start({
      targetId: 'draft-agent',
      targetSource: 'workspace',
      initialDraftMarkdown: '---\nname: draft-agent\n---\n\nDraft body.\n',
      inputParts: [{ type: 'text', text: 'Build the agent from this intent.' }],
      config: { model: 'gpt-5.5' }
    }).catch(() => undefined)
  }, [result.start])

  return (
    <div>
      <span>{result.status}</span>
      {result.error && <span>{result.error}</span>}
    </div>
  )
}

function PassiveHarness(): JSX.Element {
  const result = useAgentBuilderConversation({
    active: true,
    onResult: vi.fn()
  })

  return <span>{result.status}</span>
}

describe('useAgentBuilderConversation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    notificationHandlers = []
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return { thread: { id: 'builder-thread' } }
      }
      return {}
    })
    appServerOnNotification.mockImplementation((handler: (payload: NotificationPayload) => void) => {
      notificationHandlers.push(handler)
      return () => {
        notificationHandlers = notificationHandlers.filter((existing) => existing !== handler)
      }
    })

    installDesktopApiMock({
      appServer: {
        sendRequest: appServerSendRequest,
        onNotification: appServerOnNotification
      }
    })

    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'X:\\fixtures\\workspace' })
    useThreadStore.getState().reset()
    useThreadStore.setState({ activeThreadId: 'previous-thread' })
  })

  it('does not create a builder thread until start is called', async () => {
    render(<PassiveHarness />)

    expect(screen.getByText('idle')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/start', expect.anything())
  })

  it('binds and syncs the draft before sending the initial builder prompt', async () => {
    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('ready')).toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', {
        threadId: 'builder-thread',
        input: [{ type: 'text', text: 'Build the agent from this intent.' }],
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: 'workspace:X:\\fixtures\\workspace',
          workspacePath: 'X:\\fixtures\\workspace'
        }
      })
    })

    expect(appServerSendRequest.mock.calls.map(([method]) => method)).toEqual([
      'thread/start',
      'agent/profiles/builderDraft/update',
      'thread/subscribe',
      'turn/start'
    ])
    expect(appServerSendRequest).toHaveBeenNthCalledWith(1, 'thread/start', {
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: 'workspace:X:\\fixtures\\workspace',
        workspacePath: 'X:\\fixtures\\workspace'
      },
      config: {
        model: 'gpt-5.5',
        agentBuilderTargetId: 'draft-agent',
        agentBuilderTargetSource: 'workspace'
      },
      historyMode: 'server'
    })
    expect(appServerSendRequest).toHaveBeenNthCalledWith(2, 'agent/profiles/builderDraft/update', {
      threadId: 'builder-thread',
      rawContent: '---\nname: draft-agent\n---\n\nDraft body.\n'
    })
    expect(appServerSendRequest).toHaveBeenNthCalledWith(3, 'thread/subscribe', {
      threadId: 'builder-thread'
    })
    expect(appServerSendRequest).toHaveBeenNthCalledWith(4, 'turn/start', {
      threadId: 'builder-thread',
      input: [{ type: 'text', text: 'Build the agent from this intent.' }],
      identity: {
        channelName: 'dotcraft-desktop',
        userId: 'local',
        channelContext: 'workspace:X:\\fixtures\\workspace',
        workspacePath: 'X:\\fixtures\\workspace'
      }
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/enqueue')).toBe(false)
    expect(useThreadStore.getState().activeThreadId).toBe('builder-thread')
  })

  it('does not start the initial builder turn until the thread subscription succeeds', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return { thread: { id: 'builder-thread' } }
      }
      if (method === 'thread/subscribe') {
        throw new Error('subscribe failed')
      }
      return {}
    })

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('error')).toBeInTheDocument()
      expect(screen.getByText('subscribe failed')).toBeInTheDocument()
    })
    expect(appServerSendRequest.mock.calls.map(([method]) => method)).toEqual([
      'thread/start',
      'agent/profiles/builderDraft/update',
      'thread/subscribe'
    ])
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/start')).toBe(false)
    expect(useThreadStore.getState().activeThreadId).toBe('previous-thread')
  })

  it('does not restore the previous active thread over a user-selected thread on unmount', async () => {
    const { unmount } = render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('ready')).toBeInTheDocument()
    })

    useThreadStore.getState().setActiveThreadId('user-selected-thread')
    unmount()

    expect(useThreadStore.getState().activeThreadId).toBe('user-selected-thread')
  })

  it('surfaces builder tool results from real toolCall and toolResult notifications', async () => {
    const onResult = vi.fn()
    const onEditingField = vi.fn()
    render(<Harness onResult={onResult} onEditingField={onEditingField} />)

    await waitFor(() => {
      expect(screen.getByText('ready')).toBeInTheDocument()
      expect(appServerOnNotification).toHaveBeenCalled()
    })

    emitNotification({
      method: 'item/completed',
      params: {
        threadId: 'builder-thread',
        turnId: 'turn-1',
        item: {
          id: 'tool-call-1',
          type: 'toolCall',
          payload: {
            toolName: 'SetAgentName',
            callId: 'call-1',
            arguments: { name: 'triage-bot' }
          }
        }
      }
    })
    expect(onEditingField).toHaveBeenCalledWith('name')

    emitNotification({
      method: 'item/completed',
      params: {
        threadId: 'builder-thread',
        turnId: 'turn-1',
        item: {
          id: 'tool-result-1',
          type: 'toolResult',
          payload: {
            callId: 'call-1',
            result: '{"ok":true,"field":"name","change":{"op":"set","value":"triage-bot"}}',
            success: true
          }
        }
      }
    })

    expect(onResult).toHaveBeenCalledWith({
      ok: true,
      field: 'name',
      change: { op: 'set', value: 'triage-bot' }
    })
    expect(onEditingField).toHaveBeenLastCalledWith(null)
  })

  it('surfaces the edited field as soon as builder tool arguments stream', async () => {
    const onEditingField = vi.fn()
    render(<Harness onEditingField={onEditingField} />)

    await waitFor(() => {
      expect(screen.getByText('ready')).toBeInTheDocument()
      expect(appServerOnNotification).toHaveBeenCalled()
    })

    emitNotification({
      method: 'item/toolCall/argumentsDelta',
      params: {
        threadId: 'builder-thread',
        turnId: 'turn-1',
        itemId: 'tool-call-1',
        toolName: 'AppendAgentInstructions',
        callId: 'call-1',
        delta: '{"text":"'
      }
    })

    expect(onEditingField).toHaveBeenCalledWith('instructions')

    emitNotification({
      method: 'turn/completed',
      params: {
        turn: {
          id: 'turn-1',
          threadId: 'builder-thread'
        }
      }
    })

    expect(onEditingField).toHaveBeenLastCalledWith(null)
  })

  it('surfaces the edited field at builder tool start and keeps it through callId-only argument deltas', async () => {
    const onEditingField = vi.fn()
    render(<Harness onEditingField={onEditingField} />)

    await waitFor(() => {
      expect(screen.getByText('ready')).toBeInTheDocument()
      expect(appServerOnNotification).toHaveBeenCalled()
    })

    emitNotification({
      method: 'item/started',
      params: {
        threadId: 'builder-thread',
        turnId: 'turn-1',
        item: {
          id: 'tool-call-1',
          type: 'toolCall',
          payload: {
            toolName: 'SetAgentProviderPreference',
            callId: 'call-1'
          }
        }
      }
    })

    expect(onEditingField).toHaveBeenCalledWith('providerPreference')

    emitNotification({
      method: 'item/toolCall/argumentsDelta',
      params: {
        threadId: 'builder-thread',
        turnId: 'turn-1',
        itemId: 'tool-call-1',
        callId: 'call-1',
        delta: '{"model":"'
      }
    })

    expect(onEditingField).toHaveBeenLastCalledWith('providerPreference')

    emitNotification({
      method: 'item/completed',
      params: {
        threadId: 'builder-thread',
        turnId: 'turn-1',
        item: {
          id: 'tool-result-1',
          type: 'toolResult',
          payload: {
            callId: 'call-1',
            result: '{"ok":true,"field":"model","change":{"op":"set","value":"gpt-5.5"}}',
            success: true
          }
        }
      }
    })

    expect(onEditingField).toHaveBeenLastCalledWith(null)
  })

  it('surfaces initial turn/start failures instead of silently swallowing them', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return { thread: { id: 'builder-thread' } }
      }
      if (method === 'turn/start') {
        throw new Error('turn failed')
      }
      return {}
    })

    render(<Harness />)

    await waitFor(() => {
      expect(screen.getByText('error')).toBeInTheDocument()
      expect(screen.getByText('turn failed')).toBeInTheDocument()
    })
    expect(useThreadStore.getState().activeThreadId).toBe('previous-thread')
  })
})
