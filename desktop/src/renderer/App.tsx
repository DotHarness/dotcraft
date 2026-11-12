import { useForegroundThreadNavigation } from './hooks/useForegroundThreadNavigation'
import { useConversationWorkspace } from './hooks/useConversationWorkspace'
import { applyThreadNotification } from './stores/threadNotifications'
import { applyThreadServerRequest } from './stores/threadServerRequests'
import { useCallback, useEffect, useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { translate, type AppLocale } from '../shared/locales'
import { useLocale } from './contexts/LocaleContext'
import { basename } from './utils/path'
import { initConnectionStore, useConnectionStore } from './stores/connectionStore'
import { useThreadStore } from './stores/threadStore'
import {
  useConversationStore,
  type PendingApproval
} from './stores/conversationStore'
import { useUIStore } from './stores/uiStore'
import { useComposerPreferencesStore } from './stores/composerPreferencesStore'
import { useLinkPreferencesStore } from './stores/linkPreferencesStore'
import { useSidebarThreadOrderStore } from './stores/sidebarThreadOrderStore'
import { useSidebarSectionsStore } from './stores/sidebarSectionsStore'
import { useSidebarDestinationsStore } from './stores/sidebarDestinationsStore'
import { hydrateSummaryPinned } from './components/conversation/summary/view'
import { PET_CLOCK_MS, usePetStore } from './pet/petStore'
import { showFindToast } from './pet/findToast'
import { useDetailPanelStore } from './stores/detailPanelStore'
import { useWindowMaximized } from './hooks/useWindowMaximized'
import { useUserInputAutoResolutionBridge } from './hooks/useUserInputAutoResolutionBridge'
import { useActiveThreadModeSync } from './hooks/useActiveThreadModeSync'
import { useActiveThreadReadReceipt } from './hooks/useActiveThreadReadReceipt'
import { usePairedComputerNotices } from './hooks/usePairedComputerNotices'
import { useSatelliteNotices } from './hooks/useSatelliteNotices'
import { useDelayedFlag, WORKSPACE_OPENING_PLACEHOLDER_DELAY_MS } from './hooks/useDelayedFlag'
import { QuickOpenDialog } from './components/detail/QuickOpenDialog'
import { useAddProjectFlow } from './components/projects/AddProject'
import { ThreePanel } from './components/layout/ThreePanel'
import { useAutomationsStore } from './stores/automationsStore'
import type { AutomationDefinition, AutomationRun } from './stores/automationsStore'
import { applyWorkspaceResourceConfigChange } from './stores/providerConnectionScope'
import { applyMcpNotification } from './stores/mcpNotifications'
import { useSkillsStore } from './stores/skillsStore'
import { usePluginStore } from './stores/pluginStore'
import { useHooksStore } from './stores/hooksStore'
import { usePendingRestartStore } from './stores/pendingRestartStore'
import { useAppBindingStore } from './stores/appBindingStore'
import { useThreadRouteStore } from './stores/threadRouteStore'
import { bootstrapSatellites } from './stores/satellitesStore'
import { useGitStore } from './stores/gitStore'
import { useWorkspaceReadinessTiming } from './hooks/useWorkspaceReadinessTiming'
import { useWorkspaceProjectsStore } from './stores/workspaceProjectsStore'
import {
  replaceCurrentAppNavigationLocation,
  runWithoutAppNavigationRecording,
  startAppNavigationHistory,
  stopAppNavigationHistory
} from './stores/appNavigationStore'
import { isDefaultChatWorkspacePathCandidate } from '../shared/defaultChatWorkspace'
import { useSideChatStore } from './stores/sideChatStore'
import { openActiveSideChat, toggleSideChatFocus } from './utils/sideChat'
import { CustomMenuBar } from './components/layout/CustomMenuBar'
import { Sidebar } from './components/layout/Sidebar'
import { SettingsSidebar } from './components/layout/SettingsSidebar'
import { ConversationPanel } from './components/layout/ConversationPanel'
import { DetailPanel } from './components/layout/DetailPanel'
import { ErrorScreen } from './components/ErrorScreen'
import { WelcomeScreen } from './components/WelcomeScreen'
import { WorkspaceSetupInterstitial } from './components/WorkspaceSetupInterstitial'
import { WorkspaceSetupWizard } from './components/WorkspaceSetupWizard'
import {
  WorkspaceLaunchTransition,
  WorkspaceSetupLogoHandoff,
  WorkspaceSetupLogoHop,
  centeredLaunchLogoRect,
  elementToLaunchLogoRect,
  type LaunchLogoRect,
  type WorkspaceLaunchTransitionPhase
} from './components/WorkspaceLaunchTransition'
import { ConfirmDialogHost } from './components/ui/ConfirmDialog'
import { Button } from './components/ui/Button'
import { ToastContainer } from './components/ui/ToastContainer'
import { SkillDetailHost } from './components/skills/SkillDetailHost'
import { DesktopPluginMainViewOutlet } from './components/desktopPlugins/DesktopPluginOutlets'
import { CoreMainViewBoundary, coreMainViews, prefetchCoreMainView, retainsMainView } from './core/coreMainViewRoutes'
import { MainViewOutlet } from './components/layout/MainViewOutlet'
import { SettingsViewSkeleton } from './components/settings/SettingsSkeleton'
import { scheduleIdlePrefetch } from './core/idlePrefetch'
import { WhatsNewDialog } from './components/whats-new/WhatsNewDialog'
import { McpElicitationHost } from './components/mcp/McpElicitationHost'
import { addJobResultToast, addToast } from './stores/toastStore'
import { applyTheme, resolveTheme } from './utils/theme'
import { handleBrowserEvent } from './utils/browserEventHandler'
import { handleBrowserUseClose, handleBrowserUseOpen } from './utils/browserUseOpenHandler'
import { performAddTabAction } from './utils/detailTabActions'
import { welcomeScopeKey } from './utils/detailPanelScope'
import { useDetailPanelLifecycle } from './hooks/useDetailPanelLifecycle'
import { isFatalConnectionError, useSlowConnectingHint } from './utils/connectionUi'
import { handleAppNavigationShortcut } from './utils/appNavigationShortcut'
import { handleFindShortcut } from './find/findShortcut'
import { FindOverlay } from './find/FindOverlay'
import { initPromptSuggestionStore } from './stores/promptSuggestionStore'
import { interruptTurn } from './utils/interruptTurn'
import { collectWebCitationSources, formatWebCitations } from './utils/webCitations'
import {
  isDesktopPluginMainView,
  useDesktopPluginRegistry
} from './plugins/desktopPluginRegistry'
import {
  resolveConfigChangedPayload,
  type ConfigChangedPayload
} from './utils/configChanged'
import {
  compareAppVersions,
  getLatestWhatsNewVersion,
  getWhatsNewMediaStateKey,
  getUnseenWhatsNewReleases,
  getWhatsNewReleasesUpTo,
  type WhatsNewMediaState,
  type WhatsNewRelease
} from '../shared/whatsNew'
import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import type {
  BrowserUseApprovalRequestPayload,
  BrowserUseApprovalResponseAction,
  DiscoveredModule,
  ModuleStatusMap,
  WindowVisibilityState,
  WorkspaceSetupRequest,
  WorkspaceStatusPayload
} from '../preload/api.d'
const SETUP_PAGE_HANDOFF_PREP_MS = 260
const WORKSPACE_LAUNCH_TRANSITION_MS = 620
const WORKSPACE_LAUNCH_REVEAL_MS = 360
const APP_VERSION = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.0.0'
const DEFAULT_WINDOW_VISIBILITY_STATE: WindowVisibilityState = {
  minimized: false,
  visible: true,
  focused: true
}

const CoreChannelsView = coreMainViews.channels
const CoreAgentBuilderView = coreMainViews.agents
const CoreAutomationsView = coreMainViews.automations
const CorePluginsView = coreMainViews.skills
const CoreSettingsView = coreMainViews.settings

type WorkspaceLaunchTarget = 'unknown' | 'setup' | 'main' | 'error'

interface WorkspaceLaunchTransitionState {
  phase: WorkspaceLaunchTransitionPhase
  from: LaunchLogoRect
  to: LaunchLogoRect
  target: WorkspaceLaunchTarget
  requestPath?: string
  logoSrc?: string
}

interface SetupLogoHandoffState {
  phase: 'hold' | 'move'
  from: LaunchLogoRect
  to: LaunchLogoRect
}

const DEFAULT_RENDERER_WORKSPACE_STATUS: WorkspaceStatusPayload = {
  status: 'no-workspace',
  workspacePath: '',
  hasUserConfig: false,
  providers: []
}

function resolveProtocolWorkspacePath(status: WorkspaceStatusPayload): string {
  return (
    status.remote?.appServerWorkspacePath?.trim() ||
    status.remote?.workspaceDir?.trim() ||
    status.workspacePath ||
    ''
  )
}

function resolveWorkspaceDisplayName(status: WorkspaceStatusPayload): string {
  const remoteDisplayName = status.remote?.displayName?.trim()
  if (remoteDisplayName) return remoteDisplayName
  const remoteName = status.remote?.stackName?.trim()
  if (remoteName) return remoteName
  const path = status.workspacePath ?? ''
  return path ? basename(path) : 'DotCraft'
}

function getWhatsNewReleaseVersions(releases: WhatsNewRelease[]): string[] {
  return releases.map((release) => release.version)
}

function mediaStatesEqual(a: WhatsNewMediaState | undefined, b: WhatsNewMediaState): boolean {
  return (
    a?.releaseVersion === b.releaseVersion &&
    a.cardId === b.cardId &&
    a.status === b.status &&
    a.cachedUrl === b.cachedUrl &&
    a.error === b.error
  )
}

function mergeWhatsNewMediaStates(
  current: Record<string, WhatsNewMediaState>,
  states: WhatsNewMediaState[]
): Record<string, WhatsNewMediaState> {
  if (states.length === 0) return current
  let next: Record<string, WhatsNewMediaState> | null = null
  for (const state of states) {
    const key = getWhatsNewMediaStateKey(state.releaseVersion, state.cardId)
    if (mediaStatesEqual((next ?? current)[key], state)) continue
    next ??= { ...current }
    next[key] = state
  }
  return next ?? current
}

function areWhatsNewReleaseMediaReady(
  releases: WhatsNewRelease[],
  states: Record<string, WhatsNewMediaState>
): boolean {
  for (const release of releases) {
    for (const card of release.cards) {
      if (!card.media) continue
      const state = states[getWhatsNewMediaStateKey(release.version, card.id)]
      if (state?.status !== 'ready' || !state.cachedUrl) return false
    }
  }
  return true
}

function createInitialWorkspaceLaunchTransition(
  workspaceStatus: WorkspaceStatusPayload
): WorkspaceLaunchTransitionState | null {
  const path = workspaceStatus.workspacePath?.trim() ?? ''
  if (!path || workspaceStatus.status !== 'ready') return null

  const centerRect = centeredLaunchLogoRect()
  return {
    phase: 'connecting',
    from: centerRect,
    to: centerRect,
    target: 'main',
    requestPath: path
  }
}

function waitForWorkspaceLaunchMotion(ms: number): Promise<void> {
  return new Promise((resolve) => window.setTimeout(resolve, ms))
}

function normalizeWorkspacePathForPinnedLookup(path: string): string {
  return normalizeWorkspaceProjectKey(path)
}

function resolveActiveProjectKey(
  foregroundProjectId: string | null | undefined,
  remoteProjectId: string | null | undefined,
  protocolWorkspacePath: string | null | undefined,
  workspacePath: string | null | undefined
): string {
  const remoteKey = remoteProjectId?.trim()
  if (remoteKey) return remoteKey

  const protocolPath = protocolWorkspacePath || workspacePath || ''
  const protocolKey = normalizeWorkspaceProjectKey(protocolPath)
  const foregroundKey = normalizeWorkspaceProjectKey(foregroundProjectId)
  if (!protocolKey) return foregroundProjectId || ''
  if (foregroundKey && foregroundKey === protocolKey) return foregroundProjectId || protocolKey
  return protocolKey
}

function buildUiToolApproval(
  p: Record<string, unknown>,
  bridgeId: string,
  locale: AppLocale
): PendingApproval {
  return {
    bridgeId,
    threadId: typeof p.threadId === 'string' ? p.threadId : null,
    turnId: null,
    requestId: typeof p.approvalId === 'string' ? p.approvalId : '',
    locallySubmittedDecision: null,
    itemId: '',
    approvalType: (typeof p.approvalType === 'string' ? p.approvalType : 'remoteResource') as PendingApproval['approvalType'],
    operation: typeof p.operation === 'string' ? p.operation : '',
    target: typeof p.target === 'string' ? p.target : '',
    reason: '',
    source: 'uiTool',
    declineValue: 'decline',
    options: [
      {
        value: 'accept',
        label: translate(locale, 'approval.option.accept.label'),
        description: translate(locale, 'approval.option.accept.description')
      },
      {
        value: 'decline',
        label: translate(locale, 'approval.option.decline.label'),
        description: translate(locale, 'approval.option.decline.description')
      }
    ],
    submit: async (value: string): Promise<void> => {
      await window.api.appServer.sendServerResponse(bridgeId, { decision: value })
      useConversationStore.getState().setGenericApproval(null)
    }
  }
}

function buildBrowserUseApproval(
  request: BrowserUseApprovalRequestPayload,
  locale: AppLocale
): PendingApproval {
  const session = request.sessionName?.trim()
  const question = session
    ? translate(locale, 'browserUse.approval.messageWithSession', { session, domain: request.domain })
    : translate(locale, 'browserUse.approval.message', { domain: request.domain })
  return {
    bridgeId: '',
    threadId: null,
    turnId: null,
    requestId: request.requestId,
    locallySubmittedDecision: null,
    itemId: '',
    approvalType: 'remoteResource',
    operation: '',
    target: '',
    reason: '',
    source: 'browserUse',
    question,
    detailRows: [{ label: translate(locale, 'browserUse.approval.urlLabel'), value: request.url, mono: true }],
    declineValue: 'deny',
    options: [
      { value: 'allowDomain', label: translate(locale, 'browserUse.approval.alwaysAllow'), description: '' },
      { value: 'allowOnce', label: translate(locale, 'browserUse.approval.allowOnce'), description: '' },
      { value: 'blockDomain', label: translate(locale, 'browserUse.approval.blockDomain'), description: '' },
      { value: 'deny', label: translate(locale, 'approval.option.decline.label'), description: '' }
    ],
    submit: async (value: string): Promise<void> => {
      await window.api.workspace.viewer.browserUse.sendApprovalResponse({
        requestId: request.requestId,
        action: value as BrowserUseApprovalResponseAction
      })
      useConversationStore.getState().setGenericApproval(null)
    }
  }
}

function AppChrome({ children }: { children: ReactNode }): JSX.Element {
  const showCustomMenu = window.api.platform !== 'darwin'
  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        overflow: 'hidden'
      }}
    >
      {showCustomMenu && <CustomMenuBar />}
      <div
        style={{
          flex: 1,
          minHeight: 0,
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        {children}
      </div>
    </div>
  )
}

function WindowFrame({
  children,
  plainSurface = false,
  overlays
}: {
  children: ReactNode
  plainSurface?: boolean
  overlays?: ReactNode
}): JSX.Element {
  const useRendererRadius = window.api.platform === 'linux'
  const maximized = useWindowMaximized()
  return (
    <div
      className="dotcraft-window-frame"
      data-plain-surface={plainSurface ? 'true' : undefined}
      style={{
        position: 'relative',
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        overflow: 'hidden',
        isolation: 'isolate',
        borderRadius: useRendererRadius && !maximized ? 'var(--shell-window-radius)' : 0,
        boxShadow: 'inset 0 0 0 1px var(--shell-chrome-border)'
      }}
    >
      <AppChrome>{children}</AppChrome>
      {overlays}
    </div>
  )
}

export function App(): JSX.Element {
  useUserInputAutoResolutionBridge()
  useSatelliteNotices()
  usePairedComputerNotices()
  useEffect(() => bootstrapSatellites(), [])
  const locale = useLocale()
  const localeRef = useRef(locale)
  localeRef.current = locale
  const preloadedWorkspaceStatus = window.api?.initialWorkspaceStatus
  const hasPreloadedWorkspaceStatusRef = useRef(preloadedWorkspaceStatus != null)
  const initialWorkspaceStatusRef = useRef<WorkspaceStatusPayload>(
    preloadedWorkspaceStatus ?? DEFAULT_RENDERER_WORKSPACE_STATUS
  )
  const initialWorkspaceStatus = initialWorkspaceStatusRef.current
  const initialWorkspacePath = initialWorkspaceStatus.workspacePath ?? ''
  const initialProtocolWorkspacePath = resolveProtocolWorkspacePath(initialWorkspaceStatus)

  const [workspacePath, setWorkspacePath] = useState(initialWorkspacePath)
  const [protocolWorkspacePath, setProtocolWorkspacePath] = useState(initialProtocolWorkspacePath)
  const [workspaceName, setWorkspaceName] = useState(resolveWorkspaceDisplayName(initialWorkspaceStatus))
  const [workspaceConfigChange, setWorkspaceConfigChange] = useState<ConfigChangedPayload | null>(null)
  const [workspaceConfigChangeSeq, setWorkspaceConfigChangeSeq] = useState(0)
  const [workspaceStatus, setWorkspaceStatus] = useState<WorkspaceStatusPayload>(initialWorkspaceStatus)
  const [showSetupWizard, setShowSetupWizard] = useState(false)
  const [setupOpening, setSetupOpening] = useState(false)
  const [workspaceLaunchTransition, setWorkspaceLaunchTransition] =
    useState<WorkspaceLaunchTransitionState | null>(() =>
      createInitialWorkspaceLaunchTransition(initialWorkspaceStatus)
    )
  const [setupLogoHandoff, setSetupLogoHandoff] = useState<SetupLogoHandoffState | null>(null)
  const [launchErrorScreenVisible, setLaunchErrorScreenVisible] = useState(false)
  const [setupLogoAnchorNode, setSetupLogoAnchorNode] = useState<HTMLDivElement | null>(null)
  const [wizardLogoAnchorNode, setWizardLogoAnchorNode] = useState<HTMLDivElement | null>(null)
  const setupOpeningRef = useRef(false)
  const { status, errorType, errorMessage } = useConnectionStore()
  const isExpectedRestart = useConnectionStore((s) => s.isExpectedRestart)
  const capabilities = useConnectionStore((s) => s.capabilities)
  const workspaceOpening = useDelayedFlag(status === 'connecting', WORKSPACE_OPENING_PLACEHOLDER_DELAY_MS)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const foregroundChat = useWorkspaceProjectsStore((s) => s.chat)
  const showSlowConnectingHint = useSlowConnectingHint(status, workspacePath)
  const remoteWorkspaceActive = workspaceStatus.remote != null
  const activeProjectKey = resolveActiveProjectKey(
    foregroundProjectId,
    workspaceStatus.remote?.projectId,
    protocolWorkspacePath,
    workspacePath
  )
  const foregroundThreadListKey = activeProjectKey || protocolWorkspacePath || workspacePath
  const welcomeScopeId = welcomeScopeKey(activeProjectKey || workspacePath)
  const activeProjectKeyRef = useRef(activeProjectKey)
  activeProjectKeyRef.current = activeProjectKey
  const foregroundChatKey = foregroundChat
    ? normalizeWorkspaceProjectKey(
      foregroundChat.projectId || foregroundChat.identityWorkspacePath || foregroundChat.path
    )
    : ''
  const foregroundIsChat =
    isDefaultChatWorkspacePathCandidate(protocolWorkspacePath || workspacePath) ||
    Boolean(foregroundChatKey && foregroundChatKey === normalizeWorkspaceProjectKey(foregroundProjectId))
  const displayWorkspaceName = foregroundIsChat
    ? translate(locale, 'chatsRail.title')
    : workspaceName
  const remoteWorkspaceActiveRef = useRef(remoteWorkspaceActive)
  remoteWorkspaceActiveRef.current = remoteWorkspaceActive
  const [chromeSettingsOpenSeq, setChromeSettingsOpenSeq] = useState(0)
  const [whatsNewDialog, setWhatsNewDialog] = useState<{
    releases: WhatsNewRelease[]
    markSeenVersion?: string
  } | null>(null)
  const [whatsNewMediaStates, setWhatsNewMediaStates] = useState<Record<string, WhatsNewMediaState>>({})
  const activeMainView = useUIStore((s) => s.activeMainView)
  const quickOpenVisible = useUIStore((s) => s.quickOpenVisible)
  const activeThreadEffectiveWorkspacePath = useThreadStore((s) => s.activeThread?.effectiveWorkspacePath ?? null)
  const desktopPluginMainViews = useDesktopPluginRegistry((state) => state.mainViews)
  const activeDesktopPluginView = desktopPluginMainViews.find((entry) => entry.viewKey === activeMainView) ?? null
  const whatsNewOpenRequestSeq = useUIStore((s) => s.whatsNewOpenRequestSeq)
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const pendingRestartVisible = usePendingRestartStore((s) => s.visible)
  const pendingRestartApplying = usePendingRestartStore((s) => s.applying)
  const pendingRestartMessageKey = usePendingRestartStore((s) => s.messageKey)
  const pendingRestartApplyKey = usePendingRestartStore((s) => s.applyKey)
  const pendingRestartApplyingKey = usePendingRestartStore((s) => s.applyingKey)
  const applyPendingRestart = usePendingRestartStore((s) => s.apply)
  const ignorePendingRestart = usePendingRestartStore((s) => s.ignore)
  const workspacePathRef = useRef(initialWorkspacePath)
  const protocolWorkspacePathRef = useRef(initialProtocolWorkspacePath)
  const setupWorkspaceStatusSnapshotRef = useRef<WorkspaceStatusPayload | null>(
    initialWorkspaceStatus.status === 'needs-setup' ? initialWorkspaceStatus : null
  )
  const workspaceStatusHydratedRef = useRef(hasPreloadedWorkspaceStatusRef.current)
  const configChangedDedupeRef = useRef<Map<string, number>>(new Map())
  const moduleConnectedSnapshotRef = useRef<Map<string, boolean>>(new Map())
  const moduleConnectedSnapshotReadyRef = useRef(false)
  const moduleDisplayNameByIdRef = useRef<Map<string, string>>(new Map())
  const whatsNewAutoCheckedVersionRef = useRef<string | null>(null)
  const whatsNewMediaStatesRef = useRef<Record<string, WhatsNewMediaState>>({})
  const whatsNewDialogOpenRef = useRef(false)
  const showMainWorkspaceUiRef = useRef(false)
  const addProject = useAddProjectFlow()
  const beginAddProjectRef = useRef(addProject.beginCreate)
  beginAddProjectRef.current = addProject.beginCreate
  const windowVisibilityStateRef = useRef<WindowVisibilityState>(DEFAULT_WINDOW_VISIBILITY_STATE)
  const pendingAutoWhatsNewRef = useRef<{
    releases: WhatsNewRelease[]
    markSeenVersion?: string
  } | null>(null)

  useEffect(() => {
    const applyVisibilityState = (state: WindowVisibilityState): void => {
      windowVisibilityStateRef.current = state
    }
    const unsubscribeVisibility = window.api.window.onVisibilityChanged?.(applyVisibilityState)
    void window.api.window.getVisibilityState?.()
      .then(applyVisibilityState)
      .catch(() => undefined)
    return () => unsubscribeVisibility?.()
  }, [])

  const lastSeenWhatsNewVersionRef = useRef<string | undefined>(undefined)

  useEffect(() => {
    let firstFrame = 0
    let secondFrame = 0
    let disposed = false
    firstFrame = window.requestAnimationFrame(() => {
      secondFrame = window.requestAnimationFrame(() => {
        if (disposed) return
        const rendererReadyForShow = window.api.window.rendererReadyForShow
        if (typeof rendererReadyForShow === 'function') {
          rendererReadyForShow()
        }
      })
    })
    return () => {
      disposed = true
      window.cancelAnimationFrame(firstFrame)
      window.cancelAnimationFrame(secondFrame)
    }
  }, [])

  const { reloadThreadList, loadedProjectKey, updateWarmThread, updateWarmThreadMode, releaseWarmThread, foregroundReady } = useForegroundThreadNavigation({
    projectKey: foregroundThreadListKey,
    protocolWorkspacePath: protocolWorkspacePath || workspacePath,
    remoteWorkspace: remoteWorkspaceActive,
    locale
  })
  useWorkspaceReadinessTiming(foregroundThreadListKey, status, loadedProjectKey)

  useEffect(() => {
    const onPinnedThreadIdsChanged = window.api.settings.onPinnedThreadIdsChanged
    if (typeof onPinnedThreadIdsChanged !== 'function') return undefined
    return onPinnedThreadIdsChanged(({ workspacePath, threadIds }) => {
      const activeKey = activeProjectKeyRef.current || protocolWorkspacePathRef.current
      if (
        workspacePath !== activeKey &&
        normalizeWorkspacePathForPinnedLookup(workspacePath) !==
          normalizeWorkspacePathForPinnedLookup(protocolWorkspacePathRef.current)
      ) {
        return
      }
      useThreadStore.getState().hydratePinnedThreadIds(activeKey || workspacePath, threadIds)
    })
  }, [])

  const syncWorkspaceStatus = useCallback((payload: WorkspaceStatusPayload): void => {
    const path = payload.workspacePath ?? ''
    const protocolPath = resolveProtocolWorkspacePath(payload)
    const previousPath = workspacePathRef.current
    const isInitialWorkspaceStatus = !workspaceStatusHydratedRef.current
    workspaceStatusHydratedRef.current = true
    if (previousPath !== path) {
      useGitStore.getState().reset()
    }
    workspacePathRef.current = path
    protocolWorkspacePathRef.current = protocolPath
    if (payload.status === 'needs-setup') {
      setupWorkspaceStatusSnapshotRef.current = payload
    } else if (!path || payload.status === 'no-workspace') {
      setupWorkspaceStatusSnapshotRef.current = null
    }
    setWorkspacePath(path)
    setProtocolWorkspacePath(protocolPath)
    setWorkspaceStatus(payload)
    setWorkspaceName(resolveWorkspaceDisplayName(payload))
    if (isInitialWorkspaceStatus && path && payload.status === 'ready') {
      const centerRect = centeredLaunchLogoRect()
      setWorkspaceLaunchTransition({
        phase: 'connecting',
        from: centerRect,
        to: centerRect,
        target: 'main',
        requestPath: path
      })
    }
  }, [])

  useEffect(() => {
    if (workspaceStatus.status !== 'ready') return
    if (remoteWorkspaceActive || !workspacePath) return
    if (foregroundIsChat) return
    void useGitStore.getState().ensureBranches(workspacePath)
  }, [foregroundIsChat, remoteWorkspaceActive, workspacePath, workspaceStatus.status])

  const handleOpenWorkspaceFromWelcome = useCallback(async (request: {
    path: string
    logoRect: LaunchLogoRect
  }): Promise<void> => {
    setLaunchErrorScreenVisible(false)
    setWorkspaceLaunchTransition({
      phase: 'welcome-hold',
      from: request.logoRect,
      to: request.logoRect,
      target: 'unknown',
      requestPath: request.path
    })

    try {
      await Promise.all([
        waitForWorkspaceLaunchMotion(WORKSPACE_LAUNCH_TRANSITION_MS),
        window.api.workspace.switch(request.path)
      ])
    } catch (err) {
      setWorkspaceLaunchTransition(null)
      throw err
    }
  }, [])

  const handleStartWorkspaceSetup = useCallback((): void => {
    if (setupOpeningRef.current) return

    const sourceRect = elementToLaunchLogoRect(setupLogoAnchorNode) ?? centeredLaunchLogoRect()
    setupOpeningRef.current = true
    setSetupOpening(true)
    setSetupLogoHandoff({
      phase: 'hold',
      from: sourceRect,
      to: sourceRect
    })
    const openingDelay = new Promise<void>((resolve) => {
      window.setTimeout(resolve, SETUP_PAGE_HANDOFF_PREP_MS)
    })
    const statusRefresh = window.api.workspace
      .getStatus()
      .then((payload) => {
        syncWorkspaceStatus(payload)
        return payload.status === 'needs-setup'
      })
      .catch(() => true)

    void Promise.all([openingDelay, statusRefresh])
      .then(([, shouldShowWizard]) => {
        if (shouldShowWizard) {
          setShowSetupWizard(true)
        } else {
          setSetupLogoHandoff(null)
        }
      })
      .finally(() => {
        setupOpeningRef.current = false
        setSetupOpening(false)
      })
  }, [setupLogoAnchorNode, syncWorkspaceStatus])

  const handleRunWorkspaceSetup = useCallback(async (
    request: WorkspaceSetupRequest,
    context: { logoRect: LaunchLogoRect; logoSrc: string }
  ): Promise<void> => {
    const centerRect = centeredLaunchLogoRect()
    setLaunchErrorScreenVisible(false)
    setWorkspaceLaunchTransition({
      phase: 'setup-complete-to-center',
      from: context.logoRect,
      to: centerRect,
      target: 'main',
      requestPath: workspacePathRef.current,
      logoSrc: context.logoSrc
    })

    try {
      await window.api.workspace.runSetup(request)
    } catch (err) {
      setWorkspaceLaunchTransition((current) =>
        current?.phase === 'setup-complete-to-center' || current?.phase === 'preparing'
          ? null
          : current
      )
      throw err
    }
  }, [])

  const applyWhatsNewMediaStates = useCallback((states: WhatsNewMediaState[]): void => {
    const next = mergeWhatsNewMediaStates(whatsNewMediaStatesRef.current, states)
    if (next === whatsNewMediaStatesRef.current) return
    whatsNewMediaStatesRef.current = next
    setWhatsNewMediaStates(next)
  }, [])

  const openWhatsNew = useCallback((
    releases: WhatsNewRelease[],
    source: 'auto' | 'manual'
  ): void => {
    if (source === 'auto') {
      pendingAutoWhatsNewRef.current = null
    }
    whatsNewDialogOpenRef.current = true
    const markSeenVersion = getLatestWhatsNewVersion(releases)
    setWhatsNewDialog({
      releases,
      ...(markSeenVersion ? { markSeenVersion } : {})
    })
  }, [])

  const openPendingAutoWhatsNewIfReady = useCallback((): void => {
    const pending = pendingAutoWhatsNewRef.current
    if (!pending || !showMainWorkspaceUiRef.current || whatsNewDialogOpenRef.current) return

    const markSeenVersion = pending.markSeenVersion
    const lastSeenVersion = lastSeenWhatsNewVersionRef.current
    if (
      markSeenVersion &&
      lastSeenVersion &&
      compareAppVersions(markSeenVersion, lastSeenVersion) <= 0
    ) {
      pendingAutoWhatsNewRef.current = null
      return
    }

    if (areWhatsNewReleaseMediaReady(pending.releases, whatsNewMediaStatesRef.current)) {
      openWhatsNew(pending.releases, 'auto')
    }
  }, [openWhatsNew])

  const startWhatsNewMediaPrefetch = useCallback((
    releases: WhatsNewRelease[],
    options: { openAutoWhenReady?: boolean } = {}
  ): void => {
    const releaseVersions = getWhatsNewReleaseVersions(releases)
    if (releaseVersions.length === 0) return

    void window.api.whatsNew
      .getMediaStates(releaseVersions)
      .then((states) => {
        applyWhatsNewMediaStates(states)
        if (options.openAutoWhenReady) openPendingAutoWhatsNewIfReady()
      })
      .catch(() => {})

    void window.api.whatsNew
      .prefetchMedia(releaseVersions)
      .then((states) => {
        applyWhatsNewMediaStates(states)
        if (options.openAutoWhenReady) openPendingAutoWhatsNewIfReady()
      })
      .catch(() => {})
  }, [applyWhatsNewMediaStates, openPendingAutoWhatsNewIfReady])

  const openManualWhatsNew = useCallback((): void => {
    void window.api.whatsNew
      .getReleases()
      .then((allReleases) => {
        const releases = getWhatsNewReleasesUpTo(allReleases, APP_VERSION)
        openWhatsNew(releases, 'manual')
        startWhatsNewMediaPrefetch(releases)
      })
      .catch(() => {
        openWhatsNew([], 'manual')
      })
  }, [openWhatsNew, startWhatsNewMediaPrefetch])

  const closeWhatsNew = useCallback((): void => {
    const markSeenVersion = whatsNewDialog?.markSeenVersion
    whatsNewDialogOpenRef.current = false
    setWhatsNewDialog(null)
    if (markSeenVersion) {
      lastSeenWhatsNewVersionRef.current = markSeenVersion
      pendingAutoWhatsNewRef.current = null
      void window.api.settings.set({ lastSeenWhatsNewVersion: markSeenVersion })
    }
  }, [whatsNewDialog])

  useEffect(initPromptSuggestionStore, [])

  useEffect(() => {
    performance.mark('app:bootstrap-start')
    const unsubscribe = initConnectionStore()
    const unsubscribeWorkspace = window.api.workspace.onStatusChange((payload) => {
      syncWorkspaceStatus(payload)
    })

    void window.api.workspace.getStatus()
      .then((payload) => {
        syncWorkspaceStatus(payload)
      })
      .catch(() => {})

    return () => {
      unsubscribeWorkspace()
      unsubscribe()
    }
  }, [syncWorkspaceStatus])

  useEffect(() => {
    void window.api.workspace.getProjects?.()
      .then((payload) => {
        if (payload) useWorkspaceProjectsStore.getState().setPayload(payload)
      })
      .catch(() => {})
    const unsubscribe = window.api.workspace.onProjectsChange?.((payload) => {
      useWorkspaceProjectsStore.getState().setPayload(payload)
    })
    return () => {
      unsubscribe?.()
    }
  }, [])

  useEffect(() => {
    if (workspacePath) {
      window.api.window.setTitle(
        translate(locale, 'app.titleWithWorkspace', { name: displayWorkspaceName })
      )
    }
  }, [workspacePath, displayWorkspaceName, locale])

  useEffect(() => {
    return window.api.window.onOpenChromeSettings(() => {
      useUIStore.getState().setActiveMainView('settings')
      setChromeSettingsOpenSeq((current) => current + 1)
    })
  }, [])

  useEffect(() => {
    return window.api.window.onOpenWhatsNew(() => {
      openManualWhatsNew()
    })
  }, [openManualWhatsNew])

  useEffect(() => {
    if (whatsNewOpenRequestSeq <= 0) return
    openManualWhatsNew()
  }, [whatsNewOpenRequestSeq, openManualWhatsNew])

  useEffect(() => {
    return window.api.whatsNew.onMediaStateChanged((state) => {
      applyWhatsNewMediaStates([state])
      openPendingAutoWhatsNewIfReady()
    })
  }, [applyWhatsNewMediaStates, openPendingAutoWhatsNewIfReady])

  useEffect(() => {
    return window.api.window.onOpenThread((payload) => {
      const threadId = payload.threadId.trim()
      if (!threadId) return
      useUIStore.getState().setActiveMainView('conversation')
      useThreadStore.getState().setActiveThreadId(threadId)
    })
  }, [])

  useEffect(() => {
    return window.api.window.onOpenNewChat(({ prompt }) => {
      const ui = useUIStore.getState()
      ui.goToNewChat()
      if (prompt) ui.setComposerPrefill(prompt)
    })
  }, [])

  useEffect(() => {
    window.api.settings
      .get()
      .then((s) => {
        applyTheme(resolveTheme(s.theme))
        useUIStore.getState().setShowThinkingContent(s.showThinkingContent === true)
        useComposerPreferencesStore.getState().hydrate(s)
        useLinkPreferencesStore.getState().hydrate(s)
        usePetStore.getState().hydrate(s)
        useSidebarThreadOrderStore.getState().hydrate(s)
        useSidebarSectionsStore.getState().hydrate(s)
        useSidebarDestinationsStore.getState().hydrate(s)
        hydrateSummaryPinned(s.summaryHidden !== true)
        useUIStore.setState({
          projectsSectionCollapsed: s.projectsSectionCollapsed === true,
          pinnedSectionCollapsed: s.pinnedSectionCollapsed === true,
          chatsSectionCollapsed: s.chatsSectionCollapsed === true
        })
      })
      .catch(() => {})
  }, [])

  useEffect(() => {
    const clock = window.setInterval(() => {
      const found = usePetStore.getState().advance(PET_CLOCK_MS, 0)
      if (found) showFindToast(found)
    }, PET_CLOCK_MS)
    return () => window.clearInterval(clock)
  }, [])

  const panelThreadId = useThreadStore((state) => state.activeThreadId)
  const panelThreadProjectKey = useThreadStore((state) => state.threadListProjectKey)
  const activeConversationWorkspacePath =
    activeThreadEffectiveWorkspacePath?.trim() || workspacePath

  useDetailPanelLifecycle({
    projectKey: normalizeWorkspaceProjectKey(activeProjectKey || protocolWorkspacePath || workspacePath),
    workspacePath: activeConversationWorkspacePath,
    browserWorkspacePath: protocolWorkspacePath || workspacePath,
    threadId: panelThreadId,
    selectionProjectKey: panelThreadProjectKey,
    welcomeScopeId
  })

  useConversationWorkspace(activeConversationWorkspacePath, remoteWorkspaceActive)

  useEffect(() => {
    moduleConnectedSnapshotRef.current = new Map()
    moduleConnectedSnapshotReadyRef.current = false
    moduleDisplayNameByIdRef.current = new Map()

    if (!workspacePath) return

    let disposed = false

    const toConnectedSnapshot = (statusMap: ModuleStatusMap): Map<string, boolean> => {
      const snapshot = new Map<string, boolean>()
      for (const [moduleId, entry] of Object.entries(statusMap)) {
        snapshot.set(moduleId, entry?.connected === true)
      }
      return snapshot
    }

    const hydrateModuleMetadata = async (): Promise<void> => {
      try {
        const [modules, statusMap] = await Promise.all([
          window.api.modules.list(),
          window.api.modules.running()
        ])
        if (disposed) return
        moduleDisplayNameByIdRef.current = new Map(
          (modules as DiscoveredModule[]).map((module) => [module.moduleId, module.displayName])
        )
        moduleConnectedSnapshotRef.current = toConnectedSnapshot(statusMap)
        moduleConnectedSnapshotReadyRef.current = true
      } catch {
        if (disposed) return
        moduleConnectedSnapshotRef.current = new Map()
        moduleConnectedSnapshotReadyRef.current = true
      }
    }

    void hydrateModuleMetadata()

    const unsubscribe = window.api.modules.onStatusChanged((statusMap) => {
      if (disposed) return
      const nextSnapshot = toConnectedSnapshot(statusMap)
      if (!moduleConnectedSnapshotReadyRef.current) {
        moduleConnectedSnapshotRef.current = nextSnapshot
        moduleConnectedSnapshotReadyRef.current = true
        return
      }

      const previousSnapshot = moduleConnectedSnapshotRef.current
      for (const [moduleId, connected] of nextSnapshot) {
        const wasConnected = previousSnapshot.get(moduleId) === true
        if (!wasConnected && connected) {
          const displayName = moduleDisplayNameByIdRef.current.get(moduleId) ?? moduleId
          addToast(
            translate(localeRef.current, 'channels.modules.connectedToast', { name: displayName }),
            'success'
          )
        }
      }
      moduleConnectedSnapshotRef.current = nextSnapshot
    })

    return () => {
      disposed = true
      unsubscribe()
    }
  }, [workspacePath])

  useEffect(() => {
    if (workspaceStatus.status === 'needs-setup') return

    setupOpeningRef.current = false
    setSetupOpening(false)
    setSetupLogoHandoff(null)

    if (workspaceLaunchTransition?.phase === 'setup-complete-to-center') {
      return
    }

    setShowSetupWizard(false)
  }, [workspaceLaunchTransition?.phase, workspaceStatus.status])

  useEffect(() => {
    if (!workspaceLaunchTransition) return

    if (workspaceLaunchTransition.phase === 'welcome-to-center') {
      const timer = window.setTimeout(() => {
        setWorkspaceLaunchTransition((current) => {
          if (current?.phase !== 'welcome-to-center') return current
          const centerRect = centeredLaunchLogoRect()
          return {
            ...current,
            phase: 'connecting',
            from: centerRect,
            to: centerRect
          }
        })
      }, WORKSPACE_LAUNCH_TRANSITION_MS)
      return () => window.clearTimeout(timer)
    }

    if (
      workspaceLaunchTransition.phase === 'setup-handoff' ||
      workspaceLaunchTransition.phase === 'main-reveal' ||
      workspaceLaunchTransition.phase === 'error-reveal'
    ) {
      const duration =
        workspaceLaunchTransition.phase === 'main-reveal' ||
        workspaceLaunchTransition.phase === 'error-reveal'
          ? WORKSPACE_LAUNCH_REVEAL_MS
          : WORKSPACE_LAUNCH_TRANSITION_MS
      const timer = window.setTimeout(() => {
        setWorkspaceLaunchTransition((current) =>
          current?.phase === workspaceLaunchTransition.phase ? null : current
        )
      }, duration)
      return () => window.clearTimeout(timer)
    }
  }, [workspaceLaunchTransition])

  useEffect(() => {
    if (!workspaceLaunchTransition) return

    if (
      workspaceStatus.status === 'needs-setup' &&
      setupLogoAnchorNode &&
      (
        workspaceLaunchTransition.phase === 'welcome-hold' ||
        workspaceLaunchTransition.phase === 'welcome-to-center' ||
        workspaceLaunchTransition.phase === 'connecting'
      )
    ) {
      const targetRect = elementToLaunchLogoRect(setupLogoAnchorNode)
      if (!targetRect) return
      const sourceRect = workspaceLaunchTransition.phase === 'welcome-hold'
        ? workspaceLaunchTransition.from
        : workspaceLaunchTransition.to
      setWorkspaceLaunchTransition({
        ...workspaceLaunchTransition,
        phase: 'setup-handoff',
        from: sourceRect,
        to: targetRect,
        target: 'setup'
      })
      return
    }

    if (workspaceLaunchTransition.phase === 'welcome-hold' && workspaceStatus.status === 'ready') {
      const centerRect = centeredLaunchLogoRect()
      setWorkspaceLaunchTransition({
        ...workspaceLaunchTransition,
        phase: 'welcome-to-center',
        to: centerRect,
        target: 'main'
      })
      return
    }

    if (
      workspaceLaunchTransition.phase !== 'connecting' &&
      workspaceLaunchTransition.phase !== 'preparing'
    ) return

    if (workspaceStatus.status === 'ready' && status === 'connected') {
      performance.mark('app:workspace-main-reveal', { detail: { workspacePath } })
      const centerRect = centeredLaunchLogoRect()
      setWorkspaceLaunchTransition({
        ...workspaceLaunchTransition,
        phase: 'main-reveal',
        from: centerRect,
        to: centerRect,
        target: 'main'
      })
    }
  }, [
    setupLogoAnchorNode,
    status,
    workspaceLaunchTransition,
    workspacePath,
    workspaceStatus.status
  ])

  useEffect(() => {
    if (!setupLogoHandoff) return
    if (setupLogoHandoff.phase !== 'hold') return
    if (!showSetupWizard || !wizardLogoAnchorNode) return

    const targetRect = elementToLaunchLogoRect(wizardLogoAnchorNode)
    if (!targetRect) return
    setSetupLogoHandoff({
      ...setupLogoHandoff,
      phase: 'move',
      to: targetRect
    })
  }, [setupLogoHandoff, showSetupWizard, wizardLogoAnchorNode])

  const handleSetupLogoHopDone = useCallback((): void => {
    setSetupLogoHandoff((current) => current?.phase === 'move' ? null : current)
  }, [])

  const handleSetupCompleteHopDone = useCallback((): void => {
    setWorkspaceLaunchTransition((current) => {
      if (current?.phase !== 'setup-complete-to-center') return current
      const centerRect = centeredLaunchLogoRect()
      return { ...current, phase: 'preparing', from: centerRect, to: centerRect }
    })
  }, [])

  useEffect(() => {
    if (!workspaceLaunchTransition) return
    if (status !== 'error') return
    if (workspaceLaunchTransition.phase === 'error-reveal') return

    setLaunchErrorScreenVisible(true)
    const centerRect = centeredLaunchLogoRect()
    setWorkspaceLaunchTransition({
      ...workspaceLaunchTransition,
      phase: 'error-reveal',
      from: centerRect,
      to: centerRect,
      target: 'error'
    })
  }, [status, workspaceLaunchTransition])

  useEffect(() => {
    if (status === 'connected' || workspacePath === '') {
      setLaunchErrorScreenVisible(false)
    }
  }, [status, workspacePath])

  const prevStatusRef = useRef<string>('')

  useEffect(() => {
    if (status === 'connected' && prevStatusRef.current !== 'connected') {
      performance.mark('app:connected')
      performance.measure('app:startup', 'app:bootstrap-start', 'app:connected')
    }
    prevStatusRef.current = status
  }, [status])

  const connected = status === 'connected'
  useEffect(() => connected ? scheduleIdlePrefetch() : undefined, [connected])

  useEffect(() => {
    if (!isDesktopPluginMainView(activeMainView) || activeDesktopPluginView) return
    const ui = useUIStore.getState()
    runWithoutAppNavigationRecording(() => {
      if (capabilities?.pluginManagement === true) {
        ui.setPluginCatalogSurface('plugins')
        ui.setActiveMainView('skills')
        return
      }
      ui.setActiveMainView('conversation')
    })
    replaceCurrentAppNavigationLocation()
  }, [activeDesktopPluginView, activeMainView, capabilities?.pluginManagement])

  useEffect(() => {

    const unsubscribe = window.api.appServer.onNotification(
      function applyNotification(payload) {
        const foreground = payload.foreground !== false
          && (!payload.workspacePath || normalizeWorkspacePathForPinnedLookup(payload.workspacePath)
            === normalizeWorkspacePathForPinnedLookup(protocolWorkspacePathRef.current || workspacePathRef.current))
        if (applyThreadNotification(payload, {
          locale: localeRef.current, foreground, updateWarmThread, updateWarmThreadMode, releaseWarmThread, reloadThreadList
        })) return
        if (payload.foreground === false) {
          if (payload.method === 'thread/deleted' && payload.workspacePath) {
            const threadId = (payload.params as { threadId?: string } | undefined)?.threadId
            if (threadId) useDetailPanelStore.getState().onThreadDeleted(threadId, { projectKey: normalizeWorkspaceProjectKey(payload.workspacePath) })
          }
          return
        }
        const notificationWorkspacePath = payload.workspacePath?.trim()
        const foregroundWorkspacePath = protocolWorkspacePathRef.current || workspacePathRef.current
        if (
          payload.foreground !== true &&
          notificationWorkspacePath &&
          foregroundWorkspacePath &&
          normalizeWorkspacePathForPinnedLookup(notificationWorkspacePath) !==
            normalizeWorkspacePathForPinnedLookup(foregroundWorkspacePath)
        ) {
          return
        }
        if (applyMcpNotification(payload, localeRef.current)) return
        const method = payload.method
        const p = (payload.params ?? {}) as Record<string, unknown>
        switch (method as string) {
          case 'system/jobResult': {
            const jobName = (p.jobName as string) ?? (p.name as string) ?? 'Job'
            const resultText = (p.result as string) ?? (p.text as string) ?? ''
            const errText = (p.error as string) ?? ''
            const usage = p.tokenUsage as { input?: number; output?: number } | undefined
            let md = `**${jobName}**`
            if (errText) {
              md += `\n\n**Error**\n\n${errText}`
            } else if (resultText) {
              md += `\n\n${resultText}`
            } else {
              md += `\n\n_Completed._`
            }
            if (usage != null && ((usage.input ?? 0) > 0 || (usage.output ?? 0) > 0)) {
              md += `\n\n_Tokens: ${usage.input ?? 0} in · ${usage.output ?? 0} out_`
            }
            const tid = p.threadId as string | undefined
            if (tid) {
              md += `\n\n_Thread:_ \`${tid}\``
            }
            addJobResultToast(md, true)
            break
          }

          case 'automation/updated': {
            const store = useAutomationsStore.getState()
            if (p.removed === true && typeof p.automationId === 'string') store.removeAutomation(p.automationId)
            else if (p.automation) store.upsertAutomation(p.automation as AutomationDefinition)
            break
          }
          case 'automation/run/updated': {
            if (p.run) useAutomationsStore.getState().upsertRun(p.run as AutomationRun)
            break
          }

          case 'app/list/updated':
          case 'app/connection/changed':
          case 'thread/appBindings/changed': {
            useAppBindingStore.getState().handleNotification(method, p)
            break
          }

          case 'remoteToolHost/route/changed': {
            useThreadRouteStore.getState().handleRouteChanged(p)
            break
          }

          // A runtime-only plugin transition emits no config/changed, so this is its only signal.
          case 'plugin/snapshot/updated': {
            usePluginStore.getState().handleSnapshotUpdated(p.snapshotRevision)
            break
          }

          case 'config/changed': {
            const event = resolveConfigChangedPayload(
              payload,
              configChangedDedupeRef.current
            )
            if (!event) break

            if (!applyWorkspaceResourceConfigChange(event.regions, payload)) break
            if (event.regions.includes('skills')) {
              void useSkillsStore.getState().fetchSkills()
            }
            if (event.regions.includes('plugins')) {
              void usePluginStore.getState().fetchPlugins()
            }
            if (event.regions.includes('hooks')) {
              void useHooksStore.getState().fetchHooks()
            }

            setWorkspaceConfigChange(event)
            setWorkspaceConfigChangeSeq((seq) => seq + 1)
            break
          }

          default:
            break
        }
      }
    )
    return unsubscribe
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const unsubscribeKnown = window.api.appServer.onServerRequest((payload) => {
      const { bridgeId, method, params } = payload
      const p = (params ?? {}) as Record<string, unknown>

      if (useSideChatStore.getState().handleServerRequest(method, bridgeId, p)) return
      if (applyThreadServerRequest(payload)) return

      window.api.appServer.sendServerResponse(bridgeId, {
        error: `Unsupported server request: ${method}`
      })
    })

    const unsubscribeRaw = window.api.appServer.onServerRequestRaw((payload) => {
      const { bridgeId, method, params } = payload
      const p = (params ?? {}) as Record<string, unknown>
      if (method === 'ui/tool/approval/request') {
        // Desktop-only mutate approval is intentionally outside the stable AppServer Catalog.
        useConversationStore.getState().setGenericApproval(buildUiToolApproval(p, bridgeId, localeRef.current))
        return
      }

      window.api.appServer.sendServerResponse(bridgeId, {
        error: `Unsupported server request: ${method}`
      })
    })

    return () => {
      unsubscribeKnown()
      unsubscribeRaw()
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent): void {
      const ctrl = e.ctrlKey || e.metaKey

      if (handleAppNavigationShortcut(e)) return

      if (handleFindShortcut(e)) return

      if (ctrl && e.altKey && !e.shiftKey && e.code === 'KeyS' && (e.key.toLowerCase() === 's' || e.key === 'ß')) {
        if (openActiveSideChat((key, vars) => translate(localeRef.current, key, vars))) e.preventDefault()
        return
      }

      if (e.ctrlKey && !e.altKey && !e.shiftKey && !e.metaKey && e.key === '/') {
        if (toggleSideChatFocus()) e.preventDefault()
        return
      }

      if (e.key === 'Escape') {
        if ((e.target as HTMLElement | null)?.closest?.('[data-side-chat]')) return
        const convState = useConversationStore.getState()
        if (convState.turnStatus === 'running') {
          const activeId = useThreadStore.getState().activeThreadId
          const turnId = convState.activeTurnId
          // Don't send interrupt if we only have a local optimistic ID (server hasn't confirmed yet)
          if (activeId && turnId && !turnId.startsWith('local-turn-')) {
            void interruptTurn({
              threadId: activeId,
              turnId,
              onError: (error) => {
                addToast(translate(localeRef.current, 'composer.stopFailed', {
                  error: error instanceof Error ? error.message : String(error)
                }), 'error')
              }
            })
          }
        }
        return
      }

      if (ctrl && e.key === 'n') {
        e.preventDefault()
        useUIStore.getState().goToNewChat()
      }

      if (ctrl && e.key === 'k') {
        e.preventDefault()
        const focusFn = (window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus
        focusFn?.()
        return
      }

      if (ctrl && !e.shiftKey && e.key === 'b') {
        e.preventDefault()
        useUIStore.getState().toggleSidebar()
        return
      }

      if (ctrl && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'p') {
        if (remoteWorkspaceActiveRef.current) return
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) {
          return
        }
        const ui = useUIStore.getState()
        if (ui.quickOpenVisible) {
          e.preventDefault()
          return
        }
        e.preventDefault()
        ui.setQuickOpenVisible(true)
        useDetailPanelStore.getState().setDetailPanelVisible(true)
        return
      }

      if (ctrl && e.shiftKey && !e.altKey && e.key.toLowerCase() === 'f') {
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) return
        const ui = useUIStore.getState()
        if (ui.activeMainView !== 'conversation') return
        e.preventDefault()
        useDetailPanelStore.getState().toggleDetailPanelFullView()
        return
      }

      if (ctrl && e.shiftKey && e.key === 'B') {
        e.preventDefault()
        useDetailPanelStore.getState().toggleDetailPanel()
        return
      }

      if (ctrl && e.shiftKey && e.key === 'O') {
        if (!showMainWorkspaceUiRef.current) return
        e.preventDefault()
        beginAddProjectRef.current()
        return
      }

      if (ctrl && e.shiftKey && e.key === 'N') {
        e.preventDefault()
        void window.api.workspace.openNewWindow()
        return
      }

      if (ctrl && e.key === ',') {
        e.preventDefault()
        useUIStore.getState().setActiveMainView('settings')
        return
      }

      if (ctrl && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 't') {
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) return
        e.preventDefault()
        performAddTabAction('newBrowser', {
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      if (ctrl && !e.shiftKey && e.key === '`') {
        if (remoteWorkspaceActiveRef.current) return
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) return
        e.preventDefault()
        performAddTabAction('newTerminal', {
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      if (ctrl && e.shiftKey && e.key === 'G') {
        e.preventDefault()
        performAddTabAction('newChanges', {
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      if (ctrl && e.shiftKey && e.key === 'P') {
        e.preventDefault()
        performAddTabAction('newPlan', {
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      if (ctrl && e.shiftKey && e.key === 'C') {
        e.preventDefault()
        const convState = useConversationStore.getState()
        const turns = convState.turns
        for (let i = turns.length - 1; i >= 0; i--) {
          const items = turns[i].items
          for (let j = items.length - 1; j >= 0; j--) {
            const item = items[j]
            if (item.type === 'agentMessage' && item.text) {
              const text = formatWebCitations(item.text, collectWebCitationSources(turns.slice(0, i + 1)), 'copy')
              navigator.clipboard.writeText(text).then(() => {
                addToast(translate(localeRef.current, 'toast.copied'), 'success', 2000)
              }).catch(() => {})
              return
            }
          }
        }
        return
      }
    }

    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  useEffect(() => {
    const unsubscribeOpen = window.api.workspace.viewer.browserUse.onOpen(handleBrowserUseOpen)
    const unsubscribeClose = window.api.workspace.viewer.browserUse.onClose(handleBrowserUseClose)
    return () => {
      unsubscribeOpen()
      unsubscribeClose()
    }
  }, [])

  useEffect(() => {
    const unsubscribe = window.api.workspace.viewer.browser.onEvent((event) => {
      handleBrowserEvent(event, {
        locale: localeRef.current,
        workspacePath: workspacePathRef.current
      })
    })
    return () => {
      unsubscribe()
    }
  }, [])

  useEffect(() => {
    const unsubscribe = window.api.workspace.viewer.browserUse.onApprovalRequest((event) => {
      useConversationStore.getState().setGenericApproval(buildBrowserUseApproval(event, localeRef.current))
    })
    return () => {
      unsubscribe()
    }
  }, [])

  useActiveThreadModeSync(activeProjectKey)
  useActiveThreadReadReceipt()

  const isFatalError = isFatalConnectionError(status, errorType)
  const showErrorScreen = isFatalError || launchErrorScreenVisible
  const keepWelcomeDuringLaunch =
    (
      workspaceLaunchTransition?.phase === 'welcome-hold' &&
      workspaceStatus.status !== 'needs-setup'
    ) ||
    workspaceLaunchTransition?.phase === 'welcome-to-center'

  const showWelcome = (!workspacePath || keepWelcomeDuringLaunch) && !showErrorScreen
  const showSetupInterstitial =
    workspacePath !== '' &&
    workspaceStatus.status === 'needs-setup' &&
    !showSetupWizard &&
    !showErrorScreen
  const keepSetupFlowDuringCompletionCover =
    workspacePath !== '' &&
    showSetupWizard &&
    workspaceLaunchTransition?.phase === 'setup-complete-to-center'
  const showSetupFlow =
    workspacePath !== '' &&
    showSetupWizard &&
    !showErrorScreen &&
    (workspaceStatus.status === 'needs-setup' || keepSetupFlowDuringCompletionCover)
  const showMainWorkspaceUi =
    workspacePath !== '' &&
    workspaceStatus.status === 'ready' &&
    !showErrorScreen &&
    !showWelcome &&
    !showSetupInterstitial &&
    !showSetupFlow
  showMainWorkspaceUiRef.current = showMainWorkspaceUi

  useEffect(() => {
    if (!showMainWorkspaceUi) {
      stopAppNavigationHistory()
      return
    }
    return startAppNavigationHistory(protocolWorkspacePath || workspacePath)
  }, [protocolWorkspacePath, showMainWorkspaceUi, workspacePath])

  useEffect(() => {
    if (!showMainWorkspaceUi) return
    if (whatsNewAutoCheckedVersionRef.current === APP_VERSION) return
    whatsNewAutoCheckedVersionRef.current = APP_VERSION

    let disposed = false
    void (async () => {
      try {
        const [settings, allReleases] = await Promise.all([
          window.api.settings.get(),
          window.api.whatsNew.getReleases()
        ])
        if (disposed) return
        lastSeenWhatsNewVersionRef.current = settings.lastSeenWhatsNewVersion
        const unseen = getUnseenWhatsNewReleases(allReleases, APP_VERSION, settings.lastSeenWhatsNewVersion)
        const markSeenVersion = getLatestWhatsNewVersion(unseen)
        if (unseen.length === 0 || !markSeenVersion) return

        pendingAutoWhatsNewRef.current = {
          releases: unseen,
          markSeenVersion
        }
        startWhatsNewMediaPrefetch(unseen, { openAutoWhenReady: true })
      } catch {
        // Ignore automatic prompt failures; the manual entry point still works.
      }
    })()

    return () => {
      disposed = true
    }
  }, [showMainWorkspaceUi, startWhatsNewMediaPrefetch])

  const setupFlowWorkspaceStatus =
    workspaceStatus.status === 'needs-setup'
      ? workspaceStatus
      : setupWorkspaceStatusSnapshotRef.current ?? workspaceStatus
  const launchOverlay = workspaceLaunchTransition
    ? (
        <>
          <WorkspaceLaunchTransition
            phase={workspaceLaunchTransition.phase}
            from={workspaceLaunchTransition.from}
            to={workspaceLaunchTransition.to}
          />
          {workspaceLaunchTransition.phase === 'setup-complete-to-center' && (
            <WorkspaceSetupLogoHop
              from={workspaceLaunchTransition.from}
              to={workspaceLaunchTransition.to}
              logoSrc={workspaceLaunchTransition.logoSrc}
              onDone={handleSetupCompleteHopDone}
            />
          )}
        </>
      )
    : null
  const setupHandoffOverlay = setupLogoHandoff?.phase === 'hold'
    ? <WorkspaceSetupLogoHandoff from={setupLogoHandoff.from} />
    : setupLogoHandoff?.phase === 'move'
      ? (
          <WorkspaceSetupLogoHop
            from={setupLogoHandoff.from}
            to={setupLogoHandoff.to}
            onDone={handleSetupLogoHopDone}
          />
        )
      : null
  const hideSetupInterstitialLogo =
    (workspaceLaunchTransition != null && showSetupInterstitial) ||
    (setupLogoHandoff != null && showSetupInterstitial)
  const hideSetupWizardLogo =
    (workspaceLaunchTransition != null && showSetupFlow) ||
    (setupLogoHandoff != null && showSetupFlow)
  const deferSetupWizardContent = setupLogoHandoff?.phase === 'hold' && showSetupFlow

  let content: ReactNode

  if (showErrorScreen) {
    content = (
      <>
        <ConfirmDialogHost />
        <ToastContainer />
        <ErrorScreen
          onOpenSettings={() => {
            setLaunchErrorScreenVisible(false)
            useConnectionStore.getState().setStatus({ status: 'disconnected' })
            const ui = useUIStore.getState()
            ui.setActiveMainView('settings')
            ui.setActiveSettingsTab('connections')
          }}
        />
      </>
    )
  } else if (showWelcome) {
    content = (
      <>
        <ToastContainer />
        <WelcomeScreen onOpenWorkspace={handleOpenWorkspaceFromWelcome} />
      </>
    )
  } else if (showSetupInterstitial) {
    content = (
      <>
        <ToastContainer />
        <WorkspaceSetupInterstitial
          workspacePath={workspacePath}
          isOpening={setupOpening}
          hideLogo={hideSetupInterstitialLogo}
          logoAnchorRef={setSetupLogoAnchorNode}
          onStart={handleStartWorkspaceSetup}
          onChooseDifferentWorkspace={() => {
            void window.api.workspace.clearSelection()
          }}
        />
      </>
    )
  } else if (showSetupFlow) {
    content = (
      <>
        <ToastContainer />
        <WorkspaceSetupWizard
          workspaceStatus={setupFlowWorkspaceStatus}
          hideLogo={hideSetupWizardLogo}
          deferContent={deferSetupWizardContent}
          logoAnchorRef={setWizardLogoAnchorNode}
          onRunSetup={handleRunWorkspaceSetup}
          onCancel={() => {
            setShowSetupWizard(false)
          }}
        />
      </>
    )
  } else {
    content = (
      <>
        <ConfirmDialogHost />
        <ToastContainer />
        <SkillDetailHost />
        {addProject.dialog}
        {quickOpenVisible && (
          <QuickOpenDialog
            onClose={() => setQuickOpenVisible(false)}
          />
        )}
        {status === 'disconnected' && isExpectedRestart && (
          <div
            role="status"
            aria-live="polite"
            style={{
              padding: '8px 16px',
              backgroundColor: 'rgba(56, 189, 248, 0.12)',
              borderBottom: '1px solid rgba(56, 189, 248, 0.35)',
              color: 'var(--text-primary)',
              fontSize: '12px',
              flexShrink: 0
            }}
          >
            {translate(locale, 'settings.restartingAppServer')}
          </div>
        )}
        {activeMainView === 'settings' && pendingRestartVisible && (
          <div
            role="status"
            aria-live="polite"
            style={{
              padding: '8px 16px',
              backgroundColor: 'rgba(245, 158, 11, 0.12)',
              borderBottom: '1px solid rgba(245, 158, 11, 0.35)',
              color: 'var(--text-primary)',
              fontSize: '12px',
              flexShrink: 0,
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '8px 12px',
              flexWrap: 'wrap'
            }}
          >
            <span style={{ minWidth: '180px', flex: '1 1 220px', overflowWrap: 'anywhere' }}>
              {translate(locale, pendingRestartMessageKey)}
            </span>
            <span style={{ display: 'flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => ignorePendingRestart()}
                disabled={pendingRestartApplying}
              >
                {translate(locale, 'settings.pendingRestart.ignore')}
              </Button>
              <Button
                variant="outline"
                size="sm"
                onClick={() => {
                  void applyPendingRestart()
                }}
                disabled={pendingRestartApplying}
              >
                {pendingRestartApplying
                  ? translate(locale, pendingRestartApplyingKey)
                  : translate(locale, pendingRestartApplyKey)}
              </Button>
            </span>
          </div>
        )}
        {showSlowConnectingHint && (
          <div
            role="status"
            aria-live="polite"
            style={{
              padding: '8px 16px',
              backgroundColor: 'rgba(245, 158, 11, 0.12)',
              borderBottom: '1px solid rgba(245, 158, 11, 0.35)',
              color: 'var(--text-primary)',
              fontSize: '12px',
              flexShrink: 0
            }}
          >
            {errorMessage?.trim() || translate(locale, 'connection.startupTakingLong')}
          </div>
        )}
        <ThreePanel
          sidebar={
            activeMainView === 'settings'
              ? <SettingsSidebar />
              : (
                  <Sidebar
                    workspaceName={displayWorkspaceName}
                    workspacePath={workspaceStatus.remote?.workspaceDir?.trim() || workspacePath}
                    workspaceOpening={workspaceOpening}
                  />
                )
          }
          conversation={
            <MainViewOutlet
              activeView={activeMainView}
              retains={retainsMainView}
              renderView={(view) => {
                if (view === 'settings') {
                  prefetchCoreMainView('settings')
                  return (
                    <CoreMainViewBoundary fallback={<SettingsViewSkeleton />}>
                      <CoreSettingsView
                        key={activeProjectKey || protocolWorkspacePath || workspacePath}
                        workspacePath={workspacePath}
                        identityWorkspacePath={protocolWorkspacePath || workspacePath}
                        onThreadListRefreshRequested={() => {
                          void reloadThreadList()
                        }}
                        workspaceConfigChange={workspaceConfigChange}
                        workspaceConfigChangeSeq={workspaceConfigChangeSeq}
                        openChromeSettingsSeq={chromeSettingsOpenSeq}
                      />
                    </CoreMainViewBoundary>
                  )
                }
                if (view === 'channels') return <CoreMainViewBoundary><CoreChannelsView /></CoreMainViewBoundary>
                if (view === 'agents') return <CoreMainViewBoundary><CoreAgentBuilderView /></CoreMainViewBoundary>
                if (view === 'skills') return <CoreMainViewBoundary><CorePluginsView /></CoreMainViewBoundary>
                if (view === 'automations') return <CoreMainViewBoundary><CoreAutomationsView /></CoreMainViewBoundary>
                if (view === 'conversation') {
                  return (
                    <ConversationPanel
                      workspacePath={workspacePath}
                      identityWorkspacePath={protocolWorkspacePath || workspacePath}
                      projectKey={activeProjectKey}
                      workspaceReady={foregroundReady}
                      remoteWorkspace={remoteWorkspaceActive}
                    />
                  )
                }
                const pluginView = desktopPluginMainViews.find((entry) => entry.viewKey === view)
                return pluginView ? <DesktopPluginMainViewOutlet contribution={pluginView} /> : null
              }}
            />
          }
          detail={<DetailPanel workspacePath={activeConversationWorkspacePath} remoteWorkspace={remoteWorkspaceActive} />}
        />
      </>
    )
  }

  return (
    <WindowFrame
      plainSurface={showWelcome || showSetupInterstitial || showSetupFlow}
      overlays={(
        <>
          {setupHandoffOverlay}
          {launchOverlay}
        </>
      )}
    >
      <>
        {content}
        <FindOverlay />
        {whatsNewDialog && (
          <WhatsNewDialog
            releases={whatsNewDialog.releases}
            mediaStates={whatsNewMediaStates}
            onClose={closeWhatsNew}
          />
        )}
        <McpElicitationHost />
      </>
    </WindowFrame>
  )
}
