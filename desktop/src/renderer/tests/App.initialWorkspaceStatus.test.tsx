import { act, render, waitFor } from '@testing-library/react'
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
import { createThreadConversation, getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { useGitStore, type GitBranchListSnapshot } from '../stores/gitStore'
import { usePluginStore } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceStatusPayload } from '../../preload/api'
import type { WorkspaceProjectsPayload } from '../../shared/workspaceProjects'
import { WHATS_NEW_TEST_RELEASES } from './whatsNewFixtures'
import type { Thread, ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
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
  const legacyRequest = overrides.appServerSendRequest ?? vi.fn().mockResolvedValue({})
  // Serves the paged history methods from the legacy `thread/read(includeTurns)` mocks:
  // one turns page per legacy read, then items served per turn from that same snapshot.
  const historyReads = new Map<string, Promise<{ thread?: Thread }>>()
  const historyTurns = new Map<string, NonNullable<Thread['turns']>>()
  const appServerSendRequest = vi.fn(async (method: string, params?: Record<string, unknown>) => {
    const threadId = typeof params?.threadId === 'string' ? params.threadId : ''
    if (method === 'thread/turns/list') {
      const read = legacyRequest('thread/read', { threadId, includeTurns: true }) as Promise<{ thread?: Thread }>
      historyReads.set(threadId, read)
      const turns = (await read).thread?.turns ?? []
      historyTurns.set(threadId, turns)
      return { data: turns.map(({ items: _items, ...turn }) => turn), nextCursor: null }
    }
    if (method === 'thread/items/list') {
      const turn = (historyTurns.get(threadId) ?? []).find((entry) => entry.id === params?.turnId)
      return {
        data: (turn?.items ?? []).map((item) => ({ turnId: turn?.id, item })),
        nextCursor: null
      }
    }
    if (method === 'thread/read') {
      const pending = historyReads.get(threadId)
      if (pending) {
        historyReads.delete(threadId)
        const result = await pending
        return { ...result, thread: result.thread ? { ...result.thread, turns: [] } : result.thread }
      }
      return legacyRequest(method, { ...params, includeTurns: false })
    }
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

  it('does not send the previous workspace thread subscription through a promoted connection', async () => {
    const sharedThreadId = 'shared-thread'
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    const workspaceBThreads = createDeferred<{ data: ThreadSummary[] }>()
    const delayedWorkspaceASubscribe = createDeferred<Record<string, never>>()
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let activeWorkspace: 'a' | 'b' = 'a'
    let delayWorkspaceASubscribe = true
    const subscriptionRoutes: Array<{ method: string; threadId: string; workspace: 'a' | 'b' }> = []
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { identity?: { workspacePath?: string }; threadId?: string }
    ) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return workspaceBThreads.promise
        }
        return { data: [makeThreadSummary(sharedThreadId, readyWorkspaceStatus.workspacePath, 'A thread')] }
      }
      if (method === 'thread/subscribe' || method === 'thread/unsubscribe') {
        subscriptionRoutes.push({
          method,
          threadId: params?.threadId ?? '',
          workspace: activeWorkspace
        })
        if (
          method === 'thread/subscribe' &&
          params?.threadId === sharedThreadId &&
          activeWorkspace === 'a' &&
          delayWorkspaceASubscribe
        ) {
          delayWorkspaceASubscribe = false
          return delayedWorkspaceASubscribe.promise
        }
        return {}
      }
      if (method === 'thread/read') {
        const workspace = activeWorkspace === 'b'
          ? workspaceBStatus.workspacePath
          : readyWorkspaceStatus.workspacePath
        return { thread: makeThread(params?.threadId ?? '', workspace) }
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
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual([sharedThreadId])
    })
    act(() => {
      useThreadStore.getState().setActiveThreadId(sharedThreadId)
    })
    await waitFor(() => {
      expect(subscriptionRoutes).toContainEqual({
        method: 'thread/subscribe',
        threadId: sharedThreadId,
        workspace: 'a'
      })
    })

    subscriptionRoutes.length = 0
    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: workspaceBStatus.workspacePath,
        workspacePath: workspaceBStatus.workspacePath,
        threadId: sharedThreadId
      })
      activeWorkspace = 'b'
      emitStatus?.(workspaceBStatus)
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })
    await flushPromises()

    expect(subscriptionRoutes).toEqual([])

    workspaceBThreads.resolve({
      data: [makeThreadSummary(sharedThreadId, workspaceBStatus.workspacePath, 'B thread')]
    })
    await waitFor(() => {
      expect(useThreadStore.getState().activeThreadId).toBe(sharedThreadId)
      expect(subscriptionRoutes).toEqual([{
        method: 'thread/subscribe',
        threadId: sharedThreadId,
        workspace: 'b'
      }])
    })

    delayedWorkspaceASubscribe.resolve({})
    await flushPromises()
    expect(useThreadStore.getState().activeThreadId).toBe(sharedThreadId)
    expect(subscriptionRoutes).toEqual([{
      method: 'thread/subscribe',
      threadId: sharedThreadId,
      workspace: 'b'
    }])

    subscriptionRoutes.length = 0
    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: readyWorkspaceStatus.workspacePath,
        workspacePath: readyWorkspaceStatus.workspacePath,
        threadId: sharedThreadId
      })
      activeWorkspace = 'a'
      emitStatus?.(readyWorkspaceStatus)
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })

    await waitFor(() => {
      expect(useThreadStore.getState().activeThreadId).toBe(sharedThreadId)
      expect(subscriptionRoutes).toEqual([{
        method: 'thread/subscribe',
        threadId: sharedThreadId,
        workspace: 'a'
      }])
    })
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
          throw Object.assign(new Error(`Thread not found: ${params?.threadId ?? ''}`), { code: 'threadNotFound', rpcCode: -32010 })
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

  it('keeps the thread and records the load error when thread/read fails for another reason', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    try {
      const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
        if (method === 'thread/list') {
          return { data: [makeThreadSummary('broken-thread', readyWorkspaceStatus.workspacePath, 'Broken thread')] }
        }
        if (method === 'thread/subscribe') {
          return {}
        }
        if (method === 'thread/read') {
          throw Object.assign(new Error('Thread history is unavailable.'), {
            rpcCode: -32095,
            data: { messageKey: 'errors.threadHistoryUnavailable', fallbackText: 'Thread history is unavailable.' }
          })
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
        expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['broken-thread'])
      })

      act(() => {
        useThreadStore.getState().setActiveThreadId('broken-thread')
      })

      await waitFor(() => {
        expect(appServerSendRequest.mock.calls.some((call) => (
          call[0] === 'thread/read' &&
          (call[1] as { threadId?: string } | undefined)?.threadId === 'broken-thread'
        ))).toBe(true)
      })
      await waitFor(() => {
        const state = useThreadStore.getState()
        expect(state.activeThreadId).toBe('broken-thread')
        expect(state.threadList.map((thread) => thread.id)).toEqual(['broken-thread'])
        expect(state.activeThreadLoadError).toEqual({ threadId: 'broken-thread', message: 'Thread history is unavailable.' })
      })
    } finally {
      consoleError.mockRestore()
    }
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

    renderApp()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    const agentDeltaSpy = vi.spyOn(useConversationStore.getState(), 'onAgentMessageDelta')

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
          isBlocking: false,
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
          isBlocking: false,
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
          isBlocking: false,
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
          isBlocking: false,
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

  it('preserves pending user input metadata across a thread switch', async () => {
    const firstThreadId = 'thread-input-old'
    const secondThreadId = 'thread-input-new'
    let firstThreadWaitingInput = false
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string }
    ) => {
      if (method === 'thread/read') {
        const threadId = params?.threadId ?? firstThreadId
        const thread = makeThread(threadId, readyWorkspaceStatus.workspacePath, threadId)
        if (threadId === firstThreadId && firstThreadWaitingInput) {
          thread.runtime = {
            running: true,
            waitingOnApproval: false,
            waitingOnInput: true,
            waitingOnPlanConfirmation: false
          }
        }
        return {
          thread
        }
      }
      if (method === 'thread/list') {
        return {
          data: [
            makeThreadSummary(firstThreadId, readyWorkspaceStatus.workspacePath),
            makeThreadSummary(secondThreadId, readyWorkspaceStatus.workspacePath)
          ]
        }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
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

    act(() => {
      useConversationStore.getState().onUserInputRequest('bridge-input-switch', {
        threadId: firstThreadId,
        turnId: 'turn-input-original',
        requestId: 'request-input-switch',
        isBlocking: true,
        questions: [{ id: 'confirm', question: 'Continue?', options: [{ label: 'Yes' }] }]
      })
      useConversationStore.setState({ activeTurnId: 'turn-transient' })
      firstThreadWaitingInput = true
    })

    await act(async () => {
      useThreadStore.getState().setActiveThreadId(secondThreadId)
    })
    await waitFor(() => {
      const parked = useThreadStore.getState().parkedUserInputs.get(firstThreadId)
      expect(parked?.turnId).toBe('turn-input-original')
      expect(parked?.rawParams.turnId).toBe('turn-input-original')
      expect(parked?.rawParams.isBlocking).toBe(true)
    })

    await act(async () => {
      useThreadStore.getState().setActiveThreadId(firstThreadId)
    })
    await waitFor(() => {
      const pending = useConversationStore.getState().pendingUserInput
      expect(pending?.bridgeId).toBe('bridge-input-switch')
      expect(pending?.turnId).toBe('turn-input-original')
      expect(pending?.isBlocking).toBe(true)
    })
  })
  it('applies an event for a non-active thread with live state only to that thread', async () => {
    const activeThreadId = 'thread-active'
    const backgroundThreadId = 'thread-background'
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? activeThreadId, readyWorkspaceStatus.workspacePath) }
      }
      if (method === 'thread/list') {
        return { data: [makeThreadSummary(activeThreadId, readyWorkspaceStatus.workspacePath)] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onNotification,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(activeThreadId)

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(activeThreadId)
    })
    await flushPromises()
    const background = createThreadConversation(backgroundThreadId)

    await act(async () => {
      for (const threadId of [backgroundThreadId, 'thread-without-state']) {
        notificationHandler?.({
          method: 'turn/started',
          params: { turn: { id: `turn-${threadId}`, threadId, status: 'running', items: [] } }
        })
        notificationHandler?.({
          method: 'thread/queue/updated',
          params: { threadId, queuedInputs: [{ id: `queued-${threadId}` }] }
        })
      }
    })

    expect(background.getState().turns.map((turn) => turn.id)).toEqual([`turn-${backgroundThreadId}`])
    expect(background.getState().turnStatus).toBe('running')
    expect(background.getState().queuedInputs).toHaveLength(1)
    expect(useConversationStore.getState().turns).toHaveLength(0)
    expect(useConversationStore.getState().turnStatus).toBe('idle')
    expect(useConversationStore.getState().queuedInputs).toHaveLength(0)
    expect(getThreadConversation('thread-without-state')).toBeUndefined()
  })
})

async function renderWarmThreadsApp(
  threadIds: string[],
  workspace: {
    onWorkspaceStatusChange?: ReturnType<typeof vi.fn>
    onProjectsChange?: ReturnType<typeof vi.fn>
    workspaceGetProjects?: ReturnType<typeof vi.fn>
  } = {}
) {
  let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
  let serverRequestHandler: ((payload: { bridgeId: string; method: string; params?: unknown }) => void) | undefined
  const onNotification = vi.fn((handler: typeof notificationHandler) => {
    notificationHandler = handler
    return vi.fn()
  })
  const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
    serverRequestHandler = handler
    return vi.fn()
  })
  const legacyRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
    if (method === 'thread/read') {
      return { thread: makeThread(params?.threadId ?? threadIds[0], readyWorkspaceStatus.workspacePath) }
    }
    if (method === 'thread/list') {
      return { data: threadIds.map((threadId) => makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)) }
    }
    return {}
  })
  const { appServerSendRequest } = installApi(readyWorkspaceStatus, {
    appServerSendRequest: legacyRequest,
    onNotification,
    onServerRequest,
    settingsGet: vi.fn().mockResolvedValue({}),
    modulesList: vi.fn().mockResolvedValue([]),
    modulesRunning: vi.fn().mockResolvedValue({}),
    ...workspace
  })
  useConnectionStore.getState().setStatus({ status: 'connected' })
  renderApp()
  await waitFor(() => {
    expect(useThreadStore.getState().threadList).toHaveLength(threadIds.length)
  })

  const callsTo = (method: string, threadId?: string) => appServerSendRequest.mock.calls.filter((call) =>
    call[0] === method && (threadId == null || (call[1] as { threadId?: string } | undefined)?.threadId === threadId))

  return {
    appServerSendRequest,
    callsTo,
    notify: (method: string, params: unknown) => notificationHandler?.({ method, params }),
    request: (bridgeId: string, method: string, params: unknown) => serverRequestHandler?.({ bridgeId, method, params }),
    async open(threadId: string) {
      await act(async () => {
        useThreadStore.getState().setActiveThreadId(threadId)
      })
      await waitFor(() => {
        expect(useThreadStore.getState().activeThread?.id).toBe(threadId)
      })
      await flushPromises()
      await flushPromises()
    }
  }
}

function startedTurn(threadId: string, turnId: string) {
  return { turn: { id: turnId, threadId, status: 'running', items: [] } }
}

function completedTurn(threadId: string, turnId: string) {
  return { turn: { id: turnId, threadId, status: 'completed', items: [] } }
}

function runtimeChanged(threadId: string, runtime: Partial<ThreadRuntimeSnapshot>) {
  return {
    threadId,
    runtime: {
      running: false,
      waitingOnApproval: false,
      waitingOnInput: false,
      waitingOnPlanConfirmation: false,
      ...runtime
    }
  }
}

describe('warm threads', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useUIStore.setState({ activeMainView: 'conversation', pendingProjectThreadOpen: null })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reopens a warm thread from its live state without reading or resubscribing', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => {
      app.notify('turn/started', startedTurn('thread-a', 'turn-a'))
      app.notify('turn/completed', completedTurn('thread-a', 'turn-a'))
    })
    await app.open('thread-b')

    expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(0)
    app.appServerSendRequest.mockClear()
    act(() => {
      useThreadStore.getState().setActiveThreadId('thread-a')
    })

    expect(useThreadStore.getState().activeThread?.id).toBe('thread-a')
    expect(useConversationStore.getState().turns.map((turn) => turn.id)).toEqual(['turn-a'])
    await flushPromises()
    for (const method of ['thread/read', 'thread/turns/list', 'thread/items/list', 'thread/subscribe']) {
      expect(app.callsTo(method)).toHaveLength(0)
    }
    expect(getThreadConversation('thread-b')).toBeDefined()
  })

  it('shows a turn that finished while the thread was warm', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => { app.notify('turn/started', startedTurn('thread-a', 'turn-a')) })
    await app.open('thread-b')

    await act(async () => { app.notify('turn/completed', completedTurn('thread-a', 'turn-a')) })
    app.appServerSendRequest.mockClear()
    await app.open('thread-a')

    expect(useConversationStore.getState().turnStatus).toBe('idle')
    expect(useConversationStore.getState().turns.map((turn) => [turn.id, turn.status])).toEqual([['turn-a', 'completed']])
    expect(app.callsTo('thread/read', 'thread-a')).toHaveLength(0)
  })

  it('reconciles a reopened warm thread once when it missed its completion', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => { app.notify('turn/started', startedTurn('thread-a', 'turn-a')) })
    await app.open('thread-b')
    await act(async () => { app.notify('thread/status/changed', runtimeChanged('thread-a', {})) })

    app.appServerSendRequest.mockClear()
    await app.open('thread-a')

    await waitFor(() => {
      expect(app.callsTo('thread/read', 'thread-a')).toHaveLength(1)
    })
    expect(app.callsTo('thread/subscribe')).toHaveLength(0)
  })

  it('makes an approval requested while warm actionable on reopen', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => { app.notify('turn/started', startedTurn('thread-a', 'turn-a')) })
    await app.open('thread-b')

    await act(async () => {
      app.request('bridge-warm', 'item/approval/request', {
        threadId: 'thread-a',
        turnId: 'turn-a',
        requestId: 'request-warm',
        itemId: 'item-warm',
        approvalType: 'shell',
        operation: 'npm test',
        target: readyWorkspaceStatus.workspacePath,
        reason: 'Run tests'
      })
      app.notify('thread/status/changed', runtimeChanged('thread-a', { running: true, waitingOnApproval: true }))
    })
    expect(getThreadConversation('thread-a')?.getState().pendingApproval).toBeNull()

    app.appServerSendRequest.mockClear()
    await app.open('thread-a')

    expect(useConversationStore.getState().pendingApproval?.requestId).toBe('request-warm')
    expect(useConversationStore.getState().turnStatus).toBe('waitingApproval')
    expect(app.callsTo('thread/read')).toHaveLength(0)
  })

  it('releases the least recently viewed thread when an eleventh is left warm', async () => {
    const threadIds = Array.from({ length: 12 }, (_, index) => `thread-${index}`)
    const app = await renderWarmThreadsApp(threadIds)
    for (const threadId of threadIds.slice(0, 11)) await app.open(threadId)
    expect(app.callsTo('thread/unsubscribe')).toHaveLength(0)

    await app.open('thread-11')

    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe').map((call) => call[1])).toEqual([{ threadId: 'thread-0' }])
    })
    expect(getThreadConversation('thread-0')).toBeUndefined()
    for (const threadId of threadIds.slice(1)) expect(getThreadConversation(threadId)).toBeDefined()
  })

  it('releases a warm thread that has not been viewed for three hours', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setInterval', 'clearInterval', 'Date'] })
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await app.open('thread-b')

    await act(async () => { vi.advanceTimersByTime(2 * 60 * 60 * 1000) })
    expect(getThreadConversation('thread-a')).toBeDefined()

    await act(async () => { vi.advanceTimersByTime(61 * 60 * 1000) })
    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(1)
    })
    expect(getThreadConversation('thread-a')).toBeUndefined()
    expect(getThreadConversation('thread-b')).toBeDefined()
  })

  it('releases every warm thread when the connection is refreshed', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b', 'thread-c'])
    await app.open('thread-a')
    await app.open('thread-b')
    await app.open('thread-c')
    app.appServerSendRequest.mockClear()

    act(() => {
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })

    await waitFor(() => {
      expect(app.callsTo('thread/subscribe', 'thread-c')).toHaveLength(1)
    })
    expect(getThreadConversation('thread-a')).toBeUndefined()
    expect(getThreadConversation('thread-b')).toBeUndefined()
    expect(getThreadConversation('thread-c')).toBeDefined()
    expect(app.callsTo('thread/unsubscribe')).toHaveLength(0)
  })

  it('releases every warm thread when the foreground workspace switches', async () => {
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'], {
      onWorkspaceStatusChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    await app.open('thread-a')
    await app.open('thread-b')

    act(() => {
      emitStatus?.({ ...readyWorkspaceStatus, workspacePath: 'C:\\sample\\workspace-b' })
    })

    await waitFor(() => {
      expect(getThreadConversation('thread-a')).toBeUndefined()
      expect(getThreadConversation('thread-b')).toBeUndefined()
    })
  })

  it('releases a warm thread when it is archived', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await app.open('thread-b')

    await act(async () => { app.notify('thread/archived', { threadId: 'thread-a' }) })

    expect(getThreadConversation('thread-a')).toBeUndefined()
    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(1)
    })
  })

  it('releases a warm thread that another client rolled back', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await app.open('thread-b')

    await act(async () => { app.notify('thread/rolledBack', { threadId: 'thread-a' }) })

    expect(getThreadConversation('thread-a')).toBeUndefined()
    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(1)
    })
  })
})
