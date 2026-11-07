import { act, render } from '@testing-library/react'
import { vi } from 'vitest'
import type { ReactNode } from 'react'

vi.hoisted(() => {
  Object.defineProperty(globalThis, '__APP_VERSION__', {
    configurable: true,
    value: '0.1.6'
  })
})

import { LocaleProvider } from '../contexts/LocaleContext'
import { App } from '../App'

import { type GitBranchListSnapshot } from '../stores/gitStore'

import type { WorkspaceStatusPayload } from '../../preload/api'
import type { WorkspaceProjectsPayload } from '../../shared/workspaceProjects'
import { WHATS_NEW_TEST_RELEASES } from './whatsNewFixtures'
import type { Thread, ThreadSummary } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'

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
  WorkspaceSetupInterstitial: ({ onStart, logoAnchorRef }: {
    onStart: () => void
    logoAnchorRef?: (node: HTMLDivElement | null) => void
  }) => (
    <div data-testid="setup-interstitial" ref={logoAnchorRef}>
      <button type="button" onClick={onStart}>Start setup</button>
    </div>
  )
}))

vi.mock('../components/WorkspaceSetupWizard', () => ({
  WorkspaceSetupWizard: ({ deferContent, logoAnchorRef, onRunSetup }: {
    deferContent?: boolean
    logoAnchorRef?: (node: HTMLDivElement | null) => void
    onRunSetup?: (request: unknown, context: unknown) => Promise<unknown>
  }) => (
    <div data-testid="setup-wizard" data-defer={deferContent ? 'true' : 'false'} ref={logoAnchorRef}>
      <button
        type="button"
        onClick={() => {
          void onRunSetup?.(
            { model: 'gpt-5.6', providerMode: 'existing', providerId: 'openai', setAsUserDefault: false },
            { logoRect: { left: 40, top: 40, width: 48, height: 48 }, logoSrc: 'logo.svg' }
          )
        }}
      >
        Create workspace
      </button>
    </div>
  )
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

vi.mock('../components/agents/AgentBuilderView', () => ({
  AgentBuilderView: () => <div data-testid="agent-builder-view" />
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
  const request = overrides.appServerSendRequest ?? vi.fn().mockResolvedValue({})
  const appServerSendRequest = vi.fn(async (
    method: string,
    params?: Record<string, unknown>,
    _timeout?: number,
    options?: { onResponse?(result: unknown): void }
  ) => {
    const result = await request(method, params)
    options?.onResponse?.(result)
    return result
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
  installDesktopApiMock({
      platform: 'win32',
      initialTheme: 'light',
      initialWorkspaceStatus: {
        ...initialWorkspaceStatus,
        remote: initialWorkspaceStatus.remote
          ? {
              source: undefined,
              projectId: undefined,
              appServerWorkspacePath: undefined,
              displayName: undefined,
              endpoint: undefined,
              hostId: undefined,
              stackId: undefined,
              serverName: undefined,
              stackName: undefined,
              workspaceDir: undefined,
              composeDir: undefined,
              projectName: undefined,
              ...initialWorkspaceStatus.remote
            }
          : undefined
      },
      titleBarOverlayHeight: 36,
      titleBarOverlayRightReserve: 138,
      settings: {
        get: settingsGet,
        set: settingsSet,
        onPinnedThreadIdsChanged: undefined
      },
      window: {
        setTitle: vi.fn(),
        setTitleBarOverlayTheme: vi.fn().mockResolvedValue(undefined),
        rendererReadyForShow: vi.fn(),
        isMaximized: vi.fn().mockResolvedValue(false),
        getVisibilityState: windowGetVisibilityState,
        onMaximizedChange: vi.fn(() => vi.fn()),
        onVisibilityChanged: onWindowVisibilityChanged,
        onOpenChromeSettings: vi.fn(() => vi.fn()),
        onOpenWhatsNew: vi.fn(() => vi.fn()),
        onOpenThread: vi.fn(() => vi.fn()),
        onOpenNewChat: vi.fn(() => vi.fn())
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
        sendServerResponse: vi.fn(),
        cancelServerRequest: vi.fn().mockResolvedValue(undefined),
        getUserInputAutoResolutionSnapshot: vi.fn().mockResolvedValue([]),
        onUserInputAutoResolutionChanged: vi.fn(() => vi.fn()),
        setUserInputConversationPresented: vi.fn().mockResolvedValue(undefined),
        recordUserInputConversationActivity: vi.fn().mockResolvedValue(undefined),
        snoozeUserInputAutoResolution: vi.fn().mockResolvedValue(undefined)
      },
      skillMarket: {
        cleanupDotCraftInstall: vi.fn().mockResolvedValue(undefined)
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
      oratorio: undefined,
      menu: {
        popupTopLevel: vi.fn().mockResolvedValue(undefined)
      }
    })
  return {
    settingsGet,
    settingsSet,
    getReleases,
    getMediaStates,
    prefetchMedia,
    appServerSendRequest: request,
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

export { readyWorkspaceStatus, createDeferred, gitSnapshot, makeThreadSummary, makeThread, projectsPayloadFor, installApi, renderApp, flushPromises }
