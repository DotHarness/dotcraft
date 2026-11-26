import { useEffect, useLayoutEffect, useRef } from 'react'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { startDetailPanelPersistence } from '../stores/detailPanelPersistence'
import { useUIStore } from '../stores/uiStore'
import { presentPendingBrowserUseTab } from '../utils/browserUseOpenHandler'

export function useDetailPanelLifecycle(params: { projectKey: string; workspacePath: string; browserWorkspacePath: string; threadId: string | null; selectionProjectKey: string | null; welcomeScopeId: string }) {
  const { projectKey, workspacePath, browserWorkspacePath, welcomeScopeId } = params
  const threadId = params.selectionProjectKey === projectKey ? params.threadId : null
  const persistence = useRef<ReturnType<typeof startDetailPanelPersistence> | null>(null)
  const activeMainView = useUIStore(state => state.activeMainView)
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
  useLayoutEffect(presentPendingBrowserUseTab, [projectKey, threadId, activeMainView])
  useEffect(() => {
    const manager = startDetailPanelPersistence(window.api.workspace.viewer.panelState, async (scope, session) => {
      const tabs = session.tabs.flatMap(tab => tab.kind === 'browser' && tab.restore === 'required' && tab.browserStorageId
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
}
