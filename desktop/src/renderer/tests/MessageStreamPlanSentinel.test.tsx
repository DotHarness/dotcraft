import { StrictMode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { MessageStream } from '../components/conversation/MessageStream'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { ACCEPT_PLAN_SENTINEL_EN } from '../utils/planAcceptSentinel'
import type { ThreadGoal } from '../types/thread'

const appServerSendRequest = vi.fn()

function renderWithLocale(node: JSX.Element): void {
  render(<LocaleProvider>{node}</LocaleProvider>)
}

function makeRunningTurn(): ReturnType<typeof useConversationStore.getState>['turns'][number] {
  return {
    id: 'turn-1',
    threadId: 'thread-1',
    status: 'running',
    startedAt: new Date().toISOString(),
    items: [
      {
        id: 'u1',
        type: 'userMessage',
        status: 'completed',
        text: 'Fetch this page',
        createdAt: new Date().toISOString()
      }
    ]
  }
}

function makeGoal(threadId: string, objective: string, createdAt: string): ThreadGoal {
  return {
    threadId,
    goalId: `goal-${threadId}`,
    objective,
    status: 'active',
    tokenBudget: null,
    tokensUsed: {
      inputTokens: 0,
      outputTokens: 0,
      totalTokens: 0
    },
    timeUsedSeconds: 0,
    createdAt,
    updatedAt: createdAt
  }
}

describe('MessageStream plan-accept sentinel filtering', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    appServerSendRequest.mockResolvedValue({})
    useConversationStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().setWorkspacePath('F:\\dotcraft')
    useThreadStore.setState({
      activeThreadId: 'thread-1',
      activeThread: {
        id: 'thread-1',
        displayName: 'Test thread',
        status: 'active',
        originChannel: 'dotcraft-desktop',
        createdAt: new Date().toISOString(),
        lastActiveAt: new Date().toISOString(),
        workspacePath: 'F:\\dotcraft',
        userId: 'local',
        metadata: {},
        turns: []
      }
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'en' })
        },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
      }
    })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('hides the plan-accept sentinel user message from conversation view', () => {
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: ACCEPT_PLAN_SENTINEL_EN,
            createdAt: new Date().toISOString()
          },
          {
            id: 'a1',
            type: 'agentMessage',
            status: 'completed',
            text: 'Executing accepted plan now.',
            createdAt: new Date().toISOString()
          }
        ]
      }]
    })

    renderWithLocale(<MessageStream />)

    expect(screen.queryByText(ACCEPT_PLAN_SENTINEL_EN)).toBeNull()
    expect(screen.getByText('Executing accepted plan now.')).toBeInTheDocument()
  })

  it('marks the first user-authored objective message as sent as a goal', () => {
    const goalCreatedAt = '2026-04-16T08:00:00.000Z'
    useThreadStore.getState().setThreadGoal(makeGoal('thread-1', 'Build feature', goalCreatedAt))
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: '2026-04-16T08:00:01.000Z',
        completedAt: '2026-04-16T08:00:10.000Z',
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: 'Build feature',
            createdAt: '2026-04-16T08:00:01.000Z'
          },
          {
            id: 'a1',
            type: 'agentMessage',
            status: 'completed',
            text: 'Working on it.',
            createdAt: '2026-04-16T08:00:02.000Z'
          }
        ]
      }]
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByText('Build feature')).toBeInTheDocument()
    expect(screen.getByText('Sent as goal')).toBeInTheDocument()
    expect(screen.queryByText('Goal auto-continue')).toBeNull()
  })

  it('renders guidance user messages inline instead of grouping them with the initial request', () => {
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'running',
        startedAt: '2026-04-25T10:00:00.000Z',
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: 'Initial request',
            createdAt: '2026-04-25T10:00:00.000Z'
          },
          {
            id: 'tool-1',
            type: 'toolCall',
            status: 'completed',
            toolName: 'FollowupTool',
            toolCallId: 'call-1',
            arguments: {},
            success: true,
            createdAt: '2026-04-25T10:00:01.000Z'
          },
          {
            id: 'u-guidance',
            type: 'userMessage',
            status: 'completed',
            deliveryMode: 'guidance',
            text: 'Guidance request',
            createdAt: '2026-04-25T10:00:02.000Z'
          },
          {
            id: 'a1',
            type: 'agentMessage',
            status: 'completed',
            text: 'Assistant after guidance',
            createdAt: '2026-04-25T10:00:03.000Z'
          }
        ]
      }],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now()
    })

    renderWithLocale(<MessageStream />)

    const text = screen.getByTestId('message-stream').textContent ?? ''
    const initialIndex = text.indexOf('Initial request')
    const toolIndex = text.indexOf('Called FollowupTool')
    const guidanceIndex = text.indexOf('Guidance request')
    const assistantIndex = text.indexOf('Assistant after guidance')

    expect(initialIndex).toBeGreaterThan(-1)
    expect(toolIndex).toBeGreaterThan(-1)
    expect(guidanceIndex).toBeGreaterThan(-1)
    expect(assistantIndex).toBeGreaterThan(-1)
    expect(initialIndex).toBeLessThan(toolIndex)
    expect(toolIndex).toBeLessThan(guidanceIndex)
    expect(guidanceIndex).toBeLessThan(assistantIndex)
  })

  it('keeps running tool auto-scroll stable under StrictMode streaming updates', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    useConversationStore.setState({
      turns: [makeRunningTurn()],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now()
    })

    renderWithLocale(
      <StrictMode>
        <MessageStream />
      </StrictMode>
    )

    const stream = screen.getByTestId('message-stream')
    Object.defineProperty(stream, 'clientHeight', { configurable: true, value: 100 })
    Object.defineProperty(stream, 'scrollHeight', { configurable: true, value: 300 })
    stream.scrollTop = 180

    act(() => {
      fireEvent.scroll(stream)
      useConversationStore.getState().onToolCallArgumentsDelta({
        threadId: 'thread-1',
        turnId: 'turn-1',
        itemId: 'tool-1',
        toolName: 'WebFetch',
        callId: 'webfetch-1',
        delta: '{"url":"https://dotcraft.ai"'
      })
    })

    expect(screen.getByText('Fetching https://dotcraft.ai...')).toHaveClass('tool-running-gradient-text')
    const maxDepthErrors = consoleError.mock.calls.filter((call) =>
      call.some((part) => String(part).includes('Maximum update depth exceeded'))
    )
    expect(maxDepthErrors).toHaveLength(0)
    consoleError.mockRestore()
  })

  it('shows a Thinking fallback for a silent active running turn', () => {
    useConversationStore.setState({
      turns: [makeRunningTurn()],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now()
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByText('Thinking...')).toHaveClass('tool-running-gradient-text')
  })

  it('shows and clears the Thinking fallback when streaming assistant text stalls then resumes', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-05-22T10:00:00.000Z'))
    const lastDeltaAt = Date.now()

    useConversationStore.setState({
      turns: [{
        ...makeRunningTurn(),
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: 'Explain this',
            createdAt: '2026-05-22T09:59:59.000Z'
          },
          {
            id: 'assistant-live',
            type: 'agentMessage',
            status: 'streaming',
            text: '',
            createdAt: '2026-05-22T10:00:00.000Z'
          }
        ]
      }],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      activeItemId: 'assistant-live',
      streamingMessage: 'Partial answer',
      streamingMessageLastDeltaAt: lastDeltaAt,
      turnStartedAt: lastDeltaAt
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByText('Partial answer')).toBeInTheDocument()
    expect(screen.queryByText('Thinking...')).toBeNull()

    act(() => {
      vi.advanceTimersByTime(2000)
    })

    expect(screen.getByText('Thinking...')).toHaveClass('tool-running-gradient-text')
    const stalledText = screen.getByTestId('message-stream').textContent ?? ''
    expect(stalledText.indexOf('Partial answer')).toBeLessThan(stalledText.indexOf('Thinking...'))

    act(() => {
      useConversationStore.setState({
        streamingMessage: 'Partial answer continued',
        streamingMessageLastDeltaAt: Date.now()
      })
    })

    expect(screen.queryByText('Thinking...')).toBeNull()
    expect(screen.getByText('Partial answer continued')).toBeInTheDocument()
  })

  it('keeps RequestUserInput tool rows visually running while waiting for an answer', () => {
    useConversationStore.setState({
      turns: [{
        ...makeRunningTurn(),
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: 'Ask me',
            createdAt: new Date().toISOString()
          },
          {
            id: 'request-user-input-tool',
            type: 'toolCall',
            status: 'completed',
            toolName: 'RequestUserInput',
            toolCallId: 'call-question',
            arguments: {
              questions: [
                {
                  id: 'mode',
                  question: 'Which mode should DotCraft use?',
                  options: [{ label: 'Auto' }, { label: 'Manual' }]
                }
              ]
            },
            createdAt: '2026-04-18T11:12:02.000Z'
          }
        ]
      }],
      turnStatus: 'waitingInput',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now()
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByText('Asking questions')).toHaveClass('tool-running-gradient-text')
    expect(screen.queryByText('Thinking...')).toBeNull()
  })

  it('renders pending approvals as a lightweight running status while the composer handles decisions', async () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'zh-Hans' })
        },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
      }
    })
    useConversationStore.setState({
      turns: [{
        ...makeRunningTurn(),
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: 'Run tests',
            createdAt: new Date().toISOString()
          },
          {
            id: 'approval-1',
            type: 'approvalCard',
            status: 'completed',
            approvalType: 'shell',
            approvalOperation: 'npm test',
            approvalTarget: 'F:\\dotcraft',
            approvalState: 'pending',
            createdAt: '2026-04-18T11:12:02.000Z'
          }
        ]
      }],
      turnStatus: 'waitingApproval',
      activeTurnId: 'turn-1',
      pendingApproval: {
        bridgeId: 'bridge-approval',
        itemId: 'approval-1',
        approvalType: 'shell',
        operation: 'npm test',
        target: 'F:\\dotcraft',
        reason: 'DotCraft wants to run tests.'
      },
      turnStartedAt: Date.now()
    })

    renderWithLocale(<MessageStream />)

    expect(await screen.findByText('等待审批')).toHaveClass('tool-running-gradient-text')
    expect(screen.queryByText('正在思考...')).toBeNull()
    expect(screen.queryByText('Approval Required')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Accept' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Decline' })).not.toBeInTheDocument()
  })

  it('localizes resolved approval labels in zh-Hans', async () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: async () => ({ locale: 'zh-Hans' })
        },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { readImageAsDataUrl: vi.fn().mockResolvedValue({ dataUrl: '' }) }
      }
    })
    useConversationStore.setState({
      turns: [{
        id: 'turn-approval-resolved',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [
          {
            id: 'approval-resolved',
            type: 'approvalCard',
            status: 'completed',
            approvalType: 'shell',
            approvalOperation: 'npm test',
            approvalTarget: 'F:\\dotcraft',
            approvalState: 'acceptedForSession',
            createdAt: '2026-04-18T11:12:02.000Z'
          }
        ]
      }],
      turnStatus: 'idle',
      activeTurnId: null,
      pendingApproval: null
    })

    renderWithLocale(<MessageStream />)

    expect(await screen.findByText('本会话已允许')).toBeInTheDocument()
    expect(screen.queryByText('Accepted for session')).not.toBeInTheDocument()
  })

  it('renders and clears the transient system status divider', async () => {
    useConversationStore.setState({
      turns: [makeRunningTurn()],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now(),
      systemLabel: 'systemStatus.compacting'
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByRole('status', { name: 'Auto-compacting context' })).toBeInTheDocument()
    expect(screen.getByText('Auto-compacting context')).toHaveClass('tool-running-gradient-text')
    expect(screen.queryByText('Thinking...')).toBeNull()

    act(() => {
      useConversationStore.setState({ systemLabel: null })
    })

    await waitFor(() => {
      expect(screen.queryByRole('status', { name: 'Auto-compacting context' })).toBeNull()
    })
  })

  it('renders non-blocking background memory status with the same gradient divider', () => {
    useConversationStore.setState({
      turns: [makeRunningTurn()],
      turnStatus: 'idle',
      activeTurnId: null,
      systemLabel: null,
      backgroundMemoryStatus: 'consolidating',
      maintenanceKind: null
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByRole('status', { name: 'Consolidating memory' })).toBeInTheDocument()
    expect(screen.getByText('Consolidating memory')).toHaveClass('tool-running-gradient-text')
  })

  it('renders a persistent memory consolidation notice divider', () => {
    useConversationStore.setState({
      turns: [{
        id: 'turn-1',
        threadId: 'thread-1',
        status: 'completed',
        startedAt: new Date().toISOString(),
        completedAt: new Date().toISOString(),
        items: [
          {
            id: 'u1',
            type: 'userMessage',
            status: 'completed',
            text: 'Remember this preference',
            createdAt: new Date().toISOString()
          },
          {
            id: 'notice-memory',
            type: 'systemNotice',
            status: 'completed',
            createdAt: new Date().toISOString(),
            completedAt: new Date().toISOString(),
            systemNotice: { kind: 'memoryConsolidated' }
          }
        ]
      }],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)

    expect(screen.getByRole('separator', { name: 'Long-term memory updated' })).toBeInTheDocument()
    expect(screen.getByText('Long-term memory updated')).toBeInTheDocument()
  })

  it('shows the inline edit affordance only on the last completed text-only user message', () => {
    useConversationStore.setState({
      turns: [
        {
          id: 'turn-1',
          threadId: 'thread-1',
          status: 'completed',
          startedAt: new Date().toISOString(),
          completedAt: new Date().toISOString(),
          items: [
            {
              id: 'u1',
              type: 'userMessage',
              status: 'completed',
              text: 'Earlier message',
              createdAt: new Date().toISOString()
            }
          ]
        },
        {
          id: 'turn-2',
          threadId: 'thread-1',
          status: 'completed',
          startedAt: new Date().toISOString(),
          completedAt: new Date().toISOString(),
          items: [
            {
              id: 'u2',
              type: 'userMessage',
              status: 'completed',
              text: 'Retry this one',
              nativeInputParts: [{ type: 'text', text: 'Retry this one' }],
              createdAt: new Date().toISOString()
            }
          ]
        }
      ],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)

    const buttons = screen.getAllByRole('button', { name: 'Edit message' })
    expect(buttons).toHaveLength(1)
    fireEvent.click(buttons[0])
    const editTextarea = screen.getByRole('textbox', { name: 'Edit message text' })
    expect(editTextarea).toHaveValue('Retry this one')
    const editingMessageContainer = editTextarea.parentElement?.parentElement
    expect(editingMessageContainer?.getAttribute('style')).toContain(
      'width: min(100%, var(--conversation-reading-width))'
    )
    expect(editingMessageContainer?.getAttribute('style')).toContain(
      'max-width: var(--conversation-reading-width)'
    )
    expect(screen.queryByText('Earlier message')).toBeInTheDocument()
  })

  it('hides the edit affordance while the last turn is active', () => {
    useConversationStore.setState({
      turns: [makeRunningTurn()],
      turnStatus: 'running',
      activeTurnId: 'turn-1',
      turnStartedAt: Date.now()
    })

    renderWithLocale(<MessageStream />)

    expect(screen.queryByRole('button', { name: 'Edit message' })).toBeNull()
  })

  it('hides the edit affordance for image or non-text native input messages', () => {
    useConversationStore.setState({
      turns: [
        {
          id: 'turn-1',
          threadId: 'thread-1',
          status: 'completed',
          startedAt: new Date().toISOString(),
          completedAt: new Date().toISOString(),
          items: [
            {
              id: 'u1',
              type: 'userMessage',
              status: 'completed',
              text: 'Has an image',
              imageDataUrls: ['data:image/png;base64,abc'],
              createdAt: new Date().toISOString()
            }
          ]
        },
        {
          id: 'turn-2',
          threadId: 'thread-1',
          status: 'completed',
          startedAt: new Date().toISOString(),
          completedAt: new Date().toISOString(),
          items: [
            {
              id: 'u2',
              type: 'userMessage',
              status: 'completed',
              text: '@src/App.tsx',
              nativeInputParts: [{ type: 'fileRef', path: 'src/App.tsx' }],
              createdAt: new Date().toISOString()
            }
          ]
        }
      ],
      turnStatus: 'idle',
      activeTurnId: null
    })

    renderWithLocale(<MessageStream />)

    expect(screen.queryByRole('button', { name: 'Edit message' })).toBeNull()
  })

  it('cancels inline editing without calling AppServer', () => {
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
      target: { value: 'Changed' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Cancel' }))

    expect(screen.queryByRole('textbox', { name: 'Edit message text' })).toBeNull()
    expect(screen.getByText('Original')).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalled()
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
            workspacePath: 'F:\\dotcraft',
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

    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledTimes(2))
    expect(appServerSendRequest.mock.calls[0][0]).toBe('thread/rollback')
    expect(appServerSendRequest.mock.calls[0][1]).toMatchObject({ threadId: 'thread-1', numTurns: 1 })
    expect(appServerSendRequest.mock.calls[1][0]).toBe('turn/start')
    expect(appServerSendRequest.mock.calls[1][1]).toMatchObject({
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
            workspacePath: 'F:\\dotcraft',
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

    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledTimes(2))
    expect(screen.getByRole('textbox', { name: 'Edit message text' })).toHaveValue('Edited retry')

    fireEvent.click(screen.getByRole('button', { name: 'Send' }))

    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledTimes(3))
    expect(appServerSendRequest.mock.calls.map((call) => call[0])).toEqual([
      'thread/rollback',
      'turn/start',
      'turn/start'
    ])
    consoleError.mockRestore()
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
