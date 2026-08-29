import type { DesktopPluginHost } from '@dotcraft/plugin'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ConversationWelcome } from '../components/conversation/ConversationWelcome'
import { COMMAND_REF_CLASS, FILE_REF_CLASS, SKILL_REF_CLASS } from '../components/conversation/richInputConstants'
import { useConnectionStore } from '../stores/connectionStore'
import { normalizeGitPathKey, useGitStore } from '../stores/gitStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useSkillsStore } from '../stores/skillsStore'
import { useToastStore } from '../stores/toastStore'
import { useAppBindingStore } from '../stores/appBindingStore'
import { useConversationStore } from '../stores/conversationStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { useVoiceStore } from '../voice/voiceStore'
import type { ThreadGoal } from '../types/thread'
import type { WorkspaceConfigChangedPayload } from '../utils/workspaceConfigChanged'
import type { ModelPreference } from '../../shared/modelPreference'
import { appendVoiceTranscript, isAvailableComposerVoiceOrigin } from '../voice/composerDraftBridge'
import { installDesktopApiMock } from './desktopApiMock'
import {
  clearDesktopPluginRegistry,
  registerDesktopPluginSurface
} from '../plugins/desktopPluginRegistry'

const fileReadFile = vi.fn()
const appServerSendRequest = vi.fn()
const workspaceConfigGetCore = vi.fn()
const saveImageToTemp = vi.fn()
const getPathForFile = vi.fn((file: File) => file.name === 'notes.txt' ? 'C:\\temp\\notes.txt' : '')
const settingsGet = vi.fn()
const shellOpenExternal = vi.fn()
const shellOpenAppHandoff = vi.fn()
const shellGetProtocolHandlerName = vi.fn()
const gitListBranches = vi.fn()
const gitCheckoutBranch = vi.fn()
const gitCreateAndCheckoutBranch = vi.fn()

function desktopPluginHost(pluginId: string): DesktopPluginHost {
  return {
    plugin: { id: pluginId, version: '1.0.0', displayName: pluginId }
  } as DesktopPluginHost
}

class ResizeObserverMock {
  observe(): void {}
  unobserve(): void {}
  disconnect(): void {}
}

Object.defineProperty(window, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})
Object.defineProperty(globalThis, 'ResizeObserver', {
  configurable: true,
  writable: true,
  value: ResizeObserverMock
})

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
  remoteWorkspace = false,
  projectKey
}: {
  workspaceConfigChange?: WorkspaceConfigChangedPayload | null
  workspaceConfigChangeSeq?: number
  remoteWorkspace?: boolean
  projectKey?: string
} = {}) {
  return render(
    <LocaleProvider>
      <ConversationWelcome
        workspacePath={'X:\\fixtures\\workspace'}
        projectKey={projectKey}
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

function preference(
  model: string,
  overrides: Partial<ModelPreference> = {}
): ModelPreference {
  return {
    model,
    reasoning: { enabled: false, effort: 'medium', output: 'full' },
    speed: 'standard',
    contextWindow: { mode: 'default' },
    ...overrides
  }
}

function workspacePreferenceConfig(
  providerId: string,
  model: string,
  overrides: Partial<ModelPreference> = {}
): Record<string, unknown> {
  return {
    ProviderId: providerId,
    ProviderPreferences: {
      [providerId]: preference(model, overrides)
    }
  }
}

function configValue(config: Record<string, unknown>, key: string): unknown {
  const expected = key.toLowerCase()
  return Object.entries(config).find(([candidate]) => candidate.toLowerCase() === expected)?.[1]
}

function coreSnapshotFromConfig(config: Record<string, unknown>): Record<string, unknown> {
  const providerId = configValue(config, 'ProviderId')
  const providerPreferences = configValue(config, 'ProviderPreferences')
  return {
    providerId: typeof providerId === 'string' ? providerId : null,
    providerPreferences:
      providerPreferences != null && typeof providerPreferences === 'object' && !Array.isArray(providerPreferences)
        ? providerPreferences
        : {},
    welcomeSuggestionsEnabled: null,
    defaultApprovalPolicy: null
  }
}

describe('ConversationWelcome composer', () => {
  beforeEach(() => {
    clearDesktopPluginRegistry()
    vi.clearAllMocks()
    delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog

    useConnectionStore.getState().reset()
    useComposerDraftStore.setState({ draftsByThread: {} })
    useVoiceStore.setState({
      initialized: false,
      snapshot: { model: { phase: 'missing', bytesDownloaded: 0, bytesTotal: null }, sessions: [], capacity: 2 },
      recording: null,
      finalizing: null,
      localErrors: {}
    })
    useGitStore.getState().reset()
    useGitStore.setState({
      branchesByPath: {
        [normalizeGitPathKey('X:\\fixtures\\workspace')]: {
          path: 'X:\\fixtures\\workspace',
          status: 'available',
          snapshot: {
            current: 'main',
            detachedHead: null,
            branches: [{ name: 'main', current: true }]
          },
          refreshing: false,
          errorMessage: null,
          updatedAt: Date.now(),
          requestId: 1
        }
      }
    })
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
      welcomeDraftsByWorkspace: {},
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
      providerId: 'openai',
      requestedProviderId: 'openai',
      modelOptions: ['gpt-5.4', 'gpt-5.4-mini'],
      modelListUnsupportedEndpoint: false
    })

    fileReadFile.mockResolvedValue('{}')
    workspaceConfigGetCore.mockImplementation(async () => {
      const raw = await fileReadFile()
      const config = typeof raw === 'string' && raw.trim() ? JSON.parse(raw) as Record<string, unknown> : {}
      return {
        workspace: coreSnapshotFromConfig(config),
        userDefaults: coreSnapshotFromConfig({})
      }
    })
    settingsGet.mockResolvedValue({ locale: 'en' })
    shellOpenExternal.mockResolvedValue(undefined)
    shellOpenAppHandoff.mockResolvedValue(undefined)
    shellGetProtocolHandlerName.mockResolvedValue('Workflow App')
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
      if (method === 'command/execute') {
        return { handled: true, expandedPrompt: 'Generate AGENTS.md' }
      }
      return {}
    })

    installDesktopApiMock({
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: appServerSendRequest,
          onNotification: undefined
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
          getPathForFile
        },
        shell: {
          openExternal: shellOpenExternal,
          openAppHandoff: shellOpenAppHandoff,
          getProtocolHandlerName: shellGetProtocolHandlerName
        },
        voice: undefined
      })
  })

  afterEach(() => {
    act(() => clearDesktopPluginRegistry())
  })

  it('mounts the public Composer surfaces before a thread exists', async () => {
    const pluginId = 'welcome-surfaces'
    const host = desktopPluginHost(pluginId)
    registerDesktopPluginSurface(
      pluginId,
      host,
      'composer',
      'wrap',
      ({ children, context }) => (
        <section
          data-testid="welcome-plugin-composer"
          data-thread-id={context.threadId ?? 'none'}
          data-mode={context.mode}
        >
          {children}
        </section>
      )
    )
    registerDesktopPluginSurface(
      pluginId,
      host,
      'composer.toolbar.model',
      'wrap',
      ({ children }) => (
        <>
          <button type="button" data-testid="welcome-plugin-before-model">Review model</button>
          {children}
        </>
      )
    )
    for (const surface of [
      'composer.before',
      'composer.toolbar.leading',
      'composer.status.subscription',
      'composer.after'
    ] as const) {
      registerDesktopPluginSurface(
        pluginId,
        host,
        surface,
        'add',
        ({ context }) => (
          <span
            data-testid={`welcome-plugin-${surface}`}
            data-thread-id={context.threadId ?? 'none'}
            data-workspace={context.workspacePath ?? 'none'}
          />
        )
      )
    }
    renderWelcome()

    const composer = screen.getByTestId('welcome-plugin-composer')
    expect(composer).toContainElement(await screen.findByRole('textbox'))
    expect(composer).toHaveAttribute('data-thread-id', 'none')
    expect(composer).toHaveAttribute('data-mode', 'agent')
    for (const surface of [
      'composer.before',
      'composer.toolbar.leading',
      'composer.status.subscription',
      'composer.after'
    ]) {
      const contribution = screen.getByTestId(`welcome-plugin-${surface}`)
      expect(composer).toContainElement(contribution)
      expect(contribution).toHaveAttribute('data-thread-id', 'none')
      expect(contribution).toHaveAttribute('data-workspace', 'X:\\fixtures\\workspace')
    }
    const beforeModel = screen.getByTestId('welcome-plugin-before-model')
    const model = await screen.findByRole('button', { name: 'Select model' })
    expect(Boolean(beforeModel.compareDocumentPosition(model) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
  })

  it('unmounts the default Welcome composer for replacement and rehydrates it when restored', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'restore after plugin replacement',
      images: [],
      files: [],
      mode: 'plan',
      model: 'gpt-5.4'
    }, 'X:\\fixtures\\workspace')
    const disposeReplacement = registerDesktopPluginSurface(
      'welcome-replacement',
      desktopPluginHost('welcome-replacement'),
      'composer',
      'replace',
      ({ context }) => (
        <div
          data-testid="welcome-composer-replacement"
          data-mode={context.mode}
          data-thread-id={context.threadId ?? 'none'}
          data-busy={context.busy}
        >
          Custom composer
        </div>
      )
    )

    renderWelcome()

    const replacement = screen.getByTestId('welcome-composer-replacement')
    expect(replacement).toHaveAttribute('data-mode', 'plan')
    expect(replacement).toHaveAttribute('data-thread-id', 'none')
    expect(replacement).toHaveAttribute('data-busy', 'false')
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(isAvailableComposerVoiceOrigin('welcome-composer:X:\\fixtures\\workspace')).toBe(false)

    act(disposeReplacement)

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => expect(textbox).toHaveTextContent('restore after plugin replacement'))
    expect(isAvailableComposerVoiceOrigin('welcome-composer:X:\\fixtures\\workspace')).toBe(true)

    let disposeDynamicReplacement!: () => void
    act(() => {
      disposeDynamicReplacement = registerDesktopPluginSurface(
        'welcome-dynamic-replacement',
        desktopPluginHost('welcome-dynamic-replacement'),
        'composer',
        'replace',
        () => <div data-testid="welcome-dynamic-replacement">Dynamic composer</div>
      )
    })
    expect(screen.getByTestId('welcome-dynamic-replacement')).toBeInTheDocument()
    expect(screen.queryByRole('textbox')).toBeNull()
    expect(isAvailableComposerVoiceOrigin('welcome-composer:X:\\fixtures\\workspace')).toBe(false)

    act(disposeDynamicReplacement)
    await waitFor(() => expect(screen.getByRole('textbox')).toHaveTextContent('restore after plugin replacement'))
    expect(isAvailableComposerVoiceOrigin('welcome-composer:X:\\fixtures\\workspace')).toBe(true)
  })

  it('renders the active-only plan label behavior and themed model picker as the main composer', async () => {
    renderWelcome()

    const textbox = await screen.findByRole('textbox')

    expect(textbox).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Agent' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'Disable plan mode' })).toBeNull()
    fireEvent.keyDown(window, { key: 'M', ctrlKey: true, shiftKey: true })
    const menu = screen.getByRole('menu', { name: 'Select model' })
    expect(menu).toBeInTheDocument()
    const modelButton = screen.getByRole('button', { name: 'Select model' })
    const voiceButton = screen.getByRole('button', { name: 'Click to dictate or hold' })
    const sendButton = screen.getByRole('button', { name: 'Send message' })
    expect(sendButton).toBeInTheDocument()
    expect(Boolean(modelButton.compareDocumentPosition(voiceButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    expect(Boolean(voiceButton.compareDocumentPosition(sendButton) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    expect(screen.queryByText('Attach file')).not.toBeInTheDocument()
    expect(screen.queryByRole('combobox', { name: 'Bind an app before first turn' })).toBeNull()
    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))
    expect(screen.getByRole('option', { name: /Plan mode/ })).toBeInTheDocument()
  })

  it('uses the compact footer while processing voice input from welcome', async () => {
    renderWelcome({ projectKey: 'voice-welcome' })
    await screen.findByRole('button', { name: 'Open commands' })

    act(() => {
      useVoiceStore.setState({
        initialized: true,
        snapshot: {
          model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 },
          sessions: [{
            sessionId: 'welcome-session',
            threadId: 'welcome-composer:voice-welcome',
            intent: 'insert',
            phase: 'transcribing',
            durationMs: 1_000
          }],
          capacity: 2
        },
        recording: null
      })
    })

    expect(await screen.findByRole('button', { name: 'Processing voice input' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open commands' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled()
    expect(screen.getByText('0:01')).toBeInTheDocument()
    expect(screen.queryByTestId('approval-policy-trigger')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
    expect(screen.getByText('Work locally')).toBeInTheDocument()
  })

  it('keeps the welcome footer compact while captured audio is finalizing', async () => {
    renderWelcome({ projectKey: 'voice-finalizing' })
    await screen.findByRole('button', { name: 'Open commands' })

    act(() => {
      useVoiceStore.setState({
        initialized: true,
        snapshot: { model: { phase: 'installed', bytesDownloaded: 1, bytesTotal: 1 }, sessions: [], capacity: 2 },
        recording: null,
        finalizing: {
          threadId: 'welcome-composer:voice-finalizing',
          intent: 'insert',
          durationMs: 1_000
        }
      })
    })

    expect(await screen.findByRole('button', { name: 'Processing voice input' })).toBeDisabled()
    expect(screen.getByRole('button', { name: 'Send message' })).toBeDisabled()
    expect(screen.getByText('0:01')).toBeInTheDocument()
    expect(screen.queryByTestId('approval-policy-trigger')).toBeNull()
    expect(screen.queryByRole('button', { name: 'Select model' })).toBeNull()
    expect(screen.getByText('Work locally')).toBeInTheDocument()
  })

  it('appends voice text to the mounted welcome draft without creating a thread', async () => {
    const workspacePath = 'voice-fixture'
    render(
      <LocaleProvider>
        <ConversationWelcome workspacePath={workspacePath} />
      </LocaleProvider>
    )

    const textbox = await screen.findByRole('textbox')
    const welcomeVoiceOrigin = `welcome-composer:${workspacePath}`
    await waitFor(() => {
      expect(isAvailableComposerVoiceOrigin(welcomeVoiceOrigin)).toBe(true)
    })
    textbox.textContent = 'Existing draft'
    fireEvent.input(textbox)

    await act(async () => {
      expect(await appendVoiceTranscript(welcomeVoiceOrigin, '  spoken words  ', false)).toBe(true)
    })

    await waitFor(() => {
      expect(textbox).toHaveTextContent('Existing draft spoken words')
    })
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'thread/start')).toBe(false)
  })

  it('creates a thread and expands /init before queuing the first turn', async () => {
    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = '/init'
    fireEvent.input(textbox)
    fireEvent.keyDown(textbox, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('command/execute', {
        threadId: 'thread-welcome',
        command: '/init',
        arguments: []
      })
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Generate AGENTS.md',
        inputParts: [{ type: 'text', text: 'Generate AGENTS.md' }]
      })
    })
  })

  it('shows the Context MAX section in the welcome model picker', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.5'],
      models: [
        {
          id: 'gpt-5.5',
          contextWindow: {
            catalogWindow: 1_000_000,
            configuredWindow: 256_000,
            supportsMax: true,
            maxWindow: 1_000_000
          }
        }
      ],
      modelListUnsupportedEndpoint: false
    })
    fileReadFile.mockResolvedValue(JSON.stringify(workspacePreferenceConfig('openai', 'gpt-5.5')))

    renderWelcome()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('gpt-5.5')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))

    const menu = screen.getByRole('menu', { name: 'Select model' })
    expect(within(menu).getByText('MAX Mode')).toBeInTheDocument()
    expect(within(menu).getByRole('switch', { name: 'MAX Mode' })).not.toBeDisabled()
  })

  it('saves Fast as a welcome preset without creating a thread', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.5'],
      models: [{
        id: 'gpt-5.5',
        speed: { supportedModes: ['standard', 'fast'], defaultMode: 'standard' }
      }],
      modelListUnsupportedEndpoint: false
    })
    fileReadFile.mockResolvedValue(JSON.stringify(workspacePreferenceConfig('openai', 'gpt-5.5')))

    renderWelcome()
    await waitFor(() => expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('gpt-5.5'))
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    fireEvent.click(within(screen.getByRole('menu', { name: 'Select model' })).getByRole('menuitem', { name: /Speed/ }))
    fireEvent.click(within(screen.getByRole('listbox', { name: 'Speed' })).getByRole('option', { name: /Fast/ }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'openai',
        providerPreferences: {
          openai: preference('gpt-5.5', { speed: 'fast' })
        }
      })
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/start', expect.anything())
  })

  it('sends explicit welcome MAX context in thread/start', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.5'],
      models: [
        {
          id: 'gpt-5.5',
          contextWindow: {
            catalogWindow: 1_000_000,
            configuredWindow: 256_000,
            supportsMax: true,
            maxWindow: 1_000_000
          }
        }
      ],
      modelListUnsupportedEndpoint: false
    })
    fileReadFile.mockResolvedValue(JSON.stringify(workspacePreferenceConfig('openai', 'gpt-5.5')))

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('gpt-5.5')
    })
    textbox.textContent = 'Use the largest context for this first thread'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    fireEvent.click(within(screen.getByRole('menu', { name: 'Select model' })).getByRole('switch', { name: 'MAX Mode' }))
    expect(document.querySelector('[data-mascot-context]')).toHaveAttribute('data-mascot-context', 'max')
    fireEvent.keyDown(window, { key: 'Escape' })
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Use the largest context for this first thread'
      })
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect(start?.[1]).toEqual(expect.objectContaining({
      config: expect.objectContaining({ contextWindow: { mode: 'max' } })
    }))
  })

  it('does not write welcome contextWindow when workspace MAX default is untouched', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.5'],
      models: [
        {
          id: 'gpt-5.5',
          contextWindow: {
            catalogWindow: 1_000_000,
            configuredWindow: 256_000,
            supportsMax: true,
            maxWindow: 1_000_000
          }
        }
      ],
      modelListUnsupportedEndpoint: false
    })
    fileReadFile.mockResolvedValue(JSON.stringify(workspacePreferenceConfig('openai', 'gpt-5.5', {
      contextWindow: { mode: 'max' }
    })))

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Inherit the workspace context default'
    fireEvent.input(textbox)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('MAX')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-welcome')
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect((start?.[1] as { config?: Record<string, unknown> })?.config).not.toHaveProperty('contextWindow')
  })

  it('writes explicit default when welcome MAX inherits from workspace and the user switches it off', async () => {
    useModelCatalogStore.setState({
      status: 'ready',
      modelOptions: ['gpt-5.5'],
      models: [
        {
          id: 'gpt-5.5',
          contextWindow: {
            catalogWindow: 1_000_000,
            configuredWindow: 256_000,
            supportsMax: true,
            maxWindow: 1_000_000
          }
        }
      ],
      modelListUnsupportedEndpoint: false
    })
    fileReadFile.mockResolvedValue(JSON.stringify(workspacePreferenceConfig('openai', 'gpt-5.5', {
      contextWindow: { mode: 'max' }
    })))

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Use default context for this first thread'
    fireEvent.input(textbox)
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('MAX')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    const maxSwitch = within(screen.getByRole('menu', { name: 'Select model' })).getByRole('switch', { name: 'MAX Mode' })
    expect(maxSwitch).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(maxSwitch)
    fireEvent.keyDown(window, { key: 'Escape' })
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Use default context for this first thread'
      })
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect(start?.[1]).toEqual(expect.objectContaining({
      config: expect.objectContaining({ contextWindow: { mode: 'default' } })
    }))
  })

  it('shows ChatGPT subscription usage in the welcome composer footer', async () => {
    registerDesktopPluginSurface(
      'welcome-subscription-addition',
      desktopPluginHost('welcome-subscription-addition'),
      'composer.status.subscription',
      'add',
      () => <span data-testid="welcome-subscription-addition">Review quota</span>
    )
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
    const branch = await screen.findByRole('button', { name: 'main' })
    const addition = screen.getByTestId('welcome-subscription-addition')
    expect(Boolean(branch.compareDocumentPosition(badge) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
    expect(Boolean(badge.compareDocumentPosition(addition) & Node.DOCUMENT_POSITION_FOLLOWING)).toBe(true)
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
      model: 'model-a-v1',
      approvalPolicy: 'default'
    })
    useConnectionStore.setState((state) => ({
      capabilities: { ...state.capabilities, providerManagement: true }
    }))
    let workspaceConfig: Record<string, unknown> = {
      ...workspacePreferenceConfig('provider-a', 'model-a-v1')
    }
    fileReadFile.mockImplementation(async () => JSON.stringify(workspaceConfig))
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'provider/list') {
        return {
          providers: [
            { id: 'provider-a', displayName: 'Provider A', protocol: 'openai-responses' },
            { id: 'provider-b', displayName: 'Provider B', protocol: 'anthropic' }
          ]
        }
      }
      if (method === 'model/list') {
        return {
          providerId: params?.providerId,
          models: [{ id: params?.providerId === 'provider-b' ? 'model-b-v1' : 'model-a-v1' }]
        }
      }
      return defaultSendRequest?.(method, params)
    })

    const view = renderWelcome()
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('model-a-v1')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    expect(screen.getByRole('menuitem', { name: /Provider.*Provider A/ })).toBeInTheDocument()
    fireEvent.keyDown(document, { key: 'Escape' })

    const change: WorkspaceConfigChangedPayload = {
      source: 'workspace/config/update',
      regions: ['workspace.provider'],
      changedAt: '2026-05-26T00:00:00.000Z'
    }
    workspaceConfig = {
      ProviderId: 'provider-b',
      ProviderPreferences: {
        'provider-a': preference('model-a-v1'),
        'provider-b': preference('model-b-v1')
      }
    }
    view.rerender(
      <LocaleProvider>
        <ConversationWelcome
          workspacePath={'X:\\fixtures\\workspace'}
          workspaceConfigChange={change}
          workspaceConfigChangeSeq={1}
        />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('model-b-v1')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    expect(screen.getByRole('menuitem', { name: /Provider.*Provider B/ })).toBeInTheDocument()
  })

  it('preserves the Welcome provider and model pair across unmount and remount', async () => {
    useConnectionStore.setState((state) => ({
      capabilities: { ...state.capabilities, providerManagement: true }
    }))
    fileReadFile.mockResolvedValue(JSON.stringify({
      ...workspacePreferenceConfig('provider-b', 'model-b-v2')
    }))
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'provider/list') {
        return {
          providers: [
            { id: 'provider-a', displayName: 'Provider A', protocol: 'openai-responses' },
            { id: 'provider-b', displayName: 'Provider B', protocol: 'anthropic' }
          ]
        }
      }
      if (method === 'model/list') {
        return {
          providerId: params?.providerId,
          models: [{ id: 'model-b-v2' }]
        }
      }
      return defaultSendRequest?.(method, params)
    })

    const firstMount = renderWelcome()
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('model-b-v2')
    })
    firstMount.unmount()

    expect(useUIStore.getState().welcomeDraft).toMatchObject({
      providerId: 'provider-b',
      model: 'model-b-v2'
    })

    const secondMount = renderWelcome()
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('model-b-v2')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    expect(screen.getByRole('menuitem', { name: /Provider.*Provider B/ })).toBeInTheDocument()
    fireEvent.keyDown(document, { key: 'Escape' })
    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Start with the restored pair'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/start', expect.objectContaining({
        config: expect.objectContaining({ providerId: 'provider-b', model: 'model-b-v2' })
      }))
    })
    secondMount.unmount()
    useUIStore.setState({ welcomeDraft: null, welcomeDraftsByWorkspace: {} })
  })

  it('loads the workspace model from remote-aware core config without reading local files', async () => {
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        providerId: 'anthropic',
        providerPreferences: { anthropic: preference('claude-sonnet-4-5') },
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        providerId: 'openai',
        providerPreferences: { openai: preference('gpt-5') },
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

  it('loads an inherited personal provider preference for a local workspace without persisting it', async () => {
    useConnectionStore.setState((state) => ({
      capabilities: { ...state.capabilities, providerManagement: true }
    }))
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        providerId: 'openai',
        providerPreferences: { openai: preference('gpt-5.6-sol') },
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      }
    })
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'provider/list') {
        return {
          providers: [
            { id: 'openai', displayName: 'OpenAI', protocol: 'openai-responses' }
          ]
        }
      }
      if (method === 'model/list') {
        return { success: true, providerId: params?.providerId, models: [{ id: 'gpt-5.6-sol' }] }
      }
      return defaultSendRequest?.(method, params)
    })

    renderWelcome()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('gpt-5.6-sol')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    expect(screen.getByRole('menuitem', { name: /Provider.*OpenAI/ })).toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('workspace/config/update', expect.anything())
  })

  it('uses an inherited personal preference when switching providers and persists only the workspace override', async () => {
    useConnectionStore.setState((state) => ({
      capabilities: { ...state.capabilities, providerManagement: true }
    }))
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        providerId: null,
        providerPreferences: {},
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        providerId: 'openai',
        providerPreferences: {
          openai: preference('gpt-5.6-sol'),
          anthropic: preference('claude-sonnet-4-5')
        },
        welcomeSuggestionsEnabled: null,
        defaultApprovalPolicy: null
      }
    })
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'provider/list') {
        return {
          providers: [
            { id: 'openai', displayName: 'OpenAI', protocol: 'openai-responses' },
            { id: 'anthropic', displayName: 'Anthropic', protocol: 'anthropic' }
          ]
        }
      }
      if (method === 'model/list') {
        const model = params?.providerId === 'anthropic' ? 'claude-sonnet-4-5' : 'gpt-5.6-sol'
        return { success: true, providerId: params?.providerId, models: [{ id: model }] }
      }
      return defaultSendRequest?.(method, params)
    })

    renderWelcome()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Select model' })).toHaveTextContent('gpt-5.6-sol')
    })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    fireEvent.click(screen.getByRole('menuitem', { name: /Provider.*OpenAI/ }))
    fireEvent.click(within(screen.getByRole('listbox', { name: 'Provider' })).getByRole('option', { name: /Anthropic/ }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'anthropic',
        providerPreferences: {
          anthropic: preference('claude-sonnet-4-5')
        }
      })
    })
  })

  it('stages connected welcome apps with a switch and restores an explicit empty selection', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        appBindingVersion: 1,
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
              appId: 'com.example.workflow',
              toolNamespace: 'workflow',
              displayName: 'Workflow App',
              developerName: 'Example Labs',
              description: 'Board tools',
              pluginId: 'workflow',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: 'connected',
              nativeApp: { displayName: 'Workflow App', protocol: 'workflow', status: 'installed' },
              scopes: [],
              toolCatalog: []
            }
          ]
        }
      }
      return {}
    })

    const firstMount = renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    const switchControl = await screen.findByRole('switch', { name: 'Use Workflow App for the first turn' })
    expect(switchControl).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(switchControl)

    expect(switchControl).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByText('Authorized')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Added' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Add' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Connect' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Refresh' })).not.toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('app/connection/start', expect.anything())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/appBindings/revoke', expect.anything())
    await waitFor(() => expect(useUIStore.getState().welcomeDraft?.appIds).toEqual([]))

    firstMount.unmount()
    renderWelcome()
    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    expect(await screen.findByRole('switch', { name: 'Use Workflow App for the first turn' })).toHaveAttribute('aria-checked', 'false')
  })

  it('hides unconnected welcome apps instead of offering connection or setup actions', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        commandManagement: true,
        skillsManagement: true,
        appBindingVersion: 1,
        extensions: { welcomeSuggestions: true }
      }
    })
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'app/list') {
        return {
          apps: [{
            appId: 'com.example.workflow',
            toolNamespace: 'workflow',
            displayName: 'Workflow App',
            developerName: 'Example Labs',
            description: 'Board tools',
            pluginId: 'workflow',
            installed: true,
            enabled: true,
            catalogVisible: true,
            connectionState: 'notConnected',
            nativeApp: { displayName: 'Workflow App', protocol: 'workflow', status: 'installed' },
            scopes: [],
            toolCatalog: []
          }]
        }
      }
      if (method === 'plugin/list') return { plugins: [], snapshotRevision: 1 }
      return {}
    })

    renderWelcome()
    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    expect(await screen.findByText('No connected apps available.')).toBeInTheDocument()
    expect(screen.queryByText('Workflow App')).not.toBeInTheDocument()
    expect(screen.queryByRole('switch')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Set up' })).not.toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('app/connection/start', expect.anything())
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
        objective: 'Build feature'
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

    // Selecting Goal enters compose mode; the main composer becomes the objective.
    expect(await screen.findByRole('button', { name: 'Exit goal mode' })).toBeInTheDocument()
    const composer = screen.getByRole('textbox')
    composer.textContent = 'Panel goal'
    fireEvent.input(composer)
    fireEvent.keyDown(composer, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/goal/set', {
        threadId: 'thread-welcome',
        objective: 'Panel goal'
      })
    })
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'turn/start')).toBe(false)
    expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
      threadId: 'thread-welcome',
      text: 'Panel goal',
      inputParts: [{ type: 'text', text: 'Panel goal' }]
    })
  })

  it('opens the command picker from the footer trigger without changing draft text', async () => {
    renderWelcome()

    const textbox = screen.getByRole('textbox')
    textbox.textContent = 'Review this'
    fireEvent.input(textbox)
    setTextboxCaret(textbox, 'Review this'.length)
    fireEvent.click(screen.getByRole('button', { name: 'Open commands' }))

    expect(textbox).toHaveTextContent('Review this')
    expect(screen.getByRole('listbox')).toBeInTheDocument()
    expect(screen.queryByRole('menuitem', { name: 'Attach image' })).toBeNull()
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
      expect(payload.identity?.workspacePath).toBe('X:\\fixtures\\workspace')
    })

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Help me understand this workspace'
      })
      expect(useThreadStore.getState().activeThreadId).toBe('thread-welcome')
      expect(useUIStore.getState().welcomeDraft).toBeNull()
    })
  })

  it('keeps the full-access workspace default inherited on the created thread', async () => {
    fileReadFile.mockResolvedValue(JSON.stringify({
      Permissions: {
        DefaultApprovalPolicy: 'autoApprove'
      }
    }))

    renderWelcome()

    const approvalTrigger = await screen.findByTestId('approval-policy-trigger')
    await waitFor(() => {
      expect(approvalTrigger).toHaveTextContent('Full access')
    })

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Use the configured workspace default'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'Use the configured workspace default'
      })
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect((start?.[1] as { config?: Record<string, unknown> })?.config).not.toHaveProperty('approvalPolicy')
  })

  it('writes an explicit approval override in thread/start', async () => {
    fileReadFile.mockResolvedValue(JSON.stringify({
      Permissions: {
        DefaultApprovalPolicy: 'autoApprove'
      }
    }))

    renderWelcome()

    const approvalTrigger = await screen.findByTestId('approval-policy-trigger')
    await waitFor(() => expect(approvalTrigger).toHaveTextContent('Full access'))
    fireEvent.click(approvalTrigger)
    fireEvent.click(screen.getByTestId('approval-policy-option-prompt'))

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Require approval for this thread'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-welcome'))
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect(start?.[1]).toEqual(expect.objectContaining({
      config: expect.objectContaining({ approvalPolicy: 'prompt' })
    }))
  })

  it('writes an explicit full-access override in thread/start', async () => {
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = vi.fn().mockResolvedValue(true)
    renderWelcome()

    const approvalTrigger = await screen.findByTestId('approval-policy-trigger')
    await waitFor(() => expect(approvalTrigger).toHaveTextContent('Ask for approval'))
    fireEvent.click(approvalTrigger)
    fireEvent.click(screen.getByTestId('approval-policy-option-autoApprove'))

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Allow this thread to proceed automatically'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-welcome'))
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect(start?.[1]).toEqual(expect.objectContaining({
      config: expect.objectContaining({ approvalPolicy: 'autoApprove' })
    }))
  })

  it('resolves a legacy default welcome draft approval policy to the current workspace default', async () => {
    useUIStore.getState().setWelcomeDraft({
      text: 'legacy default policy draft',
      images: [],
      mode: 'agent',
      model: 'Default',
      approvalPolicy: 'default'
    })
    fileReadFile.mockResolvedValue(JSON.stringify({
      Permissions: {
        DefaultApprovalPolicy: 'autoApprove'
      }
    }))

    renderWelcome()

    const textbox = await screen.findByRole('textbox')
    await waitFor(() => {
      expect(textbox.textContent).toContain('legacy default policy draft')
      expect(screen.getByTestId('approval-policy-trigger')).toHaveTextContent('Full access')
    })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(useUIStore.getState().pendingWelcomeTurn).toMatchObject({
        threadId: 'thread-welcome',
        text: 'legacy default policy draft'
      })
    })
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    expect((start?.[1] as { config?: Record<string, unknown> })?.config).not.toHaveProperty('approvalPolicy')
  })

  it('creates a Profile-backed thread atomically and exposes its forced agent mode', async () => {
    useConnectionStore.setState((state) => ({
      capabilities: { ...state.capabilities, agentProfileManagement: true }
    }))
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'agent/profiles/list') {
        return {
          profiles: [{ id: 'reviewer', source: 'workspace', valid: true }]
        }
      }
      return defaultSendRequest?.(method, params)
    })
    registerDesktopPluginSurface(
      'profile-mode-context',
      desktopPluginHost('profile-mode-context'),
      'composer',
      'wrap',
      ({ children, context }) => (
        <section data-testid="profile-mode-context" data-mode={context.mode}>{children}</section>
      )
    )

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Open commands' }))
    fireEvent.click(await screen.findByRole('option', { name: /Plan mode/ }))
    await waitFor(() => expect(screen.getByTestId('profile-mode-context')).toHaveAttribute('data-mode', 'plan'))
    fireEvent.click(await screen.findByRole('option', { name: /Profile/i }))
    fireEvent.click(await screen.findByRole('button', { name: 'reviewer' }))
    await waitFor(() => expect(screen.getByTestId('profile-mode-context')).toHaveAttribute('data-mode', 'agent'))

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Review this workspace'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(useUIStore.getState().pendingWelcomeTurn?.threadId).toBe('thread-welcome'))
    const start = appServerSendRequest.mock.calls.find(([method]) => method === 'thread/start')
    const config = (start?.[1] as { config?: Record<string, unknown> })?.config
    expect(config).toEqual(expect.objectContaining({ agentProfileId: 'reviewer' }))
    expect(config).not.toHaveProperty('mode')
    expect(config).not.toHaveProperty('approvalPolicy')
    expect(appServerSendRequest.mock.calls.some(([method]) => method === 'agent/profiles/refreshThread')).toBe(false)
  })

  it('creates the first welcome thread in a new worktree when selected from the footer', async () => {
    fileReadFile.mockResolvedValue(JSON.stringify(workspacePreferenceConfig('openai', 'gpt-5.4')))
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
            workspacePath: 'X:\\fixtures\\workspace',
            effectiveWorkspacePath: 'X:\\fixtures\\workspace\\.craft\\worktrees\\dotcraft-worktree-start',
            worktree: {
              id: 'worktree-1',
              sourceThreadId: 'thread-worktree-start',
              workspacePath: 'X:\\fixtures\\workspace',
              sourceWorkspacePath: 'X:\\fixtures\\workspace',
              path: 'X:\\fixtures\\workspace\\.craft\\worktrees\\dotcraft-worktree-start',
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
      expect((call?.[1] as { config?: Record<string, unknown> }).config).toEqual(expect.objectContaining({
        mode: 'agent',
        providerId: 'openai',
        model: 'gpt-5.4',
        reasoning: expect.any(Object),
        speed: 'standard'
      }))
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
    useGitStore.getState().reset()

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
        appBindingVersion: 1,
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
              appId: 'com.example.workflow',
              toolNamespace: 'workflow',
              displayName: 'Workflow App',
              developerName: 'Example Labs',
              description: 'Board tools',
              pluginId: 'workflow',
              installed: true,
              enabled: true,
              catalogVisible: true,
              connectionState: 'connected',
              nativeApp: {
                displayName: 'Workflow App',
                protocol: 'workflow',
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
      if (method === 'thread/appBindings/enable') {
        return {
          bindingRequestId: 'request-1',
          threadId: 'thread-welcome',
          appId: 'com.example.workflow',
          requestedScopes: ['board.read'],
          state: 'connecting',
          tokenExpiresAt: '2026-05-16T00:01:00Z',
          handoff: { mode: 'customProtocol', uri: 'workflow://dotcraft/bind?request=request-1' }
        }
      }
      if (method === 'thread/appBindings/list') return bindingList.promise
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    expect(await screen.findByRole('switch', { name: 'Use Workflow App for the first turn' })).toHaveAttribute('aria-checked', 'true')

    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'List my Workflow App board items'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/appBindings/enable', expect.objectContaining({
        threadId: 'thread-welcome',
        appId: 'com.example.workflow'
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
          appId: 'com.example.workflow',
          displayName: 'Workflow App',
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
        text: 'List my Workflow App board items'
      })
    })
  })

  it('preserves the Welcome draft and deletes the unused thread when app activation fails', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        appBindingVersion: 1,
        commandManagement: true,
        skillsManagement: true,
        modelCatalogManagement: true,
        workspaceConfigManagement: true,
        extensions: { welcomeSuggestions: true }
      }
    })
    let bindingFailed = false
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'command/list') return { commands: [] }
      if (method === 'skills/list') return { skills: [] }
      if (method === 'welcome/suggestions') return { source: 'none', items: [], fingerprint: 'none' }
      if (method === 'app/list') {
        return {
          apps: [{
            appId: 'com.dotharness.oratorio',
            displayName: 'Oratorio',
            developerName: 'DotHarness',
            description: 'Task orchestration',
            pluginId: 'oratorio',
            installed: true,
            enabled: true,
            catalogVisible: true,
            managed: true,
            requiresExternalConnection: false,
            connectionState: 'connected',
            handoffModes: []
          }]
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
      if (method === 'thread/appBindings/enable') {
        bindingFailed = true
        return {
          bindingRequestId: 'request-1',
          bindingId: 'binding-1',
          state: 'connecting',
          expiresAt: '2026-05-16T00:01:00Z',
          handoff: { mode: 'desktopService', uri: 'dotcraft-service://oratorio/bind?request=request-1' }
        }
      }
      if (method === 'thread/appBindings/list') {
        return {
          bindings: bindingFailed
            ? [{
                bindingRequestId: 'request-1',
                bindingId: 'binding-1',
                threadId: 'thread-welcome',
                appId: 'com.dotharness.oratorio',
                state: 'failed',
                failureReason: 'mcpStartupFailed',
                authorityRevision: 1,
                approvedCapabilityRevision: 1
              }]
            : []
        }
      }
      if (method === 'thread/appBindings/revoke') {
        bindingFailed = false
        return { bindingId: 'binding-1', state: 'cancelled' }
      }
      if (method === 'thread/delete') return {}
      return {}
    })

    renderWelcome()

    fireEvent.click(await screen.findByRole('button', { name: 'Apps' }))
    const appSwitch = await screen.findByRole('switch', { name: 'Use Oratorio for the first turn' })
    expect(appSwitch).toHaveAttribute('aria-checked', 'false')
    fireEvent.click(appSwitch)
    expect(appSwitch).toHaveAttribute('aria-checked', 'true')
    const textbox = await screen.findByRole('textbox')
    textbox.textContent = 'Keep this draft after activation fails'
    fireEvent.input(textbox)
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/appBindings/revoke', {
        threadId: 'thread-welcome',
        bindingId: 'binding-1',
        reason: 'activation_failed'
      })
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'thread-welcome' })
    })
    expect(textbox).toHaveTextContent('Keep this draft after activation fails')
    expect(useUIStore.getState().pendingWelcomeTurn).toBeNull()
    expect(useThreadStore.getState().threadList).toHaveLength(0)
    expect(useToastStore.getState().toasts.some((toast) =>
      toast.message === 'Oratorio binding failed (state: failed, reason: mcpStartupFailed).')).toBe(true)
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
