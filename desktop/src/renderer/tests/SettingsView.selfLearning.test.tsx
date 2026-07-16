import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { SettingsSidebar } from '../components/layout/SettingsSidebar'
import { useConnectionStore } from '../stores/connectionStore'
import { usePendingRestartStore } from '../stores/pendingRestartStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const workspaceConfigGetCore = vi.fn()
const appServerSendRequest = vi.fn()
const appServerRestartManaged = vi.fn()
const appServerApplyConnectionSettings = vi.fn()

const providerListResult = {
  providers: [
    {
      id: 'openai',
      displayName: 'OpenAI',
      protocol: 'openai',
      hasApiKey: true,
      endPoint: 'https://api.deepseek.com/v1',
      networkTimeoutSeconds: null,
      isImplicit: false
    },
    {
      id: 'openai-chat',
      displayName: 'OpenAI Chat',
      protocol: 'openai-chat-completions',
      hasApiKey: true,
      endPoint: 'https://api.openai.com/v1',
      networkTimeoutSeconds: null,
      isImplicit: false
    },
    {
      id: 'openai-responses',
      displayName: 'OpenAI Responses',
      protocol: 'openai-responses',
      hasApiKey: true,
      endPoint: '',
      networkTimeoutSeconds: null,
      supportsHostedImageGeneration: true,
      isImplicit: false
    },
    {
      id: 'anthropic-main',
      displayName: 'Anthropic',
      protocol: 'anthropic',
      hasApiKey: true,
      endPoint: 'https://api.anthropic.com',
      networkTimeoutSeconds: null,
      isImplicit: false
    }
  ]
}

function PendingRestartHarness(): JSX.Element | null {
  const visible = usePendingRestartStore((s) => s.visible)
  const applying = usePendingRestartStore((s) => s.applying)
  const messageKey = usePendingRestartStore((s) => s.messageKey)
  const applyKey = usePendingRestartStore((s) => s.applyKey)
  const apply = usePendingRestartStore((s) => s.apply)
  const ignore = usePendingRestartStore((s) => s.ignore)
  if (!visible) return null
  const message = messageKey === 'settings.pendingReconnect.message'
    ? 'Connection changes are staged. Apply them to connect to the remote AppServer.'
    : 'Changes require a service restart to take effect'
  const applyLabel = applyKey === 'settings.pendingReconnect.apply' ? 'Apply & Connect' : 'Apply & Restart'
  return (
    <div role="status">
      <span>{message}</span>
      <button type="button" onClick={() => ignore()} disabled={applying}>Ignore</button>
      <button type="button" onClick={() => void apply()} disabled={applying}>{applyLabel}</button>
    </div>
  )
}

function renderView() {
  return render(
    <LocaleProvider>
      <PendingRestartHarness />
      <div style={{ display: 'flex', height: 800 }}>
        <SettingsSidebar />
        <SettingsView workspacePath="C:\\sample\\workspace" />
      </div>
    </LocaleProvider>
  )
}

function enableProviderManagement(): void {
  useConnectionStore.setState({
    status: 'connected',
    capabilities: {
      workspaceConfigManagement: true,
      memoryManagement: true,
      providerManagement: true,
      modelCatalogManagement: true
    }
  })
}

function enableProviderAndSubAgentManagement(): void {
  useConnectionStore.setState({
    status: 'connected',
    capabilities: {
      workspaceConfigManagement: true,
      memoryManagement: true,
      providerManagement: true,
      modelCatalogManagement: true,
      subAgentManagement: true
    }
  })
}

describe('SettingsView self-learning settings', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    usePendingRestartStore.getState().clear()
    useToastStore.setState({ toasts: [] })
    useUIStore.setState({
      activeMainView: 'settings',
      activeSettingsTab: 'general',
      sidebarCollapsed: false
    })
    useUIStore.getState().setShowThinkingContent(true)
    delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog

    const core: any = {
      workspace: {
        providerId: null,
        model: null,
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: false,
        memoryAutoConsolidateEnabled: false,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: 'default'
      },
      userDefaults: {
        providerId: null,
        model: null,
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null
      }
    }
    const dreamsStatus = {
      enabled: true,
      interval: '24:00:00',
      threadLookbackCount: 20,
      autoApply: false,
      historyTailChars: 20000,
      minCompletedTurnsSinceLastRun: 5,
      nextRunAt: null,
      running: false,
      activeDreamStoreId: null as string | null,
      lastRun: null as any
    }
    const dreamRuns: any[] = []

    settingsGet.mockResolvedValue({ locale: 'en', connectionMode: 'stdio', visibleChannels: [] })
    settingsSet.mockResolvedValue(undefined)
    workspaceConfigGetCore.mockImplementation(async () => core)
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'workspace/config/update') {
        if (typeof params?.providerId === 'string' || params?.providerId === null) {
          core.workspace.providerId = params.providerId
          return { providerId: core.workspace.providerId }
        }
        if (typeof params?.model === 'string' || params?.model === null) {
          core.workspace.model = params.model
          return { model: core.workspace.model }
        }
        if (typeof params?.defaultApprovalPolicy === 'string') {
          core.workspace.defaultApprovalPolicy = params.defaultApprovalPolicy
          return { defaultApprovalPolicy: core.workspace.defaultApprovalPolicy }
        }
        if (typeof params?.memoryAutoConsolidateEnabled === 'boolean') {
          core.workspace.memoryAutoConsolidateEnabled = params.memoryAutoConsolidateEnabled
          return { memoryAutoConsolidateEnabled: core.workspace.memoryAutoConsolidateEnabled }
        }
        if (typeof params?.dreamsEnabled === 'boolean') {
          core.workspace.dreamsEnabled = params.dreamsEnabled
          dreamsStatus.enabled = params.dreamsEnabled
          return { dreamsEnabled: core.workspace.dreamsEnabled }
        }
        if (typeof params?.dreamsInterval === 'string') {
          core.workspace.dreamsInterval = params.dreamsInterval
          dreamsStatus.interval = params.dreamsInterval
          return { dreamsInterval: core.workspace.dreamsInterval }
        }
        if (typeof params?.dreamsThreadLookbackCount === 'number') {
          core.workspace.dreamsThreadLookbackCount = params.dreamsThreadLookbackCount
          dreamsStatus.threadLookbackCount = params.dreamsThreadLookbackCount
          return { dreamsThreadLookbackCount: core.workspace.dreamsThreadLookbackCount }
        }
        if (typeof params?.dreamsAutoApply === 'boolean') {
          core.workspace.dreamsAutoApply = params.dreamsAutoApply
          dreamsStatus.autoApply = params.dreamsAutoApply
          return { dreamsAutoApply: core.workspace.dreamsAutoApply }
        }
        core.workspace.skillsSelfLearningEnabled = params?.skillsSelfLearningEnabled === true
        return { skillsSelfLearningEnabled: core.workspace.skillsSelfLearningEnabled }
      }
      if (method === 'dreams/status') {
        return { ...dreamsStatus }
      }
      if (method === 'dreams/run') {
        const run = {
          id: 'dream_20260511000000_test',
          status: 'succeeded',
          startedAt: '2026-05-11T00:00:00Z',
          endedAt: '2026-05-11T00:00:02Z',
          processedThreadCount: 2,
          candidateThreadCount: 2,
          dreamWritten: true,
          historyWritten: false,
          topicFilesWritten: 0,
          topicFilesDeleted: 0,
          evidenceSearchCount: 1,
          evidenceReadCount: 1,
          outputStoreId: 'store_20260511000000_test',
          reviewStatus: 'pending',
          autoApplied: false,
          errorType: null,
          evidenceThreadIds: ['thread-one'],
          writtenPaths: ['stores/store_20260511000000_test/INDEX.md'],
          threadId: 'thread_dream_fake',
          turnId: 'turn_dream_fake_2',
          turnIds: ['turn_dream_fake_1', 'turn_dream_fake_2'],
          trigger: 'manual',
          inputManifestPath: 'C:\\sample\\workspace\\.craft\\dreams\\runs\\dream_20260511000000_test\\input\\MANIFEST.md',
          message: null
        }
        dreamsStatus.lastRun = run
        dreamRuns.unshift(run)
        return { ...dreamsStatus }
      }
      if (method === 'dreams/list') {
        return { runs: [...dreamRuns] }
      }
      if (method === 'dreams/get') {
        const run = dreamRuns.find((item) => item.id === params?.runId) ?? null
        return {
          run,
          activeDreamStoreId: dreamsStatus.activeDreamStoreId,
          preview: run == null
            ? null
            : {
                activeStoreId: dreamsStatus.activeDreamStoreId,
                outputStoreId: run.outputStoreId,
                activeIndexMarkdown: dreamsStatus.activeDreamStoreId == null ? '' : '# Dream Store\n\n- Applied focus',
                outputIndexMarkdown: '# Dream Store\n\n- Pending focus',
                activeTopicPaths: [],
                outputTopicPaths: []
              }
        }
      }
      if (method === 'dreams/apply' || method === 'dreams/discard' || method === 'dreams/archive' || method === 'dreams/cancel') {
        const run = dreamRuns.find((item) => item.id === params?.runId) ?? null
        if (run != null) {
          if (method === 'dreams/apply') {
            run.reviewStatus = 'applied'
            dreamsStatus.activeDreamStoreId = run.outputStoreId
          } else if (method === 'dreams/discard') {
            run.reviewStatus = 'discarded'
          } else if (method === 'dreams/archive') {
            run.reviewStatus = 'archived'
          } else {
            run.status = 'canceled'
          }
          dreamsStatus.lastRun = run
        }
        return { run, activeDreamStoreId: dreamsStatus.activeDreamStoreId }
      }
      if (method === 'channel/list') {
        return { channels: [] }
      }
      if (method === 'provider/list') {
        return providerListResult
      }
      if (method === 'model/list') {
        return {
          success: true,
          models: [
            { id: params?.providerId === 'anthropic-main' ? 'claude-sonnet-4-5' : 'deepseek-v4-pro' }
          ]
        }
      }
      if (method === 'provider/test') {
        return {
          success: true,
          protocol: 'anthropic',
          models: [
            { id: 'claude-sonnet-4-5' },
            { id: 'claude-opus-4-1' }
          ]
        }
      }
      return {}
    })
    appServerRestartManaged.mockResolvedValue(undefined)
    appServerApplyConnectionSettings.mockResolvedValue(undefined)

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        platform: 'win32',
        settings: { get: settingsGet, set: settingsSet },
        workspaceConfig: { getCore: workspaceConfigGetCore },
        appServer: {
          sendRequest: appServerSendRequest,
          restartManaged: appServerRestartManaged,
          applyConnectionSettings: appServerApplyConnectionSettings,
          getResolvedBinary: vi.fn().mockResolvedValue({ path: null }),
          pickBinary: vi.fn()
        },
        modules: { list: vi.fn().mockResolvedValue([]) },
        workspace: {
          pickFolder: vi.fn(),
          viewer: { browserUse: { clearCookies: vi.fn() } }
        },
        shell: { openExternal: vi.fn() }
      }
    })

    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        memoryManagement: true
      }
    })
  })

  it('saves self-learning toggle, shows global restart banner, and restarts managed AppServer', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Enable self-learning' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        skillsSelfLearningEnabled: true
      })
    })
    expect(await screen.findByText('Changes require a service restart to take effect')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Apply & Restart' }))

    await waitFor(() => {
      expect(appServerRestartManaged).toHaveBeenCalledOnce()
    })
    await waitFor(() => {
      expect(screen.queryByText('Changes require a service restart to take effect')).not.toBeInTheDocument()
    })
  })

  it('groups personalization settings by conversation, learning, memory, and Dreams', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        memoryManagement: true,
        dreams: true
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))

    expect(await screen.findByText('Customize workspace suggestions, learning, memory, and response display.')).toBeInTheDocument()
    expect(screen.getByText('Conversation')).toBeInTheDocument()
    expect(screen.getByText('Learning')).toBeInTheDocument()
    expect(screen.getByText('Memory')).toBeInTheDocument()
    expect(screen.getByText('Dreams')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Manage Dreams' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Run now' })).toBeInTheDocument()
  })

  it('defaults self-learning on when workspace and user defaults are unset', async () => {
    workspaceConfigGetCore.mockResolvedValueOnce({
      workspace: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        defaultApprovalPolicy: null
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Enable self-learning' })

    expect(toggle).toHaveAttribute('aria-checked', 'true')
  })

  it('defaults thinking content display off when the setting is absent', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Show thinking content' })

    expect(toggle).toHaveAttribute('aria-checked', 'false')
  })

  it('loads and saves the thinking content display preference', async () => {
    settingsGet.mockResolvedValueOnce({
      locale: 'en',
      connectionMode: 'stdio',
      visibleChannels: [],
      showThinkingContent: false
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Show thinking content' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ showThinkingContent: true })
    })
    expect(useUIStore.getState().showThinkingContent).toBe(true)
  })

  it('shows the macOS menu bar toggle only on mac and saves it', async () => {
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        ...window.api,
        platform: 'darwin'
      }
    })
    settingsGet.mockResolvedValueOnce({
      locale: 'en',
      connectionMode: 'stdio',
      visibleChannels: [],
      showInMenuBar: false
    })

    renderView()

    const toggle = await screen.findByRole('switch', { name: 'Show in menu bar' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(settingsSet).toHaveBeenCalledWith({ showInMenuBar: true })
    })
  })

  it('saves long-term memory toggle without restart banner', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Enable long-term memory' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')

    fireEvent.click(toggle)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        memoryAutoConsolidateEnabled: true
      })
    })
    expect(screen.queryByText('Changes require a service restart to take effect')).not.toBeInTheDocument()
  })

  it('resets memory after confirmation and shows success toast', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Reset' }))

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
        title: 'Reset memory?',
        danger: true
      }))
      expect(appServerSendRequest).toHaveBeenCalledWith('memory/reset', undefined, 20_000)
    })
    expect(useToastStore.getState().toasts).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ message: 'Memory reset', type: 'success' })
      ])
    )
  })

  it('keeps memory reset hidden when the server capability is absent', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))

    expect(screen.queryByText('Reset memory')).not.toBeInTheDocument()
  })

  it('keeps Dreams controls hidden when the server capability is absent', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))

    expect(screen.queryByText('Enable Dreams')).not.toBeInTheDocument()
    expect(screen.queryByText('Dreams')).not.toBeInTheDocument()
    expect(appServerSendRequest).not.toHaveBeenCalledWith('dreams/status', undefined, 20_000)
  })

  it('loads Dreams status and saves Dreams settings', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        memoryManagement: true,
        dreams: true
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))

    expect(await screen.findByRole('switch', { name: 'Enable Dreams' })).toHaveAttribute('aria-checked', 'true')
    expect(screen.getByRole('switch', { name: 'Auto-update Dreams' })).toHaveAttribute('aria-checked', 'false')
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('dreams/status', undefined, 20_000)
    })

    fireEvent.click(screen.getByRole('switch', { name: 'Auto-update Dreams' }))
    fireEvent.change(screen.getByRole('combobox', { name: 'Dreams frequency' }), {
      target: { value: '12:00:00' }
    })
    fireEvent.change(screen.getByRole('combobox', { name: 'Recent threads' }), {
      target: { value: '50' }
    })

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({
        title: 'Auto-update Dreams?',
        danger: true
      }))
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        dreamsAutoApply: true
      })
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        dreamsInterval: '12:00:00'
      })
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        dreamsThreadLookbackCount: 50
      })
    })
  })

  it('runs Dreams now and reports completion', async () => {
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        memoryManagement: true,
        dreams: true
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Run now' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('dreams/run', undefined, 20_000)
      expect(useToastStore.getState().toasts).toEqual(
        expect.arrayContaining([
          expect.objectContaining({ message: 'Dreams run complete', type: 'success' })
        ])
      )
    })
  })

  it('opens Dreams management and sends run review to Dashboard', async () => {
    useConnectionStore.setState({
      status: 'connected',
      dashboardUrl: 'http://127.0.0.1:8080/dashboard',
      capabilities: {
        workspaceConfigManagement: true,
        memoryManagement: true,
        dreams: true
      }
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Run now' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('dreams/run', undefined, 20_000)
    })

    fireEvent.click(screen.getByRole('button', { name: 'Manage Dreams' }))

    expect(await screen.findByText('dream_20260511000000_test')).toBeInTheDocument()
    fireEvent.click(await screen.findByRole('button', { name: 'Review' }))

    await waitFor(() => {
      expect(window.api.shell.openExternal).toHaveBeenCalledWith(
        'http://127.0.0.1:8080/dashboard#dreams/run/dream_20260511000000_test'
      )
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('dreams/get', expect.anything(), expect.anything())
    expect(appServerSendRequest).not.toHaveBeenCalledWith('dreams/apply', expect.anything(), expect.anything())
  })

  it('shows memory reset failures in a toast', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'memory/reset') {
        throw new Error('disk denied')
      }
      if (method === 'channel/list') {
        return { channels: [] }
      }
      return {}
    })

    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Reset' }))

    await waitFor(() => {
      expect(useToastStore.getState().toasts).toEqual(
        expect.arrayContaining([
          expect.objectContaining({
            message: 'Failed to reset memory: disk denied',
            type: 'error'
          })
        ])
      )
    })
  })

  it('shows model provider settings without routing provider draft edits through the global restart banner', async () => {
    enableProviderManagement()
    renderView()

    expect(await screen.findByRole('button', { name: 'Model Providers' })).toBeInTheDocument()
    expect(screen.queryByText('OpenAI API Service')).not.toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Model Providers' }))
    expect(await screen.findByText('Provider list')).toBeInTheDocument()
    expect(screen.queryByLabelText('Provider id')).not.toBeInTheDocument()

    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))
    expect(await screen.findByLabelText('Provider id')).toBeInTheDocument()
    const protocolSelect = await screen.findByRole('combobox', { name: 'Protocol' })
    expect(protocolSelect).toHaveTextContent('OpenAI-Responses')
    fireEvent.click(protocolSelect)
    expect(await screen.findByRole('option', { name: 'OpenAI-Responses' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'OpenAI-Legacy' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Anthropic' })).toBeInTheDocument()
    fireEvent.keyDown(protocolSelect, { key: 'Escape' })
    const endpointInput = await screen.findByPlaceholderText('https://api.openai.com/v1') as HTMLInputElement
    fireEvent.change(endpointInput, { target: { value: 'https://models.example.test/v1' } })

    expect(screen.queryByText('Changes require a service restart to take effect')).not.toBeInTheDocument()
    expect(endpointInput.value).toBe('https://models.example.test/v1')

    fireEvent.click(screen.getByRole('button', { name: 'Back to Model Providers' }))
    expect(await screen.findByText('Provider list')).toBeInTheDocument()
    expect(screen.queryByLabelText('Provider id')).not.toBeInTheDocument()
  })

  it('sends canonical provider protocols when testing and saving provider drafts', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))
    fireEvent.change(await screen.findByLabelText('Provider id'), { target: { value: 'responses-main' } })
    appServerSendRequest.mockClear()

    fireEvent.click(await screen.findByRole('button', { name: 'Test' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('provider/test', expect.objectContaining({
        protocol: 'openai-responses'
      }), 25_000)
    })

    const protocolSelect = await screen.findByRole('combobox', { name: 'Protocol' })
    fireEvent.click(protocolSelect)
    fireEvent.click(await screen.findByRole('option', { name: 'OpenAI-Legacy' }))
    appServerSendRequest.mockClear()
    fireEvent.click(await screen.findByRole('button', { name: 'Create provider' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('provider/create', expect.objectContaining({
        id: 'responses-main',
        protocol: 'openai-chat-completions'
      }), 20_000)
    })
  })

  it('sends hosted image generation support when creating a Responses provider', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))
    fireEvent.change(await screen.findByLabelText('Provider id'), { target: { value: 'responses-main' } })

    const imageGenerationSwitch = await screen.findByRole('switch', { name: 'Support Image Generation' })
    expect(imageGenerationSwitch).toHaveAttribute('aria-checked', 'true')
    expect(screen.queryByRole('button', { name: 'Auto' })).not.toBeInTheDocument()

    appServerSendRequest.mockClear()
    fireEvent.click(await screen.findByRole('button', { name: 'Create provider' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('provider/create', expect.objectContaining({
        id: 'responses-main',
        protocol: 'openai-responses',
        supportsHostedImageGeneration: true
      }), 20_000)
    })
  })

  it('sends hosted image generation off when updating a Responses provider', async () => {
    enableProviderManagement()
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'provider/list') {
        return {
          providers: providerListResult.providers.map((provider) => provider.id === 'openai-responses'
            ? { ...provider, supportsHostedImageGeneration: false }
            : provider)
        }
      }
      return defaultSendRequest?.(method, params)
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Edit provider OpenAI Responses' }))

    const imageGenerationSwitch = await screen.findByRole('switch', { name: 'Support Image Generation' })
    expect(imageGenerationSwitch).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByRole('button', { name: 'Auto' })).not.toBeInTheDocument()

    appServerSendRequest.mockClear()
    fireEvent.click(await screen.findByRole('button', { name: 'Update provider' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('provider/update', expect.objectContaining({
        id: 'openai-responses',
        supportsHostedImageGeneration: false
      }), 20_000)
    })
  })

  it('locks id and displayName to canonical values when ChatGPT subscription auth is selected', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))

    const idInput = await screen.findByLabelText('Provider id') as HTMLInputElement
    const displayNameInput = await screen.findByLabelText('Display name') as HTMLInputElement
    fireEvent.change(idInput, { target: { value: 'my-custom' } })
    fireEvent.change(displayNameInput, { target: { value: 'My Custom' } })
    expect(idInput.value).toBe('my-custom')
    expect(displayNameInput.value).toBe('My Custom')
    expect(idInput).not.toHaveAttribute('readonly')
    const createButtonBefore = screen.queryByRole('button', { name: 'Create provider' })
    expect(createButtonBefore).toBeInTheDocument()

    // Pick the ChatGPT auth method via the auth-method toggle card. Use getByText to bypass
    // the role-traversal jsdom quirk triggered by complex CSS on nested OAuth-panel buttons.
    const chatgptCardLabel = screen.getByText('Sign in with ChatGPT')
    fireEvent.click(chatgptCardLabel.closest('button') as HTMLButtonElement)
    await waitFor(() => {
      // 'openai' already exists in the test fixture, so the next free slug is 'openai-2'.
      expect(idInput.value).toBe('openai-2')
    })
    expect(displayNameInput.value).toBe('OpenAI (ChatGPT)')
    expect(idInput).toHaveAttribute('readonly')
    expect(displayNameInput).toHaveAttribute('readonly')

    // Toggle back to API key — previous user-typed values must restore.
    const apiKeyCardLabels = screen.getAllByText('API key')
    // The auth-method toggle renders the label inside a <button>; the field label is a <label>.
    const apiKeyButtonLabel = apiKeyCardLabels.find((el) => el.closest('button')) as HTMLElement
    fireEvent.click(apiKeyButtonLabel.closest('button') as HTMLButtonElement)
    await waitFor(() => {
      expect(idInput.value).toBe('my-custom')
    })
    expect(displayNameInput.value).toBe('My Custom')
    expect(idInput).not.toHaveAttribute('readonly')
    expect(displayNameInput).not.toHaveAttribute('readonly')
  })

  it('shows the ChatGPT plan tier on the provider card instead of "No API key"', async () => {
    enableProviderManagement()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'provider/list') {
        return {
          providers: [
            {
              id: 'openai',
              displayName: 'OpenAI (ChatGPT)',
              protocol: 'openai-responses',
              hasApiKey: false,
              endPoint: '',
              networkTimeoutSeconds: null,
              isImplicit: false,
              authMethod: 'chatgptOAuth',
              chatGptAccountId: 'acct_abcd1234',
              chatGptPlanType: 'plus'
            },
            {
              id: 'openai-no-plan',
              displayName: 'Pending ChatGPT',
              protocol: 'openai-responses',
              hasApiKey: false,
              endPoint: '',
              networkTimeoutSeconds: null,
              isImplicit: false,
              authMethod: 'chatgptOAuth',
              chatGptAccountId: null,
              chatGptPlanType: null
            }
          ]
        }
      }
      return null
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const plusRow = await screen.findByRole('button', { name: 'Use provider OpenAI (ChatGPT)' })
    expect(within(plusRow).getByText('ChatGPT · Plus')).toBeInTheDocument()
    expect(within(plusRow).queryByText('No API key')).not.toBeInTheDocument()

    const notSignedRow = await screen.findByRole('button', { name: 'Use provider Pending ChatGPT' })
    expect(within(notSignedRow).getByText('ChatGPT · Not signed in')).toBeInTheDocument()
  })

  it('renders provider protocol icons and hides provider ids in the provider list', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    expect(await screen.findByText('Provider list')).toBeInTheDocument()

    const openAiRow = await screen.findByRole('button', { name: 'Use provider OpenAI' })
    const openAiChatRow = await screen.findByRole('button', { name: 'Use provider OpenAI Chat' })
    const openAiResponsesRow = await screen.findByRole('button', { name: 'Use provider OpenAI Responses' })
    const anthropicRow = await screen.findByRole('button', { name: 'Use provider Anthropic' })
    expect(openAiRow.querySelector('svg[data-provider-mark="openai"]')).toBeInTheDocument()
    expect(openAiChatRow.querySelector('svg[data-provider-mark="openai"]')).toBeInTheDocument()
    expect(openAiResponsesRow.querySelector('svg[data-provider-mark="openai"]')).toBeInTheDocument()
    expect(anthropicRow.querySelector('svg[data-provider-mark="anthropic"]')).toBeInTheDocument()

    expect(within(openAiRow).queryByText('openai')).not.toBeInTheDocument()
    expect(within(openAiRow).getByText('OpenAI-Legacy')).toBeInTheDocument()
    expect(within(openAiChatRow).getByText('OpenAI-Legacy')).toBeInTheDocument()
    expect(within(openAiResponsesRow).getByText('OpenAI-Responses')).toBeInTheDocument()
    expect(within(anthropicRow).queryByText('anthropic-main')).not.toBeInTheDocument()
    expect(within(openAiRow).getAllByText('OpenAI')).toHaveLength(1)
    expect(within(anthropicRow).getAllByText('Anthropic')).toHaveLength(2)

    fireEvent.click(screen.getByRole('button', { name: 'Edit provider OpenAI' }))
    expect(await screen.findByRole('combobox', { name: 'Protocol' })).toHaveTextContent('OpenAI-Legacy')
  })

  it('shows remembered MainAgent and SubAgent models in each provider row', async () => {
    enableProviderAndSubAgentManagement()
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        providerId: 'openai',
        model: 'main-model-v2',
        providerModels: { openai: 'main-model-v2' }
      },
      userDefaults: { providerModels: {} }
    })
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'subagent/profiles/list') {
        return {
          profiles: [],
          settings: { providerModels: { openai: 'subagent-model-v1' } }
        }
      }
      return defaultSendRequest?.(method, params)
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const openAiRow = await screen.findByRole('button', { name: 'Use provider OpenAI' })

    expect(within(openAiRow).getByText('MainAgent Model: main-model-v2')).toBeInTheDocument()
    expect(within(openAiRow).getByText('SubAgent Model: subagent-model-v1')).toBeInTheDocument()
  })

  it('uses the simplified provider list title in Chinese', async () => {
    settingsGet.mockResolvedValue({ locale: 'zh-Hans', connectionMode: 'stdio', visibleChannels: [] })
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: '模型提供商' }))
    expect(await screen.findByText('提供商列表')).toBeInTheDocument()
    expect(screen.queryByText('个人提供商列表')).not.toBeInTheDocument()
  })

  it('uses provider rows to apply the workspace provider without a restart banner', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    await screen.findByRole('button', { name: 'Edit provider Anthropic' })
    appServerSendRequest.mockClear()

    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'anthropic-main',
        model: 'claude-sonnet-4-5',
        providerModels: { 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })
    expect(screen.queryByText('Changes require a service restart to take effect')).not.toBeInTheDocument()
  })

  it('lets a remote workspace switch away from a provider missing on the remote AppServer', async () => {
    settingsGet.mockResolvedValue({
      locale: 'en',
      connectionMode: 'remote',
      activeRemoteStack: { hostId: 'host-1', stackId: 'stack-1' },
      visibleChannels: []
    })
    workspaceConfigGetCore.mockResolvedValue({
      workspace: {
        providerId: 'codex',
        model: 'gpt-5.5',
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null
      },
      userDefaults: {
        providerId: null,
        model: null,
        apiKey: null,
        endPoint: null,
        welcomeSuggestionsEnabled: null,
        skillsSelfLearningEnabled: null,
        memoryAutoConsolidateEnabled: null,
        dreamsEnabled: null,
        dreamsInterval: null,
        dreamsThreadLookbackCount: null,
        dreamsAutoApply: null,
        defaultApprovalPolicy: null
      }
    })
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'model/list' && params?.providerId === 'codex') {
        return {
          success: false,
          errorMessage: "Model provider 'codex' is not configured."
        }
      }
      return defaultSendRequest?.(method, params)
    })
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    expect(await screen.findByText(/Workspace provider "codex" is not configured/)).toBeInTheDocument()
    const anthropicRow = await screen.findByRole('button', { name: 'Use provider Anthropic' })
    appServerSendRequest.mockClear()

    fireEvent.click(anthropicRow)

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'anthropic-main',
        model: 'claude-sonnet-4-5',
        providerModels: { codex: 'gpt-5.5', 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })
  })

  it('auto-selects the first listed model when switching provider invalidates the workspace model', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const modelSelect = await screen.findByLabelText('MainAgent Model') as HTMLSelectElement
    fireEvent.change(modelSelect, { target: { value: 'deepseek-v4-pro' } })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        model: 'deepseek-v4-pro',
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })
    appServerSendRequest.mockClear()

    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'anthropic-main',
        model: 'claude-sonnet-4-5',
        providerModels: { openai: 'deepseek-v4-pro', 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })
  })

  it('preserves the workspace model when the target provider lists it', async () => {
    enableProviderManagement()
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'model/list') {
        return {
          success: true,
          models: [
            { id: 'deepseek-v4-pro' },
            ...(params?.providerId === 'anthropic-main' ? [{ id: 'claude-sonnet-4-5' }] : [])
          ]
        }
      }
      return defaultSendRequest?.(method, params)
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const modelSelect = await screen.findByRole('combobox', { name: 'MainAgent Model' })
    fireEvent.click(modelSelect)
    fireEvent.click(await screen.findByRole('option', { name: 'deepseek-v4-pro' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        model: 'deepseek-v4-pro',
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })
    appServerSendRequest.mockClear()

    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'anthropic-main',
        model: 'deepseek-v4-pro',
        providerModels: { openai: 'deepseek-v4-pro', 'anthropic-main': 'deepseek-v4-pro' }
      }, 20_000)
    })
  })

  it('restores each provider remembered model when switching back and forth', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))

    // Pick a model for the active provider (openai).
    const modelSelect = await screen.findByLabelText('MainAgent Model') as HTMLSelectElement
    fireEvent.change(modelSelect, { target: { value: 'deepseek-v4-pro' } })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        model: 'deepseek-v4-pro',
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })

    // Switch to Anthropic: its model is not deepseek, so it falls back to the first listed model.
    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'anthropic-main',
        model: 'claude-sonnet-4-5',
        providerModels: { openai: 'deepseek-v4-pro', 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })

    // Switch back to OpenAI: its previously chosen model is restored instead of being discarded.
    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Use provider OpenAI' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        providerId: 'openai',
        model: 'deepseek-v4-pro',
        providerModels: { openai: 'deepseek-v4-pro', 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })
  })

  it('persists the native SubAgent model per provider from Workspace preferences', async () => {
    enableProviderAndSubAgentManagement()
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    const subAgentSettings = { providerModels: {} as Record<string, string> }
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'subagent/profiles/list') {
        return { profiles: [], settings: { ...subAgentSettings } }
      }
      if (method === 'subagent/settings/update') {
        if (params?.providerModels) {
          subAgentSettings.providerModels = params.providerModels as Record<string, string>
        }
        return { settings: { ...subAgentSettings } }
      }
      return defaultSendRequest?.(method, params)
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const subAgentSelect = await screen.findByLabelText('SubAgent model') as HTMLSelectElement
    appServerSendRequest.mockClear()

    fireEvent.change(subAgentSelect, { target: { value: 'deepseek-v4-pro' } })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/settings/update', {
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })
  })

  it('restores each provider remembered native SubAgent model when switching providers', async () => {
    enableProviderAndSubAgentManagement()
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    const subAgentSettings = { providerModels: {} as Record<string, string> }
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'subagent/profiles/list') {
        return { profiles: [], settings: { ...subAgentSettings } }
      }
      if (method === 'subagent/settings/update') {
        if (params?.providerModels) {
          subAgentSettings.providerModels = params.providerModels as Record<string, string>
        }
        return { settings: { ...subAgentSettings } }
      }
      return defaultSendRequest?.(method, params)
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))

    // Pick a native model for the active provider (openai).
    const subAgentSelect = await screen.findByLabelText('SubAgent model') as HTMLSelectElement
    fireEvent.change(subAgentSelect, { target: { value: 'deepseek-v4-pro' } })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/settings/update', {
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })

    // Switch to Anthropic: it has no remembered native model, so it inherits (null) while openai is kept.
    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/settings/update', {
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })

    // Choose a native model for Anthropic.
    const anthropicSubAgentSelect = await screen.findByLabelText('SubAgent model') as HTMLSelectElement
    fireEvent.change(anthropicSubAgentSelect, { target: { value: 'claude-sonnet-4-5' } })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/settings/update', {
        providerModels: { openai: 'deepseek-v4-pro', 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })

    // Switch back to OpenAI: its remembered native model is restored.
    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Use provider OpenAI' }))
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('subagent/settings/update', {
        providerModels: { openai: 'deepseek-v4-pro', 'anthropic-main': 'claude-sonnet-4-5' }
      }, 20_000)
    })
  })

  it('uses a select for listed workspace models and applies the model immediately', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const modelSelect = await screen.findByLabelText('MainAgent Model') as HTMLSelectElement
    appServerSendRequest.mockClear()

    fireEvent.change(modelSelect, { target: { value: 'deepseek-v4-pro' } })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        model: 'deepseek-v4-pro',
        providerModels: { openai: 'deepseek-v4-pro' }
      }, 20_000)
    })
    expect(screen.queryByText('Choose a listed model or type one manually.')).not.toBeInTheDocument()
    expect(screen.queryByText('Changes require a service restart to take effect')).not.toBeInTheDocument()
  })

  it('falls back to manual workspace model entry when model listing is unsupported', async () => {
    enableProviderManagement()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'provider/list') return providerListResult
      if (method === 'model/list') {
        return {
          success: false,
          errorMessage: 'Endpoint does not support model listing.'
        }
      }
      if (method === 'workspace/config/update') return params
      if (method === 'channel/list') return { channels: [] }
      return {}
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    const modelListWarning = await screen.findByText('Endpoint does not support model listing.')
    expect(modelListWarning).toBeInTheDocument()
    const modelInput = await screen.findByLabelText('MainAgent Model') as HTMLInputElement
    appServerSendRequest.mockClear()

    fireEvent.change(modelInput, { target: { value: 'manual-model' } })
    fireEvent.keyDown(modelInput, { key: 'Enter' })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        model: 'manual-model',
        providerModels: { openai: 'manual-model' }
      }, 20_000)
    })
    expect(screen.queryByText('Changes require a service restart to take effect')).not.toBeInTheDocument()
  })

  it('renders provider test results inline without adding the old result card', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model Providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Edit provider Anthropic' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Test' }))

    expect(await screen.findByText('Test succeeded · 2 models found')).toBeInTheDocument()
    expect(screen.queryByText('Provider test succeeded')).not.toBeInTheDocument()
  })

  it('applies remote connection edits through the global connect banner', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connections' }))
    const modeSelect = await screen.findByRole('combobox', { name: 'Connection mode' }) as HTMLSelectElement
    fireEvent.change(modeSelect, { target: { value: 'remote' } })
    fireEvent.change(await screen.findByLabelText('Remote WebSocket URL'), {
      target: { value: 'ws://127.0.0.1:9100/ws' }
    })

    expect(await screen.findByText('Connection changes are staged. Apply them to connect to the remote AppServer.')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: 'Apply & Connect' }))

    await waitFor(() => {
      expect(appServerApplyConnectionSettings).toHaveBeenCalledWith(expect.objectContaining({
        connectionMode: 'remote'
      }))
      expect(appServerApplyConnectionSettings).toHaveBeenCalledWith(expect.objectContaining({
        remote: expect.objectContaining({ url: 'ws://127.0.0.1:9100/ws' })
      }))
      expect(appServerRestartManaged).not.toHaveBeenCalled()
    })
  })

  it('shows active remote stack connections as Servers-managed settings', async () => {
    settingsGet.mockResolvedValueOnce({
      locale: 'en',
      connectionMode: 'remote',
      activeRemoteStack: { hostId: 'host-1', stackId: 'stack-1' },
      visibleChannels: []
    })
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connections' }))

    expect(await screen.findByText('Managed by Servers')).toBeInTheDocument()
    expect(screen.getByText('This remote connection uses the saved server instance. Use Servers to disconnect or change it.')).toBeInTheDocument()
    expect(screen.queryByLabelText('Remote WebSocket URL')).not.toBeInTheDocument()
    expect(screen.queryByText('Enter a remote WebSocket URL before applying Remote mode.')).not.toBeInTheDocument()
    expect(screen.queryByText('Connection changes are staged. Apply them to connect to the remote AppServer.')).not.toBeInTheDocument()
  })

  it('does not persist remote connection edits when connection apply fails', async () => {
    appServerApplyConnectionSettings.mockRejectedValueOnce(new Error('Remote AppServer did not respond within 10 seconds.'))
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connections' }))
    const modeSelect = await screen.findByRole('combobox', { name: 'Connection mode' }) as HTMLSelectElement
    fireEvent.change(modeSelect, { target: { value: 'remote' } })
    fireEvent.change(await screen.findByLabelText('Remote WebSocket URL'), {
      target: { value: 'ws://127.0.0.1:9100/ws' }
    })

    fireEvent.click(await screen.findByRole('button', { name: 'Apply & Connect' }))

    await waitFor(() => {
      expect(appServerApplyConnectionSettings).toHaveBeenCalledOnce()
      expect(settingsSet).not.toHaveBeenCalledWith(expect.objectContaining({
        connectionMode: 'remote'
      }))
      expect(appServerRestartManaged).not.toHaveBeenCalled()
    })
  })

  it('warns and saves full access default approval policy', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    renderView()

    const approvalSelect = await screen.findByRole('combobox', { name: 'Workspace default permissions' }) as HTMLSelectElement
    expect(approvalSelect.value).toBe('default')

    fireEvent.change(approvalSelect, { target: { value: 'autoApprove' } })

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expect(appServerSendRequest).toHaveBeenCalledWith('workspace/config/update', {
        defaultApprovalPolicy: 'autoApprove'
      })
    })
  })

  it('keeps default approval policy when full access warning is cancelled', async () => {
    const confirm = vi.fn().mockResolvedValue(false)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    renderView()

    const approvalSelect = await screen.findByRole('combobox', { name: 'Workspace default permissions' }) as HTMLSelectElement
    expect(approvalSelect.value).toBe('default')

    fireEvent.change(approvalSelect, { target: { value: 'autoApprove' } })

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('workspace/config/update', {
      defaultApprovalPolicy: 'autoApprove'
    })
    expect(approvalSelect.value).toBe('default')
  })
})
