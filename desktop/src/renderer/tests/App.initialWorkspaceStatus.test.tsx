import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'

vi.hoisted(() => {
  Object.defineProperty(globalThis, '__APP_VERSION__', {
    configurable: true,
    value: '0.1.6'
  })
})

import { LocaleProvider } from '../contexts/LocaleContext'
import { App } from '../App'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useGitStore, type GitBranchListSnapshot } from '../stores/gitStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceStatusPayload } from '../../preload/api'
import {
  getWhatsNewMediaStateKey,
  type WhatsNewMediaState
} from '../../shared/whatsNew'
import type { WorkspaceProjectsPayload } from '../../shared/workspaceProjects'
import { WHATS_NEW_TEST_RELEASES } from './whatsNewFixtures'
import type { Thread, ThreadSummary } from '../types/thread'

vi.mock('../components/layout/CustomMenuBar', () => ({
  CustomMenuBar: () => <div data-testid="custom-menu-bar" />
}))

vi.mock('../components/layout/ThreePanel', () => ({
  ThreePanel: ({
    sidebar,
    conversation,
    detail
  }: {
    sidebar?: ReactNode
    conversation?: ReactNode
    detail?: ReactNode
  }) => (
    <div data-testid="three-panel">
      {sidebar}
      {conversation}
      {detail}
    </div>
  )
}))

vi.mock('../components/layout/Sidebar', () => ({
  Sidebar: ({
    workspaceName,
    workspacePath,
    remoteWorkspace
  }: {
    workspaceName: string
    workspacePath: string
    remoteWorkspace?: boolean
  }) => (
    <div
      data-testid="sidebar"
      data-workspace-name={workspaceName}
      data-workspace-path={workspacePath}
      data-remote-workspace={remoteWorkspace === true ? 'true' : 'false'}
    />
  )
}))

vi.mock('../components/layout/SettingsSidebar', () => ({
  SettingsSidebar: () => <div data-testid="settings-sidebar" />
}))

vi.mock('../components/layout/ConversationPanel', () => ({
  ConversationPanel: () => <div data-testid="conversation-panel" />
}))

vi.mock('../components/layout/DetailPanel', () => ({
  DetailPanel: () => <div data-testid="detail-panel" />
}))

vi.mock('../components/WelcomeScreen', () => ({
  WelcomeScreen: () => <div data-testid="welcome-screen" />
}))

vi.mock('../components/WorkspaceSetupInterstitial', () => ({
  WorkspaceSetupInterstitial: () => <div data-testid="setup-interstitial" />
}))

vi.mock('../components/WorkspaceSetupWizard', () => ({
  WorkspaceSetupWizard: () => <div data-testid="setup-wizard" />
}))

vi.mock('../components/ErrorScreen', () => ({
  ErrorScreen: ({ onOpenSettings }: { onOpenSettings?: () => void }) => (
    <div data-testid="error-screen">
      <button type="button" onClick={onOpenSettings}>Open Settings</button>
    </div>
  )
}))

vi.mock('../components/plugins/PluginsView', () => ({
  PluginsView: () => <div data-testid="plugins-view" />
}))

vi.mock('../components/automations/AutomationsView', () => ({
  AutomationsView: () => <div data-testid="automations-view" />
}))

vi.mock('../components/settings/SettingsView', () => ({
  SettingsView: () => <div data-testid="settings-view" />
}))

vi.mock('../components/channels/ChannelsView', () => ({
  ChannelsView: () => <div data-testid="channels-view" />
}))

vi.mock('../components/teams/TeamsView', () => ({
  TeamsView: () => <div data-testid="teams-view" />
}))

vi.mock('../components/extensions/DesktopExtensionMainView', () => ({
  DesktopExtensionMainView: () => <div data-testid="desktop-extension-main-view" />
}))

vi.mock('../components/detail/QuickOpenDialog', () => ({
  QuickOpenDialog: () => <div data-testid="quick-open-dialog" />
}))

vi.mock('../components/ui/ConfirmDialog', () => ({
  ConfirmDialogHost: () => <div data-testid="confirm-dialog-host" />
}))

vi.mock('../components/ui/ToastContainer', () => ({
  ToastContainer: () => <div data-testid="toast-container" />
}))

vi.mock('../components/whats-new/WhatsNewDialog', () => ({
  WhatsNewDialog: ({ onClose }: { onClose: () => void }) => (
    <div data-testid="whats-new-dialog">
      <button type="button" onClick={onClose}>close whats new</button>
    </div>
  )
}))

const readyWorkspaceStatus: WorkspaceStatusPayload = {
  status: 'ready',
  workspacePath: 'C:\\sample\\workspace',
  hasUserConfig: true,
  providers: []
}

const defaultChatReadyWorkspaceStatus: WorkspaceStatusPayload = {
  status: 'ready',
  workspacePath: 'C:\\Users\\me\\.craft\\workspaces\\chats',
  hasUserConfig: true,
  providers: []
}

const remoteReadyWorkspaceStatus: WorkspaceStatusPayload = {
  ...readyWorkspaceStatus,
  remote: {
    hostId: 'h1',
    stackId: 's1',
    serverName: 'Example Remote',
    stackName: 'demo-stack',
    workspaceDir: '/srv/sample/demo-stack/deploy/workspace',
    appServerWorkspacePath: '/workspace',
    composeDir: '/srv/sample/demo-stack/deploy',
    projectName: 'deploy'
  }
}

const noWorkspaceStatus: WorkspaceStatusPayload = {
  status: 'no-workspace',
  workspacePath: '',
  hasUserConfig: false,
  providers: []
}

const needsSetupWorkspaceStatus: WorkspaceStatusPayload = {
  status: 'needs-setup',
  workspacePath: 'C:\\sample\\needs-setup',
  hasUserConfig: false,
  providers: []
}

const agentTeamsPlugin: PluginEntry = {
  id: 'agent-teams',
  displayName: 'Agent Teams',
  description: 'Run a small team of DotCraft agents.',
  version: '0.1.0',
  enabled: true,
  installed: true,
  installable: true,
  removable: true,
  source: 'builtin',
  rootPath: '',
  interface: {
    displayName: 'Agent Teams',
    shortDescription: 'Run a small team of DotCraft agents.'
  },
  functions: [],
  skills: [],
  apps: [],
  mcpServers: [],
  lspServers: [],
  desktopExtensions: [
    {
      id: 'team-card-board',
      displayName: 'Team card board',
      description: 'Adds the Agent Teams card board to DotCraft Desktop.',
      entry: 'Z:\\__dotcraft_fixture__\\plugins\\agent-teams\\desktop\\team-card-board.mjs',
      styles: [],
      requiredAppIds: [],
      connectOrigins: [],
      surfaces: [
        {
          type: 'mainView',
          viewId: 'teams',
          label: 'Team',
          placement: 'sidebar',
          order: 40
        },
        {
          type: 'pluginDetail',
          title: 'Team Board',
          description: 'Unlocks the card board for Agent Team.'
        }
      ]
    }
  ]
}

function mediaStates(status: WhatsNewMediaState['status']): WhatsNewMediaState[] {
  return WHATS_NEW_TEST_RELEASES.flatMap((release) =>
    release.cards
      .filter((card) => card.media != null)
      .map((card) => ({
        releaseVersion: release.version,
        cardId: card.id,
        status,
        ...(status === 'ready'
          ? { cachedUrl: `file:///tmp/whats-new/${getWhatsNewMediaStateKey(release.version, card.id)}.gif` }
          : {})
      }))
  )
}

function createDeferred<T>() {
  let resolve!: (value: T | PromiseLike<T>) => void
  let reject!: (reason?: unknown) => void
  const promise = new Promise<T>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

function gitSnapshot(current = 'main'): GitBranchListSnapshot {
  return {
    current,
    detachedHead: null,
    branches: [{ name: current, current: true }]
  }
}

function makeThreadSummary(id: string, workspacePath: string, displayName = id): ThreadSummary {
  return {
    id,
    displayName,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    workspacePath,
    effectiveWorkspacePath: workspacePath,
    createdAt: '2026-01-01T00:00:00.000Z',
    lastActiveAt: '2026-01-01T00:00:00.000Z'
  }
}

function makeThread(id: string, workspacePath: string, displayName = id): Thread {
  return {
    ...makeThreadSummary(id, workspacePath, displayName),
    userId: 'local',
    metadata: {},
    turns: []
  }
}

function projectsPayloadFor(path: string, name = path): WorkspaceProjectsPayload {
  return {
    foregroundWorkspacePath: path,
    foregroundProjectId: path,
    secondaryLimit: 8,
    projects: [
      {
        kind: 'local',
        path,
        identityWorkspacePath: path,
        name,
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinnedThreadIds: []
      }
    ]
  }
}

function installApi(
  initialWorkspaceStatus: WorkspaceStatusPayload,
  overrides: {
    settingsGet?: ReturnType<typeof vi.fn>
    settingsSet?: ReturnType<typeof vi.fn>
    appServerSendRequest?: ReturnType<typeof vi.fn>
    onNotification?: ReturnType<typeof vi.fn>
    onServerRequest?: ReturnType<typeof vi.fn>
    onServerRequestRaw?: ReturnType<typeof vi.fn>
    modulesList?: ReturnType<typeof vi.fn>
    modulesRunning?: ReturnType<typeof vi.fn>
    getReleases?: ReturnType<typeof vi.fn>
    getMediaStates?: ReturnType<typeof vi.fn>
    prefetchMedia?: ReturnType<typeof vi.fn>
    gitListBranches?: ReturnType<typeof vi.fn>
    workspaceGetStatus?: ReturnType<typeof vi.fn>
    onWorkspaceStatusChange?: ReturnType<typeof vi.fn>
    workspaceGetProjects?: ReturnType<typeof vi.fn>
    onProjectsChange?: ReturnType<typeof vi.fn>
    windowGetVisibilityState?: ReturnType<typeof vi.fn>
    onWindowVisibilityChanged?: ReturnType<typeof vi.fn>
  } = {}
): {
  settingsGet: ReturnType<typeof vi.fn>
  settingsSet: ReturnType<typeof vi.fn>
  getReleases: ReturnType<typeof vi.fn>
  getMediaStates: ReturnType<typeof vi.fn>
  prefetchMedia: ReturnType<typeof vi.fn>
  appServerSendRequest: ReturnType<typeof vi.fn>
  workspaceGetStatus: ReturnType<typeof vi.fn>
  onWorkspaceStatusChange: ReturnType<typeof vi.fn>
  workspaceGetProjects: ReturnType<typeof vi.fn>
  onProjectsChange: ReturnType<typeof vi.fn>
  windowGetVisibilityState: ReturnType<typeof vi.fn>
  onWindowVisibilityChanged: ReturnType<typeof vi.fn>
} {
  const pending = new Promise<never>(() => {})
  const settingsGet = overrides.settingsGet ?? vi.fn(() => pending)
  const settingsSet = overrides.settingsSet ?? vi.fn().mockResolvedValue(undefined)
  const legacyRequest = overrides.appServerSendRequest ?? vi.fn().mockResolvedValue({})
  const historyReads = new Map<string, Array<Promise<{ thread?: Thread }>>>()
  const appServerSendRequest = vi.fn(async (method: string, params?: Record<string, unknown>) => {
    const threadId = typeof params?.threadId === 'string' ? params.threadId : ''
    if (method === 'thread/turns/list' || method === 'thread/items/list') {
      let reads = historyReads.get(threadId)
      if (!reads) {
        reads = []
        historyReads.set(threadId, reads)
      }
      if (method === 'thread/turns/list') {
        reads.push(legacyRequest('thread/read', { threadId, includeTurns: true }) as Promise<{ thread?: Thread }>)
      }
      const read = reads[reads.length - 1]
      if (!read) return { data: [], nextCursor: null }
      const result = await read
      const turns = result.thread?.turns ?? []
      if (method === 'thread/turns/list') {
        return { data: turns.map(({ items: _items, ...turn }) => turn), nextCursor: null }
      }
      return {
        data: turns.flatMap((turn) => (turn.items ?? []).map((item) => ({ turnId: turn.id, item }))),
        nextCursor: null
      }
    }
    const pendingReads = historyReads.get(threadId)
    if (method === 'thread/read' && pendingReads?.length) {
      const result = await pendingReads.shift()!
      if (pendingReads.length === 0) historyReads.delete(threadId)
      return { ...result, thread: result.thread ? { ...result.thread, turns: [] } : result.thread }
    }
    if (method === 'thread/read') return legacyRequest(method, { ...params, includeTurns: false })
    return legacyRequest(method, params)
  })
  const onNotification = overrides.onNotification ?? vi.fn(() => vi.fn())
  const onServerRequest = overrides.onServerRequest ?? vi.fn(() => vi.fn())
  const onServerRequestRaw = overrides.onServerRequestRaw ?? vi.fn(() => vi.fn())
  const modulesList = overrides.modulesList ?? vi.fn(() => pending)
  const modulesRunning = overrides.modulesRunning ?? vi.fn(() => pending)
  const getReleases = overrides.getReleases ?? vi.fn().mockResolvedValue(WHATS_NEW_TEST_RELEASES)
  const getMediaStates = overrides.getMediaStates ?? vi.fn().mockResolvedValue([])
  const prefetchMedia = overrides.prefetchMedia ?? vi.fn().mockResolvedValue([])
  const gitListBranches = overrides.gitListBranches ?? vi.fn().mockResolvedValue(gitSnapshot())
  const workspaceGetStatus = overrides.workspaceGetStatus ?? vi.fn(() => pending)
  const onWorkspaceStatusChange = overrides.onWorkspaceStatusChange ?? vi.fn(() => vi.fn())
  const workspaceGetProjects = overrides.workspaceGetProjects ?? vi.fn(() => pending)
  const onProjectsChange = overrides.onProjectsChange ?? vi.fn(() => vi.fn())
  const windowGetVisibilityState = overrides.windowGetVisibilityState
    ?? vi.fn().mockResolvedValue({ minimized: false, visible: true, focused: true })
  const onWindowVisibilityChanged = overrides.onWindowVisibilityChanged ?? vi.fn(() => vi.fn())
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      platform: 'win32',
      initialTheme: 'light',
      initialWorkspaceStatus,
      titleBarOverlayHeight: 36,
      titleBarOverlayRightReserve: 138,
      settings: {
        get: settingsGet,
        set: settingsSet
      },
      window: {
        setTitle: vi.fn(),
        setTitleBarOverlayTheme: vi.fn().mockResolvedValue(undefined),
        isMaximized: vi.fn().mockResolvedValue(false),
        getVisibilityState: windowGetVisibilityState,
        onMaximizedChange: vi.fn(() => vi.fn()),
        onVisibilityChanged: onWindowVisibilityChanged,
        onOpenChromeSettings: vi.fn(() => vi.fn()),
        onOpenWhatsNew: vi.fn(() => vi.fn()),
        onOpenThread: vi.fn(() => vi.fn())
      },
      whatsNew: {
        getReleases,
        getMediaStates,
        prefetchMedia,
        onMediaStateChanged: vi.fn(() => vi.fn())
      },
      appServer: {
        sendRequest: appServerSendRequest,
        getConnectionStatus: vi.fn(() => pending),
        onConnectionStatus: vi.fn(() => vi.fn()),
        onNotification,
        onServerRequest,
        onServerRequestRaw,
        sendServerResponse: vi.fn()
      },
      workspace: {
        getStatus: workspaceGetStatus,
        onStatusChange: onWorkspaceStatusChange,
        getProjects: workspaceGetProjects,
        onProjectsChange,
        switch: vi.fn().mockResolvedValue(undefined),
        runSetup: vi.fn().mockResolvedValue(undefined),
        clearSelection: vi.fn().mockResolvedValue(undefined),
        openNewWindow: vi.fn().mockResolvedValue(undefined),
        viewer: {
          browser: {
            destroy: vi.fn().mockResolvedValue(undefined),
            onEvent: vi.fn(() => vi.fn()),
            setVisible: vi.fn().mockResolvedValue(undefined),
            setActive: vi.fn().mockResolvedValue(undefined)
          },
          terminal: {
            dispose: vi.fn().mockResolvedValue(undefined)
          },
          browserUse: {
            onOpen: vi.fn(() => vi.fn()),
            onClose: vi.fn(() => vi.fn()),
            onApprovalRequest: vi.fn(() => vi.fn()),
            sendApprovalResponse: vi.fn().mockResolvedValue(undefined)
          }
        }
      },
      modules: {
        list: modulesList,
        running: modulesRunning,
        onStatusChanged: vi.fn(() => vi.fn())
      },
      file: {
        readFile: vi.fn().mockResolvedValue('')
      },
      git: {
        listBranches: gitListBranches
      },
      menu: {
        popupTopLevel: vi.fn().mockResolvedValue(undefined)
      }
    }
  })
  return {
    settingsGet,
    settingsSet,
    getReleases,
    getMediaStates,
    prefetchMedia,
    appServerSendRequest: legacyRequest,
    workspaceGetStatus,
    onWorkspaceStatusChange,
    workspaceGetProjects,
    onProjectsChange,
    windowGetVisibilityState,
    onWindowVisibilityChanged
  }
}

function renderApp() {
  return render(
    <LocaleProvider>
      <App />
    </LocaleProvider>
  )
}

async function flushPromises(): Promise<void> {
  await act(async () => {
    await Promise.resolve()
    await Promise.resolve()
    await Promise.resolve()
  })
}

describe('App initial workspace status bootstrap', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    usePluginStore.setState({
      plugins: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useUIStore.setState({
      activeMainView: 'conversation',
      pendingProjectThreadOpen: null,
      whatsNewOpenRequestSeq: 0,
      projectsSectionCollapsed: false,
      pinnedSectionCollapsed: false,
      chatsSectionCollapsed: false
    })

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('uses the welcome surface while the no-workspace welcome screen is visible', () => {
    installApi(noWorkspaceStatus)

    const { getByTestId } = renderApp()

    expect(getByTestId('welcome-screen')).toBeInTheDocument()
  })

  it('uses the same plain surface while the setup entry screen is visible', () => {
    installApi(needsSetupWorkspaceStatus)

    const { getByTestId } = renderApp()

    expect(getByTestId('setup-interstitial')).toBeInTheDocument()
  })

  it('renders the connecting launch transition on the first render for a restored ready workspace', () => {
    installApi(readyWorkspaceStatus)

    const { container, queryByTestId } = renderApp()

    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()
    expect(container.querySelector('.workspace-launch-transition__scrim')).toBeInTheDocument()
    expect(container.querySelector('.workspace-launch-transition__logo')).toBeInTheDocument()
    expect(queryByTestId('welcome-screen')).not.toBeInTheDocument()
  })

  it('renders a ready default chat workspace as the main UI without probing Git', async () => {
    const gitListBranches = vi.fn().mockResolvedValue(gitSnapshot())
    installApi(defaultChatReadyWorkspaceStatus, { gitListBranches })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await flushPromises()

    expect(screen.queryByTestId('welcome-screen')).not.toBeInTheDocument()
    expect(screen.getByTestId('three-panel')).toBeInTheDocument()
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-workspace-name', 'Chats')
    expect(gitListBranches).not.toHaveBeenCalled()
  })

  it('hydrates sidebar section collapse preferences from settings on startup', async () => {
    installApi(readyWorkspaceStatus, {
      settingsGet: vi.fn().mockResolvedValue({
        lastSeenWhatsNewVersion: '0.1.6',
        projectsSectionCollapsed: true,
        pinnedSectionCollapsed: true,
        chatsSectionCollapsed: true
      })
    })

    renderApp()

    await waitFor(() => {
      expect(useUIStore.getState().projectsSectionCollapsed).toBe(true)
      expect(useUIStore.getState().pinnedSectionCollapsed).toBe(true)
      expect(useUIStore.getState().chatsSectionCollapsed).toBe(true)
    })
  })

  it('keeps a remote restored workspace covered while the initial connection is disconnected', () => {
    installApi(remoteReadyWorkspaceStatus)

    const { container } = renderApp()

    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()

    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'disconnected',
        errorMessage: 'Reconnecting...'
      })
    })

    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()
    expect(container.querySelector('.workspace-launch-transition--main-reveal')).not.toBeInTheDocument()
    expect(screen.queryByTestId('error-screen')).not.toBeInTheDocument()
  })

  it('shows the error screen for a remote restored workspace initial connection error', async () => {
    installApi(remoteReadyWorkspaceStatus)

    const { container } = renderApp()

    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()

    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'error',
        errorMessage: 'Remote AppServer initialize timed out.',
        errorType: 'handshake-timeout'
      })
    })

    await waitFor(() => {
      expect(screen.getByTestId('error-screen')).toBeInTheDocument()
      expect(container.querySelector('.workspace-launch-transition--error-reveal')).toBeInTheDocument()
    })
    expect(container.querySelector('.workspace-launch-transition--main-reveal')).not.toBeInTheDocument()
  })

  it('keeps a local restored workspace covered while the initial connection is disconnected', () => {
    installApi(readyWorkspaceStatus)

    const { container } = renderApp()

    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()

    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'disconnected',
        errorMessage: 'Reconnecting...'
      })
    })

    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()
    expect(container.querySelector('.workspace-launch-transition--main-reveal')).not.toBeInTheDocument()
    expect(screen.queryByTestId('error-screen')).not.toBeInTheDocument()
  })

  it('waits for the local Git branch probe before revealing a connected restored workspace', async () => {
    const gitProbe = createDeferred<GitBranchListSnapshot>()
    const gitListBranches = vi.fn(() => gitProbe.promise)
    installApi(readyWorkspaceStatus, { gitListBranches })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    const { container } = renderApp()
    await flushPromises()

    expect(gitListBranches).toHaveBeenCalledWith('C:\\sample\\workspace')
    expect(container.querySelector('.workspace-launch-transition--connecting')).toBeInTheDocument()
    expect(container.querySelector('.workspace-launch-transition--main-reveal')).not.toBeInTheDocument()

    gitProbe.resolve(gitSnapshot('main'))

    await waitFor(() => {
      expect(container.querySelector('.workspace-launch-transition--main-reveal')).toBeInTheDocument()
    })
  })

  it('reveals a connected restored workspace after a local Git probe fails', async () => {
    installApi(readyWorkspaceStatus, {
      gitListBranches: vi.fn().mockRejectedValue(new Error('not a git repository'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    const { container } = renderApp()

    await waitFor(() => {
      expect(container.querySelector('.workspace-launch-transition--main-reveal')).toBeInTheDocument()
    })
  })

  it('does not wait for Git probing on remote restored workspaces', async () => {
    const gitListBranches = vi.fn(() => new Promise<never>(() => {}))
    installApi(remoteReadyWorkspaceStatus, { gitListBranches })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    const { container } = renderApp()

    await waitFor(() => {
      expect(container.querySelector('.workspace-launch-transition--main-reveal')).toBeInTheDocument()
    })
    expect(gitListBranches).not.toHaveBeenCalled()
  })

  it('does not auto-open What’s New before remote media is ready', async () => {
    const prefetchMedia = vi.fn().mockResolvedValue(mediaStates('failed'))
    const { getMediaStates } = installApi(readyWorkspaceStatus, {
      settingsGet: vi.fn().mockResolvedValue({ lastSeenWhatsNewVersion: '0.1.5' }),
      getMediaStates: vi.fn().mockResolvedValue(mediaStates('missing')),
      prefetchMedia
    })

    renderApp()

    await waitFor(() => expect(getMediaStates).toHaveBeenCalled())
    await waitFor(() => expect(prefetchMedia).toHaveBeenCalled())
    expect(screen.queryByTestId('whats-new-dialog')).not.toBeInTheDocument()
  })

  it('auto-opens What’s New after background media prefetch completes', async () => {
    installApi(readyWorkspaceStatus, {
      settingsGet: vi.fn().mockResolvedValue({ lastSeenWhatsNewVersion: '0.1.5' }),
      getMediaStates: vi.fn().mockResolvedValue(mediaStates('missing')),
      prefetchMedia: vi.fn().mockResolvedValue(mediaStates('ready'))
    })

    renderApp()

    expect(await screen.findByTestId('whats-new-dialog')).toBeInTheDocument()
  })

  it('marks the visible unseen What’s New version as seen when closed', async () => {
    const settingsSet = vi.fn().mockResolvedValue(undefined)
    installApi(readyWorkspaceStatus, {
      settingsGet: vi.fn().mockResolvedValue({ lastSeenWhatsNewVersion: '0.1.5' }),
      settingsSet,
      getMediaStates: vi.fn().mockResolvedValue(mediaStates('ready')),
      prefetchMedia: vi.fn().mockResolvedValue(mediaStates('ready'))
    })

    renderApp()

    fireEvent.click(await screen.findByRole('button', { name: 'close whats new' }))

    expect(settingsSet).toHaveBeenCalledWith({ lastSeenWhatsNewVersion: '0.1.6' })
  })

  it('opens manual What’s New immediately while remote media prefetch is pending', async () => {
    const prefetchMedia = vi.fn(() => new Promise<never>(() => {}))
    installApi(readyWorkspaceStatus, {
      settingsGet: vi.fn(() => new Promise<never>(() => {})),
      getMediaStates: vi.fn().mockResolvedValue(mediaStates('missing')),
      prefetchMedia
    })

    renderApp()
    act(() => {
      useUIStore.getState().requestOpenWhatsNew()
    })

    expect(await screen.findByTestId('whats-new-dialog')).toBeInTheDocument()
    expect(prefetchMedia).toHaveBeenCalledWith(['0.1.6'])
  })

  it('opens Settings and clears the blocking error screen for invalid remote config', async () => {
    installApi(readyWorkspaceStatus)

    renderApp()
    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'error',
        errorMessage: 'Remote WebSocket URL is invalid.',
        errorType: 'remote-config-invalid'
      })
    })

    fireEvent.click(await screen.findByRole('button', { name: 'Open Settings' }))

    await waitFor(() => {
      expect(screen.queryByTestId('error-screen')).not.toBeInTheDocument()
    })
    expect(await screen.findByTestId('settings-view')).toBeInTheDocument()
    expect(useConnectionStore.getState().status).toBe('disconnected')
    expect(useUIStore.getState().activeMainView).toBe('settings')
    expect(useUIStore.getState().activeSettingsTab).toBe('connection')
  })

  it('uses remote stack metadata for the sidebar and AppServer thread identity', async () => {
    const appServerSendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/list') {
        return { data: [] }
      }
      return {}
    })
    installApi(remoteReadyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()

    const sidebar = await screen.findByTestId('sidebar')
    expect(sidebar).toHaveAttribute('data-workspace-name', 'demo-stack')
    expect(sidebar).toHaveAttribute('data-workspace-path', '/srv/sample/demo-stack/deploy/workspace')
    expect(sidebar).toHaveAttribute('data-remote-workspace', 'true')

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'thread/list')).toBe(true)
    })
    const threadListCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'thread/list')
    const params = threadListCall?.[1] as {
      identity?: { channelContext?: string; workspacePath?: string }
      scope?: string
    } | undefined
    expect(params?.identity?.workspacePath).toBe('/workspace')
    expect(params?.identity?.channelContext).toBe('workspace:/workspace')
    expect(params?.scope).toBe('workspace')
  })

  it('reloads the foreground thread list when the workspace identity changes while connected', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const workspaceBThreads = createDeferred<{ data: ThreadSummary[] }>()
    const appServerSendRequest = vi.fn((method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return workspaceBThreads.promise
        }
        return Promise.resolve({
          data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')]
        })
      }
      return Promise.resolve({})
    })
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    const onProjectsChange = vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
      emitProjects = callback
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange,
      onProjectsChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    const callsAfterInitialLoad = appServerSendRequest.mock.calls.length
    act(() => {
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })
    await flushPromises()
    expect(appServerSendRequest.mock.calls.slice(callsAfterInitialLoad)).toHaveLength(0)

    act(() => {
      emitStatus?.(workspaceBStatus)
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList).toEqual([])
      expect(useThreadStore.getState().threadListProjectKey).toBeNull()
    })
    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === workspaceBStatus.workspacePath
      })).toBe(true)
    })

    workspaceBThreads.resolve({
      data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')]
    })

    await waitFor(() => {
      const state = useThreadStore.getState()
      expect(state.threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(state.threadListProjectKey).toBe('c:/sample/workspace-b')
    })
  })

  it('reloads when workspace status changes before the projects payload settles', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    const appServerSendRequest = vi.fn(async (method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method !== 'thread/list') return {}
      if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
        return { data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')] }
      }
      return { data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')] }
    })
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      emitStatus?.(workspaceBStatus)
    })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === workspaceBStatus.workspacePath
      })).toBe(true)
    })
    await waitFor(() => {
      const state = useThreadStore.getState()
      expect(state.threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(state.threadListProjectKey).toBe('c:/sample/workspace-b')
    })
  })

  it('ignores a stale thread list response from the previous foreground workspace', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const workspaceAThreads = createDeferred<{ data: ThreadSummary[] }>()
    const workspaceBThreads = createDeferred<{ data: ThreadSummary[] }>()
    const appServerSendRequest = vi.fn((method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method !== 'thread/list') return Promise.resolve({})
      if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
        return workspaceBThreads.promise
      }
      return workspaceAThreads.promise
    })
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    const onProjectsChange = vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
      emitProjects = callback
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange,
      onProjectsChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === readyWorkspaceStatus.workspacePath
      })).toBe(true)
    })
    act(() => {
      useThreadStore.getState().setThreadList([
        makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')
      ], readyWorkspaceStatus.workspacePath)
    })

    act(() => {
      emitStatus?.(workspaceBStatus)
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === workspaceBStatus.workspacePath
      })).toBe(true)
    })
    workspaceBThreads.resolve({
      data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')]
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-b'])
    })

    workspaceAThreads.resolve({
      data: [makeThreadSummary('thread-a-late', readyWorkspaceStatus.workspacePath, 'Late A thread')]
    })
    await flushPromises()

    const state = useThreadStore.getState()
    expect(state.threadList.map((thread) => thread.id)).toEqual(['thread-b'])
    expect(state.threadListProjectKey).toBe('c:/sample/workspace-b')
  })

  it('opens a queued background project thread after its workspace becomes foreground', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const appServerSendRequest = vi.fn(async (method: string, params?: { identity?: { workspacePath?: string }; threadId?: string }) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return { data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')] }
        }
        return { data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')] }
      }
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? 'thread-b', workspaceBStatus.workspacePath, 'B thread') }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange: vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
        emitStatus = callback
        return vi.fn()
      }),
      onProjectsChange: vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
        emitProjects = callback
        return vi.fn()
      }),
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: workspaceBStatus.workspacePath,
        workspacePath: workspaceBStatus.workspacePath,
        threadId: 'thread-b'
      })
      emitStatus?.(workspaceBStatus)
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
      expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId: 'thread-b',
        includeTurns: true
      })
    })
  })

  it('clears a queued background project thread when the promoted list does not contain it', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const appServerSendRequest = vi.fn(async (method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return { data: [makeThreadSummary('other-thread', workspaceBStatus.workspacePath, 'Other thread')] }
        }
        return { data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange: vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
        emitStatus = callback
        return vi.fn()
      }),
      onProjectsChange: vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
        emitProjects = callback
        return vi.fn()
      }),
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: workspaceBStatus.workspacePath,
        workspacePath: workspaceBStatus.workspacePath,
        threadId: 'missing-thread'
      })
      emitStatus?.(workspaceBStatus)
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['other-thread'])
      expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    })
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(appServerSendRequest.mock.calls.some((call) => {
      return call[0] === 'thread/read' && (call[1] as { threadId?: string } | undefined)?.threadId === 'missing-thread'
    })).toBe(false)
  })

  it('restores the active thread subscription when the foreground connection is refreshed', async () => {
    const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
      if (method === 'thread/list') {
        return { data: [makeThreadSummary('thread-pinned', readyWorkspaceStatus.workspacePath, 'Pinned thread')] }
      }
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? 'thread-pinned', readyWorkspaceStatus.workspacePath, 'Pinned thread') }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      workspaceGetProjects: vi.fn().mockResolvedValue({
        ...projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'),
        projects: [
          {
            ...projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A').projects[0],
            pinnedThreadIds: ['thread-pinned']
          }
        ]
      })
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toContain('thread-pinned')
    })

    act(() => {
      useThreadStore.getState().setActiveThreadId('thread-pinned')
    })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => (
        call[0] === 'thread/subscribe' &&
        (call[1] as { threadId?: string; replayRecent?: boolean } | undefined)?.threadId === 'thread-pinned'
      ))).toBe(true)
    })

    appServerSendRequest.mockClear()
    act(() => {
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/subscribe', {
        threadId: 'thread-pinned',
        replayRecent: true
      })
    })
  })

  it('removes a stale active thread when thread/read reports not found', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    try {
      const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
        if (method === 'thread/list') {
          return { data: [makeThreadSummary('missing-thread', readyWorkspaceStatus.workspacePath, 'Missing thread')] }
        }
        if (method === 'thread/subscribe') {
          return {}
        }
        if (method === 'thread/read') {
          throw new Error(`Thread not found: ${params?.threadId ?? ''}`)
        }
        return {}
      })
      installApi(readyWorkspaceStatus, {
        appServerSendRequest,
        modulesList: vi.fn().mockResolvedValue([]),
        modulesRunning: vi.fn().mockResolvedValue({}),
        settingsGet: vi.fn().mockResolvedValue({}),
        workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
      })
      useConnectionStore.getState().setStatus({ status: 'connected' })

      renderApp()

      await waitFor(() => {
        expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['missing-thread'])
      })

      act(() => {
        useThreadStore.getState().setActiveThreadId('missing-thread')
      })

      await waitFor(() => {
        expect(appServerSendRequest.mock.calls.some((call) => (
          call[0] === 'thread/read' &&
          (call[1] as { threadId?: string } | undefined)?.threadId === 'missing-thread'
        ))).toBe(true)
      })
      await waitFor(() => {
        const state = useThreadStore.getState()
        expect(state.activeThreadId).toBeNull()
        expect(state.threadList.map((thread) => thread.id)).toEqual([])
      })
    } finally {
      consoleError.mockRestore()
    }
  })

  it('refreshes active thread metadata without reloading turns', async () => {
    vi.useFakeTimers()
    const worktreeThread: Thread = {
      id: 'thread-1',
      userId: 'local',
      workspacePath: 'C:\\sample\\workspace',
      effectiveWorkspacePath: 'C:\\sample\\workspace\\.craft\\worktrees\\dotcraft-handoff',
      displayName: 'Thread',
      status: 'active',
      originChannel: 'dotcraft-desktop',
      createdAt: '2026-01-01T00:00:00.000Z',
      lastActiveAt: '2026-01-01T00:00:00.000Z',
      metadata: {},
      turns: [],
      worktree: {
        id: 'worktree-1',
        sourceThreadId: 'thread-1',
        workspacePath: 'C:\\sample\\workspace',
        sourceWorkspacePath: 'C:\\sample\\workspace',
        path: 'C:\\sample\\workspace\\.craft\\worktrees\\dotcraft-handoff',
        branchName: 'dotcraft/handoff',
        baseRef: 'main',
        head: 'abc123',
        createdAt: '2026-01-01T00:00:00.000Z'
      }
    }
    const localThread: Thread = {
      ...worktreeThread,
      effectiveWorkspacePath: 'C:\\sample\\workspace'
    }
    delete localThread.worktree
    const appServerSendRequest = vi.fn(async (method: string, params?: { includeTurns?: boolean }) => {
      if (method === 'thread/list') return { data: [worktreeThread] }
      if (method === 'thread/read') {
        return { thread: params?.includeTurns === false ? localThread : worktreeThread }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    act(() => {
      useThreadStore.getState().setActiveThreadId('thread-1')
    })
    await flushPromises()

    expect(useThreadStore.getState().activeThread?.worktree?.branchName).toBe('dotcraft/handoff')

    appServerSendRequest.mockClear()
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000)
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(useThreadStore.getState().activeThread?.worktree).toBeNull()
    expect(useThreadStore.getState().activeThread?.effectiveWorkspacePath).toBe('C:\\sample\\workspace')
    expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
      threadId: 'thread-1',
      includeTurns: false
    })
  })

  it('uses workspace scope without channel discovery after the Agent Teams plugin becomes available', async () => {
    const appServerSendRequest = vi.fn(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: [agentTeamsPlugin], diagnostics: [] }
      }
      if (method === 'thread/list') {
        return { data: [] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })

    renderApp()

    await waitFor(() => {
      const threadListCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/list')
      expect(threadListCalls.length).toBeGreaterThan(0)
    })

    const listCall = appServerSendRequest.mock.calls.find((call) => call[0] === 'thread/list')
    const params = listCall?.[1] as {
      scope?: string
      crossChannelOrigins?: string[]
      includeSubAgents?: boolean
    } | undefined
    expect(params?.scope).toBe('workspace')
    expect(params?.crossChannelOrigins).toBeUndefined()
    expect(params?.includeSubAgents).toBe(true)
    expect(appServerSendRequest.mock.calls.some((call) => call[0] === 'channel/list')).toBe(false)
  })

  it('reloads the workspace-scoped thread list when Team state changes', async () => {
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    const onNotification = vi.fn((handler: (payload: { method: string; params?: unknown }) => void) => {
      notificationHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (method: string) => {
      if (method === 'thread/list') {
        return { data: [] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onNotification,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })

    renderApp()

    await waitFor(() => {
      expect(onNotification).toHaveBeenCalled()
    })
    appServerSendRequest.mockClear()
    await act(async () => {
      usePluginStore.setState({ plugins: [agentTeamsPlugin] })
    })

    await act(async () => {
      notificationHandler?.({ method: 'teams/team/changed', params: {} })
    })

    await waitFor(() => {
      const threadListCalls = appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/list')
      expect(threadListCalls.some((call) => {
        const params = call[1] as { scope?: string; crossChannelOrigins?: string[] } | undefined
        return params?.scope === 'workspace' && params.crossChannelOrigins === undefined
      })).toBe(true)
    })
  })

  it('accepts foreground notifications even when the workspace path differs', async () => {
    let notificationHandler: ((payload: {
      method: string
      params?: unknown
      workspacePath?: string
      foreground?: boolean
    }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      onNotification,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      appServerSendRequest: vi.fn(async (method: string) => method === 'thread/list' ? { data: [] } : {})
    })

    renderApp()
    await waitFor(() => {
      expect(onNotification).toHaveBeenCalled()
    })

    await act(async () => {
      notificationHandler?.({
        method: 'thread/started',
        workspacePath: 'F:/different/workspace',
        foreground: true,
        params: {
          thread: {
            id: 'thread-foreground',
            displayName: 'Foreground thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-06-07T00:00:00.000Z',
            lastActiveAt: '2026-06-07T00:00:00.000Z'
          }
        }
      })
    })

    expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toContain('thread-foreground')
  })

  it('ignores secondary notifications even if their workspace path matches after normalization', async () => {
    let notificationHandler: ((payload: {
      method: string
      params?: unknown
      workspacePath?: string
      foreground?: boolean
    }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      onNotification,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      appServerSendRequest: vi.fn(async (method: string) => method === 'thread/list' ? { data: [] } : {})
    })

    renderApp()
    await waitFor(() => {
      expect(onNotification).toHaveBeenCalled()
    })

    await act(async () => {
      notificationHandler?.({
        method: 'thread/started',
        workspacePath: 'F:\\examples\\workspace\\',
        foreground: false,
        params: {
          thread: {
            id: 'thread-secondary',
            displayName: 'Secondary thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-06-07T00:00:00.000Z',
            lastActiveAt: '2026-06-07T00:00:00.000Z'
          }
        }
      })
    })

    expect(useThreadStore.getState().threadList.map((thread) => thread.id)).not.toContain('thread-secondary')
  })

  it('defers active conversation deltas while hidden and reconciles once when restored', async () => {
    const threadId = 'thread-hidden'
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ) => {
      if (method === 'thread/read') {
        return {
          thread: makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath, 'Hidden thread')
        }
      }
      if (method === 'thread/list') return { data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onNotification,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)
    const agentDeltaSpy = vi.spyOn(useConversationStore.getState(), 'onAgentMessageDelta')

    renderApp()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    agentDeltaSpy.mockClear()

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      notificationHandler?.({
        method: 'item/agentMessage/delta',
        params: { threadId, delta: 'hidden output' }
      })
      notificationHandler?.({
        method: 'item/commandExecution/outputDelta',
        params: { threadId, turnId: 'turn-1', itemId: 'item-1', delta: 'line 1' }
      })
    })

    expect(agentDeltaSpy).not.toHaveBeenCalled()
    expect(appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/read')).toHaveLength(0)

    await act(async () => {
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
    })

    await waitFor(() => {
      const fullReads = appServerSendRequest.mock.calls.filter((call) => {
        const params = call[1] as { includeTurns?: boolean } | undefined
        return call[0] === 'thread/read' && params?.includeTurns === true
      })
      expect(fullReads).toHaveLength(1)
    })
  })

  it('parks pending user input until history is restored when the window returns', async () => {
    const threadId = 'thread-input-hidden'
    let includeHistory = false
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ) => {
      if (method === 'thread/read') {
        const thread = makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath, 'Hidden input')
        if (includeHistory && params?.includeTurns === true) {
          thread.turns = [{
            id: 'turn-restored',
            threadId,
            status: 'waitingInput',
            createdAt: '2026-06-07T00:00:00.000Z',
            items: [{
              id: 'item-restored',
              type: 'agentMessage',
              status: 'completed',
              text: 'Restored history'
            }]
          }]
        }
        return {
          thread
        }
      }
      if (method === 'thread/list') return { data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    includeHistory = true

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      serverRequestHandler?.({
        bridgeId: 'bridge-input-hidden',
        method: 'item/tool/requestUserInput',
        params: {
          threadId,
          turnId: 'turn-1',
          requestId: 'request-input-hidden',
          questions: [
            {
              id: 'confirm',
              question: 'Continue?',
              options: [{ label: 'Yes' }, { label: 'No' }]
            }
          ]
        }
      })
    })

    expect(useConversationStore.getState().pendingUserInput).toBeNull()
    expect(useThreadStore.getState().parkedUserInputs.get(threadId)?.bridgeId).toBe('bridge-input-hidden')
    expect(appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/read')).toHaveLength(0)

    await act(async () => {
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
    })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
      expect(useConversationStore.getState().turns[0]?.items[0]?.text).toBe('Restored history')
    })
    expect(useConversationStore.getState().pendingUserInput?.bridgeId).toBe('bridge-input-hidden')
  })

  it('retains a parked request after a failed restore and activates it after retry', async () => {
    const threadId = 'thread-input-restore-retry'
    let retryMode = false
    let reconcileAttempts = 0
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => undefined)
    const appServerSendRequest = vi.fn((
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ): Promise<unknown> => {
      if (method === 'thread/read') {
        if (retryMode && params?.includeTurns === true) {
          reconcileAttempts += 1
          if (reconcileAttempts === 1) return Promise.reject(new Error('temporary read failure'))
          const thread = makeThread(threadId, readyWorkspaceStatus.workspacePath)
          thread.turns = [{
            id: 'turn-retry',
            threadId,
            status: 'waitingInput',
            createdAt: '2026-06-07T00:00:00.000Z',
            items: [{ id: 'item-retry', type: 'agentMessage', status: 'completed', text: 'Restored after retry' }]
          }]
          return Promise.resolve({ thread })
        }
        return Promise.resolve({
          thread: makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath)
        })
      }
      if (method === 'thread/list') {
        return Promise.resolve({ data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] })
      }
      return Promise.resolve({})
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => expect(serverRequestHandler).toBeDefined())
    await flushPromises()
    retryMode = true

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      serverRequestHandler?.({
        bridgeId: 'bridge-input-retry',
        method: 'item/tool/requestUserInput',
        params: {
          threadId,
          turnId: 'turn-retry',
          requestId: 'request-retry',
          questions: [{ id: 'confirm', question: 'Continue?', options: [{ label: 'Yes' }] }]
        }
      })
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
    })

    await waitFor(() => expect(reconcileAttempts).toBe(1))
    expect(useConversationStore.getState().pendingUserInput).toBeNull()
    expect(useThreadStore.getState().parkedUserInputs.has(threadId)).toBe(true)

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
    })

    await waitFor(() => {
      expect(reconcileAttempts).toBe(2)
      expect(useConversationStore.getState().turns[0]?.items[0]?.text).toBe('Restored after retry')
      expect(useConversationStore.getState().pendingUserInput?.bridgeId).toBe('bridge-input-retry')
    })
    expect(consoleError).toHaveBeenCalledWith(
      expect.stringContaining('thread/read reconcile failed'),
      expect.any(Error)
    )
  })

  it('reconciles a pending approval after the window returns', async () => {
    const threadId = 'thread-approval-hidden'
    let approvalPending = false
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string }
    ) => {
      if (method === 'thread/read') {
        const thread = makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath)
        if (approvalPending) {
          thread.turns = [{
            id: 'turn-approval',
            threadId,
            status: 'waitingApproval',
            createdAt: '2026-06-07T00:00:00.000Z',
            items: [{
              id: 'item-preface',
              type: 'agentMessage',
              status: 'completed',
              text: 'Approval context'
            }]
          }]
        }
        return { thread }
      }
      if (method === 'thread/list') return { data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    approvalPending = true

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      serverRequestHandler?.({
        bridgeId: 'bridge-approval-hidden',
        method: 'item/approval/request',
        params: {
          threadId,
          turnId: 'turn-approval',
          requestId: 'request-approval',
          itemId: 'item-approval',
          approvalType: 'shell',
          operation: 'npm test',
          target: readyWorkspaceStatus.workspacePath,
          reason: 'Run tests'
        }
      })
    })

    expect(useConversationStore.getState().pendingApproval).toBeNull()
    expect(useThreadStore.getState().parkedApprovals.get(threadId)?.[0]?.bridgeId)
      .toBe('bridge-approval-hidden')
    expect(appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/read')).toHaveLength(0)

    await act(async () => {
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
    })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    expect(useConversationStore.getState().pendingApproval?.bridgeId).toBe('bridge-approval-hidden')
  })

  it('reconciles an interactive request replayed while the window is foregrounded', async () => {
    const threadId = 'thread-input-replay'
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string }
    ) => {
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath) }
      }
      if (method === 'thread/list') return { data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()

    await act(async () => {
      serverRequestHandler?.({
        bridgeId: 'bridge-input-replay',
        method: 'item/tool/requestUserInput',
        params: {
          threadId,
          turnId: 'turn-replay',
          requestId: 'request-replay',
          questions: [{
            id: 'confirm',
            question: 'Continue?',
            options: [{ label: 'Yes' }]
          }]
        }
      })
    })

    await waitFor(() => {
      const fullReads = appServerSendRequest.mock.calls.filter((call) => {
        const params = call[1] as { includeTurns?: boolean } | undefined
        return call[0] === 'thread/read' && params?.includeTurns === true
      })
      expect(fullReads).toHaveLength(1)
    })
    expect(useConversationStore.getState().pendingUserInput?.bridgeId).toBe('bridge-input-replay')
  })

  it('discards an in-flight snapshot and reads again before activating a parked request', async () => {
    const threadId = 'thread-input-generation'
    const staleRead = createDeferred<{ thread: Thread }>()
    const latestRead = createDeferred<{ thread: Thread }>()
    let holdReconcile = false
    let heldReadCount = 0
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn((
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ): Promise<unknown> => {
      if (method === 'thread/read') {
        if (holdReconcile && params?.includeTurns === true) {
          heldReadCount += 1
          return heldReadCount === 1 ? staleRead.promise : latestRead.promise
        }
        return Promise.resolve({
          thread: makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath)
        })
      }
      if (method === 'thread/list') {
        return Promise.resolve({ data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] })
      }
      return Promise.resolve({})
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      onNotification,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    holdReconcile = true

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      notificationHandler?.({
        method: 'item/agentMessage/delta',
        params: { threadId, delta: 'missed while hidden' }
      })
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
    })
    await waitFor(() => expect(heldReadCount).toBe(1))

    await act(async () => {
      serverRequestHandler?.({
        bridgeId: 'bridge-input-generation',
        method: 'item/tool/requestUserInput',
        params: {
          threadId,
          turnId: 'turn-generation',
          requestId: 'request-generation',
          questions: [{ id: 'confirm', question: 'Continue?', options: [{ label: 'Yes' }] }]
        }
      })
    })
    expect(useConversationStore.getState().pendingUserInput).toBeNull()
    expect(useThreadStore.getState().parkedUserInputs.has(threadId)).toBe(true)

    staleRead.resolve({
      thread: makeThread(threadId, readyWorkspaceStatus.workspacePath, 'Stale snapshot')
    })
    await waitFor(() => expect(heldReadCount).toBe(2))
    expect(useConversationStore.getState().pendingUserInput).toBeNull()
    expect(useThreadStore.getState().parkedUserInputs.has(threadId)).toBe(true)

    const latestThread = makeThread(threadId, readyWorkspaceStatus.workspacePath, 'Latest snapshot')
    latestThread.turns = [{
      id: 'turn-generation',
      threadId,
      status: 'waitingInput',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{
        id: 'item-preface',
        type: 'agentMessage',
        status: 'completed',
        text: 'Latest restored preface'
      }]
    }]
    latestRead.resolve({ thread: latestThread })

    await waitFor(() => {
      expect(useConversationStore.getState().turns[0]?.items[0]?.text).toBe('Latest restored preface')
      expect(useConversationStore.getState().pendingUserInput?.bridgeId).toBe('bridge-input-generation')
    })
  })

  it('does not let an initial thread restore snapshot release a request that arrived mid-read', async () => {
    const threadId = 'thread-input-initial-restore'
    const initialRead = createDeferred<{ thread: Thread }>()
    const reconcileRead = createDeferred<{ thread: Thread }>()
    let fullReadCount = 0
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn((
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ): Promise<unknown> => {
      if (method === 'thread/read' && params?.includeTurns === true) {
        fullReadCount += 1
        return fullReadCount === 1 ? initialRead.promise : reconcileRead.promise
      }
      if (method === 'thread/list') {
        return Promise.resolve({ data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] })
      }
      return Promise.resolve({})
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => {
      expect(fullReadCount).toBe(1)
      expect(serverRequestHandler).toBeDefined()
    })

    await act(async () => {
      serverRequestHandler?.({
        bridgeId: 'bridge-input-initial-restore',
        method: 'item/tool/requestUserInput',
        params: {
          threadId,
          turnId: 'turn-initial-restore',
          requestId: 'request-initial-restore',
          questions: [{ id: 'confirm', question: 'Continue?', options: [{ label: 'Yes' }] }]
        }
      })
    })
    await waitFor(() => expect(fullReadCount).toBe(2))
    expect(useConversationStore.getState().pendingUserInput).toBeNull()

    const staleThread = makeThread(threadId, readyWorkspaceStatus.workspacePath, 'Stale initial restore')
    staleThread.turns = [{
      id: 'turn-stale-restore',
      threadId,
      status: 'completed',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{ id: 'item-stale', type: 'agentMessage', status: 'completed', text: 'Stale preface' }]
    }]
    initialRead.resolve({ thread: staleThread })
    await flushPromises()
    expect(useConversationStore.getState().pendingUserInput).toBeNull()
    expect(useConversationStore.getState().turns[0]?.items[0]?.text).not.toBe('Stale preface')

    const latestThread = makeThread(threadId, readyWorkspaceStatus.workspacePath, 'Latest restore')
    latestThread.turns = [{
      id: 'turn-initial-restore',
      threadId,
      status: 'waitingInput',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{ id: 'item-latest', type: 'agentMessage', status: 'completed', text: 'Latest preface' }]
    }]
    reconcileRead.resolve({ thread: latestThread })

    await waitFor(() => {
      expect(useConversationStore.getState().turns[0]?.items[0]?.text).toBe('Latest preface')
      expect(useConversationStore.getState().pendingUserInput?.bridgeId)
        .toBe('bridge-input-initial-restore')
    })
  })

  it('does not apply a pending-interaction reconcile after switching threads', async () => {
    const firstThreadId = 'thread-reconcile-old'
    const secondThreadId = 'thread-reconcile-new'
    let holdFirstThreadReconcile = false
    let resolveFirstThreadReconcile: ((value: { thread: Thread }) => void) | undefined
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn((
      method: string,
      params?: { threadId?: string }
    ): Promise<unknown> => {
      if (method === 'thread/read') {
        if (params?.threadId === firstThreadId && holdFirstThreadReconcile) {
          return new Promise((resolve) => {
            resolveFirstThreadReconcile = resolve
          })
        }
        return Promise.resolve({
          thread: makeThread(
            params?.threadId ?? firstThreadId,
            readyWorkspaceStatus.workspacePath,
            params?.threadId ?? firstThreadId
          )
        })
      }
      if (method === 'thread/list') {
        return Promise.resolve({
          data: [
            makeThreadSummary(firstThreadId, readyWorkspaceStatus.workspacePath),
            makeThreadSummary(secondThreadId, readyWorkspaceStatus.workspacePath)
          ]
        })
      }
      return Promise.resolve({})
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(firstThreadId)

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(firstThreadId)
    })
    await flushPromises()
    holdFirstThreadReconcile = true

    await act(async () => {
      serverRequestHandler?.({
        bridgeId: 'bridge-reconcile-old',
        method: 'item/tool/requestUserInput',
        params: {
          threadId: firstThreadId,
          turnId: 'turn-old',
          requestId: 'request-old',
          questions: [{
            id: 'confirm',
            question: 'Continue?',
            options: [{ label: 'Yes' }]
          }]
        }
      })
    })
    await waitFor(() => {
      expect(resolveFirstThreadReconcile).toBeDefined()
    })

    await act(async () => {
      useThreadStore.getState().setActiveThreadId(secondThreadId)
    })
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(secondThreadId)
    })

    const staleThread = makeThread(firstThreadId, readyWorkspaceStatus.workspacePath, 'Stale thread')
    staleThread.turns = [{
      id: 'turn-stale',
      threadId: firstThreadId,
      status: 'completed',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{ id: 'item-stale', type: 'userMessage', status: 'completed', text: 'Stale history' }]
    }]
    await act(async () => {
      resolveFirstThreadReconcile?.({ thread: staleThread })
      await Promise.resolve()
    })

    expect(useThreadStore.getState().activeThread?.id).toBe(secondThreadId)
    expect(useConversationStore.getState().turns).toHaveLength(0)
  })

  it('does not reconcile an ordinary focus transition without deferred conversation state', async () => {
    const threadId = 'thread-focus-only'
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string }
    ) => {
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath) }
      }
      if (method === 'thread/list') return { data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
      await Promise.resolve()
    })

    expect(appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/read')).toHaveLength(0)
  })
})
