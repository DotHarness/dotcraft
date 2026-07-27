/**
 * Mock preload bridge for the browser demo.
 *
 * The Desktop renderer talks to the Electron main process exclusively through
 * `window.api` (see desktop/src/preload/api.d.ts). The demo installs an inert
 * implementation before any renderer module evaluates: methods that feed
 * render-critical data return canned values; everything else resolves to a
 * benign default via a proxy fallback, so no reused component can throw.
 */
import { normalizeLocale } from '../../../desktop/src/shared/locales'
import type { AppLocale } from '../../../desktop/src/shared/locales'

const params = new URLSearchParams(window.location.search)

export const demoTheme: 'dark' | 'light' = params.get('theme') === 'light' ? 'light' : 'dark'
export const demoLocale: AppLocale = normalizeLocale(params.get('lang') ?? 'en')

export const DEMO_WORKSPACE_PATH = '/home/dev/projects/orbit'
export const DEMO_WORKSPACE_NAME = 'orbit'

const noop = (): void => {}
const unsubscribe = (): (() => void) => noop

const connectionStatusPayload = {
  status: 'connected' as const,
  serverInfo: { name: 'DotCraft AppServer', version: 'web-demo', protocolVersion: '1.0' },
  capabilities: {
    threadManagement: true,
    approvalFlow: true,
    modeSwitch: true,
    modelCatalogManagement: true,
    threadGoals: true,
    manualCompaction: true
  }
}

const workspaceStatusPayload = {
  status: 'ready' as const,
  workspacePath: DEMO_WORKSPACE_PATH,
  hasUserConfig: true,
  providers: []
}

const workspaceProjectsPayload = {
  foregroundWorkspacePath: DEMO_WORKSPACE_PATH,
  foregroundProjectId: DEMO_WORKSPACE_PATH,
  secondaryLimit: 8,
  projects: [
    {
      projectId: DEMO_WORKSPACE_PATH,
      kind: 'local' as const,
      path: DEMO_WORKSPACE_PATH,
      name: DEMO_WORKSPACE_NAME,
      state: 'foreground' as const,
      running: true,
      loaded: true,
      threadCount: 0,
      threads: []
    }
  ]
}

const modelListPayload = {
  success: true,
  models: [
    {
      id: 'claude-fable-5',
      ownedBy: 'anthropic',
      reasoning: { supportsDisable: true, efforts: ['low', 'medium', 'high'], outputs: ['none', 'summary', 'full'] }
    },
    {
      id: 'claude-opus-4-8',
      ownedBy: 'anthropic',
      reasoning: { supportsDisable: true, efforts: ['low', 'medium', 'high'], outputs: ['none', 'summary', 'full'] }
    },
    { id: 'claude-haiku-4-5', ownedBy: 'anthropic' }
  ]
}

const settingsPayload = {
  theme: demoTheme,
  locale: demoLocale,
  showThinkingContent: true,
  connectionMode: 'local' as const
}

const workspaceCoreConfigSide = {
  providerId: 'anthropic',
  model: 'claude-fable-5',
  welcomeSuggestionsEnabled: false,
  skillsSelfLearningEnabled: null,
  memoryAutoConsolidateEnabled: null,
  dreamsEnabled: null,
  dreamsInterval: null,
  dreamsThreadLookbackCount: null,
  dreamsAutoApply: null,
  defaultApprovalPolicy: 'default' as const
}

/** Routes `appServer.sendRequest` JSON-RPC methods to canned, benign results. */
function handleAppServerRequest(method: string): unknown {
  switch (method) {
    case 'model/list':
      return modelListPayload
    case 'thread/list':
      return { threads: [] }
    case 'skills/list':
      return { skills: [] }
    case 'mcp/list':
      return { servers: [] }
    case 'cron/list':
      return { jobs: [] }
    default:
      return {}
  }
}

const explicitApi = {
  platform: 'win32' as const,
  initialTheme: demoTheme,
  initialLocale: demoLocale,
  initialWorkspaceStatus: workspaceStatusPayload,
  titleBarOverlayHeight: 0,
  titleBarOverlayRightReserve: 0,
  menu: {
    popupTopLevel: async () => {}
  },
  appServer: {
    sendRequest: async (method: string) => handleAppServerRequest(method),
    listModels: async () => modelListPayload,
    requestWorkspaceConfigSchema: async () => null,
    getConnectionStatus: async () => connectionStatusPayload,
    getResolvedBinary: async () => ({ source: 'bundled' as const, path: null }),
    pickBinary: async () => null,
    restartManaged: async () => {},
    applyConnectionSettings: async () => {},
    onNotification: unsubscribe,
    onConnectionStatus: unsubscribe,
    onServerRequest: unsubscribe,
    sendServerResponse: noop
  },
  workspaceConfig: {
    getCore: async () => ({
      workspace: workspaceCoreConfigSide,
      userDefaults: workspaceCoreConfigSide
    })
  },
  window: {
    setTitle: noop,
    setTitleBarOverlayTheme: async () => {},
    minimize: async () => {},
    toggleMaximize: async () => false,
    close: async () => {},
    isMaximized: async () => false,
    rendererReadyForShow: noop,
    onMaximizedChange: unsubscribe,
    getWorkspacePath: async () => DEMO_WORKSPACE_PATH,
    onOpenChromeSettings: unsubscribe,
    onOpenWhatsNew: unsubscribe,
    onOpenThread: unsubscribe
  },
  shell: {
    openPath: async () => '',
    openExternal: async () => {},
    openAppHandoff: async () => {},
    getProtocolHandlerName: async () => '',
    listEditors: async () => [],
    launchEditor: async () => {},
    launchLocalPathInEditor: async () => {},
    openLocalPath: async () => {},
    revealLocalPath: async () => {},
    showItemInFolder: async () => {}
  },
  file: {
    writeFile: async () => {},
    readFile: async () => '',
    deleteFile: async () => {},
    exists: async () => false
  },
  git: {
    commit: async () => '',
    getBranch: async () => 'main',
    listBranches: async () => ({
      current: 'main',
      detachedHead: null,
      branches: [{ name: 'main', current: true }]
    }),
    checkoutBranch: async () => {},
    createAndCheckoutBranch: async () => {}
  },
  workspace: {
    pickFolder: async () => null,
    createLocalProject: async () => ({ path: DEMO_WORKSPACE_PATH, gitInitialized: true }),
    pickFiles: async () => [],
    getPathForFile: () => '',
    switch: async () => {},
    clearSelection: async () => {},
    getRecent: async () => [],
    getProjects: async () => workspaceProjectsPayload,
    removeRecent: async () => {},
    disconnectRemote: async () => {},
    onProjectsChange: unsubscribe,
    clearRecent: async () => {},
    getStatus: async () => workspaceStatusPayload,
    onStatusChange: unsubscribe,
    listSetupModels: async () => ({ kind: 'unsupported' as const }),
    runSetup: async () => ({}),
    openNewWindow: async () => {},
    checkLock: async () => ({ locked: false }),
    saveImageToTemp: async () => ({ path: '' }),
    readImageAsDataUrl: async () => ({ dataUrl: '' }),
    searchFiles: async () => ({ files: [], indexStatus: 'ready' as const, indexedCount: 0 }),
    viewer: {
      listFiles: async () => ({ files: [], indexStatus: 'ready' as const, indexedCount: 0 }),
      listDir: async () => ({ dirPath: DEMO_WORKSPACE_PATH, entries: [] }),
      classify: async () => ({ contentClass: 'unsupported' as const, mime: '', sizeBytes: 0 }),
      readText: async () => ({ text: '', truncated: false, encoding: 'utf-8' }),
      authorizeFile: async (p: { absolutePath: string }) => ({ absolutePath: p.absolutePath }),
      toViewerUrl: async () => ({ url: '' }),
      browser: {
        create: async (p: { tabId: string }) => ({
          tabId: p.tabId,
          currentUrl: 'about:blank',
          title: '',
          canGoBack: false,
          canGoForward: false,
          loading: false
        }),
        destroy: async () => {},
        navigate: async () => {},
        back: async () => {},
        forward: async () => {},
        reload: async () => {},
        stop: async () => {},
        setBounds: async () => {},
        setVisible: async () => {},
        setActive: async () => {},
        openExternal: async () => {},
        snapshot: async () => null,
        onEvent: unsubscribe
      },
      browserUse: {
        onOpen: unsubscribe,
        onClose: unsubscribe,
        onApprovalRequest: unsubscribe,
        sendApprovalResponse: async () => {},
        clearCookies: async () => ({ ok: true })
      },
      terminal: {
        create: async (p: { tabId: string }) => ({ tabId: p.tabId, pid: 0, shell: '', cwd: DEMO_WORKSPACE_PATH }),
        attach: async (p: { tabId: string }) => ({
          tabId: p.tabId,
          pid: 0,
          shell: '',
          cwd: DEMO_WORKSPACE_PATH,
          buffer: ''
        }),
        write: async () => {},
        resize: async () => {},
        dispose: async () => {},
        onData: unsubscribe,
        onExit: unsubscribe
      }
    }
  },
  modules: {
    list: async () => [],
    userDirectory: async () => ({ path: '' }),
    checkDirectory: async () => ({ exists: false }),
    openFolder: async () => ({ ok: true }),
    pickDirectory: async () => null,
    rescan: async () => [],
    setActiveVariant: async () => ({ ok: true }),
    readConfig: async () => ({ exists: false, config: null }),
    writeConfig: async () => ({ ok: true }),
    start: async () => ({ ok: true }),
    stop: async () => ({ ok: true }),
    running: async () => ({}),
    getLogs: async () => ({ lines: [] }),
    qrStatus: async () => ({ active: false, qrDataUrl: null }),
    onStatusChanged: unsubscribe,
    onQrUpdate: unsubscribe,
    onRescanSummary: unsubscribe
  },
  settings: {
    get: async () => settingsPayload,
    set: async () => {},
    onPinnedThreadIdsChanged: unsubscribe
  },
  skillMarket: {
    search: async () => ({ skills: [], total: 0 }),
    detail: async () => ({}),
    install: async () => ({}),
    prepareDotCraftInstall: async () => ({}),
    bindDotCraftInstall: async () => {},
    cleanupDotCraftInstall: async () => {}
  },
  profile: {
    getGithubIdentity: async () => null
  },
  chrome: {
    checkSetup: async () => ({
      extension: null,
      nativeHost: null,
      chromeRunning: null,
      installedBrowsers: null,
      bridge: null
    }),
    installNativeHost: async () => ({}),
    openChrome: async () => ({})
  },
  desktopExtensions: {
    authorizeExtension: async () => ({ grantId: '' }),
    revokeExtension: async () => ({ ok: true }),
    toPluginUrl: async () => ({ url: '' }),
    fetchJson: async () => ({}),
    postJson: async () => ({}),
    getAppConnectionStatus: async () => ({}),
    startAppConnection: async () => ({}),
    openApp: async () => {}
  },
  whatsNew: {
    getReleases: async () => [],
    getMediaStates: async () => [],
    prefetchMedia: async () => [],
    onMediaStateChanged: unsubscribe
  },
  updates: {
    getState: async () => ({ status: 'idle' }),
    check: async () => ({ status: 'idle' }),
    downloadAndInstall: async () => ({ status: 'idle' }),
    onStateChanged: unsubscribe
  },
  remoteServers: {
    list: async () => [],
    sshConfig: async () => ({}),
    create: async () => ({}),
    update: async () => ({}),
    delete: async () => ({ ok: true }),
    test: async () => ({}),
    listStacks: async () => [],
    discoverStacks: async () => [],
    status: async () => ({}),
    logs: async () => ({ text: '', tail: 0 }),
    action: async () => ({}),
    openInDesktop: async () => ({ ok: false, hostId: '', stackId: '', localPort: 0 }),
    openDashboard: async () => ({ ok: false, localPort: 0 }),
    disconnect: async () => ({ ok: true })
  }
}

/**
 * Safety net for API surface the demo does not model: unknown members resolve
 * to a callable that returns a thenable (works as `await`ed promise) which is
 * itself callable (works as an unsubscribe function).
 */
function inertResult(): unknown {
  const fn = (..._args: unknown[]): unknown => inertResult()
  fn.then = (onFulfilled?: (value: unknown) => unknown): Promise<unknown> =>
    Promise.resolve(undefined).then(onFulfilled)
  fn.catch = (): unknown => fn
  fn.finally = (onFinally?: () => void): unknown => {
    onFinally?.()
    return fn
  }
  return fn
}

function withInertFallback<T extends object>(target: T): T {
  return new Proxy(target, {
    get(obj, prop, receiver) {
      if (prop in obj) {
        const value = Reflect.get(obj, prop, receiver)
        return typeof value === 'object' && value !== null && !Array.isArray(value)
          ? withInertFallback(value as object)
          : value
      }
      if (typeof prop === 'symbol' || prop === 'then') return undefined
      return inertResult()
    }
  })
}

export function installMockApi(): void {
  ;(window as unknown as { api: unknown }).api = withInertFallback(
    explicitApi
  ) as unknown as Window['api']
}
