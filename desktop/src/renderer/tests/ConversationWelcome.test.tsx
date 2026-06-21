import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { COMMAND_REF_CLASS, FILE_REF_CLASS, SKILL_REF_CLASS } from '../components/conversation/richInputConstants'
import { useConnectionStore } from '../stores/connectionStore'
import { useGitStore } from '../stores/gitStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useToastStore } from '../stores/toastStore'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useConversationStore } from '../stores/conversationStore'
import type { ThreadGoal } from '../types/thread'
import type { WorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'

const fileReadFile = vi.fn()
const appServerSendRequest = vi.fn()
const workspaceConfigGetCore = vi.fn()
const saveImageToTemp = vi.fn()
const pickFiles = vi.fn()
const getPathForFile = vi.fn((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')
const settingsGet = vi.fn()
const shellOpenExternal = vi.fn()
const shellGetProtocolHandlerName = vi.fn()
const gitListBranches = vi.fn()
const gitCheckoutBranch = vi.fn()
const gitCreateAndCheckoutBranch = vi.fn()

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function linearizeSelection(root: Node): string {
  let out = ''
  const walk = (node: Node): void => {
    if (node.nodeType === Node.TEXT_NODE) {
      out += node.textContent ?? ''
      return
    }
    if (node.nodeType !== Node.ELEMENT_NODE) return
    const el = node as HTMLElement
    if (
      el.classList.contains(FILE_REF_CLASS) ||
      el.classList.contains(COMMAND_REF_CLASS) ||
      el.classList.contains(SKILL_REF_CLASS) ||
      el.tagName === 'BR'
    ) {
      out += ' '
      return
    }
    for (const child of Array.from(node.childNodes)) {
      walk(child)
    }
  }
  walk(root)
  return out
}

function getTextboxSelection(textbox: HTMLElement): { start: number; end: number } | null {
  const selection = window.getSelection()
  if (!selection || selection.rangeCount === 0) return null
  const range = selection.getRangeAt(0)
  if (!textbox.contains(range.startContainer) || !textbox.contains(range.endContainer)) return null

  const startRange = document.createRange()
  startRange.selectNodeContents(textbox)
  startRange.setEnd(range.startContainer, range.startOffset)
  const endRange = document.createRange()
  endRange.selectNodeContents(textbox)
  endRange.setEnd(range.endContainer, range.endOffset)

  const startContainer = document.createElement('div')
  startContainer.appendChild(startRange.cloneContents())
  const endContainer = document.createElement('div')
  endContainer.appendChild(endRange.cloneContents())

  return {
    start: linearizeSelection(startContainer).length,
    end: linearizeSelection(endContainer).length
  }
}

function setTextboxCaret(textbox: HTMLElement, offset: number): void {
  const textNode = textbox.firstChild
  if (!textNode) throw new Error('textbox has no text node')
  const range = document.createRange()
  range.setStart(textNode, offset)
  range.setEnd(textNode, offset)
  const selection = window.getSelection()
  selection?.removeAllRanges()
  selection?.addRange(range)
}

function renderWelcome({
  workspaceConfigChange = null,
  workspaceConfigChangeSeq = 0,
  remoteWorkspace = false
}: {
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
  remoteWorkspace?: boolean
} = {}) {
  return render(
    <LocaleProvider>
      <ConversationWelcome
        workspacePath="F:\\dotcraft"
        remoteWorkspace={remoteWorkspace}
        workspaceConfigChange={workspaceConfigChange}
        workspaceConfigChangeSeq={workspaceConfigChangeSeq}
      />
    </LocaleProvider>
  )
}

function makeGoal(threadId = 'thread-welcome', objective = 'Build feature'): ThreadGoal {
  return {
    threadId,
    objective,
    status: 'active',
    tokenBudget: null,
    tokensUsed: 0,
    timeUsedSeconds: 0,
    createdAt: 1704067200,
    updatedAt: 1704067200
  }
}

function agentTeamsWelcomeApp(overrides: Record<string, unknown> = {}) {
  return {
    appId: 'com.dotharness.dotcraft-teams',
    toolNamespace: 'teams',
    displayName: 'Agent Teams',
    developerName: 'DotHarness',
    description: 'Create Team missions',
    pluginId: 'agent-teams',
    installed: true,
    enabled: true,
    catalogVisible: true,
    managed: true,
    requiresExternalConnection: false,
    connectionState: 'connected',
    nativeApp: { displayName: 'Agent Teams', protocol: '', status: 'installed' },
    scopes: [{ id: 'mission.manage', displayName: 'Create Team missions', description: 'Create Team missions', risk: 'mutate' }],
    toolCatalog: [{ name: 'CreateTeam', scope: 'mission.manage', risk: 'mutate', defaultExposure: 'direct' }],
    ...overrides
  }
}

describe('ConversationWelcome composer', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().reset()
    useConversationStore.setState({ remoteWorkspaceActive: false })
    useModelCatalogStore.getState().reset()
    useProvidersStore.getState().reset()
    useAppBindingStore.getState().reset()
    useToastStore.setState({ toasts: [] })
    useSkillsStore.setState({
      skills: [],
      loading: false,
      error: null,
      selectedSkillName: null,
      skillContent: null,
      contentLoading: false
    })
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
      autoShowPlanForItem: null,
      composerPrefill: null,
      pendingWelcomeTurn: null,
      welcomeDraft: null,
      _pendingWelcomeTimer: null
    })

    useConnectionStore.setState({
      status: 'connected',
      serverInfo: null,
      dashboardUrl: null,
      errorMessage: null,
      errorType: null,
      binarySource: null,
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        extensions: {
          welcomeSuggestions: true
        }
      }
    })
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.4', 'gpt-5.4-mini'],
      modelListUnsupportedEndpoint: false
    })

    fileReadFile.mockResolvedValue('{}')
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        model: null,
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        model: null,
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      }
    })
    settingsGet.mockResolvedValue({ locale: 'en' })
    shellOpenExternal.mockResolvedValue(undefined)
    shellGetProtocolHandlerName.mockResolvedValue('Oratorio')
    gitListBranches.mockResolvedValue({
      current: 'main',
      detachedHead: null,
      branches: [{ name: 'main', current: true }]
    })
    gitCheckoutBranch.mockResolvedValue(undefined)
    gitCreateAndCheckoutBranch.mockResolvedValue(undefined)
    getPathForFile.mockImplementation((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') {
        return {
          commands: [
            {
              name: '/code-review',
              aliases: ['/cr'],
              description: 'Review files',
              category: 'custom',
              requiresAdmin: false
            }
          ]
        }
      }
      if (method === 'skills/list') {
        return {
          skills: [
            {
              name: 'memory',
              description: 'Recall project context',
              source: 'builtin',
              available: true,
              enabled: true,
              path: '/skills/memory/SKILL.md'
            }
          ]
        }
      }
      if (method === 'welcome/suggestions') {
        return {
          source: 'none',
          items: [],
          fingerprint: 'none'
        }
      }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: appServerSendRequest
        },
        workspaceConfig: {
          getCore: workspaceConfigGetCore
        },
        file: {
          readFile: fileReadFile
        },
        git: {
          listBranches: gitListBranches,
          checkoutBranch: gitCheckoutBranch,
          createAndCheckoutBranch: gitCreateAndCheckoutBranch
        },
        workspace: {
          saveImageToTemp,
          pickFiles,
          getPathForFile
        },
        shell: {
          openExternal: shellOpenExternal,
          getProtocolHandlerName: shellGetProtocolHandlerName
        }
      }
    })
  })

  it('renders the active-only plan label behavior and themed model picker as the main composer', async () => {
    renderWelcome()

    const textbox = await screen.findByRole('textbox')

    expect(textbox).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Agent' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()
    fireEvent.keyDown(window, { key: 'M', ctrlKey: true, shiftKey: true })
    const listbox = screen.getByRole('listbox', { name: 'Select model' })
    expect(listbox).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeInTheDocument()
    expect(screen.queryByText('Attach file')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Bind an app before first turn' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    expect(screen.getByRole('menuitemcheckbox', { name: 'Plan mode' })).toHaveAttribute('aria-checked', 'false')
  })

  it('shows ChatGPT subscription usage in the welcome composer footer', async () => {
    useConnectionStore.setState({
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        providerManagement: true,
        extensions: {
          welcomeSuggestions: true
        }
      }
    })
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
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      return {}
    })

    renderWelcome()

    const badge = await screen.findByRole('button', { name: /ChatGPT.*96% left in the 5h window.*76% left this week/i })
    expect(badge).not.toHaveAttribute('title')
    expect(badge.querySelector('img')).toBeNull()
    expect(badge.querySelector('svg[data-provider-mark="openai"]')).toBeInTheDocument()
    expect(screen.queryByText('96% 5h')).toBeNull()
    expect(screen.queryByText('76% wk')).toBeNull()

    fireEvent.mouseEnter(badge.parentElement as HTMLElement)
    expect(await screen.findByRole('tooltip')).toHaveTextContent('96% 5h, 76% wk')
  })

  it('refreshes a stale draft model after workspace provider config changes', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Continue this draft',
      images: [],
      files: [],
      mode: 'agent',
      model: 'mimo-v2.5-pro',
      approvalPolicy: 'default'
    })
    fileReadFile.mockResolvedValue(JSON.stringify({ Model: 'claude-sonnet-4-5' }))

    const view = renderWelcome()
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('mimo-v2.5-pro')
    })

    const change: WorkspaceConfigChangedPayload = {
      source: 'workspace/config/update',
      regions: ['workspace.provider'],
      changedAt: '2026-05-26T00:00:00.000Z'
    }
    view.rerender(
      <LocaleProvider>
        <ConversationWelcome
          workspacePath="F:\\dotcraft"
          workspaceConfigChange={change}
          workspaceConfigChangeSeq={1}
        />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('claude-sonnet-4-5')
    })
  })

  it('loads the workspace model from remote-aware core config without reading local files', async () => {
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        model: 'claude-sonnet-4-5',
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        model: 'gpt-5',
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      }
    })

    renderWelcome({ remoteWorkspace: true })

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('claude-sonnet-4-5')
    })
    expect(workspaceConfigGetCore).toHaveBeenCalled()
    expect(fileReadFile).not.toHaveBeenCalled()
  })

  it('shows connected welcome apps as enabled switches and preserves manual disable', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        appBinding: true,
        extensions: { welcomeSuggestions: true }
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'app/list') {
        return {
          apps: [
            {
              appId: 'com.dotharness.oratorio',
              toolNamespace: 'oratorio',
              displayName: 'Oratorio',
              developerName: 'DotHarness',
              description: 'Board tools',
              pluginId: 'oratorio',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: 'connected',
              nativeApp: { displayName: 'Oratorio', protocol: 'oratorio', status: 'installed' },
              scopes: [],
              toolCatalog: []
            }
          ]
        }
      }
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    const appSwitch = await screen.findByRole('switch', { name: 'Use Oratorio for the first turn' })
    expect(appSwitch).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByText('Authorized')).toBeInTheDocument()
    expect(screen.queryByText('Connected')).not.toBeInTheDocument()

    fireEvent.click(appSwitch)
    expect(appSwitch).toHaveAttribute('aria-checked', 'false')
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => {
      expect(screen.getByRole('switch', { name: 'Use Oratorio for the first turn' }))
        .toHaveAttribute('aria-checked', 'false')
    })
  })

  it('keeps manually enabled managed Teams selected on welcome while leaving it off by default', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        appBinding: true,
        extensions: { welcomeSuggestions: true }
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'app/list') return { apps: [agentTeamsWelcomeApp()] }
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    expect(await screen.findByRole('switch', { name: 'Use Agent Teams for the first turn' }))
      .toHaveAttribute('aria-checked', 'false')

    fireEvent.click(screen.getByRole('switch', { name: 'Use Agent Teams for the first turn' }))
    fireEvent.click(screen.getByRole('button', { name: 'Refresh' }))

    await waitFor(() => {
      expect(screen.getByRole('switch', { name: 'Use Agent Teams for the first turn' }))
        .toHaveAttribute('aria-checked', 'true')
    })

    fireEvent.click(screen.getByRole('switch', { name: 'Use Agent Teams for the first turn' }))
    await waitFor(() => {
      expect(screen.getByRole('switch', { name: 'Use Agent Teams for the first turn' }))
        .toHaveAttribute('aria-checked', 'false')
    })
  })

  it('handles /goal on the welcome screen by queuing the objective as the first turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true,
        commandManagement: true,
        skillsManagement: true
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: null,
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      if (method === 'thread/goal/set') return { goal: makeGoal('thread-welcome', 'Build feature') }
      return {}
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = '/goal Build feature'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-welcome',
        objective: 'Build feature',
        mode: 'upsertOrUpdate'
      })
    })
    const startIndex = appServerSendRequest.mock.calls.findIndex((call) => call[0] === 'thread/start')
    const goalIndex = appServerSendRequest.mock.calls.findIndex((call) => call[0] === 'thread/goal/set')
    expect(startIndex).toBeGreaterThanOrEqual(0)
    expect(goalIndex).toBeGreaterThan(startIndex)
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'turn/start')).toBe(false)
    expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
      threadId: 'thread-welcome',
      text: 'Build feature',
      inputParts: [{ type: 'text', text: 'Build feature' }]
    })
    expect(useThreadStore.getState().activeThreadId).toBe('thread-welcome')
    expect(useThreadStore.getState().goalSnapshots.get('thread-welcome')?.objective).toBe('Build feature')
  })

  it('shows plan mode as a welcome system action and clears the slash query when selected', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {}
    })
    renderWelcome()

    const textbox = screen.getByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setTextboxCaret(textbox, 1)
    fireEvent.input(textbox)

    expect(await screen.findByText('Enable plan mode')).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('option', { name: /plan/i }))

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Disable plan mode' })).toBeInTheDocument()
    })
    expect(textbox.textContent?.trim()).toBe('')
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/start', expect.anything())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('handles welcome /agent locally without starting a thread', async () => {
    renderWelcome()

    const textbox = screen.getByRole('textbox')
    fireEvent.keyDown(textbox, { key: 'Tab', shiftKey: true })
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Disable plan mode' })).toBeInTheDocument()
    })

    textbox.textContent = '/agent'
    setTextboxCaret(textbox, 6)
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()
    })
    expect(textbox.textContent?.trim()).toBe('')
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/start', expect.anything())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('turn/start', expect.anything())
  })

  it('does not create a thread for welcome /goal pause without a current goal', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = '/goal pause'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(useToastStore.getState().toasts.some((toast) => toast.message.includes('No current goal'))).toBe(true)
    })
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'thread/start')).toBe(false)
  })

  it('creates a goal-backed thread from the welcome Goal panel', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        threadGoals: true
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: null,
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      if (method === 'thread/goal/set') return { goal: makeGoal('thread-welcome', 'Panel goal') }
      return {}
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    fireEvent.focus(textbox)
    textbox.textContent = '/'
    setTextboxCaret(textbox, 1)
    fireEvent.input(textbox)
    fireEvent.click(await screen.findByRole('option', { name: /goal/i }))

    fireEvent.change(await screen.findByPlaceholderText('Describe the goal for this thread'), {
      target: { value: 'Panel goal' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Set goal' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-welcome',
        objective: 'Panel goal',
        mode: 'upsertOrUpdate'
      })
    })
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'turn/start')).toBe(false)
    expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
      threadId: 'thread-welcome',
      text: 'Panel goal',
      inputParts: [{ type: 'text', text: 'Panel goal' }]
    })
  })

  it('opens the compact attachment menu and still routes file references through pickFiles', async () => {
    pickFiles.mockResolvedValue([{ path: 'C:\\temp\\brief.md', fileName: 'brief.md' }])

    renderWelcome()

    fireEvent.click(screen.getByRole('button', { name: 'Add attachment' }))
    expect(screen.getByRole('menuitem', { name: 'Attach image' })).toBeInTheDocument()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Reference file' }))

    await waitFor(() => {
      expect(pickFiles).toHaveBeenCalled()
      expect(screen.getByText('brief.md')).toBeInTheDocument()
    })
  })

  it('creates a thread and stores the pending welcome turn on first send', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'stale draft',
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Help me understand this workspace'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      const threadStartCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'thread/start')
      expect(threadStartCall?.[0]).toBe('thread/start')
      const payload = threadStartCall?.[1] as {
        historyMode?: string
        identity?: { workspacePath?: string }
      }
      expect(payload.historyMode).toBe('server')
      expect(payload.identity?.workspacePath).toContain('dotcraft')
    })

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Help me understand this workspace',
        mode: 'agent',
        model: ''
      })
      expect(useThreadStore.getState().activeThreadId).toBe('thread-welcome')
      expect(useUIStore.getState().welcomeDraft).toBeNull()
    })
  })

  it('creates the first welcome thread in a new worktree when selected from the footer', async () => {
    useConnectionStore.setState({
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        gitWorktrees: true,
        extensions: {
          welcomeSuggestions: true
        }
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'worktree/createAndStart') {
        return {
          thread: {
            id: 'thread-worktree-start',
            displayName: 'Worktree thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            workspacePath: 'F:\\dotcraft',
            effectiveWorkspacePath: 'F:\\dotcraft\\.craft\\worktrees\\dotcraft-worktree-start',
            worktree: {
              id: 'worktree-1',
              sourceThreadId: 'thread-worktree-start',
              workspacePath: 'F:\\dotcraft',
              sourceWorkspacePath: 'F:\\dotcraft',
              path: 'F:\\dotcraft\\.craft\\worktrees\\dotcraft-worktree-start',
              branchName: 'dotcraft/worktree-start',
              baseRef: 'main',
              head: 'abc123',
              createdAt: '2026-04-16T08:00:00.000Z'
            },
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: /Work locally/ }))
    fireEvent.click(screen.getByRole('button', { name: 'New worktree' }))
    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Build this in isolation'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      const call = appServerSendRequest.mock.calls.find((entry) => entry[0] === 'worktree/createAndStart')
      expect(call?.[0]).toBe('worktree/createAndStart')
      expect((call?.[1] as { baseRef?: string }).baseRef).toBe('main')
      expect(appServerSendRequest.mock.calls.some((entry) => entry[0] === 'thread/start')).toBe(false)
    })
    expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-worktree-start')
  })

  it('hides the workspace footer for non-git workspaces and starts locally', async () => {
    useConnectionStore.setState({
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        gitWorktrees: true,
        extensions: {
          welcomeSuggestions: true
        }
      }
    })
    gitListBranches.mockRejectedValue(new Error('not a git repository'))

    renderWelcome()

    await waitFor(() => {
      expect(gitListBranches).toHaveBeenCalled()
    })
    expect(screen.queryByRole('button', { name: /Work locally/ })).toBeNull()
    expect(screen.queryByRole('button', { name: /main/ })).toBeNull()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Start from a plain folder'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((entry) => entry[0] === 'thread/start')).toBe(true)
    })
    expect(appServerSendRequest.mock.calls.some((entry) => entry[0] === 'worktree/createAndStart')).toBe(false)
  })

  it('waits for selected welcome app bindings before storing the first pending turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        appBinding: true,
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        extensions: {
          welcomeSuggestions: true
        }
      }
    })
    const bindingList = createDeferred<{
      bindings: Array<Record<string, unknown>>
    }>()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'app/list') {
        return {
          apps: [
            {
              appId: 'com.dotharness.oratorio',
              toolNamespace: 'oratorio',
              displayName: 'Oratorio',
              developerName: 'DotHarness',
              description: 'Board tools',
              pluginId: 'oratorio',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: 'connected',
              nativeApp: {
                displayName: 'Oratorio',
                protocol: 'oratorio',
                status: 'installed'
              },
              scopes: [{ id: 'board.read', displayName: 'Board read', risk: 'read' }],
              toolCatalog: [{ name: 'ListBoardItems', scope: 'board.read', risk: 'read', defaultExposure: 'direct' }]
            }
          ]
        }
      }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      if (method === 'app/binding/request/create') {
        return {
          bindingRequestId: 'request-1',
          threadId: 'thread-welcome',
          appId: 'com.dotharness.oratorio',
          requestedScopes: ['board.read'],
          state: 'pending',
          tokenExpiresAt: '2026-05-16T00:01:00Z',
          handoff: { mode: 'customProtocol', uri: 'oratorio://dotcraft/bind?request=request-1' }
        }
      }
      if (method === 'thread/appBindings/list') return bindingList.promise
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    const appSwitch = await screen.findByRole('switch', { name: 'Use Oratorio for the first turn' })
    expect(appSwitch).toHaveAttribute('aria-checked', 'true')

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'List my Oratorio board items'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('app/binding/request/create', expect.objectContaining({
        threadId: 'thread-welcome',
        appId: 'com.dotharness.oratorio',
        source: 'welcome'
      }))
    })
    const startingButton = screen.getByRole('button', { name: 'Starting conversation' })
    expect(startingButton).toBeDisabled()
    expect(startingButton).toHaveAttribute('aria-busy', 'true')
    expect(useUIStore.getState().pendingWelcomeTurn).toBeNull()

    bindingList.resolve({
      bindings: [
        {
          bindingRequestId: 'request-1',
          bindingId: 'binding-1',
          threadId: 'thread-welcome',
          appId: 'com.dotharness.oratorio',
          displayName: 'Oratorio',
          state: 'active',
          connectionState: 'connected',
          grantedScopes: ['board.read'],
          attachedToolCount: 1,
          lastChangedAt: '2026-05-16T00:00:00Z'
        }
      ]
    })

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'List my Oratorio board items'
      })
    })
  })

  it('creates a managed Teams welcome binding before storing the first pending turn', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        appBinding: true,
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        extensions: {
          welcomeSuggestions: true
        }
      }
    })
    let bound = false
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'app/list') return { apps: [agentTeamsWelcomeApp()] }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      if (method === 'app/binding/request/create') {
        bound = true
        return {
          bindingRequestId: 'binding-teams',
          threadId: 'thread-welcome',
          appId: 'com.dotharness.dotcraft-teams',
          requestedScopes: ['mission.manage'],
          state: 'active',
          tokenExpiresAt: '2026-05-16T00:01:00Z',
          handoff: { mode: 'managed' }
        }
      }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: bound
            ? [
                {
                  bindingRequestId: 'binding-teams',
                  bindingId: 'binding-teams',
                  threadId: 'thread-welcome',
                  appId: 'com.dotharness.dotcraft-teams',
                  displayName: 'Agent Teams',
                  state: 'active',
                  connectionState: 'connected',
                  managed: true,
                  requiresExternalConnection: false,
                  grantedScopes: ['mission.manage'],
                  attachedToolCount: 1,
                  lastChangedAt: '2026-05-16T00:00:00Z'
                }
              ]
            : []
        }
      }
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    fireEvent.click(await screen.findByRole('switch', { name: 'Use Agent Teams for the first turn' }))

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Ask the team to investigate the workspace'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('app/binding/request/create', expect.objectContaining({
        threadId: 'thread-welcome',
        appId: 'com.dotharness.dotcraft-teams',
        requestedScopes: ['mission.manage'],
        requestedTools: ['CreateTeam'],
        source: 'welcome'
      }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('app/connection/start', expect.anything())
    expect(shellOpenExternal).not.toHaveBeenCalled()
    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Ask the team to investigate the workspace'
      })
    })
  })

  it('hydrates from welcomeDraft and persists latest draft on unmount', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'resume draft message',
      selectionStart: 6,
      selectionEnd: 6,
      images: [],
      mode: 'plan',
      model: 'gpt-5.4-mini'
    })

    const mounted = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.textContent).toContain('resume draft message')
    })
    await waitFor(() => {
      expect(getTextboxSelection(textbox)).toEqual({ start: 6, end: 6 })
    })
    expect(screen.getByRole('button', { name: 'Disable plan mode' })).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Explore this workspace' }))
    mounted.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      mode: 'plan',
      model: 'gpt-5.4-mini'
    })
    expect(useUIStore.getState().welcomeDraft?.text).toContain('Give me a quick overview of this project')
  })

  it('preserves caret position across thread switch and welcome remount', async () => {
    const firstMount = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    fireEvent.input(textbox, { target: { textContent: 'restore this caret' } })
    setTextboxCaret(textbox, 7)
    fireEvent.mouseUp(textbox)

    await waitFor(() => {
      expect(getTextboxSelection(textbox)).toEqual({ start: 7, end: 7 })
    })

    firstMount.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      text: 'restore this caret',
      selectionStart: 7,
      selectionEnd: 7
    })

    const secondMount = renderWelcome()
    const restoredTextbox = await screen.findByRole('textbox')

    await waitFor(() => {
      expect(restoredTextbox.textContent).toContain('restore this caret')
      expect(getTextboxSelection(restoredTextbox)).toEqual({ start: 7, end: 7 })
    })

    secondMount.unmount()
  })

  it('hydrates structured welcome drafts back into inline tags', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Check @src/foo.ts then /code-review and $memory',
      segments: [
        { type: 'text', value: 'Check ' },
        { type: 'file', relativePath: 'src/foo.ts' },
        { type: 'text', value: ' then ' },
        { type: 'command', command: '/code-review' },
        { type: 'text', value: ' and ' },
        { type: 'skill', skillName: 'memory' }
      ],
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    const mounted = renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.querySelector(`.${FILE_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    })
    expect(textbox.textContent).not.toContain('$memory')

    mounted.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      text: 'Check @src/foo.ts then /code-review and $memory'
    })
    expect(useUIStore.getState().welcomeDraft?.segments).toEqual([
      { type: 'text', value: 'Check ' },
      { type: 'file', relativePath: 'src/foo.ts' },
      { type: 'text', value: ' then ' },
      { type: 'command', command: '/code-review' },
      { type: 'text', value: ' and ' },
      { type: 'skill', skillName: 'memory' }
    ])
  })

  it('restores text drafts into tags and keeps serialized text when sending', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Check @src/foo.ts /code-review $memory',
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.querySelector(`.${FILE_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).not.toBeNull()
      expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).not.toBeNull()
    })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Check @src/foo.ts /code-review $memory',
        inputParts: [
          { type: 'text', text: 'Check ' },
          { type: 'fileRef', path: 'src/foo.ts', displayPath: 'src/foo.ts' },
          { type: 'text', text: ' ' },
          { type: 'commandRef', name: 'code-review', rawText: '/code-review' },
          { type: 'text', text: ' ' },
          { type: 'skillRef', name: 'memory' }
        ]
      })
    })
  })

  it('keeps unmatched legacy slash and skill tokens as plain text', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Try /unknown and $unknown',
      images: [],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.textContent).toContain('/unknown')
      expect(textbox.textContent).toContain('$unknown')
    })
    expect(textbox.querySelector(`.${COMMAND_REF_CLASS}`)).toBeNull()
    expect(textbox.querySelector(`.${SKILL_REF_CLASS}`)).toBeNull()
  })

  it('hydrates file attachments from welcomeDraft and keeps them when creating the pending welcome turn', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'Review this file',
      images: [],
      files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }],
      mode: 'agent',
      model: 'Default'
    })

    renderWelcome()

    expect(await screen.findByText('notes.txt')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Review this file',
        files: [{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]
      })
    })
  })

  it('replaces static welcome suggestions when dynamic suggestions load successfully', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'welcome/suggestions') {
        return {
          source: 'dynamic',
          fingerprint: 'dynamic-1',
          items: [
            {
              title: 'Review desktop welcome flow',
              prompt: 'Review the Desktop welcome flow and identify where we should inject dynamic quick suggestions.'
            },
            {
              title: 'Map thread history inputs',
              prompt: 'Trace how current workspace thread history is loaded so we can feed it into welcome suggestion generation.'
            }
          ]
        }
      }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      return {}
    })

    renderWelcome()

    expect(await screen.findByText('Review desktop welcome flow')).toBeInTheDocument()
    await waitFor(() => {
      const methods = appServerSendRequest.mock.calls.map((call) => call[0])
      expect(methods).toContain('welcome/suggestions')
    })
    expect(screen.queryByText('Explore this workspace')).not.toBeInTheDocument()
  })

  it('keeps static welcome suggestions when the server returns none', async () => {
    renderWelcome()

    expect(await screen.findByRole('button', { name: 'Explore this workspace' })).toBeInTheDocument()
    await waitFor(() => {
      const methods = appServerSendRequest.mock.calls.map((call) => call[0])
      expect(methods).toContain('welcome/suggestions')
    })
    expect(screen.queryAllByTestId('welcome-suggestion-skeleton')).toHaveLength(0)
  })

  it('keeps the connecting hint while rendering opening skeletons for footer and quick starts', async () => {
    useConnectionStore.setState({
      status: 'connecting',
      serverInfo: null,
      dashboardUrl: null,
      errorMessage: null,
      errorType: null,
      binarySource: null,
      capabilities: null
    })

    renderWelcome()

    await waitFor(() => {
      expect(screen.getByTestId('welcome-footer-skeleton')).toBeInTheDocument()
      expect(screen.getAllByTestId('welcome-suggestion-skeleton')).toHaveLength(4)
    })
    expect(screen.getByText('Connecting to workspace…')).toBeInTheDocument()
    expect(screen.queryByTestId('welcome-hint-skeleton')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Explore this workspace' })).not.toBeInTheDocument()
  })

  it('does not request welcome suggestions when the workspace config disables them', async () => {
    fileReadFile.mockResolvedValue(
      JSON.stringify({
        WelcomeSuggestions: {
          Enabled: false
        }
      })
    )

    renderWelcome()

    await screen.findByRole('button', { name: 'Explore this workspace' })
    await waitFor(() => {
      const methods = appServerSendRequest.mock.calls.map((call) => call[0])
      expect(methods).not.toContain('welcome/suggestions')
    })
  })

  it('clicking a dynamic suggestion prefills the welcome composer', async () => {
    const dynamicPrompt = 'Audit how workspace memory is currently loaded and suggest how to reuse it for welcome suggestions.'

    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'welcome/suggestions') {
        return {
          source: 'dynamic',
          fingerprint: 'dynamic-2',
          items: [
            {
              title: 'Audit workspace memory usage',
              prompt: dynamicPrompt
            }
          ]
        }
      }
      if (method === 'thread/start') {
        return {
          thread: {
            id: 'thread-welcome',
            displayName: 'Welcome thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-04-16T08:00:00.000Z',
            lastActiveAt: '2026-04-16T08:00:00.000Z'
          }
        }
      }
      return {}
    })

    renderWelcome()

    const dynamicButton = await screen.findByRole('button', { name: 'Audit workspace memory usage' })
    fireEvent.click(dynamicButton)

    const textbox = await screen.findByRole('textbox')
    expect(textbox.textContent).toContain('Audit how workspace memory is currently loaded')
    await waitFor(() => {
      expect(getTextboxSelection(textbox)).toEqual({
        start: dynamicPrompt.length,
        end: dynamicPrompt.length
      })
    })
  })

  it('strips markdown markers from dynamic suggestion titles only', async () => {
    const rawPrompt = 'Inspect `feat/welcome_suggestion` path and keep _this marker_ in prompt output.'

    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'welcome/suggestions') {
        return {
          source: 'dynamic',
          fingerprint: 'dynamic-markdown-title',
          items: [
            {
              title: '审查 `feat/welcome_suggestion` 后端',
              prompt: rawPrompt
            },
            {
              title: '**Trace** *welcome/suggestions*',
              prompt: 'Trace welcome/suggestions lifecycle hooks.'
            },
            {
              title: 'Review __welcome__ flow',
              prompt: 'Review __welcome__ flow details.'
            }
          ]
        }
      }
      return {}
    })

    renderWelcome()

    const firstButton = await screen.findByRole('button', { name: '审查 feat/welcome_suggestion 后端' })
    expect(screen.getByRole('button', { name: 'Trace welcome/suggestions' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Review welcome flow' })).toBeInTheDocument()

    fireEvent.click(firstButton)

    const textbox = await screen.findByRole('textbox')
    expect(textbox.textContent).toContain(rawPrompt)
  })

  it('keeps fallback suggestions visible while dynamic suggestions are pending', async () => {
    const deferred = createDeferred<{
      source: string
      fingerprint: string
      items: Array<{ title: string; prompt: string }>
    }>()

    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'welcome/suggestions') {
        return deferred.promise
      }
      return {}
    })

    renderWelcome()

    expect(await screen.findByRole('button', { name: 'Explore this workspace' })).toBeInTheDocument()
    expect(screen.queryAllByTestId('welcome-suggestion-skeleton')).toHaveLength(0)

    deferred.resolve({
      source: 'dynamic',
      fingerprint: 'dynamic-loading',
      items: [
        {
          title: 'Inspect suggestion loading',
          prompt: 'Inspect suggestion loading state transitions on the welcome screen.'
        }
      ]
    })

    expect(await screen.findByRole('button', { name: 'Inspect suggestion loading' })).toBeInTheDocument()
    expect(screen.queryAllByTestId('welcome-suggestion-skeleton')).toHaveLength(0)
  })

})
