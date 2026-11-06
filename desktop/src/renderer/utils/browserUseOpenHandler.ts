import type { BrowserUseClosePayload, BrowserUseOpenPayload } from '../../shared/viewer/types'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'

export function handleBrowserUseOpen(payload: BrowserUseOpenPayload): void {
  if (!payload.threadId || !payload.tabId) return

  const viewerStore = useDetailPanelStore.getState()
  if (payload.projectKey === undefined) return
  const threadState = viewerStore.getThreadState(payload.threadId, payload.projectKey)
  const hasActiveViewerTab = threadState.activeTabId != null
  const isActiveThread = useThreadStore.getState().activeThreadId === payload.threadId
    && viewerStore.currentProjectKey === payload.projectKey
  const shouldFocus =
    isActiveThread
    && payload.focusMode === 'first-open'

  const tabId = viewerStore.openBrowser({
    threadId: payload.threadId,
    projectKey: payload.projectKey,
    browserStorageId: payload.browserStorageId,
    tabId: payload.tabId,
    target: payload.tabId,
    initialUrl: payload.initialUrl || 'about:blank',
    initialLabel: payload.title?.trim() || 'Browser',
    activate: shouldFocus || !hasActiveViewerTab
  })

  if (!shouldFocus) return
  useUIStore.getState().setActiveMainView('conversation')
  viewerStore.setActiveTab(payload.threadId, tabId)
  useDetailPanelStore.getState().setActiveViewerTab(tabId)
}

export function handleBrowserUseClose(payload: BrowserUseClosePayload): void {
  if (!payload.threadId || !payload.tabId) return
  useDetailPanelStore.getState().closeTab(payload.threadId, payload.tabId)
}
