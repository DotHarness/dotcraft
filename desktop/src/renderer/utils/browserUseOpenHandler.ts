import type { BrowserUseClosePayload, BrowserUseOpenPayload } from '../../shared/viewer/types'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

export function handleBrowserUseOpen(payload: BrowserUseOpenPayload): void {
  if (!payload.threadId || !payload.tabId) return

  const viewerStore = useViewerTabStore.getState()
  const threadState = viewerStore.getThreadState(payload.threadId)
  const hasActiveViewerTab = threadState.activeTabId != null
  const isActiveThread = useThreadStore.getState().activeThreadId === payload.threadId
  const shouldFocus =
    isActiveThread
    && payload.focusMode === 'first-open'

  const tabId = viewerStore.openBrowser({
    threadId: payload.threadId,
    tabId: payload.tabId,
    target: payload.tabId,
    initialUrl: payload.initialUrl || 'about:blank',
    initialLabel: payload.title?.trim() || 'Browser',
    activate: shouldFocus || !hasActiveViewerTab
  })

  if (!shouldFocus) return
  useUIStore.getState().setActiveMainView('conversation')
  viewerStore.setActiveTab(payload.threadId, tabId)
  useUIStore.getState().setActiveViewerTab(tabId)
}

export function handleBrowserUseClose(payload: BrowserUseClosePayload): void {
  if (!payload.threadId || !payload.tabId) return
  useViewerTabStore.getState().closeTab(payload.threadId, payload.tabId)
}
