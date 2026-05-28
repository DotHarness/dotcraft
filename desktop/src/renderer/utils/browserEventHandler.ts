import { translate, type AppLocale } from '../../shared/locales'
import type { BrowserEventPayload } from '../../shared/viewer/types'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

interface BrowserEventHandlerOptions {
  locale: AppLocale
  workspacePath: string
}

export function handleBrowserEvent(
  event: BrowserEventPayload,
  { locale, workspacePath }: BrowserEventHandlerOptions
): void {
  const state = useViewerTabStore.getState()
  const threadId = event.threadId ?? state.currentThreadId
  if (!threadId) return

  switch (event.type) {
    case 'did-start-loading':
      state.updateBrowserTab(threadId, event.tabId, {
        loading: true,
        crashed: false,
        blockedMessage: undefined,
        downloadMessage: undefined
      })
      return
    case 'did-stop-loading':
      state.updateBrowserTab(threadId, event.tabId, {
        loading: false,
        ...(event.url ? { currentUrl: event.url } : {})
      })
      return
    case 'did-navigate':
      state.updateBrowserTab(threadId, event.tabId, {
        ...(event.url ? { currentUrl: event.url } : {}),
        blockedMessage: undefined,
        loading: false
      })
      return
    case 'did-fail-load':
      state.updateBrowserTab(threadId, event.tabId, {
        loading: false,
        ...(event.message ? { errorMessage: event.message } : {})
      })
      return
    case 'page-title-updated':
      state.updateBrowserTab(threadId, event.tabId, {
        ...(event.title ? { title: event.title } : {})
      })
      return
    case 'page-favicon-updated':
      state.updateBrowserTab(threadId, event.tabId, {
        ...(event.faviconDataUrl ? { faviconDataUrl: event.faviconDataUrl } : {})
      })
      return
    case 'blocked-navigation':
      state.updateBrowserTab(threadId, event.tabId, {
        loading: false,
        blockedMessage: event.message ?? translate(locale, 'viewer.browser.blockedScheme')
      })
      return
    case 'download-blocked':
      state.updateBrowserTab(threadId, event.tabId, {
        downloadMessage: event.message ?? translate(locale, 'viewer.browser.downloadBlocked')
      })
      return
    case 'crashed':
      state.updateBrowserTab(threadId, event.tabId, {
        crashed: true,
        loading: false
      })
      return
    case 'update-history-flags':
      state.updateBrowserTab(threadId, event.tabId, {
        ...(typeof event.canGoBack === 'boolean' ? { canGoBack: event.canGoBack } : {}),
        ...(typeof event.canGoForward === 'boolean' ? { canGoForward: event.canGoForward } : {})
      })
      return
    case 'external-handoff':
      return
    case 'automation-started':
    case 'automation-updated':
      state.updateBrowserTab(threadId, event.tabId, {
        automationActive: event.automationActive ?? true,
        ...(event.sessionName !== undefined ? { automationSessionName: event.sessionName } : {}),
        ...(event.action !== undefined ? { lastAutomationAction: event.action } : {})
      })
      return
    case 'automation-stopped':
      state.updateBrowserTab(threadId, event.tabId, {
        automationActive: false,
        ...(event.sessionName !== undefined ? { automationSessionName: event.sessionName } : {}),
        ...(event.action !== undefined ? { lastAutomationAction: event.action } : {})
      })
      return
    case 'virtual-cursor':
      state.updateBrowserTab(threadId, event.tabId, {
        ...(typeof event.x === 'number' && typeof event.y === 'number'
          ? { virtualCursor: { x: event.x, y: event.y } }
          : {})
      })
      return
    case 'request-new-tab':
      handleRequestNewBrowserTab(event, threadId, state.currentThreadId, workspacePath, locale)
      return
    default:
      return
  }
}

function handleRequestNewBrowserTab(
  event: BrowserEventPayload,
  threadId: string,
  currentThreadId: string | null,
  workspacePath: string,
  locale: AppLocale
): void {
  if (!event.url || !workspacePath) return

  const state = useViewerTabStore.getState()
  const sourceTab = state.getThreadState(threadId).tabs.find((tab) => tab.id === event.tabId)
  if (sourceTab?.kind !== 'browser') return

  const shouldActivate = threadId === currentThreadId
  const newTabId = state.openBrowser({
    threadId,
    initialUrl: event.url,
    initialLabel: translate(locale, 'viewer.newBrowserTab'),
    activate: shouldActivate
  })

  if (shouldActivate) {
    useUIStore.getState().setActiveViewerTab(newTabId)
  }

  void window.api.workspace.viewer.browser.create({
    tabId: newTabId,
    threadId,
    workspacePath,
    initialUrl: event.url
  })
}
