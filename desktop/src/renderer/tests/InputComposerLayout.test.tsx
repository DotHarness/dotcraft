import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { InputComposer } from '../components/conversation/InputComposer'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useGitStore } from '../stores/gitStore'
import type { ConversationTurn } from '../types/conversation'

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})

const settingsGet = vi.fn()
const appServerSendRequest = vi.fn()

function renderComposer(extraProps: Partial<ComponentProps<typeof InputComposer>> = {}): void {
  render(
    <LocaleProvider>
      <InputComposer
        threadId="thread-1"
        workspacePath="C:\\sample\\workspace"
        modelName="gpt-5.4"
        modelOptions={['gpt-5.4', 'gpt-5.4-mini']}
        {...extraProps}
      />
    </LocaleProvider>
  )
}

function setCaretToEnd(element: HTMLElement): void {
  const selection = window.getSelection()
  if (!selection) return
  const range = document.createRange()
  range.selectNodeContents(element)
  range.collapse(false)
  selection.removeAllRanges()
  selection.addRange(range)
}

function userTurn(id: string, text: string): ConversationTurn {
  return {
    id,
    threadId: 'thread-1',
    status: 'completed',
    startedAt: new Date().toISOString(),
    completedAt: new Date().toISOString(),
    items: [
      {
        id: `${id}-user`,
        type: 'userMessage',
        status: 'completed',
        text,
        createdAt: new Date().toISOString(),
        completedAt: new Date().toISOString()
      }
    ]
  }
}

describe('InputComposer layout', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    appServerSendRequest.mockResolvedValue({})

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        appServer: { sendRequest: appServerSendRequest },
        workspace: { saveImageToTemp: vi.fn() }
      }
    })

    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    useGitStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({
      activeMainView: 'conversation',
      automationsTab: 'tasks',
      sidebarCollapsed: false,
      sidebarWidth: 240,
      detailPanelVisible: true,
      detailPanelWidth: 400,
      activeDetailTab: 'changes',
      selectedChangedFile: null,
      autoShowTriggeredForTurn: null,
      composerPrefill: null,
      pendingWelcomeTurn: null,
      _pendingWelcomeTimer: null
    })
    useThreadStore.setState({
      threadList: [
        {
          id: 'thread-1',
          displayName: 'Layout test',
          status: 'active',
          originChannel: 'dotcraft-desktop',
          createdAt: new Date().toISOString(),
          lastActiveAt: new Date().toISOString()
        }
      ]
    })
  })

  it('renders plan mode as an active-only label and exposes the mode switch from the attachment menu', async () => {
    renderComposer()

    screen.getByRole('textbox')
    expect(screen.queryByRole('button', { name: 'Agent' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Plan' })).toBeNull()

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    const planModeMenuItem = screen.getByRole('menuitemcheckbox', { name: 'Plan mode' })
    expect(planModeMenuItem).toHaveAttribute('aria-checked', 'false')
    fireEvent.click(planModeMenuItem)

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Disable plan mode' })).toBeInTheDocument()
      expect(screen.getByText('Plan')).toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'plan'
      })
    })
    expect(screen.getByRole('menuitemcheckbox', { name: 'Plan mode' })).toHaveAttribute('aria-checked', 'true')
    expect(Boolean(
      screen.getByRole('button', { name: 'Add attachment' })
        .compareDocumentPosition(screen.getByTestId('approval-policy-trigger')) & Node.DOCUMENT_POSITION_FOLLOWING
    )).toBe(true)
    expect(Boolean(
      screen.getByTestId('approval-policy-trigger')
        .compareDocumentPosition(screen.getByRole('button', { name: 'Disable plan mode' })) & Node.DOCUMENT_POSITION_FOLLOWING
    )).toBe(true)

    fireEvent.click(screen.getByRole('button', { name: 'Disable plan mode' }))
    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Disable plan mode' })).not.toBeInTheDocument()
      expect(appServerSendRequest).toHaveBeenLastCalledWith('thread/mode/set', {
        threadId: 'thread-1',
        mode: 'agent'
      })
    })

    const modelButton = screen.getByRole('button', { name: 'Select model' })
    fireEvent.focus(modelButton)
    const tooltip = screen.getByRole('tooltip')
    expect(within(tooltip).getByText('Select model')).toBeInTheDocument()
    expect(within(tooltip).getByText('Ctrl')).toBeInTheDocument()
    expect(within(tooltip).getByText('Shift')).toBeInTheDocument()
    expect(within(tooltip).getByText('M')).toBeInTheDocument()

    fireEvent.keyDown(window, { key: 'M', ctrlKey: true, shiftKey: true })
    const menu = screen.getByRole('menu', { name: 'Select model' })
    fireEvent.click(within(menu).getByRole('menuitem', { name: /Model/ }))
    const listbox = screen.getByRole('listbox', { name: 'Model' })

    expect(listbox).toBeInTheDocument()
    expect(within(listbox).getByRole('option', { name: 'gpt-5.4-mini' })).toBeInTheDocument()
  })

  it('keeps send button available alongside the inline toolbar', () => {
    renderComposer()

    const sendButton = screen.getByRole('button', { name: 'Send message' })

    expect(sendButton).toBeInTheDocument()
  })

  it('disables plan and agent mode controls in the agent builder variant', async () => {
    const onBeforeSend = vi.fn().mockResolvedValue(undefined)

    renderComposer({ variant: 'agentBuilder', minimalChrome: true, onBeforeSend })

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    expect(screen.queryByRole('menuitemcheckbox', { name: 'Plan mode' })).toBeNull()

    const textbox = screen.getByRole('textbox')
    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/mode/set')).toBe(false)

    textbox.textContent = '/plan'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(onBeforeSend).toHaveBeenCalledTimes(1)
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
        threadId: 'thread-1'
      }))
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/mode/set')).toBe(false)
  })

  it('submits detached agent builder intro input through the override with serialized input parts', async () => {
    const submitOverride = vi.fn().mockResolvedValue(undefined)

    renderComposer({
      threadId: 'agent-builder-intro',
      variant: 'agentBuilder',
      minimalChrome: true,
      placeholder: 'Describe the agent you want...',
      submitOverride
    })

    const textbox = screen.getByRole('textbox')
    expect(textbox).toHaveAttribute('aria-placeholder', 'Describe the agent you want...')

    textbox.textContent = 'Build a support triage agent'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(submitOverride).toHaveBeenCalledWith(expect.objectContaining({
        text: 'Build a support triage agent',
        inputParts: [{ type: 'text', text: 'Build a support triage agent' }]
      }))
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/start')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'turn/enqueue')).toBe(false)
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/mode/set')).toBe(false)
  })

  it('localizes the plan mode label and attachment menu switch', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    useConversationStore.setState({ threadMode: 'plan' })

    renderComposer()

    expect(await screen.findByRole('button', { name: '关闭计划模式' })).toBeInTheDocument()
    expect(screen.getByText('计划')).toBeInTheDocument()

    fireEvent.click(await screen.findByRole('button', { name: '添加附件' }))
    expect(screen.getByRole('menuitemcheckbox', { name: '计划模式' })).toHaveAttribute('aria-checked', 'true')
  })

  it('renders the SubAgent dock as a responsive attached accessory above the composer surface', () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'subagent/children/list') {
        return {
          data: [
            {
              edge: {
                parentThreadId: 'thread-1',
                childThreadId: 'child-1',
                agentNickname: 'Lovelace',
                profileName: 'native',
                runtimeType: 'native',
                supportsSendInput: true,
                supportsResume: true,
                supportsClose: true,
                status: 'open'
              },
              thread: {
                id: 'child-1',
                displayName: 'Lovelace',
                status: 'active',
                originChannel: 'subagent',
                createdAt: new Date().toISOString(),
                lastActiveAt: new Date().toISOString(),
                runtime: {
                  running: true,
                  waitingOnApproval: false,
                  waitingOnPlanConfirmation: false
                }
              }
            }
          ]
        }
      }
      return {}
    })

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    const overlay = screen.getByTestId('composer-top-accessory-overlay')
    const shell = overlay.parentElement
    const composerLayer = overlay.nextElementSibling

    expect(overlay).toContainElement(dock)
    expect(composerLayer).toContainElement(screen.getByRole('textbox'))
    expect(shell).toContainElement(overlay)
    expect(dock.parentElement).toBe(overlay)
  })

  it('renders queued messages inside the background activity dock with neutral drag handles', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'first queued follow-up',
          status: 'queued',
          createdAt: new Date().toISOString()
        },
        {
          id: 'queued-2',
          threadId: 'thread-1',
          displayText: 'second queued follow-up',
          status: 'guidancePending',
          createdAt: new Date().toISOString()
        }
      ]
    })

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    expect(screen.getByText('2 queued messages')).toBeInTheDocument()
    expect(within(dock).getByText('first queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByText('second queued follow-up')).toBeInTheDocument()
    expect(within(dock).getAllByRole('button', { name: 'Reorder queued message' })).toHaveLength(2)
    expect(within(dock).getByRole('button', { name: 'Steering' })).toBeDisabled()
    expect(within(dock).getAllByRole('button', { name: 'Reorder queued message' })[1]).toBeDisabled()
  })

  it('separates queued messages from background agents inside one dock', () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'queued follow-up',
          status: 'queued',
          createdAt: new Date().toISOString()
        }
      ]
    })
    useSubAgentStore.getState().setChildren('thread-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'thread-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    renderComposer()

    const dock = screen.getByTestId('subagent-dock')
    expect(within(dock).getByText('1 background agents')).toBeInTheDocument()
    expect(within(dock).getByText('queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByRole('button', { name: 'Expand background agents' })).toBeInTheDocument()

    fireEvent.click(within(dock).getByRole('button', { name: 'Expand background agents' }))

    expect(within(dock).getByText('queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByRole('button', { name: 'Collapse background agents' })).toBeInTheDocument()

    fireEvent.click(within(dock).getByRole('button', { name: 'Collapse background agents' }))

    expect(within(dock).getByText('queued follow-up')).toBeInTheDocument()
    expect(within(dock).getByRole('button', { name: 'Expand background agents' })).toBeInTheDocument()
  })

  it('removes queued messages through the dock action', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'remove this follow-up',
          status: 'queued',
          createdAt: new Date().toISOString()
        }
      ]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/queue/remove') {
        return { queuedInputs: [] }
      }
      return {}
    })

    renderComposer()

    fireEvent.click(screen.getByRole('button', { name: 'Remove' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/queue/remove', {
        threadId: 'thread-1',
        queuedInputId: 'queued-1'
      })
      expect(screen.queryByText('remove this follow-up')).not.toBeInTheDocument()
    })
  })

  it('calls turn queue reorder when queued message order changes', async () => {
    const queued = [
      {
        id: 'queued-1',
        threadId: 'thread-1',
        displayText: 'first',
        status: 'queued',
        createdAt: new Date().toISOString()
      },
      {
        id: 'queued-2',
        threadId: 'thread-1',
        displayText: 'second',
        status: 'queued',
        createdAt: new Date().toISOString()
      }
    ]
    useConversationStore.setState({ queuedInputs: queued })
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'turn/queue/reorder') {
        const ids = params?.orderedQueuedInputIds as string[]
        return {
          queuedInputs: ids.map((id) => queued.find((item) => item.id === id))
        }
      }
      return {}
    })

    renderComposer()

    const handles = screen.getAllByRole('button', { name: 'Reorder queued message' })
    handles[1].focus()
    fireEvent.keyDown(handles[1], { key: 'ArrowUp', code: 'ArrowUp' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/queue/reorder', {
        threadId: 'thread-1',
        orderedQueuedInputIds: ['queued-2', 'queued-1']
      })
    })
  })

  it('rolls back queued message order when reorder fails', async () => {
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: 'first',
          status: 'queued',
          createdAt: new Date().toISOString()
        },
        {
          id: 'queued-2',
          threadId: 'thread-1',
          displayText: 'second',
          status: 'queued',
          createdAt: new Date().toISOString()
        }
      ]
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'turn/queue/reorder') {
        throw new Error('stale queue')
      }
      return {}
    })

    renderComposer()

    const handles = screen.getAllByRole('button', { name: 'Reorder queued message' })
    fireEvent.keyDown(handles[1], { key: 'ArrowUp', code: 'ArrowUp' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) =>
        toast.message === 'Failed to reorder queued messages: stale queue'
      )).toBe(true)
    })
    expect(useConversationStore.getState().queuedInputs.map((item) => item.id)).toEqual(['queued-1', 'queued-2'])
  })

  it('keeps the context usage ring aligned to the model picker height with a smaller donut', () => {
    useConversationStore.getState().setContextUsage({
      tokens: 2500,
      contextWindow: 10000,
      autoCompactThreshold: 8000,
      warningThreshold: 7000,
      errorThreshold: 9000,
      percentLeft: 0.75
    })

    renderComposer()

    const ring = screen.getByRole('img', { name: 'Context usage: 25% used' })

    expect(ring).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Select model' })).toBeInTheDocument()
  })

  it('shows ChatGPT subscription usage when the default model catalog resolves to an OAuth provider', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      providerId: 'openai',
      requestedProviderId: null,
      modelOptions: ['gpt-5.5'],
      models: [{ id: 'gpt-5.5' }],
      modelListUnsupportedEndpoint: false,
      errorCode: null,
      errorMessage: null
    })
    useConnectionStore.setState({
      capabilities: {
        providerManagement: true
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'provider/list') {
        return {
          providers: [
            {
              id: 'openai',
              displayName: 'OpenAI (ChatGPT)',
              protocol: 'openai-responses',
              authMethod: 'chatgptOAuth',
              chatGptAccountId: 'acct_1234567890',
              chatGptPlanType: 'plus'
            }
          ]
        }
      }
      if (method === 'auth/openai/usage') {
        return {
          available: true,
          planType: 'plus',
          primary: {
            usedPercent: 4,
            windowSeconds: 18_000,
            resetAt: '2099-01-01T00:00:00.000Z'
          },
          secondary: {
            usedPercent: 24,
            windowSeconds: 604_800,
            resetAt: '2099-01-07T00:00:00.000Z'
          },
          fetchedAt: '2026-05-25T12:00:00.000Z'
        }
      }
      return {}
    })

    renderComposer()

    const badge = await screen.findByRole('button', { name: /ChatGPT.*96% left in the 5h window.*76% left this week/i })
    expect(badge).not.toHaveAttribute('title')
    expect(badge.querySelector('img')).toBeNull()
    expect(badge.querySelector('svg[data-provider-mark="openai"]')).toBeInTheDocument()
    expect(screen.queryByText('96% 5h')).toBeNull()
    expect(screen.queryByText('76% wk')).toBeNull()

    fireEvent.mouseEnter(badge.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('96% 5h, 76% wk')

    fireEvent.click(badge)

    expect(screen.queryByRole('tooltip')).not.toBeInTheDocument()
    expect(screen.getByRole('dialog', { name: 'ChatGPT' })).toBeInTheDocument()
    expect(screen.getByText('96% left')).toBeInTheDocument()
    expect(screen.getByText('76% left')).toBeInTheDocument()
  })

  it('matches the running stop button to the enabled send button style and shows Esc as a shortcut keycap', async () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })

    renderComposer()

    const stopButton = screen.getByRole('button', { name: 'Stop turn' })

    expect(stopButton).toBeInTheDocument()

    fireEvent.mouseEnter(stopButton.parentElement as HTMLElement)
    const tooltip = await screen.findByRole('tooltip')

    expect(within(tooltip).getByText('Stop')).toBeInTheDocument()
    expect(within(tooltip).getByText('Esc')).toBeInTheDocument()
    expect(tooltip).not.toHaveTextContent('Stop (Esc)')
  })

  it('shows the queued send action instead of stop while running with draft text', () => {
    useConversationStore.setState({
      turnStatus: 'running',
      activeTurnId: 'turn-123'
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'follow up'
    fireEvent.input(textbox)

    expect(screen.getByRole('button', { name: 'Queue message' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Stop turn' })).toBeNull()
  })

  it('queues Enter submissions while thread maintenance is active', async () => {
    useConversationStore.setState({
      turnStatus: 'idle',
      activeTurnId: null,
      maintenanceKind: 'consolidating'
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'next while memory is consolidating'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/enqueue', expect.objectContaining({
        threadId: 'thread-1'
      }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('sends directly while background memory consolidation is active', async () => {
    useConversationStore.setState({
      turnStatus: 'idle',
      activeTurnId: null,
      maintenanceKind: null,
      backgroundMemoryStatus: 'consolidating'
    })

    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'next while memory is consolidating'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter', code: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('turn/start', expect.objectContaining({
        threadId: 'thread-1'
      }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/enqueue', expect.anything())
  })

  it('cycles recent user messages with ArrowUp and ArrowDown while preserving the current draft', () => {
    useConversationStore.setState({
      turns: [
        userTurn('turn-old', 'older request'),
        userTurn('turn-new', 'newer request')
      ]
    })
    renderComposer()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'current draft'
    fireEvent.input(textbox)
    setCaretToEnd(textbox)

    fireEvent.keyDown(textbox, { key: 'ArrowUp', code: 'ArrowUp' })
    expect(textbox.textContent).toBe('newer request')

    fireEvent.keyDown(textbox, { key: 'ArrowUp', code: 'ArrowUp' })
    expect(textbox.textContent).toBe('older request')

    fireEvent.keyDown(textbox, { key: 'ArrowDown', code: 'ArrowDown' })
    expect(textbox.textContent).toBe('newer request')

    fireEvent.keyDown(textbox, { key: 'ArrowDown', code: 'ArrowDown' })
    expect(textbox.textContent).toBe('current draft')
  })

  it('uses maintenance interrupt for an empty busy composer without an active turn', async () => {
    useConversationStore.setState({
      turnStatus: 'idle',
      activeTurnId: null,
      maintenanceKind: 'consolidating'
    })

    renderComposer()

    fireEvent.click(screen.getByRole('button', { name: 'Stop turn' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/maintenance/interrupt', {
        threadId: 'thread-1'
      })
    })
  })

  it('summarizes queued non-text inputs with localized labels', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans' })
    useConversationStore.setState({
      queuedInputs: [
        {
          id: 'queued-1',
          threadId: 'thread-1',
          displayText: '',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: [
            { type: 'fileRef', path: 'docs/a.md' },
            { type: 'fileRef', path: 'docs/b.md' },
            { type: 'localImage', path: 'C:\\temp\\diagram.png' }
          ]
        },
        {
          id: 'queued-2',
          threadId: 'thread-1',
          displayText: '',
          status: 'queued',
          createdAt: new Date().toISOString(),
          nativeInputParts: []
        }
      ]
    })

    renderComposer()

    await waitFor(() => {
      expect(screen.getByText('2 个文件, 1 张图片')).toBeInTheDocument()
    })
    expect(screen.getByText('已排队消息')).toBeInTheDocument()
  })

  it('does not enter edit mode or expose an edit cancel action from composer', () => {
    renderComposer()

    expect((window as Window & { __inputComposerEditLastMessage?: unknown }).__inputComposerEditLastMessage).toBeUndefined()
    expect(screen.queryByRole('button', { name: 'Cancel' })).toBeNull()
  })
})
