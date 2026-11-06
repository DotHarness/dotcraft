import { useEffect, useLayoutEffect, useRef } from 'react'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useDetailPanelPresentation } from '../stores/detailPanelPresentation'
import { startDetailPanelPersistence } from '../stores/detailPanelPersistence'
import { useUIStore } from '../stores/uiStore'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'

export function useDetailPanelLifecycle(params: { projectKey: string; workspacePath: string; browserWorkspacePath: string; threadId: string | null; selectionProjectKey: string | null; welcomeScopeId: string }) {
  const { projectKey, workspacePath, browserWorkspacePath, welcomeScopeId } = params
  const threadId = params.selectionProjectKey === projectKey ? params.threadId : null
  const persistence = useRef<ReturnType<typeof startDetailPanelPersistence> | null>(null)
  const panel = useDetailPanelPresentation()
  const activeMainView = useUIStore(state => state.activeMainView)
  const quickOpen = useUIStore(state => state.quickOpenVisible)
  const blocked = useTransientOverlayStore(state => state.nativeViewBlockerCount > 0)
  const lastVisibility = useRef(new Map<string, boolean>())
  const lastActive = useRef<string | null>(null)
  useLayoutEffect(() => {
    const store = useDetailPanelStore.getState()
    const outgoing = store.getCurrentScope()
    const incomingId = threadId ?? welcomeScopeId
    if (outgoing && (outgoing.projectKey !== projectKey || outgoing.threadId !== incomingId)) {
      for (const tab of store.getPanel(outgoing.threadId, outgoing.projectKey).tabs) {
        if (tab.kind === 'browser') void window.api.workspace.viewer.browser.setVisible({
          tabId: tab.id,
          visible: false
        })
      }
    }
    store.onWorkspaceSwitched(browserWorkspacePath, projectKey)
    store.setWelcomeScope(welcomeScopeId)
    store.onThreadSwitched(incomingId)
    const current = store.getCurrentPanel()
    if (current.workspacePath !== workspacePath || current.browserWorkspacePath !== browserWorkspacePath) store.patchPanel({
      workspacePath,
      browserWorkspacePath
    })
    const scope = store.getCurrentScope()
    if (scope) void window.api.workspace.viewer.browser.setForegroundScope({
      projectKey: scope.projectKey,
      threadId: scope.threadId
    })
  }, [projectKey, workspacePath, browserWorkspacePath, threadId, welcomeScopeId])
  useEffect(() => {
    const manager = startDetailPanelPersistence(window.api.workspace.viewer.panelState, async (scope, session) => {
      const tabs = session.tabs.flatMap(tab => tab.kind === 'browser' && tab.browserStorageId
        ? [{
          tabId: tab.id,
          browserStorageId: tab.browserStorageId,
          initialUrl: tab.currentUrl
        }]
        : [])
      if (!tabs.length) return []
      return window.api.workspace.viewer.browser.restoreState({
        workspacePath: session.browserWorkspacePath,
        projectKey: scope.projectKey,
        threadId: scope.threadId,
        tabs
      })
    })
    persistence.current = manager
    const scope = useDetailPanelStore.getState().getCurrentScope()
    if (scope?.projectKey && useDetailPanelStore.getState().getCurrentPanel().browserWorkspacePath) void manager.hydrate(scope)
    return () => {
      manager.dispose()
      persistence.current = null
    }
  }, [])
  useEffect(() => {
    const scope = useDetailPanelStore.getState().getCurrentScope()
    if (scope?.projectKey && browserWorkspacePath) void persistence.current?.hydrate(scope)
  }, [projectKey, browserWorkspacePath, threadId, welcomeScopeId])
  useEffect(() => {
    const current = useDetailPanelStore.getState().getCurrentPanel()
    const desired = new Map<string, boolean>()
    const hidden = quickOpen || blocked || activeMainView !== 'conversation' || !panel.detailPanelVisible
    let active: string | null = null
    for (const tab of current.tabs) {
      if (tab.kind !== 'browser') continue
      const visible = !hidden && panel.activeDetailTab.kind === 'viewer' && panel.activeDetailTab.id === tab.id
      desired.set(tab.id, visible)
      if (visible) active = tab.id
    }
    for (const [tabId, visible] of desired) {
      if (lastVisibility.current.get(tabId) !== visible) void window.api.workspace.viewer.browser.setVisible({
        tabId,
        visible
      })
    }
    lastVisibility.current = desired
    if (active && active !== lastActive.current) void window.api.workspace.viewer.browser.setActive({
      tabId: active
    })
    lastActive.current = active
  }, [panel.activeDetailTab, panel.detailPanelVisible, panel.tabs, activeMainView, quickOpen, blocked, projectKey, threadId])
}
