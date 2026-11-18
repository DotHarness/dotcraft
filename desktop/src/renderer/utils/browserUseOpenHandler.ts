import type { PanelScope } from '../../shared/viewer/panelState'
import type { BrowserUseClosePayload, BrowserUseOpenPayload } from '../../shared/viewer/types'
import { useUIStore } from '../stores/uiStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'

export function handleBrowserUseOpen(payload: BrowserUseOpenPayload): void {
  const { projectKey, threadId, tabId } = payload
  if (projectKey === undefined || !threadId || !tabId) return

  useDetailPanelStore.getState().openBrowser({
    threadId,
    projectKey,
    browserStorageId: payload.browserStorageId,
    tabId,
    target: tabId,
    initialUrl: payload.initialUrl || 'about:blank',
    initialLabel: payload.title?.trim() || 'Browser',
    activate: false
  })
  if (payload.focusMode === 'first-open') handleBrowserUseVisibility({ projectKey, threadId }, tabId, true)
}

export function handleBrowserUseVisibility({ projectKey, threadId }: PanelScope, tabId: string, visible: boolean): void {
  const store = useDetailPanelStore.getState()
  const panel = store.getPanel(threadId, projectKey)
  const canPresent = store.currentThreadId === threadId && store.currentProjectKey === projectKey
    && useUIStore.getState().activeMainView === 'conversation'

  if (visible) {
    store.patchPanel(canPresent ? {
      activeTabId: tabId,
      activeDetailTab: { kind: 'viewer', id: tabId },
      detailPanelPreferredVisible: true,
      pendingBrowserPresentation: undefined
    } : { pendingBrowserPresentation: tabId }, threadId, projectKey)
    return
  }

  const hide = canPresent && panel.activeDetailTab.kind === 'viewer' && panel.activeDetailTab.id === tabId
  const cancel = panel.pendingBrowserPresentation === tabId
  if (hide || cancel) store.patchPanel({
    ...(hide ? { detailPanelPreferredVisible: false } : {}),
    ...(cancel ? { pendingBrowserPresentation: undefined } : {})
  }, threadId, projectKey)
}

export function presentPendingBrowserUseTab(): void {
  const { scope, pendingBrowserPresentation } = useDetailPanelStore.getState().getCurrentPanel()
  if (pendingBrowserPresentation && useUIStore.getState().activeMainView === 'conversation') {
    handleBrowserUseVisibility(scope, pendingBrowserPresentation, true)
  }
}

export function handleBrowserUseClose(payload: BrowserUseClosePayload): void {
  if (!payload.threadId || !payload.tabId) return
  useDetailPanelStore.getState().closeTab(payload.threadId, payload.tabId)
}
