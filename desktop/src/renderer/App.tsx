import { useCallback, useEffect, useRef, useState } from 'react'
import type { CSSProperties, ReactNode } from 'react'
import { translate, type AppLocale } from '../shared/locales'
import { useLocale } from './contexts/LocaleContext'
import { basename } from './utils/path'
import { initConnectionStore, useConnectionStore } from './stores/connectionStore'
import { useThreadStore, type ThreadRuntimeSnapshot } from './stores/threadStore'
import {
  selectLatestCreatePlanTurnId,
  selectStreamingPlanItemId,
  useConversationStore
} from './stores/conversationStore'
import { useUIStore } from './stores/uiStore'
import { useViewerTabStore } from './stores/viewerTabStore'
import { useWindowMaximized } from './hooks/useWindowMaximized'
import { QuickOpenDialog } from './components/detail/QuickOpenDialog'
import { ThreePanel } from './components/layout/ThreePanel'
import { PluginsView } from './components/plugins/PluginsView'
import { AutomationsView } from './components/automations/AutomationsView'
import { useAutomationsStore } from './stores/automationsStore'
import { useCronStore, type CronJobWire } from './stores/cronStore'
import { useReviewPanelStore } from './stores/reviewPanelStore'
import type { AutomationTask } from './stores/automationsStore'
import { useModelCatalogStore } from './stores/modelCatalogStore'
import { useProvidersStore } from './stores/providersStore'
import { useMcpStore, type McpServerStatusWire } from './stores/mcpStore'
import { useSkillsStore } from './stores/skillsStore'
import { usePluginStore } from './stores/pluginStore'
import { usePendingRestartStore } from './stores/pendingRestartStore'
import { useSubAgentStore } from './stores/subAgentStore'
import { useAppBindingStore } from './stores/appBindingStore'
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
  centeredLaunchLogoRect,
  elementToLaunchLogoRect,
  type LaunchLogoRect,
  type WorkspaceSetupLogoHandoffPhase,
  type WorkspaceLaunchTransitionPhase
} from './components/WorkspaceLaunchTransition'
import { ConfirmDialogHost } from './components/ui/ConfirmDialog'
import { ToastContainer } from './components/ui/ToastContainer'
import { SettingsView } from './components/settings/SettingsView'
import { ChannelsView } from './components/channels/ChannelsView'
import { TeamsView } from './components/teams/TeamsView'
import { WhatsNewDialog } from './components/whats-new/WhatsNewDialog'
import { addJobResultToast, addToast } from './stores/toastStore'
import type { ContextUsageSnapshotWire, SessionIdentity, Thread, ThreadGoal, ThreadSummary } from './types/thread'
import { wireTurnToConversationTurn } from './types/conversation'
import type { ConversationItem, ConversationTurn, QueuedTurnInput } from './types/conversation'
import type { SubAgentEntry } from './types/toolCall'
import { applyTheme, resolveTheme } from './utils/theme'
import { resolveDefaultCrossChannelOrigins } from './utils/visibleChannelsDefaults'
import { buildComposerInputParts } from './utils/composeInputParts'
import { getFallbackThreadName } from './utils/threadFallbackName'
import { handleBrowserEvent } from './utils/browserEventHandler'
import { handleBrowserUseOpen } from './utils/browserUseOpenHandler'
import { performAddTabAction } from './utils/detailTabActions'
import { getSubAgentParentThreadId, isSubAgentThread } from './utils/subAgentThreads'
import { isFatalConnectionError, useSlowConnectingHint } from './utils/connectionUi'
import { isAgentTeamsPluginEnabled } from './utils/agentTeamsPlugin'
import {
  resolveWorkspaceConfigChangedPayload,
  type WorkspaceConfigChangedPayload
} from './utils/workspaceConfigChanged'
import {
  compareAppVersions,
  getLatestWhatsNewVersion,
  getWhatsNewMediaStateKey,
  getUnseenWhatsNewReleases,
  getWhatsNewReleasesUpTo,
  type WhatsNewMediaState,
  type WhatsNewRelease
} from '../shared/whatsNew'
import type {
  BrowserUseApprovalRequestPayload,
  BrowserUseApprovalResponseAction,
  DiscoveredModule,
  ModuleStatusMap,
  WorkspaceSetupRequest,
  WorkspaceStatusPayload
} from '../preload/api.d'
import './styles/tokens.css'

const SETUP_PAGE_HANDOFF_PREP_MS = 260
const SETUP_PAGE_HANDOFF_MOVE_MS = 420
const WORKSPACE_LAUNCH_TRANSITION_MS = 620
const WORKSPACE_LAUNCH_REVEAL_MS = 360
const APP_VERSION = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.0.0'

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
  phase: WorkspaceSetupLogoHandoffPhase
  from: LaunchLogoRect
  to: LaunchLogoRect
}

const DEFAULT_RENDERER_WORKSPACE_STATUS: WorkspaceStatusPayload = {
  status: 'no-workspace',
  workspacePath: '',
  hasUserConfig: false,
  providers: []
}

function serverTextVars(value: unknown): Record<string, string | number> | undefined {
  if (!value || typeof value !== 'object' || Array.isArray(value)) return undefined
  const vars: Record<string, string | number> = {}
  for (const [key, raw] of Object.entries(value as Record<string, unknown>)) {
    if (typeof raw === 'string' || typeof raw === 'number') {
      vars[key] = raw
    } else if (typeof raw === 'boolean') {
      vars[key] = raw ? 'true' : 'false'
    }
  }
  return Object.keys(vars).length > 0 ? vars : undefined
}

function serverFallbackText(
  locale: AppLocale,
  messageKey: unknown,
  params: unknown,
  fallbackText: unknown,
  legacyMessage?: unknown
): string | null {
  const fallback =
    typeof fallbackText === 'string'
      ? fallbackText
      : typeof legacyMessage === 'string'
        ? legacyMessage
        : null
  if (typeof messageKey !== 'string' || messageKey.trim() === '') {
    return fallback
  }
  const key = messageKey.trim()
  const localized = translate(locale, key, serverTextVars(params))
  return localized === key ? fallback : localized
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
  return path.trim().replace(/\\/g, '/').replace(/\/+$/u, '').toLowerCase()
}

function resolvePinnedThreadIdsForWorkspace(
  pinnedByWorkspace: Record<string, string[]> | undefined,
  workspacePath: string
): string[] {
  if (!pinnedByWorkspace) return []
  const exact = pinnedByWorkspace[workspacePath]
  if (Array.isArray(exact)) return exact

  const normalizedWorkspacePath = normalizeWorkspacePathForPinnedLookup(workspacePath)
  const match = Object.entries(pinnedByWorkspace).find(
    ([candidate]) => normalizeWorkspacePathForPinnedLookup(candidate) === normalizedWorkspacePath
  )
  return match?.[1] ?? []
}

function BrowserUseApprovalDialog({
  locale,
  request,
  onRespond
}: {
  locale: AppLocale
  request: BrowserUseApprovalRequestPayload
  onRespond: (action: BrowserUseApprovalResponseAction) => void
}): JSX.Element {
  const session = request.sessionName?.trim()
  const message = session
    ? translate(locale, 'browserUse.approval.messageWithSession', { session, domain: request.domain })
    : translate(locale, 'browserUse.approval.message', { domain: request.domain })
  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="browser-approval-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
    >
      <div
        style={{
          width: '460px',
          maxWidth: 'calc(100vw - 48px)',
          border: '1px solid var(--border-default)',
          borderRadius: '12px',
          background: 'var(--bg-secondary)',
          boxShadow: 'var(--shadow-level-3)',
          padding: '22px'
        }}
      >
        <h2
          id="browser-approval-title"
          style={{ margin: 0, fontSize: '16px', fontWeight: 700, color: 'var(--text-primary)' }}
        >
          {translate(locale, 'browserUse.approval.title')}
        </h2>
        <p style={{ margin: '8px 0 14px', fontSize: '13px', lineHeight: 1.5, color: 'var(--text-secondary)' }}>
          {message}
        </p>
        <div
          style={{
            border: '1px solid var(--border-default)',
            borderRadius: '8px',
            background: 'var(--bg-primary)',
            padding: '10px',
            fontFamily: 'var(--font-mono)',
            fontSize: '12px',
            color: 'var(--text-secondary)',
            overflowWrap: 'anywhere'
          }}
        >
          {request.url}
        </div>
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '8px', marginTop: '18px', flexWrap: 'wrap' }}>
          <button type="button" onClick={() => onRespond('deny')} style={dialogSecondaryButtonStyle()}>
            {translate(locale, 'browserUse.approval.cancel')}
          </button>
          <button type="button" onClick={() => onRespond('blockDomain')} style={dialogSecondaryButtonStyle(true)}>
            {translate(locale, 'browserUse.approval.blockDomain')}
          </button>
          <button type="button" onClick={() => onRespond('allowOnce')} style={dialogSecondaryButtonStyle()}>
            {translate(locale, 'browserUse.approval.allowOnce')}
          </button>
          <button type="button" onClick={() => onRespond('allowDomain')} style={dialogPrimaryButtonStyle()}>
            {translate(locale, 'browserUse.approval.alwaysAllow')}
          </button>
        </div>
      </div>
    </div>
  )
}

function dialogSecondaryButtonStyle(danger = false): CSSProperties {
  return {
    padding: '8px 12px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: danger ? 'var(--error)' : 'var(--text-primary)',
    fontSize: '13px',
    fontWeight: 600,
    cursor: 'pointer'
  }
}

function dialogPrimaryButtonStyle(): CSSProperties {
  return {
    padding: '8px 12px',
    border: '1px solid var(--text-primary)',
    borderRadius: '8px',
    background: 'var(--text-primary)',
    color: 'var(--bg-primary)',
    fontSize: '13px',
    fontWeight: 700,
    cursor: 'pointer'
  }
}

function topBannerSecondaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '6px 10px',
    border: '1px solid var(--border-default)',
    borderRadius: '8px',
    background: 'transparent',
    color: 'var(--text-primary)',
    fontSize: '12px',
    fontWeight: 600,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1
  }
}

function topBannerPrimaryButtonStyle(disabled = false): CSSProperties {
  return {
    padding: '6px 10px',
    border: '1px solid var(--text-primary)',
    borderRadius: '8px',
    background: 'var(--text-primary)',
    color: 'var(--bg-primary)',
    fontSize: '12px',
    fontWeight: 700,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1
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
      style={{
        position: 'relative',
        display: 'flex',
        flexDirection: 'column',
        height: '100%',
        width: '100%',
        overflow: 'hidden',
        isolation: 'isolate',
        borderRadius: useRendererRadius && !maximized ? 'var(--shell-window-radius)' : 0,
        background: plainSurface ? 'var(--welcome-surface)' : 'var(--chrome-glass)',
        boxShadow: 'inset 0 0 0 1px var(--shell-chrome-border)'
      }}
    >
      <AppChrome>{children}</AppChrome>
      {overlays}
    </div>
  )
}

/**
 * Root application component.
 * - Initializes connection store and thread store
 * - Loads thread list when connected
 * - Wires thread/started + thread/statusChanged notifications
 * - Registers global shortcuts for navigation and thread actions
 * - Spec §9, §12
 */
export function App(): JSX.Element {
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

  const [workspacePath, setWorkspacePath] = useState(initialWorkspacePath)
  const [workspaceName, setWorkspaceName] = useState(
    initialWorkspacePath ? basename(initialWorkspacePath) : 'DotCraft'
  )
  const [workspaceConfigChange, setWorkspaceConfigChange] = useState<WorkspaceConfigChangedPayload | null>(null)
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
  const showSlowConnectingHint = useSlowConnectingHint(status, workspacePath)
  const [browserUseApprovalRequests, setBrowserUseApprovalRequests] = useState<BrowserUseApprovalRequestPayload[]>([])
  const [chromeSettingsOpenSeq, setChromeSettingsOpenSeq] = useState(0)
  const [whatsNewDialog, setWhatsNewDialog] = useState<{
    releases: WhatsNewRelease[]
    markSeenVersion?: string
  } | null>(null)
  const [whatsNewMediaStates, setWhatsNewMediaStates] = useState<Record<string, WhatsNewMediaState>>({})
  const activeMainView = useUIStore((s) => s.activeMainView)
  const activeDetailTab = useUIStore((s) => s.activeDetailTab)
  const detailPanelVisible = useUIStore((s) => s.detailPanelVisible)
  const quickOpenVisible = useUIStore((s) => s.quickOpenVisible)
  const plugins = usePluginStore((s) => s.plugins)
  const agentTeamsAvailable = isAgentTeamsPluginEnabled(plugins)
  const agentTeamsAvailableRef = useRef(agentTeamsAvailable)
  agentTeamsAvailableRef.current = agentTeamsAvailable
  const whatsNewOpenRequestSeq = useUIStore((s) => s.whatsNewOpenRequestSeq)
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const pendingRestartVisible = usePendingRestartStore((s) => s.visible)
  const pendingRestartApplying = usePendingRestartStore((s) => s.applying)
  const pendingRestartMessageKey = usePendingRestartStore((s) => s.messageKey)
  const pendingRestartApplyKey = usePendingRestartStore((s) => s.applyKey)
  const pendingRestartApplyingKey = usePendingRestartStore((s) => s.applyingKey)
  const applyPendingRestart = usePendingRestartStore((s) => s.apply)
  const ignorePendingRestart = usePendingRestartStore((s) => s.ignore)
  const {
    setThreadList,
    setLoading
  } = useThreadStore()

  const workspacePathRef = useRef(initialWorkspacePath)
  const setupWorkspaceStatusSnapshotRef = useRef<WorkspaceStatusPayload | null>(
    initialWorkspaceStatus.status === 'needs-setup' ? initialWorkspaceStatus : null
  )
  const workspaceStatusHydratedRef = useRef(hasPreloadedWorkspaceStatusRef.current)
  const workspaceConfigChangedDedupeRef = useRef<Map<string, number>>(new Map())
  const moduleConnectedSnapshotRef = useRef<Map<string, boolean>>(new Map())
  const moduleConnectedSnapshotReadyRef = useRef(false)
  const moduleDisplayNameByIdRef = useRef<Map<string, string>>(new Map())
  const whatsNewAutoCheckedVersionRef = useRef<string | null>(null)
  const whatsNewMediaStatesRef = useRef<Record<string, WhatsNewMediaState>>({})
  const whatsNewDialogOpenRef = useRef(false)
  const showMainWorkspaceUiRef = useRef(false)
  const pendingAutoWhatsNewRef = useRef<{
    releases: WhatsNewRelease[]
    markSeenVersion?: string
  } | null>(null)
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

  const reloadThreadList = useCallback(async (options?: { includeTeams?: boolean }) => {
    const path = workspacePathRef.current
    const identity: SessionIdentity = {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${path}`,
      workspacePath: path
    }
    setLoading(true)
    try {
      const settings = await window.api.settings.get()
      const crossChannelOrigins = await resolveDefaultCrossChannelOrigins({
        includeTeams: options?.includeTeams ?? agentTeamsAvailableRef.current
      })
      const params = { identity, crossChannelOrigins, includeSubAgents: true }
      const result = await window.api.appServer.sendRequest('thread/list', params)
      const res = result as { data: ThreadSummary[] }
      const threadStore = useThreadStore.getState()
      threadStore.setThreadList(res.data ?? [])
      threadStore.hydratePinnedThreadIds(
        path,
        resolvePinnedThreadIdsForWorkspace(settings.pinnedThreadIdsByWorkspace, path)
      )
      threadStore.prunePinnedThreadIds()
    } catch (err: unknown) {
      console.error('Failed to load thread list:', err)
      setThreadList([])
    } finally {
      setLoading(false)
    }
  }, [setThreadList, setLoading])

  // -------------------------------------------------------------------------
  // Bootstrap: workspace path + connection store
  // -------------------------------------------------------------------------
  const syncWorkspaceStatus = useCallback((payload: WorkspaceStatusPayload): void => {
    const path = payload.workspacePath ?? ''
    const isInitialWorkspaceStatus = !workspaceStatusHydratedRef.current
    workspaceStatusHydratedRef.current = true
    workspacePathRef.current = path
    if (payload.status === 'needs-setup') {
      setupWorkspaceStatusSnapshotRef.current = payload
    } else if (!path || payload.status === 'no-workspace') {
      setupWorkspaceStatusSnapshotRef.current = null
    }
    setWorkspacePath(path)
    setWorkspaceStatus(payload)
    setWorkspaceName(path ? basename(path) : 'DotCraft')
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
      const setupResult = await window.api.workspace.runSetup(request)
      const importWarning = setupResult?.bootstrapImport?.warning
      if (importWarning) {
        addToast(translate(localeRef.current, 'setupWizard.import.warningToast'), 'warning')
      }
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
    if (workspacePath) {
      window.api.window.setTitle(
        translate(locale, 'app.titleWithWorkspace', { name: workspaceName })
      )
    }
  }, [workspacePath, workspaceName, locale])

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
    window.api.settings
      .get()
      .then((s) => {
        applyTheme(resolveTheme(s.theme))
        useUIStore.getState().setShowThinkingContent(s.showThinkingContent === true)
      })
      .catch(() => {})
  }, [])

  // Keep conversation store workspace path in sync (cumulative diff IPC reads)
  useEffect(() => {
    if (workspacePath) {
      useConversationStore.getState().setWorkspacePath(workspacePath)
    }
  }, [workspacePath])

  // Notify viewerTabStore when workspace changes so all viewer tabs are cleared.
  useEffect(() => {
    useViewerTabStore.getState().onWorkspaceSwitched(workspacePath, {
      onBrowserTabRemoved: (tab) => {
        void window.api.workspace.viewer.browser.destroy({ tabId: tab.id })
      },
      onTerminalTabRemoved: (tab) => {
        void window.api.workspace.viewer.terminal.dispose({ tabId: tab.id })
      }
    })
    useUIStore.getState().resetAutoShowReasons()
  }, [workspacePath])

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

    if (
      workspaceLaunchTransition.phase === 'welcome-to-center' ||
      workspaceLaunchTransition.phase === 'setup-complete-to-center'
    ) {
      const timer = window.setTimeout(() => {
        setWorkspaceLaunchTransition((current) => {
          if (
            !current ||
            (
              current.phase !== 'welcome-to-center' &&
              current.phase !== 'setup-complete-to-center'
            )
          ) {
            return current
          }
          const centerRect = centeredLaunchLogoRect()
          if (current.phase === 'setup-complete-to-center') {
            return {
              ...current,
              phase: 'preparing',
              from: centerRect,
              to: centerRect
            }
          }
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
      const centerRect = centeredLaunchLogoRect()
      setWorkspaceLaunchTransition({
        ...workspaceLaunchTransition,
        phase: 'main-reveal',
        from: centerRect,
        to: centerRect,
        target: 'main'
      })
    }
  }, [setupLogoAnchorNode, status, workspaceLaunchTransition, workspaceStatus.status])

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

  useEffect(() => {
    if (!setupLogoHandoff || setupLogoHandoff.phase !== 'move') return

    const timer = window.setTimeout(() => {
      setSetupLogoHandoff((current) => current?.phase === 'move' ? null : current)
    }, SETUP_PAGE_HANDOFF_MOVE_MS)
    return () => window.clearTimeout(timer)
  }, [setupLogoHandoff])

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

  // -------------------------------------------------------------------------
  // Load thread list when connection becomes "connected"
  // -------------------------------------------------------------------------
  const prevStatusRef = useRef<string>('')
  const prevAgentTeamsAvailableRef = useRef(agentTeamsAvailable)
  useEffect(() => {
    if (status === 'connected' && prevStatusRef.current !== 'connected') {
      performance.mark('app:connected')
      performance.measure('app:startup', 'app:bootstrap-start', 'app:connected')
      void reloadThreadList()

      const caps = useConnectionStore.getState().capabilities
      if (caps?.automations) {
        void useAutomationsStore.getState().fetchTasks()
      }
      if (caps?.cronManagement) {
        void useCronStore.getState().fetchJobs()
      }
      if (caps?.modelCatalogManagement) {
        void useModelCatalogStore.getState().loadIfNeeded(true)
      }
      if (caps?.providerManagement) {
        void useProvidersStore.getState().reload()
      }
      if (caps?.pluginManagement) {
        void usePluginStore.getState().fetchPlugins()
      }
      const hasTasks = caps?.automations === true
      const hasCron = caps?.cronManagement === true
      if (hasCron && !hasTasks) {
        useUIStore.getState().setAutomationsTab('cron')
      } else {
        useUIStore.getState().setAutomationsTab('tasks')
      }
    }
    // Reset all stores when disconnecting (e.g. workspace switch)
    if (status === 'disconnected' || status === 'error') {
      useThreadStore.getState().reset()
      useConversationStore.getState().reset()
      useModelCatalogStore.getState().reset()
      useProvidersStore.getState().reset()
      useMcpStore.getState().reset()
      usePluginStore.setState({
        plugins: [],
        diagnostics: [],
        selectedPluginId: null,
        selectedPlugin: null,
        loading: false,
        error: null,
        detailLoading: false
      })
      useCronStore.getState().reset()
      useAutomationsStore.getState().selectTask(null)
      useSubAgentStore.getState().reset()
      useUIStore.getState().setAutomationsTab('tasks')
      useUIStore.getState().resetDetailTabs()
      if (useUIStore.getState().activeMainView !== 'settings') {
        useUIStore.getState().setActiveMainView('conversation')
      }
      useUIStore.getState().setPendingWelcomeTurn(null)
    }

    prevStatusRef.current = status
  }, [status, reloadThreadList])

  useEffect(() => {
    if (status === 'connected' && capabilities?.pluginManagement === true) {
      void usePluginStore.getState().fetchPlugins()
    }
  }, [capabilities?.pluginManagement, status])

  useEffect(() => {
    const becameAvailable = agentTeamsAvailable && !prevAgentTeamsAvailableRef.current
    prevAgentTeamsAvailableRef.current = agentTeamsAvailable
    if (status === 'connected' && becameAvailable) {
      void reloadThreadList()
    }
  }, [agentTeamsAvailable, reloadThreadList, status])

  useEffect(() => {
    if (activeMainView !== 'teams' || agentTeamsAvailable) return
    const ui = useUIStore.getState()
    if (capabilities?.pluginManagement === true) {
      ui.setPluginCatalogSurface('plugins')
      ui.setActiveMainView('skills')
      return
    }
    ui.setActiveMainView('conversation')
  }, [activeMainView, agentTeamsAvailable, capabilities?.pluginManagement])

  useEffect(() => {
    if (status === 'connected' && capabilities?.modelCatalogManagement === true) {
      void useModelCatalogStore.getState().loadIfNeeded()
      return
    }
    if (status === 'disconnected' || status === 'error') {
      useModelCatalogStore.getState().reset()
    }
  }, [capabilities?.modelCatalogManagement, status])

  // -------------------------------------------------------------------------
  // Wire protocol notifications
  // -------------------------------------------------------------------------
  useEffect(() => {
    // Use empty deps so this effect runs exactly once (on mount) and is cleaned
    // up on unmount. Store actions are accessed via .getState() to avoid closure
    // stale-reference issues and to prevent re-subscription on state changes.
    const unsubscribe = window.api.appServer.onNotification(
      (payload: { method: string; params: unknown }) => {
        const method = payload.method
        const p = (payload.params ?? {}) as Record<string, unknown>
        const conv = useConversationStore.getState()
        const { addThread: doAddThread, updateThreadStatus: doUpdateStatus } =
          useThreadStore.getState()
        const shouldUpdateActiveConversation = (threadId: string | null | undefined): boolean => {
          if (!threadId) return true
          return useThreadStore.getState().activeThreadId === threadId
        }
        const shouldUpdateReviewThread = (threadId: string | null | undefined): boolean => {
          if (!threadId) return false
          return useReviewPanelStore.getState().reviewThreadId === threadId
        }

        switch (method) {
          // ── Thread lifecycle ──────────────────────────────────────────
          case 'thread/started': {
            const pp = p as { thread: ThreadSummary }
            doAddThread(pp.thread)
            if (pp.thread && isSubAgentThread(pp.thread)) {
              const parentThreadId = getSubAgentParentThreadId(pp.thread)
              if (parentThreadId) {
                void useSubAgentStore.getState().fetchChildren(parentThreadId)
              }
            }
            break
          }

          case 'thread/renamed': {
            const pp = p as { threadId: string; displayName: string }
            if (pp.displayName?.trim()) {
              useThreadStore.getState().renameThread(pp.threadId, pp.displayName)
            }
            break
          }

          case 'thread/deleted': {
            const pp = p as { threadId: string }
            if (pp.threadId) {
              void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: pp.threadId }).catch(() => {})
            }
            useThreadStore.getState().removeThreadTree(pp.threadId)
            break
          }

          case 'thread/statusChanged': {
            const pp = p as { threadId: string; newStatus: string }
            if (pp.newStatus === 'archived') {
              useThreadStore.getState().removeThreadTree(pp.threadId)
            } else {
              doUpdateStatus(pp.threadId, pp.newStatus as 'active' | 'paused' | 'archived')
            }
            break
          }

          case 'teams/team/changed': {
            void reloadThreadList({ includeTeams: true })
            break
          }

          case 'thread/queue/updated': {
            const pp = p as { threadId?: string; queuedInputs?: unknown[] }
            if (shouldUpdateActiveConversation(pp.threadId)) {
              useConversationStore.getState().setQueuedInputs((pp.queuedInputs ?? []) as QueuedTurnInput[])
            }
            break
          }

          case 'thread/goal/updated': {
            const pp = p as { threadId?: string; goal?: ThreadGoal }
            if (pp.goal) {
              useThreadStore.getState().setThreadGoal(pp.goal)
            }
            break
          }

          case 'thread/goal/cleared': {
            const pp = p as { threadId?: string }
            if (pp.threadId) {
              useThreadStore.getState().clearThreadGoal(pp.threadId)
            }
            break
          }

          case 'thread/runtimeChanged': {
            const pp = p as {
              threadId?: string
              runtime?: Partial<ThreadRuntimeSnapshot>
            }
            const threadId = typeof pp.threadId === 'string' ? pp.threadId : ''
            if (!threadId) break

            const threadStore = useThreadStore.getState()
            const threadSummary = threadStore.threadList.find((thread) => thread.id === threadId)
            const runtimeSnapshot: ThreadRuntimeSnapshot = {
              running: pp.runtime?.running === true,
              busy: pp.runtime?.busy === true,
              waitingOnApproval: pp.runtime?.waitingOnApproval === true,
              waitingOnInput: pp.runtime?.waitingOnInput === true,
              waitingOnPlanConfirmation: pp.runtime?.waitingOnPlanConfirmation === true,
              maintenanceKind: pp.runtime?.maintenanceKind ?? null
            }
            threadStore.applyRuntimeSnapshot(threadId, {
              ...runtimeSnapshot
            }, {
              isActive: threadStore.activeThreadId === threadId,
              isDesktopOrigin: threadSummary?.originChannel?.toLowerCase() === 'dotcraft-desktop'
            })
            useSubAgentStore.getState().updateChildRuntime(threadId, {
              ...runtimeSnapshot
            })
            if (threadStore.activeThreadId === threadId) {
              useConversationStore.getState().setMaintenanceKind(runtimeSnapshot.maintenanceKind)
            }
            break
          }

          case 'thread/error': {
            const tid = (p.threadId as string | undefined) ?? ''
            const reason = (p.reason as string | undefined) ?? (p.message as string | undefined) ?? 'unknown'
            if (reason === 'not-found' || reason.includes('not found')) {
              useThreadStore.getState().removeThread(tid)
              addToast(translate(localeRef.current, 'toast.threadNotFound'), 'warning')
            }
            break
          }

          case 'thread/archived': {
            const pp = p as { threadId: string }
            const activeId = useThreadStore.getState().activeThreadId
            useThreadStore.getState().removeThreadTree(pp.threadId)
            if (activeId === pp.threadId) {
              addToast(translate(localeRef.current, 'toast.threadArchived'), 'info')
            }
            break
          }

          // ── Turn lifecycle ────────────────────────────────────────────
          case 'turn/started': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const startedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            if (shouldUpdateActiveConversation(startedThreadId)) {
              conv.onTurnStarted(rawTurn)
            }
            if (shouldUpdateReviewThread(startedThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnStarted(rawTurn)
            }
            break
          }

          case 'turn/completed': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const completedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            if (shouldUpdateActiveConversation(completedThreadId)) {
              conv.onTurnCompleted(rawTurn)
            }
            // Fallback: poll thread/read if sidebar still has no displayName (e.g. missed thread/renamed).
            // Primary updates come from thread/renamed broadcast and thread/read on selection.
            if (completedThreadId) {
              void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: completedThreadId }).catch(() => {})
              const ts = useThreadStore.getState()
              const threadEntry = ts.threadList.find((t) => t.id === completedThreadId)
              if (!threadEntry?.displayName) {
                void window.api.appServer
                  .sendRequest('thread/read', { threadId: completedThreadId })
                  .then((res) => {
                    const r = res as { thread?: { displayName?: string | null } }
                    const name = r?.thread?.displayName
                    if (name) useThreadStore.getState().renameThread(completedThreadId, name)
                  })
                  .catch(() => { /* non-critical — ignore */ })
              }
            }
            if (shouldUpdateReviewThread(completedThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnCompleted(rawTurn)
            }
            break
          }

          case 'turn/failed': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const failedThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            const error = (p.error as string) ?? (p.message as string) ?? 'Unknown error'
            const errorCode = (p.code as number | undefined)
              ?? ((p.error as Record<string, unknown> | undefined)?.code as number | undefined)
            if (failedThreadId) {
              void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: failedThreadId }).catch(() => {})
            }
            // -32020 = approval timeout — update the pending approval card
            if (shouldUpdateActiveConversation(failedThreadId)) {
              if (errorCode === -32020 || error.includes('-32020')) {
                conv.onApprovalTimeout()
              }
              conv.onTurnFailed(rawTurn, error)
            }
            if (shouldUpdateReviewThread(failedThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnFailed(rawTurn, error)
            }
            break
          }

          case 'turn/cancelled': {
            const rawTurn = (p.turn ?? p) as Record<string, unknown>
            const cancelledThreadId = (rawTurn.threadId as string | undefined) ?? (p.threadId as string | undefined)
            const reason = (p.reason as string) ?? ''
            if (cancelledThreadId) {
              void window.api.skillMarket?.cleanupDotCraftInstall?.({ threadId: cancelledThreadId }).catch(() => {})
            }
            if (shouldUpdateActiveConversation(cancelledThreadId)) {
              conv.onTurnCancelled(rawTurn, reason)
            }
            if (shouldUpdateReviewThread(cancelledThreadId)) {
              const rs = useReviewPanelStore.getState()
              rs.onTurnCancelled(rawTurn, reason)
            }
            break
          }

          // ── Item lifecycle ────────────────────────────────────────────
          case 'item/started': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onItemStarted(p)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onItemStarted(p)
            }
            break
          }

          case 'item/agentMessage/delta': {
            const tid = (p.threadId as string | undefined) ?? ''
            const delta = (p.delta as string) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onAgentMessageDelta(delta)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onAgentMessageDelta(delta)
            }
            break
          }

          case 'item/reasoning/delta': {
            const tid = (p.threadId as string | undefined) ?? ''
            const delta = (p.delta as string) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onReasoningDelta(delta)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onReasoningDelta(delta)
            }
            break
          }

          case 'item/commandExecution/outputDelta': {
            const tid = (p.threadId as string | undefined) ?? ''
            const params = {
              threadId: (p.threadId as string | undefined),
              turnId: (p.turnId as string | undefined),
              itemId: (p.itemId as string | undefined),
              delta: (p.delta as string | undefined)
            }
            if (shouldUpdateActiveConversation(tid)) {
              conv.onCommandExecutionDelta(params)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onCommandExecutionDelta(params)
            }
            break
          }

          case 'item/toolCall/argumentsDelta': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onToolCallArgumentsDelta({
                threadId: (p.threadId as string | undefined),
                turnId: (p.turnId as string | undefined),
                itemId: (p.itemId as string | undefined),
                toolName: (p.toolName as string | undefined),
                callId: (p.callId as string | undefined),
                delta: (p.delta as string | undefined)
              })
            }
            break
          }

          case 'item/completed': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (shouldUpdateActiveConversation(tid)) {
              conv.onItemCompleted(p)
            }
            if (shouldUpdateReviewThread(tid)) {
              const rs = useReviewPanelStore.getState()
              rs.onItemCompleted(p)
            }
            break
          }

          case 'item/usage/delta': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (!shouldUpdateActiveConversation(tid)) break
            const input = (p.inputTokens as number) ?? 0
            const output = (p.outputTokens as number) ?? 0
            const totalInput = typeof p.totalInputTokens === 'number' ? (p.totalInputTokens as number) : null
            const totalOutput = typeof p.totalOutputTokens === 'number' ? (p.totalOutputTokens as number) : null
            const contextUsage = typeof p.contextUsage === 'object' && p.contextUsage !== null
              ? p.contextUsage as ContextUsageSnapshotWire
              : null
            conv.onUsageDelta(input, output, totalInput, totalOutput, contextUsage)
            break
          }

          // ── SubAgent progress ─────────────────────────────────────────
          case 'subagent/progress': {
            const entries = (p.entries as SubAgentEntry[]) ?? []
            const threadId = (p.threadId as string | undefined) ?? ''
            if (threadId) {
              const subAgentStore = useSubAgentStore.getState()
              const knownChildCount = subAgentStore.childrenByParent.get(threadId)?.length ?? 0
              subAgentStore.updateProgress(threadId, entries)
              const nextSubAgentStore = useSubAgentStore.getState()
              if (
                entries.length > 0
                && knownChildCount < entries.length
                && !nextSubAgentStore.loadingParents.has(threadId)
              ) {
                void nextSubAgentStore.fetchChildren(threadId)
              }
            }
            if (shouldUpdateReviewThread(threadId)) {
              useReviewPanelStore.getState().onSubagentProgress(entries)
            }
            if (shouldUpdateActiveConversation(threadId)) {
              conv.onSubagentProgress(entries)
            }
            break
          }

          case 'subagent/graphChanged': {
            const parentThreadId = (p.parentThreadId as string | undefined) ?? ''
            if (parentThreadId) {
              void useSubAgentStore.getState().fetchChildren(parentThreadId)
            }
            void reloadThreadList()
            break
          }

          // ── System events ─────────────────────────────────────────────
          case 'system/event': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (!shouldUpdateActiveConversation(tid)) break
            const kind = (p.kind as string) ?? ''
            const serverMessage = serverFallbackText(
              localeRef.current,
              p.messageKey,
              p.params,
              p.fallbackText,
              p.message
            )
            conv.onSystemEvent(kind, {
              turnId: typeof p.turnId === 'string' ? (p.turnId as string) : null,
              message: serverMessage,
              tokenCount: typeof p.tokenCount === 'number' ? (p.tokenCount as number) : null,
              percentLeft: typeof p.percentLeft === 'number' ? (p.percentLeft as number) : null,
              contextUsage: typeof p.contextUsage === 'object' && p.contextUsage !== null
                ? p.contextUsage as ContextUsageSnapshotWire
                : null
            })
            if (kind === 'consolidationFailed') {
              addToast(
                serverMessage ?? translate(localeRef.current, 'systemNotice.consolidationFailed.message'),
                'warning'
              )
            }
            break
          }

          // ── Plan updates ──────────────────────────────────────────────
          case 'plan/updated': {
            const tid = (p.threadId as string | undefined) ?? ''
            if (!tid || !shouldUpdateActiveConversation(tid)) break
            conv.onPlanUpdated(p as Record<string, unknown>)
            // Auto-show detail panel on Plan tab
            useUIStore.getState().setActiveDetailTab('plan')
            break
          }

          // ── Approval resolved ──────────────────────────────────────────
          case 'item/approval/resolved':
            conv.onApprovalResolved()
            break

          case 'item/tool/requestUserInput/resolved':
            conv.onUserInputResolved()
            break

          // ── Job results ───────────────────────────────────────────────
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

          case 'cron/stateChanged': {
            const removed = p.removed === true
            const job = p.job as CronJobWire | undefined
            if (removed && job?.id) {
              useCronStore.getState().removeJobLocal(job.id)
              if (useCronStore.getState().selectedCronJobId === job.id) {
                useCronStore.getState().selectCronJob(null)
              }
            } else if (job) {
              useCronStore.getState().upsertJob(job)
            }
            break
          }

          // ── Automation task updates ────────────────────────────────────
          case 'automation/task/updated': {
            const task = (p.task ?? {}) as AutomationTask
            useAutomationsStore.getState().upsertTask(task)
            {
              const rs = useReviewPanelStore.getState()
              if (rs.openedTaskId === task.id && rs.taskDetail) {
                useReviewPanelStore.setState({
                  taskDetail: { ...rs.taskDetail, ...task }
                })
              }
            }
            break
          }

          case 'mcp/status/updated': {
            const server = (p.server ?? null) as McpServerStatusWire | null
            if (server?.name) {
              useMcpStore.getState().upsertStatus(server)
            }
            break
          }

          case 'app/list/updated':
          case 'app/connection/changed':
          case 'thread/appBindings/changed': {
            useAppBindingStore.getState().handleNotification(method, p)
            break
          }

          case 'workspace/configChanged': {
            const event = resolveWorkspaceConfigChangedPayload(
              payload,
              workspaceConfigChangedDedupeRef.current
            )
            if (!event) break

            if (event.regions.includes('skills')) {
              void useSkillsStore.getState().fetchSkills()
            }
            if (event.regions.includes('plugins')) {
              void usePluginStore.getState().fetchPlugins()
            }
            if (
              event.regions.includes('providers') ||
              event.regions.includes('workspace.provider') ||
              event.regions.includes('workspace.model')
            ) {
              useModelCatalogStore.getState().reset()
              if (useConnectionStore.getState().capabilities?.modelCatalogManagement === true) {
                void useModelCatalogStore.getState().loadIfNeeded(true)
              }
            }
            if (event.regions.includes('providers') || event.regions.includes('workspace.provider')) {
              if (useConnectionStore.getState().capabilities?.providerManagement === true) {
                void useProvidersStore.getState().reload()
              }
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

  // -------------------------------------------------------------------------
  // Server-initiated requests (approval and model question flows)
  // -------------------------------------------------------------------------
  useEffect(() => {
    const unsubscribe = window.api.appServer.onServerRequest((payload) => {
      const { bridgeId, method, params } = payload
      const p = (params ?? {}) as Record<string, unknown>

      if (method === 'item/approval/request') {
        const threadId = typeof p.threadId === 'string' ? p.threadId : null
        const turnId = typeof p.turnId === 'string' ? p.turnId : null
        const activeThreadId = useThreadStore.getState().activeThreadId
        if (threadId && threadId !== activeThreadId) {
          useThreadStore.getState().parkApproval(threadId, {
            bridgeId,
            turnId,
            rawParams: p
          })
          return
        }
        useConversationStore.getState().onApprovalRequest(bridgeId, p)
        return
      }
      if (method === 'item/tool/requestUserInput') {
        const threadId = typeof p.threadId === 'string' ? p.threadId : null
        const turnId = typeof p.turnId === 'string' ? p.turnId : null
        const activeThreadId = useThreadStore.getState().activeThreadId
        if (threadId && threadId !== activeThreadId) {
          useThreadStore.getState().parkUserInput(threadId, {
            bridgeId,
            turnId,
            rawParams: p
          })
          return
        }
        useConversationStore.getState().onUserInputRequest(bridgeId, p)
        return
      }
        // Unknown server requests: respond with null to unblock AppServer
        // (will be handled by specific cases above in future)
        window.api.appServer.sendServerResponse(bridgeId, {
          error: `Unsupported server request: ${method}`
        })
      })
    return unsubscribe
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // -------------------------------------------------------------------------
  // Auto-show detail panel when first file change is detected in a new turn
  // -------------------------------------------------------------------------
  const changedFilesSize = useConversationStore((s) => s.changedFiles.size)
  const activeTurnIdForAutoShow = useConversationStore((s) => s.activeTurnId)
  useEffect(() => {
    if (changedFilesSize === 0) return
    const uiState = useUIStore.getState()
    const currentTurnId = activeTurnIdForAutoShow
    if (!currentTurnId) return
    // Only auto-show once per turn
    if (uiState.autoShowTriggeredForTurn === currentTurnId) return
    useUIStore.getState().markAutoShowForTurn(currentTurnId)
    useUIStore.getState().setActiveDetailTab('changes')
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [changedFilesSize])

  // -------------------------------------------------------------------------
  // Auto-switch detail panel to Plan tab when CreatePlan starts streaming
  // -------------------------------------------------------------------------
  const streamingPlanItemId = useConversationStore(selectStreamingPlanItemId)
  useEffect(() => {
    if (!streamingPlanItemId) return
    const uiState = useUIStore.getState()
    if (uiState.autoShowPlanForItem === streamingPlanItemId) return
    useUIStore.getState().markAutoShowPlanForItem(streamingPlanItemId)
    useUIStore.getState().setActiveDetailTab('plan')
  }, [streamingPlanItemId])

  // -------------------------------------------------------------------------
  // Global keyboard shortcuts
  // -------------------------------------------------------------------------
  useEffect(() => {
    function handleKeyDown(e: KeyboardEvent): void {
      const ctrl = e.ctrlKey || e.metaKey

      // Escape: cancel running turn
      if (e.key === 'Escape') {
        const convState = useConversationStore.getState()
        if (convState.turnStatus === 'running') {
          const activeId = useThreadStore.getState().activeThreadId
          const turnId = convState.activeTurnId
          // Don't send interrupt if we only have a local optimistic ID (server hasn't confirmed yet)
          if (activeId && turnId && !turnId.startsWith('local-turn-')) {
            void window.api.appServer
              .sendRequest('turn/interrupt', { threadId: activeId, turnId })
              .catch((err: unknown) => console.error('turn/interrupt failed:', err))
          }
        }
        return
      }

      // Ctrl+N: new thread
      if (ctrl && e.key === 'n') {
        e.preventDefault()
        if (useConnectionStore.getState().status !== 'connected') return
        useUIStore.getState().goToNewChat()
      }

      // Ctrl+K: open thread search
      if (ctrl && e.key === 'k') {
        e.preventDefault()
        const focusFn = (window as Window & { __sidebarSearchFocus?: () => void }).__sidebarSearchFocus
        focusFn?.()
        return
      }

      // Ctrl+B: toggle sidebar
      if (ctrl && !e.shiftKey && e.key === 'b') {
        e.preventDefault()
        useUIStore.getState().toggleSidebar()
        return
      }

      // Ctrl+P / Cmd+P: open Quick-Open file finder
      if (ctrl && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'p') {
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
        ui.setDetailPanelVisible(true)
        return
      }

      // Ctrl+Shift+B: toggle detail panel
      if (ctrl && e.shiftKey && e.key === 'B') {
        e.preventDefault()
        useUIStore.getState().toggleDetailPanel()
        return
      }

      // Ctrl+Shift+O: switch workspace
      if (ctrl && e.shiftKey && e.key === 'O') {
        e.preventDefault()
        window.api.workspace.clearSelection()
          .catch((err: unknown) => console.error('Ctrl+Shift+O workspace switch failed:', err))
        return
      }

      // Ctrl+Shift+N: open new window
      if (ctrl && e.shiftKey && e.key === 'N') {
        e.preventDefault()
        void window.api.workspace.openNewWindow()
        return
      }

      // Ctrl+,: open settings
      if (ctrl && e.key === ',') {
        e.preventDefault()
        useUIStore.getState().setActiveMainView('settings')
        return
      }

      // Ctrl+T: open a new browser tab in the detail panel
      if (ctrl && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 't') {
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) return
        e.preventDefault()
        performAddTabAction('newBrowser', {
          threadId: useThreadStore.getState().activeThreadId,
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      // Ctrl+` : open a new terminal tab in the detail panel
      if (ctrl && !e.shiftKey && e.key === '`') {
        const target = e.target as HTMLElement | null
        if (target?.closest('[role="dialog"], [aria-modal="true"]')) return
        e.preventDefault()
        performAddTabAction('newTerminal', {
          threadId: useThreadStore.getState().activeThreadId,
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      // Ctrl+Shift+G: open the Changes (Diff) tab
      if (ctrl && e.shiftKey && e.key === 'G') {
        e.preventDefault()
        performAddTabAction('newChanges', {
          threadId: useThreadStore.getState().activeThreadId,
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      // Ctrl+Shift+P: open the Plan (Progress) tab
      if (ctrl && e.shiftKey && e.key === 'P') {
        e.preventDefault()
        performAddTabAction('newPlan', {
          threadId: useThreadStore.getState().activeThreadId,
          workspacePath: workspacePathRef.current,
          t: (key, vars) => translate(localeRef.current, key, vars)
        })
        return
      }

      // Ctrl+Shift+C: copy last agent message to clipboard
      if (ctrl && e.shiftKey && e.key === 'C') {
        e.preventDefault()
        const convState = useConversationStore.getState()
        const turns = convState.turns
        for (let i = turns.length - 1; i >= 0; i--) {
          const items = turns[i].items
          for (let j = items.length - 1; j >= 0; j--) {
            const item = items[j]
            if (item.type === 'agentMessage' && item.text) {
              navigator.clipboard.writeText(item.text).then(() => {
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

  // -------------------------------------------------------------------------
  // Thread selection: when activeThreadId changes, load full thread + subscribe
  // -------------------------------------------------------------------------
  const prevThreadIdRef = useRef<string | null>(null)
  const browserVisibilitySentRef = useRef<Map<string, boolean>>(new Map())
  const activeBrowserTabSentRef = useRef<string | null>(null)
  /**
   * Tracks the thread we currently hold a server-side subscription for.
   * Used as a guard against React StrictMode's mount->cleanup->remount cycle:
   * the cleanup function does NOT reset this ref, so the remount sees
   * subscribedRef === curr and skips the duplicate subscribe call.
   */
  const subscribedThreadIdRef = useRef<string | null>(null)
  const { activeThreadId } = useThreadStore()

  useEffect(() => {
    const unsubscribe = window.api.workspace.viewer.browserUse.onOpen(handleBrowserUseOpen)
    return () => {
      unsubscribe()
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
      setBrowserUseApprovalRequests((current) => {
        if (current.some((item) => item.requestId === event.requestId)) return current
        return [...current, event]
      })
    })
    return () => {
      unsubscribe()
    }
  }, [])

  const respondToBrowserUseApproval = useCallback(
    (request: BrowserUseApprovalRequestPayload, action: BrowserUseApprovalResponseAction): void => {
      setBrowserUseApprovalRequests((current) => current.filter((item) => item.requestId !== request.requestId))
      window.api.workspace.viewer.browserUse
        .sendApprovalResponse({ requestId: request.requestId, action })
        .catch((err: unknown) => {
          addToast(
            translate(localeRef.current, 'browserUse.approval.sendFailed', {
              error: err instanceof Error ? err.message : String(err)
            }),
            'error'
          )
        })
    },
    []
  )

  // Keep viewerTabStore in sync with active thread, and restore/fallback
  // uiStore.activeDetailTab according to the incoming thread's viewer state.
  useEffect(() => {
    const viewerStore = useViewerTabStore.getState()
    useUIStore.getState().resetAutoShowReasons()
    const outgoingThreadId = viewerStore.currentThreadId
    if (outgoingThreadId) {
      const outgoingState = viewerStore.getThreadState(outgoingThreadId)
      for (const tab of outgoingState.tabs) {
        if (tab.kind === 'browser') {
          void window.api.workspace.viewer.browser.setVisible({ tabId: tab.id, visible: false })
        }
      }
    }

    viewerStore.onThreadSwitched(activeThreadId)

    if (activeThreadId) {
      const threadState = viewerStore.getThreadState(activeThreadId)
      if (threadState.activeTabId) {
        useUIStore.getState().setActiveViewerTab(threadState.activeTabId, { reveal: false })
        const activeTab = threadState.tabs.find((tab) => tab.id === threadState.activeTabId)
        const uiState = useUIStore.getState()
        if (activeTab?.kind === 'browser' && uiState.detailPanelVisible && uiState.activeMainView === 'conversation') {
          void window.api.workspace.viewer.browser.setActive({ tabId: activeTab.id })
        }
      } else {
        const { activeDetailTab } = useUIStore.getState()
        if (activeDetailTab.kind === 'viewer') {
          useUIStore.getState().closeViewerTab({ reveal: false })
        }
      }
    }
  }, [activeThreadId])

  // Hide native browser views when non-conversation surfaces or overlays are shown.
  useEffect(() => {
    const viewerStore = useViewerTabStore.getState()
    const threadId = viewerStore.currentThreadId
    if (!threadId) return
    const threadState = viewerStore.getThreadState(threadId)
    const desiredVisibility = new Map<string, boolean>()
    for (const tab of threadState.tabs) {
      if (tab.kind === 'browser') {
        desiredVisibility.set(tab.id, false)
      }
    }
    const shouldHideBrowser =
      quickOpenVisible
      || activeMainView !== 'conversation'
      || !detailPanelVisible
      || activeDetailTab.kind !== 'viewer'

    let activeBrowserTabId: string | null = null
    if (!shouldHideBrowser) {
      const activeTab = threadState.tabs.find((tab) => tab.id === activeDetailTab.id)
      if (activeTab?.kind === 'browser') {
        desiredVisibility.set(activeTab.id, true)
        activeBrowserTabId = activeTab.id
      }
    }

    const lastVisibility = browserVisibilitySentRef.current
    for (const [tabId, visible] of desiredVisibility.entries()) {
      if (lastVisibility.get(tabId) === visible) continue
      lastVisibility.set(tabId, visible)
      void window.api.workspace.viewer.browser.setVisible({ tabId, visible })
    }

    for (const tabId of [...lastVisibility.keys()]) {
      if (desiredVisibility.has(tabId)) continue
      lastVisibility.delete(tabId)
    }

    if (activeBrowserTabId && activeBrowserTabSentRef.current !== activeBrowserTabId) {
      activeBrowserTabSentRef.current = activeBrowserTabId
      void window.api.workspace.viewer.browser.setActive({ tabId: activeBrowserTabId })
    } else if (!activeBrowserTabId) {
      activeBrowserTabSentRef.current = null
    }
  }, [activeDetailTab, activeMainView, detailPanelVisible, quickOpenVisible])

  useEffect(() => {
    const prev = prevThreadIdRef.current
    const curr = activeThreadId
    const convBeforeReset = useConversationStore.getState()
    const latestCreatePlanTurnId = selectLatestCreatePlanTurnId(convBeforeReset)
    const planApprovalDismissed = useUIStore.getState().planApprovalDismissed
    const prevHasPendingPlanConfirmation =
      prev != null
      && convBeforeReset.threadMode === 'plan'
      && convBeforeReset.turnStatus === 'idle'
      && convBeforeReset.pendingApproval == null
      && convBeforeReset.pendingUserInput == null
      && latestCreatePlanTurnId != null
      && planApprovalDismissed[latestCreatePlanTurnId] !== true

    if (prev && prev !== curr && convBeforeReset.pendingApproval != null) {
      const pending = convBeforeReset.pendingApproval
      useThreadStore.getState().parkApproval(prev, {
        bridgeId: pending.bridgeId,
        turnId: convBeforeReset.activeTurnId,
        rawParams: {
          threadId: prev,
          turnId: convBeforeReset.activeTurnId,
          approvalType: pending.approvalType,
          operation: pending.operation,
          target: pending.target,
          reason: pending.reason
        }
      })
      useThreadStore.getState().applyRuntimeSnapshot(prev, {
        running: convBeforeReset.turnStatus === 'running' || convBeforeReset.turnStatus === 'waitingApproval',
        waitingOnApproval: true,
        waitingOnInput: false,
        waitingOnPlanConfirmation: false
      }, {
        isActive: false,
        isDesktopOrigin: true
      })
    }

    if (prev && prev !== curr && convBeforeReset.pendingUserInput != null) {
      const pending = convBeforeReset.pendingUserInput
      useThreadStore.getState().parkUserInput(prev, {
        bridgeId: pending.bridgeId,
        turnId: convBeforeReset.activeTurnId,
        rawParams: {
          threadId: prev,
          turnId: convBeforeReset.activeTurnId,
          requestId: pending.requestId,
          questions: pending.questions
        }
      })
      useThreadStore.getState().applyRuntimeSnapshot(prev, {
        running: convBeforeReset.turnStatus === 'running' || convBeforeReset.turnStatus === 'waitingInput',
        waitingOnApproval: false,
        waitingOnInput: true,
        waitingOnPlanConfirmation: false
      }, {
        isActive: false,
        isDesktopOrigin: true
      })
    }

    if (prev && prev !== curr && prevHasPendingPlanConfirmation) {
      useThreadStore.getState().applyRuntimeSnapshot(prev, {
        running: false,
        waitingOnApproval: false,
        waitingOnInput: false,
        waitingOnPlanConfirmation: true
      }, {
        isActive: false,
        isDesktopOrigin: true
      })
    }

    // Always reset conversation state on thread switch
    useConversationStore.getState().reset()

    // Unsubscribe from previous thread when genuinely switching (not StrictMode remount)
    if (prev && prev !== curr) {
      window.api.appServer
        .sendRequest('thread/unsubscribe', { threadId: prev })
        .catch(() => {
          // Best-effort, ignore errors
        })
      if (subscribedThreadIdRef.current === prev) {
        subscribedThreadIdRef.current = null
      }
    }

    if (curr) {
      const requestedId = curr
      performance.mark(`app:thread-switch-start:${requestedId}`)
      window.api.appServer
        .sendRequest('thread/read', { threadId: curr, includeTurns: true })
        .then(async (result) => {
          // Stale guard: user may have switched threads while we were loading
          if (useThreadStore.getState().activeThreadId !== requestedId) {
            useUIStore.getState().cancelPendingWelcomeTurnForThread(requestedId)
            return
          }
          const res = result as { thread: Thread }
          useThreadStore.getState().setActiveThread(res.thread)
          const runtime = res.thread.runtime
          useThreadStore.getState().applyRuntimeSnapshot(requestedId, {
            running: runtime?.running === true
              || (res.thread.turns ?? []).some((turn) =>
                turn.status === 'running' || turn.status === 'waitingApproval' || turn.status === 'waitingInput'
              ),
            busy: runtime?.busy === true,
            waitingOnApproval: runtime?.waitingOnApproval === true
              || (res.thread.turns ?? []).some((turn) =>
                turn.status === 'waitingApproval'
              ),
            waitingOnInput: runtime?.waitingOnInput === true
              || (res.thread.turns ?? []).some((turn) =>
                turn.status === 'waitingInput'
              ),
            waitingOnPlanConfirmation: runtime?.waitingOnPlanConfirmation === true,
            maintenanceKind: runtime?.maintenanceKind ?? null
          }, {
            isActive: true,
            isDesktopOrigin: res.thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
          })
          {
            const name = res.thread.displayName?.trim()
            if (name) {
              const entry = useThreadStore.getState().threadList.find((t) => t.id === requestedId)
              if (entry && entry.displayName !== name) {
                useThreadStore.getState().renameThread(requestedId, name)
              }
            }
          }
          // Populate conversationStore with historical turns
          const rawTurns = (res.thread.turns ?? []) as unknown as Array<Record<string, unknown>>
          const convTurns = rawTurns.map(wireTurnToConversationTurn)
          performance.mark(`app:thread-switch-rendered:${requestedId}`)
          performance.measure('app:thread-switch', `app:thread-switch-start:${requestedId}`, `app:thread-switch-rendered:${requestedId}`)
          useConversationStore.getState().setTurns(convTurns)
          if (res.thread.plan) {
            useConversationStore.getState().onPlanUpdated(res.thread.plan)
          }
          {
            const rawMode = res.thread.configuration?.mode ?? res.thread.configuration?.Mode
            const mode = typeof rawMode === 'string' && rawMode.toLowerCase() === 'plan'
              ? 'plan'
              : 'agent'
            useConversationStore.getState().setThreadMode(mode)
          }
          useConversationStore.getState().setQueuedInputs(res.thread.queuedInputs ?? [])
          useConversationStore.getState().setContextUsage(res.thread.contextUsage ?? null)
          useConversationStore.getState().setMaintenanceKind(runtime?.maintenanceKind ?? null)
          void useSubAgentStore.getState().fetchChildren(requestedId)
          const parked = useThreadStore.getState().consumeParkedApproval(requestedId)
          if (parked) {
            useConversationStore.getState().onApprovalRequest(parked.bridgeId, parked.rawParams)
          }
          const parkedUserInput = useThreadStore.getState().consumeParkedUserInput(requestedId)
          if (parkedUserInput) {
            useConversationStore.getState().onUserInputRequest(parkedUserInput.bridgeId, parkedUserInput.rawParams)
          }

          // Welcome composer: send first turn after historical turns are loaded so reset/setTurns do not drop optimistic UI.
          const pendingWelcome = useUIStore.getState().consumePendingWelcomeTurnIfMatch(requestedId)
          if (pendingWelcome != null) {
            const threadId = requestedId
            const path = workspacePathRef.current
            const pendingText = pendingWelcome.text.trim()
            const pendingInputParts = pendingWelcome.inputParts
              ?? buildComposerInputParts({
                text: pendingText,
                files: pendingWelcome.files ?? [],
                images: pendingWelcome.images ?? []
              }).inputParts
            const pendingImages = pendingWelcome.images
            const pendingFiles = pendingWelcome.files ?? []
            const welcomeMode = pendingWelcome.mode ?? 'agent'
            const rawWelcomeModel =
              typeof pendingWelcome.model === 'string' ? pendingWelcome.model.trim() : ''
            const welcomeModel =
              rawWelcomeModel !== '' && rawWelcomeModel !== 'Default' ? rawWelcomeModel : ''
            const welcomeApprovalPolicy = pendingWelcome.approvalPolicy === 'autoApprove'
              ? 'autoApprove'
              : 'default'
            const welcomeReasoning = pendingWelcome.reasoning
            useConversationStore.getState().setThreadMode(welcomeMode)
            if (
              welcomeModel.length > 0 ||
              welcomeMode !== 'agent' ||
              welcomeApprovalPolicy === 'autoApprove' ||
              welcomeReasoning != null
            ) {
              const existingConfig =
                res.thread.configuration && typeof res.thread.configuration === 'object'
                  ? { ...(res.thread.configuration as Record<string, unknown>) }
                  : {}
              const setCaseInsensitiveField = (
                target: Record<string, unknown>,
                key: string,
                value: unknown
              ): void => {
                const lower = key.toLowerCase()
                const existingKey = Object.keys(target).find((k) => k.toLowerCase() === lower)
                if (existingKey) target[existingKey] = value
                else target[key] = value
              }
              setCaseInsensitiveField(existingConfig, 'mode', welcomeMode)
              if (welcomeModel.length > 0) {
                setCaseInsensitiveField(existingConfig, 'model', welcomeModel)
              }
              if (welcomeApprovalPolicy === 'autoApprove') {
                setCaseInsensitiveField(existingConfig, 'approvalPolicy', welcomeApprovalPolicy)
              }
              if (welcomeReasoning != null) {
                setCaseInsensitiveField(existingConfig, 'reasoning', welcomeReasoning)
              }
              let welcomeConfigApplied = false
              try {
                await window.api.appServer.sendRequest('thread/config/update', { threadId, config: existingConfig })
                welcomeConfigApplied = true
              } catch (configErr: unknown) {
                console.error('thread/config/update (welcome configuration) failed:', configErr)
              }
              if (welcomeConfigApplied) {
                const active = useThreadStore.getState().activeThread
                if (active && active.id === threadId) {
                  const mergedCfg: Record<string, unknown> = { ...(active.configuration ?? {}) }
                  setCaseInsensitiveField(mergedCfg, 'mode', welcomeMode)
                  if (welcomeModel.length > 0) {
                    setCaseInsensitiveField(mergedCfg, 'model', welcomeModel)
                  }
                  if (welcomeApprovalPolicy === 'autoApprove') {
                    setCaseInsensitiveField(mergedCfg, 'approvalPolicy', welcomeApprovalPolicy)
                  }
                  if (welcomeReasoning != null) {
                    setCaseInsensitiveField(mergedCfg, 'reasoning', welcomeReasoning)
                  }
                  useThreadStore.getState().setActiveThread({
                    ...active,
                    configuration: mergedCfg as typeof active.configuration
                  })
                }
              }
            }
            const threadEntry = useThreadStore.getState().threadList.find((t) => t.id === threadId)
            if (!threadEntry?.displayName) {
              const autoName = getFallbackThreadName({
                visibleText: pendingText,
                imagesCount: pendingImages?.length ?? 0,
                filesCount: pendingFiles.length,
                fallbackThreadName: translate(localeRef.current, 'toast.imageMessage'),
                fileFallbackThreadName: translate(localeRef.current, 'toast.fileReferenceMessage'),
                attachmentFallbackThreadName: translate(localeRef.current, 'toast.attachmentMessage')
              })
              useThreadStore.getState().renameThread(threadId, autoName)
            }
            const optimisticItemId = `local-${Date.now()}`
            const optimisticTurnId = `local-turn-${Date.now()}`
            const optimisticNow = new Date().toISOString()
            const userItem: ConversationItem = {
              id: optimisticItemId,
              type: 'userMessage',
              status: 'completed',
              text: pendingText,
              nativeInputParts: pendingInputParts.filter((part) => part.type !== 'localImage' && part.type !== 'image'),
              imageDataUrls: pendingImages?.map((i) => i.dataUrl),
              images: pendingImages?.map((i) => ({
                path: i.tempPath,
                mimeType: i.mimeType,
                fileName: i.fileName
              })),
              createdAt: optimisticNow,
              completedAt: optimisticNow
            }
            const optimisticTurn: ConversationTurn = {
              id: optimisticTurnId,
              threadId,
              status: 'running',
              items: [userItem],
              startedAt: optimisticNow
            }
            useConversationStore.getState().addOptimisticTurn(optimisticTurn)

            if (pendingInputParts.length === 0) {
              useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
            } else {
              void window.api.appServer
                .sendRequest('turn/start', {
                  threadId,
                  input: pendingInputParts,
                  identity: {
                    channelName: 'dotcraft-desktop',
                    userId: 'local',
                    channelContext: `workspace:${path}`,
                    workspacePath: path
                  }
                })
              .then((result) => {
                const res = result as { turn?: { id?: string } }
                if (res.turn?.id) {
                  useConversationStore.getState().promoteOptimisticTurn(optimisticTurnId, res.turn.id)
                }
              })
              .catch((turnErr: unknown) => {
                console.error('Welcome screen turn/start failed:', turnErr)
                useConversationStore.getState().removeOptimisticTurn(optimisticTurnId)
              })
            }
          }
        })
        .catch((err: unknown) => {
          console.error('thread/read failed:', err)
          useUIStore.getState().cancelPendingWelcomeTurnForThread(requestedId)
          addToast(translate(localeRef.current, 'toast.threadNotFound'), 'warning')
        })

      // Guard: skip subscribe if we already hold a subscription for this thread.
      // React StrictMode runs this effect twice (mount, cleanup, remount) — but the
      // cleanup does NOT reset this ref, so the remount finds subscribedRef === curr
      // and skips the call, preventing a second server-side subscription.
      if (subscribedThreadIdRef.current !== curr) {
        subscribedThreadIdRef.current = curr
        window.api.appServer
          .sendRequest('thread/subscribe', { threadId: curr })
          .catch((err: unknown) => console.error('thread/subscribe failed:', err))
      }
    } else {
      // No active thread: unsubscribe whatever we were subscribed to
      if (subscribedThreadIdRef.current) {
        void window.api.appServer
          .sendRequest('thread/unsubscribe', { threadId: subscribedThreadIdRef.current })
          .catch(() => {})
        subscribedThreadIdRef.current = null
      }
      useThreadStore.getState().setActiveThread(null)
    }

    prevThreadIdRef.current = curr
    // No cleanup return here: a cleanup that resets subscribedThreadIdRef defeats
    // the StrictMode guard above. Thread-switch unsubscription is handled by the
    // prev !== curr block. On window close the connection terminates anyway.
  }, [activeThreadId])

  // -------------------------------------------------------------------------
  // Render
  // -------------------------------------------------------------------------
  const isFatalError = isFatalConnectionError(status, errorType)
  const showErrorScreen = isFatalError || launchErrorScreenVisible
  const keepWelcomeDuringLaunch =
    (
      workspaceLaunchTransition?.phase === 'welcome-hold' &&
      workspaceStatus.status !== 'needs-setup'
    ) ||
    workspaceLaunchTransition?.phase === 'welcome-to-center'

  // No workspace configured yet (first launch or welcome screen)
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
        <WorkspaceLaunchTransition
          phase={workspaceLaunchTransition.phase}
          from={workspaceLaunchTransition.from}
          to={workspaceLaunchTransition.to}
          logoSrc={workspaceLaunchTransition.logoSrc}
        />
      )
    : null
  const setupHandoffOverlay = setupLogoHandoff
    ? (
        <WorkspaceSetupLogoHandoff
          phase={setupLogoHandoff.phase}
          from={setupLogoHandoff.from}
          to={setupLogoHandoff.to}
        />
      )
    : null
  const hideSetupInterstitialLogo =
    (workspaceLaunchTransition != null && showSetupInterstitial) ||
    (setupLogoHandoff != null && showSetupInterstitial)
  const hideSetupWizardLogo =
    (workspaceLaunchTransition != null && showSetupFlow) ||
    (setupLogoHandoff != null && showSetupFlow)
  const deferSetupWizardContent = setupLogoHandoff != null && showSetupFlow

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
            ui.setActiveSettingsTab('connection')
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
          workspacePath={workspacePath}
          workspaceStatus={setupFlowWorkspaceStatus}
          hideLogo={hideSetupWizardLogo}
          deferContent={deferSetupWizardContent}
          logoAnchorRef={setWizardLogoAnchorNode}
          onRunSetup={handleRunWorkspaceSetup}
          onChooseDifferentWorkspace={() => {
            void window.api.workspace.clearSelection()
          }}
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
        {quickOpenVisible && (
          <QuickOpenDialog
            onClose={() => setQuickOpenVisible(false)}
          />
        )}
        {browserUseApprovalRequests[0] && (
          <BrowserUseApprovalDialog
            locale={locale}
            request={browserUseApprovalRequests[0]}
            onRespond={(action) => respondToBrowserUseApproval(browserUseApprovalRequests[0], action)}
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
              <button
                type="button"
                onClick={() => ignorePendingRestart()}
                disabled={pendingRestartApplying}
                style={topBannerSecondaryButtonStyle(pendingRestartApplying)}
              >
                {translate(locale, 'settings.pendingRestart.ignore')}
              </button>
              <button
                type="button"
                onClick={() => {
                  void applyPendingRestart()
                }}
                disabled={pendingRestartApplying}
                style={topBannerPrimaryButtonStyle(pendingRestartApplying)}
              >
                {pendingRestartApplying
                  ? translate(locale, pendingRestartApplyingKey)
                  : translate(locale, pendingRestartApplyKey)}
              </button>
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
              : <Sidebar workspaceName={workspaceName} workspacePath={workspacePath} />
          }
          conversation={
            <div data-testid={`view-${activeMainView}`} style={{ display: 'contents' }}>
              {activeMainView === 'settings' ? (
                <SettingsView
                  workspacePath={workspacePath}
                  onThreadListRefreshRequested={() => {
                    void reloadThreadList()
                  }}
                  workspaceConfigChange={workspaceConfigChange}
                  workspaceConfigChangeSeq={workspaceConfigChangeSeq}
                  openChromeSettingsSeq={chromeSettingsOpenSeq}
                />
              ) : activeMainView === 'channels' ? (
                <ChannelsView />
              ) : activeMainView === 'skills' ? (
                <PluginsView />
              ) : activeMainView === 'automations' ? (
                <AutomationsView />
              ) : activeMainView === 'teams' ? (
                agentTeamsAvailable ? <TeamsView /> : capabilities?.pluginManagement === true ? <PluginsView /> : (
                  <ConversationPanel
                    workspacePath={workspacePath}
                    workspaceConfigChange={workspaceConfigChange}
                    workspaceConfigChangeSeq={workspaceConfigChangeSeq}
                  />
                )
              ) : (
                <ConversationPanel
                  workspacePath={workspacePath}
                  workspaceConfigChange={workspaceConfigChange}
                  workspaceConfigChangeSeq={workspaceConfigChangeSeq}
                />
              )}
            </div>
          }
          detail={<DetailPanel workspacePath={workspacePath} />}
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
        {whatsNewDialog && (
          <WhatsNewDialog
            releases={whatsNewDialog.releases}
            mediaStates={whatsNewMediaStates}
            onClose={closeWhatsNew}
          />
        )}
      </>
    </WindowFrame>
  )
}
