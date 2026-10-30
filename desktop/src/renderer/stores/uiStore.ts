import { create } from 'zustand'
import type { ComposerFileAttachment, ImageAttachment, InputPart, ThreadMode } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ApprovalPolicyWire, ThreadConfigurationWire } from '../types/thread'
import type { InferenceSpeedWire, ReasoningEffortWire, ReasoningOutputWire } from './modelCatalogStore'
import { normalizeSettingsTab, type SettingsTab } from '../types/settings'
import type { AppNavigationLocation } from './appNavigationStore'
import type { DiffMarkerMode } from '../../shared/appearance'
import { useThreadStore } from './threadStore'
import { activeDetailScopeId } from '../utils/detailPanelScope'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import {
  AGENT_BUILDER_CHAT_DEFAULT_WIDTH,
  AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO,
  AGENT_BUILDER_CHAT_MIN_WIDTH
} from '../utils/agentBuilderLayout'

const SIDEBAR_DEFAULT_WIDTH = 240
const SIDEBAR_MIN_WIDTH = 200
const SIDEBAR_COLLAPSED_WIDTH = 48

const DETAIL_DEFAULT_WIDTH = 600
const DETAIL_MIN_WIDTH = 300
const DETAIL_DEFAULT_MAIN_SURFACE_WIDTH = 1676
const DETAIL_DEFAULT_WIDTH_RATIO = DETAIL_DEFAULT_WIDTH / DETAIL_DEFAULT_MAIN_SURFACE_WIDTH

const EXPLORER_DEFAULT_WIDTH = 260
const EXPLORER_MIN_WIDTH = 180
const EXPLORER_MAX_WIDTH = 480

const PENDING_WELCOME_TIMEOUT_MS = 30_000

export type SystemDetailTab = 'changes' | 'plan' | 'subagents' | 'board'
export type ChangesDiffMode = 'inline' | 'split'

/** @deprecated Use `ActiveDetailTab` instead. */
export type DetailPanelTab = SystemDetailTab

export type ActiveDetailTab =
  | { kind: 'system'; id: SystemDetailTab }
  | { kind: 'viewer'; id: string }
  | { kind: 'launcher' }

const SYSTEM_TAB_ORDER: readonly SystemDetailTab[] = ['changes', 'plan', 'subagents', 'board']

function withSystemTabOpen(open: SystemDetailTab[], id: SystemDetailTab): SystemDetailTab[] {
  if (open.includes(id)) return open
  return SYSTEM_TAB_ORDER.filter((tab) => tab === id || open.includes(tab))
}

interface DetailRevealOptions {
  reveal?: boolean
}

export type BuiltInMainView = 'conversation' | 'agents' | 'skills' | 'automations' | 'settings' | 'channels'
export type DesktopPluginMainView = `desktop-plugin:${string}:${string}`
export type ActiveMainView = BuiltInMainView | DesktopPluginMainView
export type SelectedChannelKey = `module:${string}` | `external:${string}` | null

export type PluginCatalogSurface = 'plugins' | 'skills'

export interface ComposerImageAttachmentRequest {
  dataUrl: string
  fileName: string
  mimeType: string
}

export interface WelcomeDraft {
  text: string
  segments?: ComposerDraftSegment[]
  selectionStart?: number
  selectionEnd?: number
  images: ImageAttachment[]
  files?: ComposerFileAttachment[]
  mode: ThreadMode
  providerId?: string
  model: string
  reasoning?: {
    enabled: boolean
    effort: ReasoningEffortWire
    output: ReasoningOutputWire
  }
  speed?: InferenceSpeedWire
  approvalPolicy?: Extract<ApprovalPolicyWire, 'default' | 'prompt' | 'autoApprove'>
  updatedAt: number
}

export interface PendingProjectThreadOpen {
  projectKey: string
  workspacePath: string
  threadId: string
}

export interface PendingWelcomeTurnInput {
  threadId: string
  text: string
  inputParts: InputPart[]
  images?: ImageAttachment[]
  files?: ComposerFileAttachment[]
  appIds?: string[]
  sentAsGoal?: boolean
}

/**
 * A welcome submission whose thread does not exist yet. `requestId` is client-only and never
 * becomes a thread id; `threadId` fills in once creation answers.
 */
export interface PendingThreadCreation {
  requestId: string
  createdAt: number
  workspacePath: string
  text: string
  inputParts: InputPart[]
  sentAsGoal?: boolean
  threadName: string
  configuration: ThreadConfigurationWire
  threadId?: string
}

export interface EnvironmentSettingsTarget {
  projectPath: string
  environmentId?: string
  create?: boolean
  returnTo: AppNavigationLocation | null
}

export interface UIState {
  activeMainView: ActiveMainView
  pluginCatalogSurface: PluginCatalogSurface
  browserDownloadHistoryOpen: boolean
  activeSettingsTab: SettingsTab
  environmentSettingsTarget: EnvironmentSettingsTarget | null
  /** Selected channel detail, kept outside ChannelsView so app history can restore it. */
  selectedChannelKey: SelectedChannelKey
  settingsCloseRequestSeq: number
  whatsNewOpenRequestSeq: number
  sidebarPreferredCollapsed: boolean
  sidebarCollapsed: boolean
  sidebarWidth: number
  detailPanelPreferredVisible: boolean
  detailPanelPreferredVisibleByThread: Record<string, boolean>
  detailPanelVisible: boolean
  detailPanelFullView: boolean
  detailPanelWidthRatio: number
  detailPanelWidth: number
  agentBuilderChatWidthRatio: number
  agentBuilderChatWidth: number
  responsiveLayout: 'full' | 'no-detail' | 'collapsed'
  activeDetailTab: ActiveDetailTab
  openSystemTabs: SystemDetailTab[]
  lastActiveSystemTab: SystemDetailTab
  quickOpenVisible: boolean
  explorerVisible: boolean
  explorerWidth: number
  explorerRevealPath: string | null
  selectedChangeKey: string | null
  changesDiffModeByThread: Record<string, ChangesDiffMode>
  changesWordWrap: boolean
  autoShowReasons: Set<string>
  composerPrefill: string | null
  composerImageAttachmentRequest: {
    id: number
    image: ComposerImageAttachmentRequest
  } | null
  pendingWelcomeTurn: (PendingWelcomeTurnInput & { createdAt: number }) | null
  pendingThreadCreation: PendingThreadCreation | null
  pendingProjectThreadOpen: PendingProjectThreadOpen | null
  welcomeDraft: WelcomeDraft | null
  /** Unsent welcome drafts keyed by normalized workspace path. */
  welcomeDraftsByWorkspace: Record<string, WelcomeDraft>
  welcomeDraftWorkspacePath: string | null
  welcomeDraftStageSeq: number
  planApprovalDismissed: Record<string, boolean>
  showThinkingContent: boolean
  projectsSectionCollapsed: boolean
  pinnedSectionCollapsed: boolean
  chatsSectionCollapsed: boolean
  diffMarkers: DiffMarkerMode
}

interface UIStore extends UIState {
  setActiveMainView(view: ActiveMainView): void
  setPluginCatalogSurface(surface: PluginCatalogSurface): void
  goToNewChat(options?: { workspacePath?: string; clearDraft?: boolean }): void
  setActiveSettingsTab(tab: SettingsTab): void
  openEnvironmentSettings(target: EnvironmentSettingsTarget): void
  clearEnvironmentSettingsTarget(): void
  setSelectedChannelKey(key: SelectedChannelKey): void
  requestCloseSettings(): void
  requestOpenWhatsNew(): void
  setDiffMarkers(mode: DiffMarkerMode): void
  toggleSidebar(): void
  setSidebarCollapsed(collapsed: boolean): void
  setSidebarWidth(width: number): void
  toggleDetailPanel(): void
  setDetailPanelVisible(visible: boolean): void
  toggleDetailPanelFullView(): void
  setDetailPanelFullView(fullView: boolean): void
  setResponsiveLayout(layout: 'full' | 'no-detail' | 'collapsed'): void
  setDetailPanelWidth(width: number, mainSurfaceWidth?: number | null): void
  setAgentBuilderChatWidth(width: number, splitWidth?: number | null): void
  setActiveDetailTab(tab: SystemDetailTab, options?: DetailRevealOptions): void
  closeSystemTab(tab: SystemDetailTab, fallbackViewerId?: string | null): void
  resetDetailTabs(): void
  syncDetailPanelForThread(scopeId: string | null, activeViewerTabId: string | null): void
  setActiveViewerTab(tabId: string, options?: DetailRevealOptions): void
  closeViewerTab(options?: DetailRevealOptions): void
  setQuickOpenVisible(visible: boolean): void
  toggleExplorer(): void
  setExplorerVisible(visible: boolean): void
  setExplorerWidth(width: number): void
  revealInExplorer(absoluteDir: string): void
  consumeExplorerReveal(): void
  selectChangeKey(key: string | null): void
  getChangesDiffMode(threadId: string | null): ChangesDiffMode
  setChangesDiffMode(threadId: string | null, mode: ChangesDiffMode): void
  toggleChangesWordWrap(): void
  showChangesForKey(key: string): void
  /** Auto-show detail panel once for a reason. Returns true when newly triggered. */
  maybeAutoShowForReason(reasonId: string): boolean
  resetAutoShowReasons(): void
  /** Set text to be picked up by InputComposer on its next mount. */
  setComposerPrefill(text: string): void
  consumeComposerPrefill(): string | null
  requestComposerImageAttachment(image: ComposerImageAttachmentRequest): void
  consumeComposerImageAttachmentRequest(): ComposerImageAttachmentRequest | null
  setPendingWelcomeTurn(payload: PendingWelcomeTurnInput | null): void
  setPendingThreadCreation(payload: PendingThreadCreation | null): void
  resolvePendingThreadCreation(requestId: string, threadId: string): void
  consumePendingWelcomeTurnIfMatch(threadId: string): Omit<PendingWelcomeTurnInput, 'threadId'> | null
  cancelPendingWelcomeTurnForThread(threadId: string): void
  setPendingProjectThreadOpen(payload: PendingProjectThreadOpen | null): void
  consumePendingProjectThreadOpen(projectKey: string, threadIds: Iterable<string>): PendingProjectThreadOpen | null
  clearPendingProjectThreadOpen(projectKey?: string, threadId?: string): void
  setWelcomeDraft(draft: Omit<WelcomeDraft, 'updatedAt'> | null, workspacePath?: string): void
  clearWelcomeDraft(workspacePath?: string): void
  stageWelcomeDraft(draft: Omit<WelcomeDraft, 'updatedAt'>): void
  setWelcomeDraftWorkspace(workspacePath: string): void
  getWelcomeDraftForWorkspace(workspacePath: string): WelcomeDraft | null
  dismissPlanApproval(turnId: string): void
  setShowThinkingContent(visible: boolean): void
  setProjectsSectionCollapsed(collapsed: boolean): void
  setPinnedSectionCollapsed(collapsed: boolean): void
  setChatsSectionCollapsed(collapsed: boolean): void
  resetPlanApprovalDismissed(): void
}

interface InternalState {
  _pendingWelcomeTimer: ReturnType<typeof setTimeout> | null
}

export function resolveResponsivePanels(
  layout: UIState['responsiveLayout'],
  sidebarPreferredCollapsed: boolean,
  detailPanelPreferredVisible: boolean
): Pick<UIState, 'sidebarCollapsed' | 'detailPanelVisible'> {
  switch (layout) {
    case 'collapsed':
      return {
        sidebarCollapsed: true,
        detailPanelVisible: false
      }
    case 'no-detail':
      return {
        sidebarCollapsed: sidebarPreferredCollapsed,
        detailPanelVisible: false
      }
    case 'full':
    default:
      return {
        sidebarCollapsed: sidebarPreferredCollapsed,
        detailPanelVisible: detailPanelPreferredVisible
      }
  }
}

function withActiveScopeDetailVisibility(
  current: Record<string, boolean>,
  visible: boolean
): Record<string, boolean> {
  const scopeId = activeDetailScopeId()
  if (!scopeId || current[scopeId] === visible) return current
  return { ...current, [scopeId]: visible }
}

function scopedSystemTabs(openSystemTabs: SystemDetailTab[]): SystemDetailTab[] {
  return useThreadStore.getState().activeThreadId ? openSystemTabs : []
}

function normalizeWorkspaceDraftKey(path: string | null | undefined): string {
  return (path ?? '').trim().replace(/\\/g, '/').replace(/\/+$/u, '').toLowerCase()
}

function cloneWelcomeDraft(draft: WelcomeDraft): WelcomeDraft {
  return {
    ...draft,
    images: [...draft.images],
    files: draft.files ? [...draft.files] : [],
    segments: draft.segments ? [...draft.segments] : undefined
  }
}

export const useUIStore = create<UIStore & InternalState>((set, get) => ({
  activeMainView: 'conversation',
  pluginCatalogSurface: 'plugins',
  browserDownloadHistoryOpen: false,
  activeSettingsTab: 'general',
  environmentSettingsTarget: null,
  selectedChannelKey: null,
  settingsCloseRequestSeq: 0,
  whatsNewOpenRequestSeq: 0,
  sidebarPreferredCollapsed: false,
  sidebarCollapsed: false,
  sidebarWidth: SIDEBAR_DEFAULT_WIDTH,
  detailPanelPreferredVisible: false,
  detailPanelPreferredVisibleByThread: {},
  detailPanelVisible: false,
  detailPanelFullView: false,
  detailPanelWidthRatio: DETAIL_DEFAULT_WIDTH_RATIO,
  detailPanelWidth: DETAIL_DEFAULT_WIDTH,
  agentBuilderChatWidthRatio: AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO,
  agentBuilderChatWidth: AGENT_BUILDER_CHAT_DEFAULT_WIDTH,
  responsiveLayout: 'full',
  activeDetailTab: { kind: 'launcher' },
  openSystemTabs: [],
  lastActiveSystemTab: 'changes',
  quickOpenVisible: false,
  explorerVisible: false,
  explorerWidth: EXPLORER_DEFAULT_WIDTH,
  explorerRevealPath: null,
  selectedChangeKey: null,
  changesDiffModeByThread: {},
  changesWordWrap: false,
  autoShowReasons: new Set<string>(),
  composerPrefill: null,
  composerImageAttachmentRequest: null,
  pendingWelcomeTurn: null,
  pendingThreadCreation: null,
  pendingProjectThreadOpen: null,
  welcomeDraft: null,
  welcomeDraftsByWorkspace: {},
  welcomeDraftWorkspacePath: null,
  welcomeDraftStageSeq: 0,
  planApprovalDismissed: {},
  showThinkingContent: false,
  projectsSectionCollapsed: false,
  pinnedSectionCollapsed: false,
  chatsSectionCollapsed: false,
  diffMarkers: 'color',

  setActiveMainView(view) {
    set({ activeMainView: view })
  },

  setPluginCatalogSurface(surface) {
    set({ pluginCatalogSurface: surface })
  },

  goToNewChat(options) {
    useThreadStore.getState().setActiveThreadId(null)
    if (options?.clearDraft) {
      get().clearWelcomeDraft(options.workspacePath)
    }
    if (options?.workspacePath) {
      get().setWelcomeDraftWorkspace(options.workspacePath)
    }
    set({ activeMainView: 'conversation', planApprovalDismissed: {} })
  },

  setActiveSettingsTab(tab) {
    set({ activeSettingsTab: normalizeSettingsTab(tab), browserDownloadHistoryOpen: false })
  },

  openEnvironmentSettings(target) {
    set({
      environmentSettingsTarget: target,
      activeSettingsTab: 'worktrees',
      browserDownloadHistoryOpen: false,
      activeMainView: 'settings'
    })
  },

  clearEnvironmentSettingsTarget() {
    set({ environmentSettingsTarget: null })
  },

  setSelectedChannelKey(key) {
    set({ selectedChannelKey: key })
  },

  requestCloseSettings() {
    set((state) => ({ settingsCloseRequestSeq: state.settingsCloseRequestSeq + 1 }))
  },

  requestOpenWhatsNew() {
    set((state) => ({ whatsNewOpenRequestSeq: state.whatsNewOpenRequestSeq + 1 }))
  },

  toggleSidebar() {
    set((state) => {
      const sidebarPreferredCollapsed = !state.sidebarPreferredCollapsed
      return {
        sidebarPreferredCollapsed,
        ...resolveResponsivePanels(
          state.responsiveLayout,
          sidebarPreferredCollapsed,
          state.detailPanelPreferredVisible
        )
      }
    })
  },

  setSidebarCollapsed(collapsed: boolean) {
    set((state) => ({
      sidebarPreferredCollapsed: collapsed,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        collapsed,
        state.detailPanelPreferredVisible
      )
    }))
  },

  setSidebarWidth(width: number) {
    const clamped = Math.max(SIDEBAR_MIN_WIDTH, width)
    set({ sidebarWidth: clamped })
  },

  toggleDetailPanel() {
    set((state) => {
      const detailPanelPreferredVisible = !state.detailPanelPreferredVisible
      return {
        detailPanelPreferredVisible,
        detailPanelPreferredVisibleByThread: withActiveScopeDetailVisibility(
          state.detailPanelPreferredVisibleByThread,
          detailPanelPreferredVisible
        ),
        ...resolveResponsivePanels(
          state.responsiveLayout,
          state.sidebarPreferredCollapsed,
          detailPanelPreferredVisible
        )
      }
    })
  },

  setDetailPanelVisible(visible: boolean) {
    set((state) => ({
      detailPanelPreferredVisible: visible,
      detailPanelPreferredVisibleByThread: withActiveScopeDetailVisibility(
        state.detailPanelPreferredVisibleByThread,
        visible
      ),
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        visible
      )
    }))
  },

  toggleDetailPanelFullView() {
    get().setDetailPanelFullView(!get().detailPanelFullView)
  },

  setDetailPanelFullView(fullView: boolean) {
    if (!fullView) {
      set({ detailPanelFullView: false })
      return
    }
    get().setDetailPanelVisible(true)
    if (canShowDetailPanelFullView(get())) set({ detailPanelFullView: true })
  },

  setResponsiveLayout(layout) {
    set((state) => ({
      responsiveLayout: layout,
      ...resolveResponsivePanels(
        layout,
        state.sidebarPreferredCollapsed,
        state.detailPanelPreferredVisible
      )
    }))
  },

  setDetailPanelWidth(width: number, mainSurfaceWidth?: number | null) {
    const clamped = Math.max(DETAIL_MIN_WIDTH, width)
    if (mainSurfaceWidth != null && mainSurfaceWidth > 0) {
      set({
        detailPanelWidth: clamped,
        detailPanelWidthRatio: clamped / mainSurfaceWidth
      })
      return
    }
    set({ detailPanelWidth: clamped })
  },

  setAgentBuilderChatWidth(width: number, splitWidth?: number | null) {
    const clamped = Math.max(AGENT_BUILDER_CHAT_MIN_WIDTH, width)
    if (splitWidth != null && splitWidth > 0) {
      set({
        agentBuilderChatWidth: clamped,
        agentBuilderChatWidthRatio: clamped / splitWidth
      })
      return
    }
    set({ agentBuilderChatWidth: clamped })
  },

  setActiveDetailTab(tab: SystemDetailTab, options?: DetailRevealOptions) {
    const state = get()
    const detailPanelPreferredVisible = options?.reveal === false
      ? state.detailPanelPreferredVisible
      : true
    set({
      activeDetailTab: { kind: 'system', id: tab },
      openSystemTabs: withSystemTabOpen(state.openSystemTabs, tab),
      lastActiveSystemTab: tab,
      detailPanelPreferredVisible,
      detailPanelPreferredVisibleByThread: options?.reveal === false
        ? state.detailPanelPreferredVisibleByThread
        : withActiveScopeDetailVisibility(state.detailPanelPreferredVisibleByThread, true),
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  closeSystemTab(tab: SystemDetailTab, fallbackViewerId?: string | null) {
    const state = get()
    const openSystemTabs = state.openSystemTabs.filter((id) => id !== tab)
    const wasActive = state.activeDetailTab.kind === 'system' && state.activeDetailTab.id === tab
    if (!wasActive) {
      set({ openSystemTabs })
      return
    }
    const nextActive: ActiveDetailTab = openSystemTabs.length > 0
      ? { kind: 'system', id: openSystemTabs[openSystemTabs.length - 1] }
      : fallbackViewerId
        ? { kind: 'viewer', id: fallbackViewerId }
        : { kind: 'launcher' }
    // Closing the last tab empties the panel — auto-hide it, because the launcher
    // is only meant to appear when the user manually opens an empty panel.
    if (nextActive.kind === 'launcher') {
      set({
        openSystemTabs,
        activeDetailTab: nextActive,
        detailPanelPreferredVisible: false,
        detailPanelPreferredVisibleByThread: withActiveScopeDetailVisibility(
          state.detailPanelPreferredVisibleByThread,
          false
        ),
        ...resolveResponsivePanels(
          state.responsiveLayout,
          state.sidebarPreferredCollapsed,
          false
        )
      })
      return
    }
    set({ openSystemTabs, activeDetailTab: nextActive })
  },

  resetDetailTabs() {
    const state = get()
    set({
      openSystemTabs: [],
      activeDetailTab: { kind: 'launcher' },
      detailPanelPreferredVisible: false,
      detailPanelPreferredVisibleByThread: {},
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        false
      )
    })
  },

  syncDetailPanelForThread(scopeId, activeViewerTabId) {
    const state = get()
    const systemTabs = scopedSystemTabs(state.openSystemTabs)
    const hasViewerTab = activeViewerTabId != null
    const hasSystemTab = systemTabs.length > 0
    const savedVisible = scopeId != null
      && state.detailPanelPreferredVisibleByThread[scopeId] === true
    const detailPanelPreferredVisible = savedVisible && (hasViewerTab || hasSystemTab)
    const activeDetailTab: ActiveDetailTab = hasViewerTab
      ? { kind: 'viewer', id: activeViewerTabId }
      : hasSystemTab
        ? {
            kind: 'system',
            id: systemTabs.includes(state.lastActiveSystemTab)
              ? state.lastActiveSystemTab
              : systemTabs[systemTabs.length - 1]
          }
        : { kind: 'launcher' }

    set({
      activeDetailTab,
      detailPanelPreferredVisible,
      detailPanelFullView: false,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  setActiveViewerTab(tabId: string, options?: DetailRevealOptions) {
    const state = get()
    const detailPanelPreferredVisible = options?.reveal === false
      ? state.detailPanelPreferredVisible
      : true
    set({
      activeDetailTab: { kind: 'viewer', id: tabId },
      detailPanelPreferredVisible,
      detailPanelPreferredVisibleByThread: options?.reveal === false
        ? state.detailPanelPreferredVisibleByThread
        : withActiveScopeDetailVisibility(state.detailPanelPreferredVisibleByThread, true),
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  closeViewerTab(options?: DetailRevealOptions) {
    const state = get()
    const open = scopedSystemTabs(state.openSystemTabs)
    const fallsBackToLauncher = open.length === 0
    const nextActive: ActiveDetailTab = fallsBackToLauncher
      ? { kind: 'launcher' }
      : { kind: 'system', id: open.includes(state.lastActiveSystemTab) ? state.lastActiveSystemTab : open[open.length - 1] }
    // Closing the last tab empties the panel — auto-hide it, because the launcher
    // is only meant to appear when the user manually opens an empty panel.
    const detailPanelPreferredVisible = fallsBackToLauncher
      ? false
      : options?.reveal === false
        ? state.detailPanelPreferredVisible
        : true
    set({
      activeDetailTab: nextActive,
      detailPanelPreferredVisible,
      detailPanelPreferredVisibleByThread: fallsBackToLauncher
        ? withActiveScopeDetailVisibility(state.detailPanelPreferredVisibleByThread, false)
        : state.detailPanelPreferredVisibleByThread,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  setQuickOpenVisible(visible: boolean) {
    set({ quickOpenVisible: visible })
  },

  toggleExplorer() {
    set((state) => ({ explorerVisible: !state.explorerVisible }))
  },

  setExplorerVisible(visible: boolean) {
    set({ explorerVisible: visible })
  },

  setExplorerWidth(width: number) {
    const clamped = Math.min(EXPLORER_MAX_WIDTH, Math.max(EXPLORER_MIN_WIDTH, width))
    set({ explorerWidth: clamped })
  },

  revealInExplorer(absoluteDir: string) {
    set({ explorerVisible: true, explorerRevealPath: absoluteDir })
  },

  consumeExplorerReveal() {
    if (get().explorerRevealPath !== null) {
      set({ explorerRevealPath: null })
    }
  },

  selectChangeKey(key) {
    set({ selectedChangeKey: key })
  },

  getChangesDiffMode(threadId) {
    if (!threadId) return 'inline'
    return get().changesDiffModeByThread[threadId] ?? 'inline'
  },

  setChangesDiffMode(threadId, mode) {
    if (!threadId) return
    set((state) => ({
      changesDiffModeByThread: {
        ...state.changesDiffModeByThread,
        [threadId]: mode
      }
    }))
  },

  toggleChangesWordWrap() {
    set((state) => ({ changesWordWrap: !state.changesWordWrap }))
  },

  showChangesForKey(key) {
    const state = get()
    const detailPanelPreferredVisible = true
    set({
      activeDetailTab: { kind: 'system', id: 'changes' },
      openSystemTabs: withSystemTabOpen(state.openSystemTabs, 'changes'),
      lastActiveSystemTab: 'changes',
      selectedChangeKey: key,
      detailPanelPreferredVisible,
      detailPanelPreferredVisibleByThread: withActiveScopeDetailVisibility(
        state.detailPanelPreferredVisibleByThread,
        true
      ),
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  maybeAutoShowForReason(reasonId) {
    const normalized = reasonId.trim()
    if (!normalized) return false
    const state = get()
    if (state.autoShowReasons.has(normalized)) return false
    const autoShowReasons = new Set(state.autoShowReasons)
    autoShowReasons.add(normalized)
    const detailPanelPreferredVisible = true
    set({
      autoShowReasons,
      detailPanelPreferredVisible,
      detailPanelPreferredVisibleByThread: withActiveScopeDetailVisibility(
        state.detailPanelPreferredVisibleByThread,
        true
      ),
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
    return true
  },

  resetAutoShowReasons() {
    set({ autoShowReasons: new Set<string>() })
  },

  setComposerPrefill(text) {
    set({ composerPrefill: text })
  },

  consumeComposerPrefill() {
    const text = get().composerPrefill
    set({ composerPrefill: null })
    return text
  },

  requestComposerImageAttachment(image) {
    set({ composerImageAttachmentRequest: { id: Date.now(), image } })
  },

  consumeComposerImageAttachmentRequest() {
    const request = get().composerImageAttachmentRequest
    set({ composerImageAttachmentRequest: null })
    return request?.image ?? null
  },

  setPendingThreadCreation(payload) {
    set({ pendingThreadCreation: payload })
  },

  resolvePendingThreadCreation(requestId, threadId) {
    set((state) => state.pendingThreadCreation?.requestId === requestId
      ? { pendingThreadCreation: { ...state.pendingThreadCreation, threadId } }
      : {})
  },

  setPendingWelcomeTurn(payload) {
    const existing = get()._pendingWelcomeTimer
    if (existing != null) {
      clearTimeout(existing)
    }

    if (payload == null) {
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      return
    }

    const timer = setTimeout(() => {
      const current = get().pendingWelcomeTurn
      if (current != null) {
        console.warn('pendingWelcomeTurn timed out, clearing')
        set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      }
    }, PENDING_WELCOME_TIMEOUT_MS)

    set({
      pendingWelcomeTurn: {
        ...payload,
        createdAt: Date.now()
      },
      _pendingWelcomeTimer: timer
    })
  },

  consumePendingWelcomeTurnIfMatch(threadId) {
    const p = get().pendingWelcomeTurn
    if (p && p.threadId === threadId) {
      const timer = get()._pendingWelcomeTimer
      if (timer != null) {
        clearTimeout(timer)
      }
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      const { text, inputParts, images, files, appIds, sentAsGoal } = p
      return {
        text,
        inputParts,
        ...(images !== undefined ? { images } : {}),
        ...(files !== undefined ? { files } : {}),
        ...(appIds !== undefined ? { appIds } : {}),
        ...(sentAsGoal !== undefined ? { sentAsGoal } : {})
      }
    }
    return null
  },

  cancelPendingWelcomeTurnForThread(threadId) {
    const p = get().pendingWelcomeTurn
    if (p?.threadId === threadId) {
      const timer = get()._pendingWelcomeTimer
      if (timer != null) {
        clearTimeout(timer)
      }
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
    }
  },

  setPendingProjectThreadOpen(payload) {
    if (payload == null) {
      set({ pendingProjectThreadOpen: null })
      return
    }

    const projectKey = normalizeWorkspaceProjectKey(payload.projectKey || payload.workspacePath)
    const threadId = payload.threadId.trim()
    if (!projectKey || !threadId) {
      set({ pendingProjectThreadOpen: null })
      return
    }

    set({
      pendingProjectThreadOpen: {
        projectKey,
        workspacePath: payload.workspacePath,
        threadId
      }
    })
  },

  consumePendingProjectThreadOpen(projectKey, threadIds) {
    const pending = get().pendingProjectThreadOpen
    if (!pending) return null
    if (normalizeWorkspaceProjectKey(projectKey) !== normalizeWorkspaceProjectKey(pending.projectKey)) {
      return null
    }

    set({ pendingProjectThreadOpen: null })
    const matchingThreadId = [...threadIds].some((id) => id === pending.threadId)
    return matchingThreadId ? pending : null
  },

  clearPendingProjectThreadOpen(projectKey, threadId) {
    const pending = get().pendingProjectThreadOpen
    if (!pending) return
    if (
      projectKey != null &&
      normalizeWorkspaceProjectKey(projectKey) !== normalizeWorkspaceProjectKey(pending.projectKey)
    ) {
      return
    }
    if (threadId != null && pending.threadId !== threadId) return
    set({ pendingProjectThreadOpen: null })
  },

  setWelcomeDraft(draft, workspacePath) {
    const current = get()
    const rawPath = workspacePath ?? current.welcomeDraftWorkspacePath ?? ''
    const key = normalizeWorkspaceDraftKey(rawPath)
    if (draft == null) {
      set((state) => {
        const drafts = { ...state.welcomeDraftsByWorkspace }
        delete drafts[key]
        const mirrorsCurrent = normalizeWorkspaceDraftKey(state.welcomeDraftWorkspacePath) === key
        return {
          welcomeDraftsByWorkspace: drafts,
          ...(mirrorsCurrent ? { welcomeDraft: null } : {})
        }
      })
      return
    }
    const nextDraft: WelcomeDraft = {
      ...draft,
      images: [...draft.images],
      files: draft.files ? [...draft.files] : [],
      segments: draft.segments ? [...draft.segments] : undefined,
      selectionStart: draft.selectionStart,
      selectionEnd: draft.selectionEnd,
      updatedAt: Date.now()
    }
    set({
      welcomeDraft: nextDraft,
      welcomeDraftWorkspacePath: rawPath,
      welcomeDraftsByWorkspace: {
        ...get().welcomeDraftsByWorkspace,
        [key]: nextDraft
      }
    })
  },

  clearWelcomeDraft(workspacePath) {
    get().setWelcomeDraft(null, workspacePath)
  },

  stageWelcomeDraft(draft) {
    get().setWelcomeDraft(draft)
    set((state) => ({ welcomeDraftStageSeq: state.welcomeDraftStageSeq + 1 }))
    get().goToNewChat()
  },

  setWelcomeDraftWorkspace(workspacePath) {
    const key = normalizeWorkspaceDraftKey(workspacePath)
    const draft = get().welcomeDraftsByWorkspace[key] ?? null
    set({
      welcomeDraftWorkspacePath: workspacePath,
      welcomeDraft: draft ? cloneWelcomeDraft(draft) : null
    })
  },

  getWelcomeDraftForWorkspace(workspacePath) {
    const key = normalizeWorkspaceDraftKey(workspacePath)
    const draft = get().welcomeDraftsByWorkspace[key] ?? null
    return draft ? cloneWelcomeDraft(draft) : null
  },

  dismissPlanApproval(turnId) {
    if (!turnId) return
    set((state) => ({
      planApprovalDismissed: {
        ...state.planApprovalDismissed,
        [turnId]: true
      }
    }))
  },

  setShowThinkingContent(visible) {
    set({ showThinkingContent: visible })
  },

  setProjectsSectionCollapsed(collapsed) {
    set({ projectsSectionCollapsed: collapsed })
    void window.api?.settings
      ?.set({ projectsSectionCollapsed: collapsed })
      .catch((err: unknown) => console.error('settings:set projectsSectionCollapsed failed:', err))
  },

  setPinnedSectionCollapsed(collapsed) {
    set({ pinnedSectionCollapsed: collapsed })
    void window.api?.settings
      ?.set({ pinnedSectionCollapsed: collapsed })
      .catch((err: unknown) => console.error('settings:set pinnedSectionCollapsed failed:', err))
  },

  setChatsSectionCollapsed(collapsed) {
    set({ chatsSectionCollapsed: collapsed })
    void window.api?.settings
      ?.set({ chatsSectionCollapsed: collapsed })
      .catch((err: unknown) => console.error('settings:set chatsSectionCollapsed failed:', err))
  },

  setDiffMarkers(mode) {
    set({ diffMarkers: mode })
  },

  resetPlanApprovalDismissed() {
    set({ planApprovalDismissed: {} })
  },

  _pendingWelcomeTimer: null
}))

function canShowDetailPanelFullView(
  state: Pick<UIState, 'detailPanelVisible' | 'activeMainView' | 'activeDetailTab'>
): boolean {
  return state.detailPanelVisible
    && state.activeMainView === 'conversation'
    && state.activeDetailTab.kind !== 'launcher'
}

useUIStore.subscribe((state) => {
  if (state.detailPanelFullView && !canShowDetailPanelFullView(state)) {
    useUIStore.setState({ detailPanelFullView: false })
  }
})

export {
  SIDEBAR_DEFAULT_WIDTH,
  SIDEBAR_MIN_WIDTH,
  SIDEBAR_COLLAPSED_WIDTH,
  DETAIL_DEFAULT_WIDTH,
  DETAIL_MIN_WIDTH,
  DETAIL_DEFAULT_MAIN_SURFACE_WIDTH,
  DETAIL_DEFAULT_WIDTH_RATIO,
  AGENT_BUILDER_CHAT_DEFAULT_WIDTH,
  AGENT_BUILDER_CHAT_MIN_WIDTH,
  AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO,
  EXPLORER_DEFAULT_WIDTH,
  EXPLORER_MIN_WIDTH,
  EXPLORER_MAX_WIDTH
}
