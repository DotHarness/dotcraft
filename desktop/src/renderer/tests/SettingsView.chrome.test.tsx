import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { SettingsSidebar } from '../components/layout/SettingsSidebar'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const appServerSendRequest = vi.fn()
const chromeCheckSetup = vi.fn()
const chromeInstallNativeHost = vi.fn()
const chromeOpenChrome = vi.fn()

const browserPlugin: PluginEntry = {
  id: 'browser',
  displayName: 'Browser',
  description: 'Control the in-app browser with DotCraft',
  version: '1.0.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Browser',
    shortDescription: 'Control the in-app browser with DotCraft',
    developerName: 'DotHarness',
    category: 'Coding'
  },
  functions: [],
  skills: [{ name: 'browser', description: 'Browser', enabled: false }],
  mcpServers: [],
  lspServers: []
}

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

function renderView(): void {
  render(
    <LocaleProvider>
      <div style={{ display: 'flex', height: 800 }}>
        <SettingsSidebar />
        <SettingsView workspacePath="X:\\fixtures\\workspace" />
      </div>
    </LocaleProvider>
  )
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

  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
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
    }
  })
}

describe('SettingsView Chrome computer control', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    installWindowApi()
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, uninstalledChromePlugin], diagnostics: [] }
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
      plugins: [browserPlugin, uninstalledChromePlugin],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'general', sidebarCollapsed: false })
  })

  it('renders Browser and Computer use navigation labels', async () => {
    renderView()

    const browserNav = await screen.findByRole('button', { name: 'Browser' })
    expect(browserNav).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Computer use' })).toBeInTheDocument()

    fireEvent.click(browserNav)
    expect(await screen.findByText("Manage DotCraft's browser.")).toBeInTheDocument()
  })

  it('renders the Chrome install shortcut when the plugin is not installed', async () => {
    renderView()

    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))

    expect(await screen.findByText('Control')).toBeInTheDocument()
    expect(screen.getByText('Chrome')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Install' })).toBeInTheDocument()
    expect(screen.queryByText('Always allowed apps')).not.toBeInTheDocument()
  })

  it('opens Chrome management details and runs setup checks', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, installedChromePlugin], diagnostics: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    usePluginStore.setState({ plugins: [browserPlugin, installedChromePlugin] })

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))

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
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, installedChromePlugin], diagnostics: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    usePluginStore.setState({ plugins: [browserPlugin, installedChromePlugin] })
    chromeCheckSetup.mockResolvedValue({
      extension: { ok: false, code: 'extensionNotReady', message: 'DotCraft Chrome extension is not ready.', action: 'openExtensions' },
      nativeHost: { ok: true, code: 'nativeHostReady', message: 'Chrome Native Host is installed.' },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.' },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.' },
      backend: { ok: true, code: 'backendConnected', message: 'Chrome backend is connected.' },
      bridge: { ok: true, code: 'backendConnected', message: 'Chrome backend is connected.' }
    })

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))

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
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, installedChromePlugin], diagnostics: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    usePluginStore.setState({ plugins: [browserPlugin, installedChromePlugin] })
    chromeCheckSetup.mockResolvedValue({
      extension: { ok: true, code: 'extensionReady', message: 'DotCraft Chrome extension is ready.' },
      nativeHost: { ok: true, code: 'nativeHostReady', message: 'Chrome Native Host is installed.' },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.' },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.' },
      backend: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' },
      bridge: { ok: false, code: 'backendDisconnected', message: 'Chrome backend is disconnected.', action: 'clickExtensionRefresh' }
    })

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))

    expect(await screen.findByText('Disconnected')).toBeInTheDocument()
    expect(screen.queryByText('Diagnostics')).not.toBeInTheDocument()
    expect(screen.queryByText('Chrome backend')).not.toBeInTheDocument()
    expect(screen.queryByText('Make sure Chrome is open, click the DotCraft Chrome extension icon, then refresh status.')).not.toBeInTheDocument()
    expect(screen.queryByText('Chrome backend is disconnected.')).not.toBeInTheDocument()
  })

  it('shows Install Host when the native host is missing', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, installedChromePlugin], diagnostics: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    usePluginStore.setState({ plugins: [browserPlugin, installedChromePlugin] })
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

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))

    expect(await screen.findByRole('button', { name: 'Install Host' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install or repair Native Host' })).not.toBeInTheDocument()
  })

  it('shows Repair Host when the native host wrapper needs repair', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, installedChromePlugin], diagnostics: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    usePluginStore.setState({ plugins: [browserPlugin, installedChromePlugin] })
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

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))

    expect(await screen.findByRole('button', { name: 'Repair Host' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Install or repair Native Host' })).not.toBeInTheDocument()
    expect(screen.queryByText('C:\\Chrome\\native-host.json')).not.toBeInTheDocument()
  })

  it('repairs the Chrome native host from the detail action', async () => {
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') return { channels: [] }
      if (method === 'plugin/list') return { plugins: [browserPlugin, installedChromePlugin], diagnostics: [] }
      if (method === 'skills/list') return { skills: [] }
      return {}
    })
    usePluginStore.setState({ plugins: [browserPlugin, installedChromePlugin] })

    renderView()
    fireEvent.click(await screen.findByRole('button', { name: 'Computer use' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Manage' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Repair Host' }))

    await waitFor(() => expect(chromeInstallNativeHost).toHaveBeenCalled())
    expect(chromeCheckSetup).toHaveBeenCalled()
  })
})
