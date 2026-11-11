import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { SettingsSidebar } from '../components/layout/SettingsSidebar'
import { useConnectionStore } from '../stores/connectionStore'
import { useConfigStore } from '../stores/configStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { activateTestWorkspace, testWorkspaceScope } from './workspaceResourceFixtures'
import { activateWorkspaceResources } from '../stores/providerConnectionScope'
import { chooseValueIn } from './selectHarness'
import { usePendingRestartStore } from '../stores/pendingRestartStore'
import { useToastStore } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import type { ModelPreference } from '../../shared/modelPreference'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
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
    },
    {
      id: 'openai-chat',
      displayName: 'OpenAI Chat',
      protocol: 'openai-chat-completions',
      hasApiKey: true,
      endPoint: 'https://api.openai.com/v1',
      networkTimeoutSeconds: null,
    },
    {
      id: 'openai-responses',
      displayName: 'OpenAI Responses',
      protocol: 'openai-responses',
      hasApiKey: true,
      endPoint: '',
      networkTimeoutSeconds: null,
      supportsImageGeneration: true,
    },
    {
      id: 'anthropic-main',
      displayName: 'Anthropic',
      protocol: 'anthropic',
      hasApiKey: true,
      endPoint: 'https://api.anthropic.com',
      networkTimeoutSeconds: null,
    }
  ]
}

function preference(model: string): ModelPreference {
  return {
    model,
    reasoning: { enabled: false, effort: 'medium', output: 'full' },
    speed: 'standard',
  }
}

let serverConfig: Record<string, any> = {}

function writeServerConfig(keyPath: string, value: unknown): void {
  const segments = keyPath.split('.')
  let target = serverConfig
  for (const segment of segments.slice(0, -1)) {
    target[segment] = { ...(target[segment] ?? {}) }
    target = target[segment]
  }
  target[segments[segments.length - 1]] = value
}

function expectWrite(keyPath: string, value: unknown): void {
  expect(appServerSendRequest).toHaveBeenCalledWith(
    'config/value/write',
    { keyPath, value, mergeStrategy: 'replace' },
    20_000
  )
}

function expectBatchWrite(edits: Record<string, unknown>): void {
  expect(appServerSendRequest).toHaveBeenCalledWith('config/batchWrite', {
    edits: Object.entries(edits).map(([keyPath, value]) => ({ keyPath, value, mergeStrategy: 'replace' }))
  }, 20_000)
}

async function chooseModelPickerValue(label: string, model: string): Promise<void> {
  const trigger = await screen.findByRole('button', { name: label })
  await waitFor(() => expect(trigger).not.toBeDisabled())
  fireEvent.click(trigger)
  const panel = await screen.findByRole('dialog', { name: 'Select model' })
  const [modelButton] = within(panel).getAllByRole('button').filter((button) => button.getAttribute('aria-haspopup') === 'menu')
  fireEvent.click(modelButton)
  fireEvent.click(within(panel).getByRole('menuitem', { name: /Model/ }))
  fireEvent.click(await screen.findByRole('option', { name: model }))
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

describe('SettingsView', () => {
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

    serverConfig = {
      ProviderId: 'openai',
      ProviderPreferences: {},
      InstantInterruptEnabled: true,
      PromptSuggestions: { Enabled: true },
      Memory: { Enabled: true, AutoExtract: false },
      Skills: { IncludeSharedSkills: true },
      Dreams: { Enabled: true, Interval: '1.00:00:00', AutoApply: false },
      Permissions: { DefaultApprovalPolicy: 'default' },
      Tools: { CodeMode: { Mode: 'Only' }, ImageGeneration: { Enabled: true, Provider: null } }
    }
    const dreamsStatus = {
      enabled: true,
      interval: '24:00:00',
      autoApply: false,
      minSessionsSinceLastRun: 3,
      nextRunAt: null,
      running: false,
      lastRun: null as any
    }
    const dreamRuns: any[] = []

    settingsGet.mockResolvedValue({ locale: 'en', connectionMode: 'stdio' })
    settingsSet.mockResolvedValue(undefined)
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'config/read') {
        return {
          config: structuredClone(serverConfig),
          origins: {},
          ...(params?.includeLayers === true
            ? { layers: [{ name: { type: 'user' }, version: 'sha256:user', config: structuredClone(serverConfig) }] }
            : {})
        }
      }
      if (method === 'config/value/write' || method === 'config/batchWrite') {
        const edits = method === 'config/batchWrite'
          ? params?.edits as Array<{ keyPath: string; value: unknown }>
          : [params as { keyPath: string; value: unknown }]
        for (const edit of edits) writeServerConfig(edit.keyPath, edit.value)
        return { status: 'ok', version: 'sha256:test', filePath: 'C:\\sample\\workspace\\.craft\\config.json' }
      }
      if (method === 'dreams/status') {
        return { ...dreamsStatus }
      }
      if (method === 'dreams/run') {
        const runNumber = dreamRuns.length + 1
        const run = {
          id: runNumber === 1 ? 'dream_20260511000000_test' : `dream_20260511000000_test_${runNumber}`,
          status: 'succeeded',
          trigger: 'manual',
          startedAt: '2026-05-11T00:00:00Z',
          endedAt: '2026-05-11T00:00:02Z',
          candidateThreadCount: 2,
          evidenceThreadIds: ['thread-one'],
          evidenceSearchCount: 1,
          evidenceReadCount: 1,
          changes: { added: [], updated: ['package-manager.md'], deleted: ['old-build-note.md'], indexChanged: true },
          reviewStatus: 'pending',
          autoApplied: false,
          summary: 'Merged two package-manager memories.',
          threadId: 'thread_dream_fake',
          turnId: 'turn_dream_fake'
        }
        dreamsStatus.lastRun = run
        dreamRuns.unshift(run)
        return { ...dreamsStatus }
      }
      if (method === 'dreams/list') {
        return { runs: dreamRuns.filter((run) => run.reviewStatus !== 'archived') }
      }
      if (method === 'dreams/archive') {
        const run = dreamRuns.find((item) => item.id === params?.runId) ?? null
        if (run != null) run.reviewStatus = 'archived'
        return { run }
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

    installDesktopApiMock({
      platform: 'win32',
      settings: { get: settingsGet, set: settingsSet },
      appServer: {
        sendRequest: appServerSendRequest,
        onNotification: () => () => undefined,
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
    })

    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        memoryManagement: true
      }
    })
    activateTestWorkspace()
  })

  it('saves the shared skills toggle and restarts managed AppServer', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Personalization' }))
    const toggle = await screen.findByRole('switch', { name: 'Use shared skills' })
    await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'))

    fireEvent.click(toggle)

    await waitFor(() => {
      expectWrite('Skills.IncludeSharedSkills', false)
    })

    fireEvent.click(await screen.findByRole('button', { name: 'Apply & Restart' }))

    await waitFor(() => {
      expect(appServerRestartManaged).toHaveBeenCalledOnce()
    })
  })

  it('archives all Dreams runs sequentially and warns when only some succeed', async () => {
    const confirm = vi.fn().mockResolvedValue(true)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm
    const defaultSendRequest = appServerSendRequest.getMockImplementation()
    appServerSendRequest.mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'dreams/archive' && params?.runId === 'dream_20260511000000_test_2') {
        throw new Error('archive denied')
      }
      return defaultSendRequest?.(method, params)
    })
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
    fireEvent.click(await screen.findByRole('button', { name: 'Manage Dreams' }))
    const runNow = await screen.findByRole('button', { name: 'Run now' })
    fireEvent.click(runNow)
    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.filter(([method]) => method === 'dreams/run')).toHaveLength(1)
    })
    fireEvent.click(runNow)
    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.filter(([method]) => method === 'dreams/run')).toHaveLength(2)
    })

    fireEvent.click(screen.getByRole('button', { name: 'Archive all' }))

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledOnce()
      expect(appServerSendRequest).toHaveBeenCalledWith('dreams/archive', {
        runId: 'dream_20260511000000_test_2'
      }, 20_000)
      expect(appServerSendRequest).toHaveBeenCalledWith('dreams/archive', {
        runId: 'dream_20260511000000_test'
      }, 20_000)
      expect(useToastStore.getState().toasts).toEqual(
        expect.arrayContaining([
          expect.objectContaining({
            type: 'warning'
          })
        ])
      )
    })
  })

  it('auto-selects the first listed model when switching provider invalidates the workspace model', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model providers' }))
    await chooseModelPickerValue('Main model', 'deepseek-v4-pro')

    await waitFor(() => {
      expectWrite('ProviderPreferences.openai', preference('deepseek-v4-pro'))
    })
    appServerSendRequest.mockClear()

    fireEvent.click(screen.getByRole('button', { name: 'Providers' }))
    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))

    await waitFor(() => {
      expectBatchWrite({
        ProviderId: 'anthropic-main',
        'ProviderPreferences.anthropic-main': preference('claude-sonnet-4-5')
      })
    })
  })

  it('saves the endpoint of the preset a saved provider switches to', async () => {
    enableProviderManagement()
    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Model providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Providers' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Edit provider Anthropic' }))
    await chooseValueIn(await screen.findByRole('combobox', { name: 'Provider' }), 'deepseek')
    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Update provider' }))

    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledWith('config/batchWrite', {
      edits: [{ keyPath: 'Providers.anthropic-main', value: { EndPoint: 'https://api.deepseek.com/anthropic' }, mergeStrategy: 'upsert' }]
    }, 20_000))
  })

  it('restores each provider remembered model when switching back and forth', async () => {
    enableProviderManagement()
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Model providers' }))

    await chooseModelPickerValue('Main model', 'deepseek-v4-pro')
    await waitFor(() => {
      expectWrite('ProviderPreferences.openai', preference('deepseek-v4-pro'))
    })

    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Providers' }))
    fireEvent.click(screen.getByRole('button', { name: 'Use provider Anthropic' }))
    await waitFor(() => {
      expectBatchWrite({
        ProviderId: 'anthropic-main',
        'ProviderPreferences.anthropic-main': preference('claude-sonnet-4-5')
      })
    })

    appServerSendRequest.mockClear()
    fireEvent.click(screen.getByRole('button', { name: 'Use provider OpenAI' }))
    await waitFor(() => {
      expectBatchWrite({
        ProviderId: 'openai',
        'ProviderPreferences.openai': preference('deepseek-v4-pro')
      })
    })
  })

  it('reuses loaded workspace configuration, provider details and models when Settings remounts', async () => {
    enableProviderManagement()
    serverConfig.ProviderPreferences = { openai: preference('deepseek-v4-pro') }
    const first = renderView()
    await waitFor(() => {
      expect(useConfigStore.getState().confirmed).toBe(true)
      expect(useProvidersStore.getState().status).toBe('ready')
      expect(useModelCatalogStore.getState().getCatalog('openai').status).toBe('ready')
    })
    const resourceReads = () => appServerSendRequest.mock.calls
      .filter(([method]) => ['config/read', 'provider/list', 'model/list'].includes(method)).map(([method]) => method)
    const reads = resourceReads()
    first.unmount()

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Model providers' }))

    expect(resourceReads()).toEqual(reads)
    expect(reads.filter((method) => method === 'config/read')).toHaveLength(1)
    expect(reads.filter((method) => method === 'provider/list')).toHaveLength(1)
    expect(reads.filter((method) => method === 'model/list')).toHaveLength(1)
  })

  it('does not seed a missing model preference from an unconfirmed cached configuration', async () => {
    enableProviderManagement()
    await useConfigStore.getState().refresh()
    await useProvidersStore.getState().reload()
    await useModelCatalogStore.getState().loadIfNeeded(false, 'openai')
    activateWorkspaceResources(null)
    activateWorkspaceResources(testWorkspaceScope)
    appServerSendRequest.mockClear()

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Model providers' }))
    expect(useConfigStore.getState().confirmed).toBe(false)
    expect(appServerSendRequest.mock.calls.filter(([method]) => method.startsWith('config/'))).toEqual([])

    serverConfig.ProviderPreferences = { openai: preference('deepseek-v4-pro') }
    await act(async () => { await useConfigStore.getState().refresh() })
    expect(appServerSendRequest.mock.calls.filter(([method]) => method === 'config/value/write' || method === 'config/batchWrite'))
      .toEqual([])
  })

  it('offers a retry when the shared provider list fails to load', async () => {
    enableProviderManagement()
    const request = appServerSendRequest.getMockImplementation()!
    let failed = false
    appServerSendRequest.mockImplementation((method: string, ...args: unknown[]) => {
      if (method === 'provider/list' && !failed) {
        failed = true
        return Promise.reject(new Error('provider service unavailable'))
      }
      return request(method, ...args)
    })
    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Retry' }))

    await waitFor(() => expect(useProvidersStore.getState()).toMatchObject({ status: 'ready', error: null }))
    expect(appServerSendRequest.mock.calls.filter(([method]) => method === 'provider/list')).toHaveLength(2)
  })

  it('does not persist remote connection edits when connection apply fails', async () => {
    appServerApplyConnectionSettings.mockRejectedValueOnce(new Error('Remote AppServer did not respond within 10 seconds.'))
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Connections' }))
    const modeSelect = await screen.findByRole('combobox', { name: 'Mode' }, { timeout: 5000 }) as HTMLSelectElement
    await chooseValueIn(modeSelect, 'remote')
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

    useUIStore.setState({ activeSettingsTab: 'personalization' })
    renderView()

    const approvalSelect = await screen.findByRole('combobox', { name: 'Default permissions' }) as HTMLSelectElement
    await waitFor(() => expect(approvalSelect.value).toBe('default'))

    await chooseValueIn(approvalSelect, 'autoApprove')

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
      expectWrite('Permissions.DefaultApprovalPolicy', 'autoApprove')
    })
  })

  it('keeps default approval policy when full access warning is cancelled', async () => {
    const confirm = vi.fn().mockResolvedValue(false)
    ;(window as Window & { __confirmDialog?: unknown }).__confirmDialog = confirm

    useUIStore.setState({ activeSettingsTab: 'personalization' })
    renderView()

    const approvalSelect = await screen.findByRole('combobox', { name: 'Default permissions' }) as HTMLSelectElement
    await waitFor(() => expect(approvalSelect.value).toBe('default'))

    await chooseValueIn(approvalSelect, 'autoApprove')

    await waitFor(() => {
      expect(confirm).toHaveBeenCalledWith(expect.objectContaining({ danger: true }))
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('config/value/write', expect.anything(), expect.anything())
    expect(approvalSelect.value).toBe('default')
  })
})
