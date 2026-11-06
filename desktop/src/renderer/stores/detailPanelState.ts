import type { ViewerTab, ViewerContentClass, FileNavigationHint, BrowserViewerTab, TerminalViewerTab, BrowserRestoreIntent } from '../../shared/viewer/types'
import type { PanelScope, PanelSystemTab, PanelActiveTab } from '../../shared/viewer/panelState'
import type { FileReveal } from './viewerTabLabels'

export type { FileReveal } from './viewerTabLabels'

export type SystemDetailTab = PanelSystemTab

export type ActiveDetailTab = PanelActiveTab

export interface PanelSession {
  scope: PanelScope
  workspacePath: string
  browserWorkspacePath: string
  tabs: ViewerTab[]
  activeTabId: string | null
  activeDetailTab: ActiveDetailTab
  openSystemTabs: SystemDetailTab[]
  lastActiveSystemTab: SystemDetailTab
  detailPanelPreferredVisible: boolean
  detailPanelFullView: boolean
  detailPanelWidth: number
  detailPanelWidthRatio: number
  autoShowReasons: Set<string>
}

export interface ViewerTabActions {
  openFiles(params: { threadId: string; initialLabel: string }): string
  /** Focuses and returns the existing tab when one already has the same absolutePath. */
  openFile(params: {
    threadId: string
    absolutePath: string
    relativePath: string
    contentClass: ViewerContentClass
    sizeBytes?: number
    forceNew?: boolean
    navigationHint?: FileNavigationHint
    reveal?: FileReveal
  }): string
  openBrowser(params: {
    threadId: string
    tabId?: string
    target?: string
    initialUrl?: string
    initialLabel?: string
    activate?: boolean
    projectKey?: string
    browserStorageId?: string
    restore?: BrowserRestoreIntent
  }): string
  openTerminal(params: {
    threadId: string
    cwd: string
    initialLabel?: string
  }): string
  runInTerminal(params: {
    threadId: string
    tabId: string
    cwd: string
    title: string
    command: string
    env?: Record<string, string>
  }): string
  openWorkflow(params: { threadId: string; runId: string; initialLabel: string }): string
  openAutomation(params: { threadId: string; automationId: string; label: string }): string
  openSideChat(params: { threadId: string; sideChatKey: string; label: string }): string
  focusBrowserTabByUrl(params: { threadId: string; url: string }): string | null
  updateBrowserTab(threadId: string, tabId: string, patch: Partial<BrowserViewerTab>): void
  updateTerminalTab(threadId: string, tabId: string, patch: Partial<TerminalViewerTab>): void
  closeTab(threadId: string, tabId: string): void
  setActiveTab(threadId: string, tabId: string): void
  setWordWrap(threadId: string, tabId: string, wordWrap: boolean): void
  clearPendingReveal(threadId: string, tabId: string, revision: number): void
  onThreadSwitched(newThreadId: string | null): void
  setWelcomeScope(scopeId: string): void
  onThreadDeleted(
    threadId: string,
    options?: {
      projectKey?: string
      onBrowserTabRemoved?: (tab: BrowserViewerTab) => void
      onTerminalTabRemoved?: (tab: TerminalViewerTab) => void
    }
  ): void
  onWorkspaceSwitched(newWorkspacePath: string, projectKey?: string): void
  getThreadState(threadId: string, projectKey?: string): PanelSession
  getCurrentTabs(): ViewerTab[]
  getCurrentActiveTabId(): string | null
}

export interface DetailPanelStore extends ViewerTabActions {
  byThread: Map<string, PanelSession>
  deletedScopes: Map<string, PanelScope>
  currentThreadId: string | null
  welcomeScopeId: string | null
  currentProjectKey: string
  currentWorkspacePath: string | null
  scopeKey(threadId: string, projectKey?: string): string
  getCurrentScope(): PanelScope | null
  getScopeForTab(tabId: string): PanelScope | null
  getCurrentPanel(): PanelSession
  getPanel(threadId?: string, projectKey?: string): PanelSession
  patchPanel(patch: Partial<PanelSession>, threadId?: string, projectKey?: string): void
  toggleDetailPanel(): void
  setDetailPanelVisible(visible: boolean): void
  toggleDetailPanelFullView(): void
  setDetailPanelFullView(fullView: boolean): void
  setDetailPanelWidth(width: number, mainSurfaceWidth?: number | null): void
  setActiveDetailTab(tab: SystemDetailTab, options?: { reveal?: boolean }): void
  setActiveViewerTab(tabId: string, options?: { reveal?: boolean }): void
  closeSystemTab(tab: SystemDetailTab, fallbackViewerId?: string | null): void
  closeViewerTab(options?: { reveal?: boolean }): void
  maybeAutoShowForReason(reason: string): boolean
  resetAutoShowReasons(): void
  handOffWelcomePanel(welcomeScopeId: string, threadId: string): PanelSession | null
  reset(): void
}
