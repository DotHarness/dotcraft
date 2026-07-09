import { create } from 'zustand'
import type { ComposerFileAttachment, ImageAttachment, InputPart, ThreadMode } from '../types/conversation'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ApprovalPolicyWire, ContextWindowConfigurationWire } from '../types/thread'
import type { ReasoningEffortWire, ReasoningOutputWire } from './modelCatalogStore'
import type { SettingsTab } from '../types/settings'
import type { DiffMarkerMode } from '../../shared/appearance'
import { useThreadStore } from './threadStore'
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

/** Built-in workspace explorer (docked inside the file viewer). */
const EXPLORER_DEFAULT_WIDTH = 260
const EXPLORER_MIN_WIDTH = 180
const EXPLORER_MAX_WIDTH = 480

/** Timeout for pending welcome turn to prevent permanent residue */
const PENDING_WELCOME_TIMEOUT_MS = 30_000

export type SystemDetailTab = 'changes' | 'plan'
export type ChangesDiffMode = 'inline' | 'split'

/** @deprecated Use `ActiveDetailTab` instead. Kept for backwards compatibility. */
export type DetailPanelTab = SystemDetailTab

/**
 * Discriminated union identifying the active detail panel tab.
 * `launcher` is the empty state: no system tab and no viewer tab is open, so the
 * panel shows the launcher card grid instead of a tab body.
 */
export type ActiveDetailTab =
  | { kind: 'system'; id: SystemDetailTab }
  | { kind: 'viewer'; id: string }
  | { kind: 'launcher' }

/** Canonical left-to-right order of the optional system tabs in the tab strip. */
const SYSTEM_TAB_ORDER: readonly SystemDetailTab[] = ['changes', 'plan']

/** Insert `id` into the open system tabs list, preserving canonical order. */
function withSystemTabOpen(open: SystemDetailTab[], id: SystemDetailTab): SystemDetailTab[] {
  if (open.includes(id)) return open
  return SYSTEM_TAB_ORDER.filter((tab) => tab === id || open.includes(tab))
}

interface DetailRevealOptions {
  reveal?: boolean
}

/** Main content area: conversation vs auxiliary surfaces (Skills, Automations, Settings). */
export type BuiltInMainView = 'conversation' | 'skills' | 'automations' | 'settings' | 'channels' | 'teams'
export type ExtensionMainView = `extension:${string}`
export type ActiveMainView = BuiltInMainView | ExtensionMainView

/** Secondary surface inside the plugin/skill catalog view. */
export type PluginCatalogSurface = 'plugins' | 'skills'

/** Automations view: Tasks (orchestrator) vs Cron (scheduled jobs). */
export type AutomationsTab = 'tasks' | 'cron'

export interface WelcomeDraft {
  text: string
  segments?: ComposerDraftSegment[]
  selectionStart?: number
  selectionEnd?: number
  images: ImageAttachment[]
  files?: ComposerFileAttachment[]
  mode: ThreadMode
  model: string
  reasoning?: {
    enabled: boolean
    effort: ReasoningEffortWire
    output: ReasoningOutputWire
  }
  contextWindow?: ContextWindowConfigurationWire
  approvalPolicy?: Extract<ApprovalPolicyWire, 'default' | 'prompt' | 'autoApprove'>
  updatedAt: number
}

export interface PendingProjectThreadOpen {
  projectKey: string
  workspacePath: string
  threadId: string
}

export interface UIState {
  /** Which primary view fills the center column (conversation panel slot). */
  activeMainView: ActiveMainView
  /** Active tab inside the plugin/skill catalog view. */
  pluginCatalogSurface: PluginCatalogSurface
  /** Active tab inside Automations view (spec §21.1). */
  automationsTab: AutomationsTab
  /** Active section inside the Settings surface. */
  activeSettingsTab: SettingsTab
  /** Monotonic request counter used by the Settings sidebar Back action. */
  settingsCloseRequestSeq: number
  /** Monotonic request counter used by renderer-owned What's New entry points. */
  whatsNewOpenRequestSeq: number
  /** User preference for whether the sidebar is collapsed when width allows it. */
  sidebarPreferredCollapsed: boolean
  sidebarCollapsed: boolean
  sidebarWidth: number
  /** User preference for whether the detail panel is visible when width allows it. */
  detailPanelPreferredVisible: boolean
  detailPanelVisible: boolean
  /** User preference for detail panel width as a share of the main work surface. */
  detailPanelWidthRatio: number
  /** Last resolved detail panel width, used as a fallback before layout measurement. */
  detailPanelWidth: number
  /** User preference for Agent Builder chat width as a share of the builder split surface. */
  agentBuilderChatWidthRatio: number
  /** Last resolved Agent Builder chat pane width, used as a fallback before layout measurement. */
  agentBuilderChatWidth: number
  /** Current responsive layout classification used to constrain panel visibility. */
  responsiveLayout: 'full' | 'no-detail' | 'collapsed'
  /** Active detail panel tab — a system tab, a viewer tab, or the launcher. */
  activeDetailTab: ActiveDetailTab
  /**
   * The optional system tabs (Diff / Progress) currently open in the strip, in
   * canonical order. Empty by default — they are no longer pinned; they open via
   * the launcher, the `+` menu, or agent auto-show, and can be closed.
   */
  openSystemTabs: SystemDetailTab[]
  /**
   * Last active system tab, saved when the user switches to a viewer tab. Used as
   * a fallback hint when the last viewer tab is closed — only honored if the tab
   * is still present in `openSystemTabs`.
   */
  lastActiveSystemTab: SystemDetailTab
  /** Whether the Quick-Open file finder dialog is visible. */
  quickOpenVisible: boolean
  /** Whether the built-in workspace explorer is docked open in the file viewer. */
  explorerVisible: boolean
  /** Width (px) of the docked explorer sub-panel. */
  explorerWidth: number
  /**
   * One-shot absolute directory the explorer should expand to and scroll into
   * view (set when a breadcrumb folder segment is clicked). Cleared by the
   * explorer once consumed.
   */
  explorerRevealPath: string | null
  /** Currently selected file path in the Changes tab */
  selectedChangedFile: string | null
  /** Per-thread display mode for the Changes diff stream. */
  changesDiffModeByThread: Record<string, ChangesDiffMode>
  /** Whether long diff lines wrap instead of scrolling horizontally. */
  changesWordWrap: boolean
  /**
   * Tracks the turn ID for which the detail panel was auto-shown.
   * Prevents re-triggering after the user manually hides the panel.
   */
  autoShowTriggeredForTurn: string | null
  /**
   * Tracks the streaming CreatePlan item ID for which the Plan tab auto-switch
   * has already been triggered.
   */
  autoShowPlanForItem: string | null
  /** Generic one-shot auto-show reasons to avoid repeated auto-open fights. */
  autoShowReasons: Set<string>
  /** Text to pre-fill into the InputComposer when its next mounts. */
  composerPrefill: string | null
  /**
   * First message to send after thread/read completes for a thread created from the
   * welcome screen (avoids optimistic UI being cleared by conversation reset).
   */
  pendingWelcomeTurn: {
    threadId: string
    text: string
    inputParts?: InputPart[]
    images?: ImageAttachment[]
    files?: ComposerFileAttachment[]
    /** Agent/plan chosen on Welcome before thread exists; applied after thread/read. */
    mode?: ThreadMode
    /** Model chosen on Welcome before thread exists; applied after thread/read. */
    model?: string
    /** Reasoning chosen on Welcome before thread exists; applied after thread/read. */
    reasoning?: {
      enabled: boolean
      effort: ReasoningEffortWire
      output: ReasoningOutputWire
    }
    /** Context-window choice selected on Welcome before thread exists; applied after thread/read. */
    contextWindow?: ContextWindowConfigurationWire
    /** Approval policy chosen on Welcome before thread exists; applied after thread/read. */
    approvalPolicy?: Extract<ApprovalPolicyWire, 'default' | 'prompt' | 'autoApprove'>
    /** True when this first turn establishes the thread goal (durable "sent as goal"). */
    sentAsGoal?: boolean
    createdAt: number
  } | null
  /** Background project thread click waiting for the target workspace's foreground thread list. */
  pendingProjectThreadOpen: PendingProjectThreadOpen | null
  /** Unsent draft on ConversationWelcome, preserved across thread navigation. */
  welcomeDraft: WelcomeDraft | null
  /** Unsent welcome drafts keyed by normalized workspace path. */
  welcomeDraftsByWorkspace: Record<string, WelcomeDraft>
  /** Workspace path currently mirrored in `welcomeDraft`. */
  welcomeDraftWorkspacePath: string | null
  /** Per-turn dismissal marker for the plan approval composer. */
  planApprovalDismissed: Record<string, boolean>
  /** User preference for rendering reasoning text in the conversation. */
  showThinkingContent: boolean
  /** Sidebar preference: whether the Projects section is collapsed. */
  projectsSectionCollapsed: boolean
  /** Sidebar preference: whether the Chats section is collapsed. */
  chatsSectionCollapsed: boolean
  /** Appearance preference for how code diffs are rendered. */
  diffMarkers: DiffMarkerMode
}

interface UIStore extends UIState {
  setActiveMainView(view: ActiveMainView): void
  setPluginCatalogSurface(surface: PluginCatalogSurface): void
  /** Deselect current thread and open Welcome composer in conversation view. */
  goToNewChat(options?: { workspacePath?: string; clearDraft?: boolean }): void
  setAutomationsTab(tab: AutomationsTab): void
  setActiveSettingsTab(tab: SettingsTab): void
  requestCloseSettings(): void
  requestOpenWhatsNew(): void
  setDiffMarkers(mode: DiffMarkerMode): void
  toggleSidebar(): void
  setSidebarCollapsed(collapsed: boolean): void
  setSidebarWidth(width: number): void
  toggleDetailPanel(): void
  setDetailPanelVisible(visible: boolean): void
  setResponsiveLayout(layout: 'full' | 'no-detail' | 'collapsed'): void
  setDetailPanelWidth(width: number, mainSurfaceWidth?: number | null): void
  setAgentBuilderChatWidth(width: number, splitWidth?: number | null): void
  /**
   * Opens a system tab (`'changes' | 'plan'`) if not already open, makes it the
   * active tab, and reveals the panel. This is the "auto-open + focus" entry point.
   */
  setActiveDetailTab(tab: SystemDetailTab, options?: DetailRevealOptions): void
  /**
   * Closes an open system tab. If it was active, falls back to the nearest
   * remaining open system tab, else the given viewer tab, else the launcher.
   */
  closeSystemTab(tab: SystemDetailTab, fallbackViewerId?: string | null): void
  /** Resets the detail panel to its empty state (no tabs open → launcher). */
  resetDetailTabs(): void
  /** Activates a viewer tab by its ID and makes the detail panel visible. */
  setActiveViewerTab(tabId: string, options?: DetailRevealOptions): void
  /** Closes the viewer panel and falls back to an open system tab or the launcher. */
  closeViewerTab(options?: DetailRevealOptions): void
  /** Show or hide the Quick-Open dialog. */
  setQuickOpenVisible(visible: boolean): void
  /** Toggle the docked workspace explorer. */
  toggleExplorer(): void
  /** Show or hide the docked workspace explorer. */
  setExplorerVisible(visible: boolean): void
  /** Set the docked explorer width (clamped to its min/max). */
  setExplorerWidth(width: number): void
  /** Open the explorer and request it expand to / reveal the given directory. */
  revealInExplorer(absoluteDir: string): void
  /** Clear the one-shot explorer reveal target after it has been consumed. */
  consumeExplorerReveal(): void
  selectChangedFile(filePath: string | null): void
  getChangesDiffMode(threadId: string | null): ChangesDiffMode
  setChangesDiffMode(threadId: string | null, mode: ChangesDiffMode): void
  /** Toggle word wrap for the Changes diff stream. */
  toggleChangesWordWrap(): void
  /** Open detail panel, switch to Changes tab, select the given file */
  showChangesForFile(filePath: string): void
  /** Mark auto-show as triggered for a given turn (prevents re-trigger) */
  markAutoShowForTurn(turnId: string): void
  /** Mark plan auto-switch as triggered for a given CreatePlan item. */
  markAutoShowPlanForItem(itemId: string): void
  /** Auto-show detail panel once for a reason. Returns true when newly triggered. */
  maybeAutoShowForReason(reasonId: string): boolean
  /** Clears one-shot auto-show reason memory (e.g. on thread/workspace change). */
  resetAutoShowReasons(): void
  /** Set text to be picked up by InputComposer on its next mount. */
  setComposerPrefill(text: string): void
  /** Read and clear the prefill text atomically. */
  consumeComposerPrefill(): string | null
  /** Queue first turn for a thread created from the welcome composer. */
  setPendingWelcomeTurn(
    payload: {
      threadId: string
      text: string
      inputParts?: InputPart[]
      images?: ImageAttachment[]
      files?: ComposerFileAttachment[]
      mode?: ThreadMode
      model?: string
      reasoning?: {
        enabled: boolean
        effort: ReasoningEffortWire
        output: ReasoningOutputWire
      }
      contextWindow?: ContextWindowConfigurationWire
      approvalPolicy?: Extract<ApprovalPolicyWire, 'default' | 'prompt' | 'autoApprove'>
      sentAsGoal?: boolean
    } | null
  ): void
  /** If pending matches threadId, return payload and clear; otherwise return null. */
  consumePendingWelcomeTurnIfMatch(
    threadId: string
  ): {
    text: string
    inputParts?: InputPart[]
    images?: ImageAttachment[]
    files?: ComposerFileAttachment[]
    mode?: ThreadMode
    model?: string
    reasoning?: {
      enabled: boolean
      effort: ReasoningEffortWire
      output: ReasoningOutputWire
    }
    contextWindow?: ContextWindowConfigurationWire
    approvalPolicy?: Extract<ApprovalPolicyWire, 'default' | 'prompt' | 'autoApprove'>
    sentAsGoal?: boolean
  } | null
  /** Clear pending welcome turn when it targets the given thread (e.g. thread/read failed). */
  cancelPendingWelcomeTurnForThread(threadId: string): void
  setPendingProjectThreadOpen(payload: PendingProjectThreadOpen | null): void
  consumePendingProjectThreadOpen(projectKey: string, threadIds: Iterable<string>): PendingProjectThreadOpen | null
  clearPendingProjectThreadOpen(projectKey?: string, threadId?: string): void
  setWelcomeDraft(draft: Omit<WelcomeDraft, 'updatedAt'> | null, workspacePath?: string): void
  clearWelcomeDraft(workspacePath?: string): void
  setWelcomeDraftWorkspace(workspacePath: string): void
  getWelcomeDraftForWorkspace(workspacePath: string): WelcomeDraft | null
  dismissPlanApproval(turnId: string): void
  setShowThinkingContent(visible: boolean): void
  /** Toggle+persist whether the Projects sidebar section is collapsed. */
  setProjectsSectionCollapsed(collapsed: boolean): void
  /** Toggle+persist whether the Chats sidebar section is collapsed. */
  setChatsSectionCollapsed(collapsed: boolean): void
  resetPlanApprovalDismissed(): void
}

/** Internal state not exposed in UIState but used for timeout management */
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

function normalizeWorkspaceDraftKey(path: string | null | undefined): string {
  return (path ?? '').trim().replace(/\\/g, '/').replace(/\/+$/u, '').toLowerCase()
}

function cloneWelcomeDraft(draft: WelcomeDraft): WelcomeDraft {
  return {
    ...draft,
    images: [...draft.images],
    files: draft.files ? [...draft.files] : [],
    segments: draft.segments ? [...draft.segments] : undefined,
    contextWindow: draft.contextWindow ? { ...draft.contextWindow } : undefined
  }
}

export const useUIStore = create<UIStore & InternalState>((set, get) => ({
  activeMainView: 'conversation',
  pluginCatalogSurface: 'plugins',
  automationsTab: 'tasks',
  activeSettingsTab: 'general',
  settingsCloseRequestSeq: 0,
  whatsNewOpenRequestSeq: 0,
  sidebarPreferredCollapsed: false,
  sidebarCollapsed: false,
  sidebarWidth: SIDEBAR_DEFAULT_WIDTH,
  detailPanelPreferredVisible: false,
  detailPanelVisible: false,
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
  selectedChangedFile: null,
  changesDiffModeByThread: {},
  changesWordWrap: false,
  autoShowTriggeredForTurn: null,
  autoShowPlanForItem: null,
  autoShowReasons: new Set<string>(),
  composerPrefill: null,
  pendingWelcomeTurn: null,
  pendingProjectThreadOpen: null,
  welcomeDraft: null,
  welcomeDraftsByWorkspace: {},
  welcomeDraftWorkspacePath: null,
  planApprovalDismissed: {},
  showThinkingContent: false,
  projectsSectionCollapsed: false,
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

  setAutomationsTab(tab) {
    set({ automationsTab: tab })
  },

  setActiveSettingsTab(tab) {
    set({ activeSettingsTab: tab })
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
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        visible
      )
    }))
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
    // The closed tab was active — pick the nearest remaining open system tab,
    // else the supplied viewer tab, else the launcher.
    const nextActive: ActiveDetailTab = openSystemTabs.length > 0
      ? { kind: 'system', id: openSystemTabs[openSystemTabs.length - 1] }
      : fallbackViewerId
        ? { kind: 'viewer', id: fallbackViewerId }
        : { kind: 'launcher' }
    // Closing the last tab empties the panel — auto-hide it instead of leaving the
    // launcher (welcome) state on screen. The launcher is only meant to appear when
    // the user manually opens an empty panel. A remaining system/viewer tab keeps
    // the panel visible (untouched, since the user closed the tab from inside it).
    if (nextActive.kind === 'launcher') {
      set({
        openSystemTabs,
        activeDetailTab: nextActive,
        detailPanelPreferredVisible: false,
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
    set({ openSystemTabs: [], activeDetailTab: { kind: 'launcher' } })
  },

  setActiveViewerTab(tabId: string, options?: DetailRevealOptions) {
    const state = get()
    const detailPanelPreferredVisible = options?.reveal === false
      ? state.detailPanelPreferredVisible
      : true
    set({
      activeDetailTab: { kind: 'viewer', id: tabId },
      detailPanelPreferredVisible,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  closeViewerTab(options?: DetailRevealOptions) {
    const state = get()
    // Fall back to an open system tab (preferring the last-active one if it is
    // still open), otherwise the launcher.
    const open = state.openSystemTabs
    const fallsBackToLauncher = open.length === 0
    const nextActive: ActiveDetailTab = fallsBackToLauncher
      ? { kind: 'launcher' }
      : { kind: 'system', id: open.includes(state.lastActiveSystemTab) ? state.lastActiveSystemTab : open[open.length - 1] }
    // Closing the last tab empties the panel — auto-hide it instead of leaving the
    // launcher (welcome) state on screen, so the welcome page only appears when the
    // user manually opens an empty panel. This also covers the thread-switch sync
    // (closeViewerTab({ reveal: false })): switching to a thread with no tabs closes
    // the panel rather than auto-opening it onto the launcher. A remaining system
    // tab keeps the panel visible (honoring an explicit reveal:false).
    const detailPanelPreferredVisible = fallsBackToLauncher
      ? false
      : options?.reveal === false
        ? state.detailPanelPreferredVisible
        : true
    set({
      activeDetailTab: nextActive,
      detailPanelPreferredVisible,
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

  selectChangedFile(filePath) {
    set({ selectedChangedFile: filePath })
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

  showChangesForFile(filePath) {
    const state = get()
    const detailPanelPreferredVisible = true
    set({
      activeDetailTab: { kind: 'system', id: 'changes' },
      openSystemTabs: withSystemTabOpen(state.openSystemTabs, 'changes'),
      lastActiveSystemTab: 'changes',
      selectedChangedFile: filePath,
      detailPanelPreferredVisible,
      ...resolveResponsivePanels(
        state.responsiveLayout,
        state.sidebarPreferredCollapsed,
        detailPanelPreferredVisible
      )
    })
  },

  markAutoShowForTurn(turnId) {
    set({ autoShowTriggeredForTurn: turnId })
  },

  markAutoShowPlanForItem(itemId) {
    set({ autoShowPlanForItem: itemId })
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
        ...(payload.contextWindow !== undefined ? { contextWindow: { ...payload.contextWindow } } : {}),
        createdAt: Date.now()
      },
      _pendingWelcomeTimer: timer
    })
  },

  consumePendingWelcomeTurnIfMatch(threadId) {
    const p = get().pendingWelcomeTurn
    if (p && p.threadId === threadId) {
      // Clear the timeout timer
      const timer = get()._pendingWelcomeTimer
      if (timer != null) {
        clearTimeout(timer)
      }
      set({ pendingWelcomeTurn: null, _pendingWelcomeTimer: null })
      const { text, inputParts, images, files, mode, model, reasoning, contextWindow, approvalPolicy, sentAsGoal } = p
      return {
        text,
        ...(inputParts !== undefined ? { inputParts } : {}),
        ...(images !== undefined ? { images } : {}),
        ...(files !== undefined ? { files } : {}),
        ...(mode !== undefined ? { mode } : {}),
        ...(model !== undefined ? { model } : {}),
        ...(reasoning !== undefined ? { reasoning } : {}),
        ...(contextWindow !== undefined ? { contextWindow: { ...contextWindow } } : {}),
        ...(approvalPolicy !== undefined ? { approvalPolicy } : {}),
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
      contextWindow: draft.contextWindow ? { ...draft.contextWindow } : undefined,
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

  // Internal state for timeout timer (not exposed in UIState interface)
  _pendingWelcomeTimer: null
}))

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
