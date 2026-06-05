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
import { buildExtensionMainViewKey } from '../utils/desktopExtensionRegistry'
import type { WorkspaceStatusPayload } from '../../preload/api'
import {
  getWhatsNewMediaStateKey,
  type WhatsNewMediaState
} from '../../shared/whatsNew'
import { WHATS_NEW_TEST_RELEASES } from './whatsNewFixtures'
import type { Thread } from '../types/thread'

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
      entry: 'E:\\dotcraft\\plugins\\agent-teams\\desktop\\team-card-board.mjs',
      styles: [],
      permissions: ['appServer:teams/*', 'navigation'],
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

function installApi(
  initialWorkspaceStatus: WorkspaceStatusPayload,
  overrides: {
    settingsGet?: ReturnType<typeof vi.fn>
    settingsSet?: ReturnType<typeof vi.fn>
    appServerSendRequest?: ReturnType<typeof vi.fn>
    onNotification?: ReturnType<typeof vi.fn>
    modulesList?: ReturnType<typeof vi.fn>
    modulesRunning?: ReturnType<typeof vi.fn>
    getReleases?: ReturnType<typeof vi.fn>
    getMediaStates?: ReturnType<typeof vi.fn>
    prefetchMedia?: ReturnType<typeof vi.fn>
    gitListBranches?: ReturnType<typeof vi.fn>
  } = {}
): {
  settingsGet: ReturnType<typeof vi.fn>
  settingsSet: ReturnType<typeof vi.fn>
  getReleases: ReturnType<typeof vi.fn>
  getMediaStates: ReturnType<typeof vi.fn>
  prefetchMedia: ReturnType<typeof vi.fn>
} {
  const pending = new Promise<never>(() => {})
  const settingsGet = overrides.settingsGet ?? vi.fn(() => pending)
  const settingsSet = overrides.settingsSet ?? vi.fn().mockResolvedValue(undefined)
  const appServerSendRequest = overrides.appServerSendRequest ?? vi.fn().mockResolvedValue({})
  const onNotification = overrides.onNotification ?? vi.fn(() => vi.fn())
  const modulesList = overrides.modulesList ?? vi.fn(() => pending)
  const modulesRunning = overrides.modulesRunning ?? vi.fn(() => pending)
  const getReleases = overrides.getReleases ?? vi.fn().mockResolvedValue(WHATS_NEW_TEST_RELEASES)
  const getMediaStates = overrides.getMediaStates ?? vi.fn().mockResolvedValue([])
  const prefetchMedia = overrides.prefetchMedia ?? vi.fn().mockResolvedValue([])
  const gitListBranches = overrides.gitListBranches ?? vi.fn().mockResolvedValue(gitSnapshot())
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
        onMaximizedChange: vi.fn(() => vi.fn()),
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
        onServerRequest: vi.fn(() => vi.fn()),
        sendServerResponse: vi.fn()
      },
      workspace: {
        getStatus: vi.fn(() => pending),
        onStatusChange: vi.fn(() => vi.fn()),
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
        popupAddTabMenu: vi.fn().mockResolvedValue(null)
      }
    }
  })
  return { settingsGet, settingsSet, getReleases, getMediaStates, prefetchMedia }
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
      whatsNewOpenRequestSeq: 0
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
    const params = threadListCall?.[1] as { identity?: { channelContext?: string; workspacePath?: string } } | undefined
    expect(params?.identity?.workspacePath).toBe('/workspace')
    expect(params?.identity?.channelContext).toBe('workspace:/workspace')
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

  it('keeps the Team surface behind the installed and enabled agent-teams plugin', async () => {
    installApi(readyWorkspaceStatus)
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })
    useUIStore.setState({ activeMainView: 'teams' })

    renderApp()

    expect(screen.getByTestId('plugins-view')).toBeInTheDocument()
    await waitFor(() => {
      expect(useUIStore.getState().activeMainView).toBe('skills')
    })
    expect(screen.queryByTestId('desktop-extension-main-view')).not.toBeInTheDocument()
    expect(screen.queryByTestId('teams-view')).not.toBeInTheDocument()
  })

  it('migrates legacy Team view to the Agent Teams desktop extension when installed and enabled', async () => {
    const appServerSendRequest = vi.fn(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: [agentTeamsPlugin], diagnostics: [] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, { appServerSendRequest })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { pluginManagement: true }
    })
    usePluginStore.setState({ plugins: [agentTeamsPlugin] })
    useUIStore.setState({ activeMainView: 'teams' })
    const extensionView = buildExtensionMainViewKey('agent-teams', 'team-card-board', 'teams')

    renderApp()

    await waitFor(() => {
      expect(useUIStore.getState().activeMainView).toBe(extensionView)
    })
    expect(screen.getByTestId('desktop-extension-main-view')).toBeInTheDocument()
    expect(screen.queryByTestId('teams-view')).not.toBeInTheDocument()
  })

  it('reloads thread list with teams origin after the Agent Teams plugin becomes available', async () => {
    const appServerSendRequest = vi.fn(async (method: string) => {
      if (method === 'plugin/list') {
        return { plugins: [agentTeamsPlugin], diagnostics: [] }
      }
      if (method === 'channel/list') {
        return {
          channels: [
            { name: 'acp', category: 'builtin' },
            { name: 'cron', category: 'system' },
            { name: 'teams', category: 'system' }
          ]
        }
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
      expect(threadListCalls.some((call) => {
        const params = call[1] as { crossChannelOrigins?: string[] } | undefined
        return params?.crossChannelOrigins?.includes('teams') === true
      })).toBe(true)
    })

    const teamsCall = appServerSendRequest.mock.calls
      .filter((call) => call[0] === 'thread/list')
      .find((call) => {
        const params = call[1] as { crossChannelOrigins?: string[] } | undefined
        return params?.crossChannelOrigins?.includes('teams') === true
      })
    const params = teamsCall?.[1] as { crossChannelOrigins?: string[]; includeSubAgents?: boolean } | undefined
    expect(params?.crossChannelOrigins).toEqual(['acp', 'teams'])
    expect(params?.includeSubAgents).toBe(true)
  })

  it('reloads thread list with teams origin when Team state changes after plugin state updates', async () => {
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    const onNotification = vi.fn((handler: (payload: { method: string; params?: unknown }) => void) => {
      notificationHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (method: string) => {
      if (method === 'channel/list') {
        return {
          channels: [
            { name: 'acp', category: 'builtin' },
            { name: 'teams', category: 'system' }
          ]
        }
      }
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
        const params = call[1] as { crossChannelOrigins?: string[] } | undefined
        return params?.crossChannelOrigins?.includes('teams') === true
      })).toBe(true)
    })
  })
})
