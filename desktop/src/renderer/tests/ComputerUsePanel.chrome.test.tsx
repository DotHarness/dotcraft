import { beforeEach, describe, expect, it, vi } from 'vitest'
import { installDesktopApiMock } from './desktopApiMock'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ComputerUsePanel } from '../components/settings/panels/computerUse/ComputerUsePanel'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()
const chromeCheckSetup = vi.fn()
const chromeInstallNativeHost = vi.fn()
const chromeOpenChrome = vi.fn()

const uninstalledChromePlugin: PluginEntry = {
  id: 'chrome',
  displayName: 'Chrome',
  description: 'Use your existing Chrome tabs and signed-in sites with DotCraft',
  version: '0.1.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Chrome',
    shortDescription: 'Use your existing Chrome tabs and signed-in sites with DotCraft',
    developerName: 'DotHarness',
    category: 'Coding'
  },
  functions: [],
  skills: [{ name: 'chrome', description: 'Chrome', enabled: false }],
  mcpServers: [],
  lspServers: []
}

const installedChromePlugin: PluginEntry = {
  ...uninstalledChromePlugin,
  enabled: true,
  installed: true,
  installable: false,
  skills: [{ name: 'chrome', description: 'Chrome', enabled: true }]
}

let catalog: PluginEntry[] = []
let revision = 1

function setCatalog(plugins: PluginEntry[]): void {
  catalog = plugins
  usePluginStore.setState({ plugins })
}

function renderPanel(): void {
  render(
    <LocaleProvider>
      <ComputerUsePanel />
    </LocaleProvider>
  )
}

async function openChromeDetail(): Promise<void> {
  renderPanel()
  fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))
  await waitFor(() => expect(chromeCheckSetup).toHaveBeenCalledTimes(2))
  await act(() => Promise.all(chromeCheckSetup.mock.results.map((result) => result.value)))
}

function installWindowApi(locale = 'en'): void {
  settingsGet.mockResolvedValue({ locale, connectionMode: 'local' })
  settingsSet.mockResolvedValue(undefined)
  chromeCheckSetup.mockResolvedValue({
    extension: { ok: true, code: 'extensionReady', message: 'DotCraft Chrome extension is ready.' },
    nativeHost: { ok: true, code: 'nativeHostReady', message: 'Chrome Native Host is installed.', safeDetails: { exists: true, hostExists: true, wrapperValid: true } },
    chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.', safeDetails: { processCount: 1 } },
    installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.', safeDetails: { browserCount: 1 } },
    backend: { ok: true, code: 'backendConnected', message: 'Chrome backend is connected.', safeDetails: { candidateCount: 1, protocolVersion: 3, backendId: 'chrome-extension' } },
    bridge: { ok: true, code: 'backendConnected', message: 'Chrome backend is connected.', safeDetails: { candidateCount: 1, protocolVersion: 3, backendId: 'chrome-extension' } }
  })
  chromeInstallNativeHost.mockResolvedValue({ ok: true, manifestPath: 'host.json' })
  chromeOpenChrome.mockResolvedValue({ ok: true })

  installDesktopApiMock({
    platform: 'darwin',
    settings: { get: settingsGet, set: settingsSet },
    workspaceConfig: {
      getCore: vi.fn().mockResolvedValue({
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
    },
    appServer: {
      sendRequest: appServerSendRequest,
      restartManaged: vi.fn(),
      getResolvedBinary: vi.fn().mockResolvedValue({ path: null }),
      pickBinary: vi.fn()
    },
    modules: { list: vi.fn().mockResolvedValue([]) },
    workspace: {
      pickFolder: vi.fn(),
      viewer: { browserUse: { clearCookies: vi.fn() } }
    },
    chrome: {
      checkSetup: chromeCheckSetup,
      installNativeHost: chromeInstallNativeHost,
      openChrome: chromeOpenChrome
    },
    shell: { openExternal: vi.fn() }
  })
}

describe('ComputerUsePanel Chrome control', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installWindowApi()
    revision = 1
    appServerSendRequest.mockImplementation(async (method: string, params?: { id?: string }) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: catalog, diagnostics: [], snapshotRevision: revision }
      if (method === 'plugin/install') {
        catalog = catalog.map((plugin) => plugin.id === params?.id ? installedChromePlugin : plugin)
        return { outcome: 'applied', plugin: installedChromePlugin, snapshotRevision: ++revision }
      }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    useConnectionStore.getState().reset()
    useConnectionStore.setState({
      status: 'connected',
      capabilities: {
        workspaceConfigManagement: true,
        pluginManagement: true
      }
    })
    usePluginStore.setState({
      plugins: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false,
      snapshotRevision: 0,
      completeSnapshotRevision: 0
    })
    setCatalog([uninstalledChromePlugin])
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'general', sidebarCollapsed: false })
  })

  it('turns Chrome on through the install dialog when the plugin is not installed', async () => {
    renderPanel()

    const toggle = await screen.findByRole('switch', { name: 'Toggle Google Chrome' })
    expect(toggle).toHaveAttribute('aria-checked', 'false')
    expect(screen.queryByRole('button', { name: 'Manage' })).not.toBeInTheDocument()
    expect(chromeCheckSetup).not.toHaveBeenCalled()

    fireEvent.click(toggle)
    const dialog = await screen.findByRole('dialog')
    expect(appServerSendRequest).not.toHaveBeenCalledWith('plugin/install', expect.anything())
    fireEvent.click(within(dialog).getByRole('button', { name: 'Add to DotCraft' }))

    await waitFor(() => expect(appServerSendRequest).toHaveBeenCalledWith('plugin/install', { id: 'chrome' }))
    expect(await screen.findByRole('button', { name: 'Refresh status' })).toBeInTheDocument()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(chromeCheckSetup).toHaveBeenCalled()
  })

  it('opens Chrome management details and runs setup checks', async () => {
    setCatalog([installedChromePlugin])

    await openChromeDetail()

    await waitFor(() => expect(chromeCheckSetup).toHaveBeenCalled())
    await waitFor(() => expect(screen.getByText('Google Chrome')).toBeInTheDocument())
    expect(await screen.findByText('Connected')).toBeInTheDocument()
    expect(screen.getByText('Connection status')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Refresh status' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Open Chrome' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Repair Host' })).toBeInTheDocument()
    expect(screen.queryByText('Diagnostics')).not.toBeInTheDocument()
    expect(screen.queryByText('DotCraft extension')).not.toBeInTheDocument()
    expect(screen.queryByText('Chrome backend')).not.toBeInTheDocument()
    expect(screen.queryByText('Extension setup')).not.toBeInTheDocument()
    expect(screen.queryByText('C:\\Chrome\\chrome.exe')).not.toBeInTheDocument()
    expect(screen.queryByText('host.json')).not.toBeInTheDocument()
    expect(screen.queryByText('pekajfcokkicggfjmickmkngmmoojlda')).not.toBeInTheDocument()
    expect(screen.queryByText('Default')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Open extensions' })).not.toBeInTheDocument()
  })

  it('shows the Chrome extensions shortcut only when the extension needs attention', async () => {
    setCatalog([installedChromePlugin])
    chromeCheckSetup.mockResolvedValue({
      extension: { ok: false, code: 'extensionNotReady', message: 'DotCraft Chrome extension is not ready.', action: 'openExtensions' },
      nativeHost: { ok: true, code: 'nativeHostReady', message: 'Chrome Native Host is installed.' },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.' },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.' },
      backend: { ok: true, code: 'backendConnected', message: 'Chrome backend is connected.' },
      bridge: { ok: true, code: 'backendConnected', message: 'Chrome backend is connected.' }
    })

    await openChromeDetail()

    const openExtensions = await screen.findByRole('button', { name: 'Open extensions' })
    fireEvent.click(openExtensions)

    await waitFor(() => expect(chromeOpenChrome).toHaveBeenCalledWith({
      url: 'chrome://extensions'
    }))
    expect(screen.queryByText('Extension setup')).not.toBeInTheDocument()
    expect(screen.queryByText('pekajfcokkicggfjmickmkngmmoojlda')).not.toBeInTheDocument()
    expect(screen.queryByText('C:\\Chrome\\chrome.exe')).not.toBeInTheDocument()
    expect(screen.queryByText('host.json')).not.toBeInTheDocument()
  })

  it('shows a disconnected status when the Chrome backend is down', async () => {
    setCatalog([installedChromePlugin])
    chromeCheckSetup.mockResolvedValue({
      extension: { ok: true, code: 'extensionReady', message: 'DotCraft Chrome extension is ready.' },
      nativeHost: { ok: true, code: 'nativeHostReady', message: 'Chrome Native Host is installed.' },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.' },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.' },
      backend: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' },
      bridge: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' }
    })

    await openChromeDetail()

    expect(await screen.findByText('Disconnected')).toBeInTheDocument()
    expect(screen.queryByText('Diagnostics')).not.toBeInTheDocument()
    expect(screen.queryByText('Chrome backend')).not.toBeInTheDocument()
    expect(screen.queryByText('Make sure Chrome is open, click the DotCraft Chrome extension icon, then refresh status.')).not.toBeInTheDocument()
    expect(screen.queryByText('Chrome backend is disconnected.')).not.toBeInTheDocument()
  })

  it('shows Install Host when the native host is missing', async () => {
    setCatalog([installedChromePlugin])
    chromeCheckSetup.mockResolvedValue({
      extension: { ok: true, code: 'extensionReady', message: 'DotCraft Chrome extension is ready.' },
      nativeHost: {
        ok: false,
        code: 'nativeHostMissing',
        message: 'Chrome Native Host needs to be installed or repaired.',
        action: 'repairNativeHost',
        safeDetails: { exists: false, hostExists: false, wrapperValid: false }
      },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.' },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.' },
      backend: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' },
      bridge: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' }
    })

    await openChromeDetail()

    expect(await screen.findByRole('button', { name: 'Install Host' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install or repair Native Host' })).not.toBeInTheDocument()
  })

  it('shows Repair Host when the native host wrapper needs repair', async () => {
    setCatalog([installedChromePlugin])
    chromeCheckSetup.mockResolvedValue({
      extension: { ok: true, code: 'extensionReady', message: 'DotCraft Chrome extension is ready.' },
      nativeHost: {
        ok: false,
        code: 'nativeHostNeedsRepair',
        message: 'Chrome Native Host needs to be installed or repaired.',
        action: 'repairNativeHost',
        safeDetails: { exists: true, hostExists: true, wrapperValid: false }
      },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.' },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.' },
      backend: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' },
      bridge: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' }
    })

    await openChromeDetail()

    expect(await screen.findByRole('button', { name: 'Repair Host' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install or repair Native Host' })).not.toBeInTheDocument()
    expect(screen.queryByText('C:\\Chrome\\native-host.json')).not.toBeInTheDocument()
  })

  it('repairs the Chrome native host from the detail action', async () => {
    setCatalog([installedChromePlugin])

    await openChromeDetail()
    fireEvent.click(await screen.findByRole('button', { name: 'Repair Host' }))

    await waitFor(() => expect(chromeInstallNativeHost).toHaveBeenCalled())
    expect(chromeCheckSetup).toHaveBeenCalled()
  })

  it('opens the Chrome detail page from the Chrome settings deep link', async () => {
    setCatalog([installedChromePlugin])

    render(
      <LocaleProvider>
        <SettingsView workspacePath="X:\\fixtures\\workspace" openChromeSettingsSeq={1} />
      </LocaleProvider>
    )

    expect(await screen.findByRole('button', { name: 'Refresh status' })).toBeInTheDocument()
    expect(useUIStore.getState().activeSettingsTab).toBe('computerControl')
  })
})
