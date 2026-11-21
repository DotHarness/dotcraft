import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useProvidersStore } from '../stores/providersStore'
import { wireTurnToConversationTurn } from '../types/conversation'
import { installDesktopApiMock } from './desktopApiMock'

const appServerSendRequest = vi.fn()

function renderWithLocale(node: JSX.Element): ReturnType<typeof render> {
  return render(<LocaleProvider>{node}</LocaleProvider>)
}

describe('MessageStream', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockResolvedValue({})
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().setWorkspacePath('X:\\fixtures\\workspace')
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeThread: {
        id: 'thread-1',
        displayName: 'Test thread',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString(),
        workspacePath: 'X:\\fixtures\\workspace',
        userId: 'local',
        metadata: {},
        turns: []
      }
    })
    installDesktopApiMock({
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: {
          sendRequest: appServerSendRequest,
          sendRequestRaw: async () => ({}),
          onNotificationRaw: () => () => undefined
        },
        workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
      })
  })

  afterEach(() => {
    vi.useRealTimers()
    act(() => useProvidersStore.setState({ providers: [] }))
  })

  it('renders a web URL in a user message as a browser link', () => {
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [{
          id: 'u1',
          type: 'userMessage',
          status: 'completed',
          text: 'See https://example.com/docs.',
          createdAt: new Date().toISOString()
        }]
      }],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)

    const link = screen.getByRole('link', { name: 'https://example.com/docs' })
    expect(link).toHaveAttribute('href', 'https://example.com/docs')
    expect(link).toHaveAttribute('data-inline-reference-kind', 'browser')
  })

  it('renders one handoff with focusable provider details in both live and hydrated history', () => {
    const createdAt = '2026-01-01T00:00:00Z'
    const threadId = 'thread-1'
    const rawTurn = {
      id: 'turn-1', threadId, status: 'running', createdAt,
      items: [{ id: 'message', type: 'userMessage', status: 'completed', createdAt, payload: { text: 'Continue' } }]
    }
    const item = {
      id: 'handoff', type: 'systemNotice', status: 'completed', createdAt,
      payload: {
        kind: 'contextHandoff',
        from: { providerId: 'claude', model: 'opus' },
        to: { providerId: 'claude', model: 'sonnet' }
      }
    }
    useProvidersStore.setState({
      providers: [{ id: 'claude', displayName: 'Anthropic', protocol: 'anthropic', authMethod: 'apiKey' }]
    })
    useConversationStore.setState({
      turns: [wireTurnToConversationTurn(rawTurn)],
      turnStatus: 'running',
      activeTurnId: rawTurn.id
    })
    renderWithLocale(<MessageStream />)
    expect(screen.queryByRole('group', { name: 'opus', exact: true })).not.toBeInTheDocument()

    act(() => {
      const event = { threadId, turnId: rawTurn.id, item }
      useConversationStore.getState().onItemCompleted(event)
      useConversationStore.getState().onItemCompleted(event)
    })
    fireEvent.focus(screen.getByRole('group', { name: 'sonnet', exact: true }))
    expect(screen.getByRole('tooltip', { name: 'Anthropic' })).toBeInTheDocument()

    act(() => useConversationStore.setState({
      turns: [wireTurnToConversationTurn({
        ...rawTurn, status: 'completed', items: [...rawTurn.items, item]
      })],
      turnStatus: 'idle',
      activeTurnId: null
    }))
    expect(screen.getByRole('group', { name: 'opus', exact: true })).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'sonnet', exact: true })).toBeInTheDocument()
  })

  it('sends inline edited text by rolling back before turn/start', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/rollback') {
        return {
          thread: {
            id: 'thread-1',
            displayName: 'Test thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: new Date().toISOString(),
            lastActiveAt: new Date().toISOString(),
            workspacePath: 'X:\\fixtures\\workspace',
            userId: 'local',
            metadata: {},
            turns: [],
            contextUsage: null
          }
        }
      }
      if (method === 'turn/start') {
        return { turn: { id: 'turn-retry' } }
      }
      return {}
    })
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [{
          id: 'u1',
          type: 'userMessage',
          status: 'completed',
          text: 'Original',
          createdAt: new Date().toISOString()
        }]
      }],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit message' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Edit message text' }), {
      target: { value: 'Edited retry' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'turn/start')).toBe(true))
    expect(appServerSendRequest.mock.calls[0][0]).toBe('thread/rollback')
    expect(appServerSendRequest.mock.calls[0][1]).toMatchObject({ threadId: 'thread-1', numTurns: 1 })
    const turnStart = appServerSendRequest.mock.calls.find((call) => call[0] === 'turn/start')!
    expect(turnStart[1]).toMatchObject({
      threadId: 'thread-1',
      input: [{ type: 'text', text: 'Edited retry' }]
    })
  })

  it('keeps inline draft after turn/start fails and does not rollback twice', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    let turnStartAttempts = 0
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/rollback') {
        return {
          thread: {
            id: 'thread-1',
            displayName: 'Test thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: new Date().toISOString(),
            lastActiveAt: new Date().toISOString(),
            workspacePath: 'X:\\fixtures\\workspace',
            userId: 'local',
            metadata: {},
            turns: [],
            contextUsage: null
          }
        }
      }
      if (method === 'turn/start') {
        turnStartAttempts += 1
        if (turnStartAttempts === 1) throw new Error('network down')
        return { turn: { id: 'turn-retry' } }
      }
      return {}
    })
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [{
          id: 'u1',
          type: 'userMessage',
          status: 'completed',
          text: 'Original',
          createdAt: new Date().toISOString()
        }]
      }],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit message' }))
    fireEvent.change(screen.getByRole('textbox', { name: 'Edit message text' }), {
      target: { value: 'Edited retry' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(turnStartAttempts).toBe(1))
    expect(screen.getByRole('textbox', { name: 'Edit message text' })).toHaveValue('Edited retry')

    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(turnStartAttempts).toBe(2))
    expect(appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/rollback')).toHaveLength(1)
    consoleError.mockRestore()
  })

  it('does not expose edit on a history tail hidden by empty completion', () => {
    const createdAt = '2026-01-01T00:00:00Z'
    useConversationStore.setState({
      turns: [{
        id: 'turn-1', threadId: 'thread-1', status: 'completed', startedAt: createdAt,
        items: [
          { id: 'user', type: 'userMessage', status: 'completed', text: 'Generate an image', createdAt },
          { id: 'reply', type: 'agentMessage', status: 'completed', text: 'Image saved.', createdAt }
        ]
      }], turnStatus: 'idle', activeTurnId: null
    })
    renderWithLocale(<MessageStream />)
    expect(screen.getByRole('button', { name: 'Edit message' })).toBeInTheDocument()

    act(() => useConversationStore.getState().setTurns([
      ...useConversationStore.getState().turns,
      {
        id: 'turn-2', threadId: 'thread-1', status: 'completed', startedAt: createdAt,
        items: [{
          id: 'notice', type: 'userMessage', status: 'completed', createdAt,
          deliveryMode: 'queued', triggerKind: 'terminal', triggerStatus: 'completed',
          triggerRefId: 'term-1', text: 'Background command finished'
        }]
      }
    ]))

    expect(screen.queryByRole('button', { name: 'Edit message' })).toBeNull()
    expect(useConversationStore.getState().turns).toHaveLength(2)
    expect(appServerSendRequest).not.toHaveBeenCalled()
  })

  it('prevents stale inline edit from rolling back a newer last turn', async () => {
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [{
          id: 'u1',
          type: 'userMessage',
          status: 'completed',
          text: 'Original',
          createdAt: new Date().toISOString()
        }]
      }],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)
    fireEvent.click(screen.getByRole('button', { name: 'Edit message' }))
    act(() => {
      useConversationStore.getState().setTurns([
        ...useConversationStore.getState().turns,
        {
          id: 'turn-2',
          threadId: 'thread-1',
          status: 'completed',
          startedAt: new Date().toISOString(),
          items: [{
            id: 'u2',
            type: 'userMessage',
            status: 'completed',
            text: 'Newer message',
            createdAt: new Date().toISOString()
          }]
        }
      ])
    })
    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => {
      expect(screen.queryByRole('textbox', { name: 'Edit message text' })).toBeNull()
    })
    expect(appServerSendRequest).not.toHaveBeenCalled()
  })
})
