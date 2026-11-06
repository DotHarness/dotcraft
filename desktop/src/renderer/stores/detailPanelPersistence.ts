import type { BrowserRestoreResult } from '../../shared/viewer/types'
import type { PanelScope, PanelCheckpoint, PanelStateApi } from '../../shared/viewer/panelState'
import { panelScopeKey } from '../../shared/viewer/panelState'
import { normalizePanelCheckpoint, toPanelCheckpoint } from '../../shared/viewer/panelCheckpoint'
import { useDetailPanelStore, type PanelSession } from './detailPanelStore'

function checkpoint(panel: PanelSession): PanelCheckpoint {
  return toPanelCheckpoint({
    version: 1,
    tabs: panel.tabs,
    systemTabs: panel.openSystemTabs,
    activeTab: panel.activeDetailTab,
    visible: panel.detailPanelPreferredVisible,
    fullView: panel.detailPanelFullView,
    width: panel.detailPanelWidth
  })
}

export function startDetailPanelPersistence(api: PanelStateApi, restoreBrowserState: (scope: PanelScope, panel: PanelSession) => Promise<BrowserRestoreResult[]>) {
  const hydrated = new Set<string>()
  const inFlight = new Map<string, Promise<void>>()
  const generations = new Map<string, number>()
  const closedTabs = new Map<string, Set<string>>()
  let stopped = false
  const report = () => console.warn('Desktop panel checkpoint could not be saved or restored.')
  const save = (scope: PanelScope, value: PanelCheckpoint | null): void => {
    void api.update(scope, value).catch(report)
  }
  const unsubscribe = useDetailPanelStore.subscribe((state, previous) => {
    for (const [key, scope] of state.deletedScopes) {
      if (previous.deletedScopes.get(key) === scope) continue
      generations.set(key, (generations.get(key) ?? 0) + 1)
      hydrated.delete(key)
      save(scope, null)
    }
    for (const [key, oldPanel] of previous.byThread) {
      const next = state.byThread.get(key)
      if (!next) {
        generations.set(key, (generations.get(key) ?? 0) + 1)
        const moved = [...state.byThread.values()].find(panel => panel.tabs === oldPanel.tabs)
        if (moved && hydrated.has(key)) hydrated.add(panelScopeKey(moved.scope))
        hydrated.delete(key)
        if (!state.deletedScopes.has(key)) save(oldPanel.scope, null)
      } else if (next !== oldPanel) {
        const removed = oldPanel.tabs.filter(tab => !next.tabs.some(item => item.id === tab.id))
        const removedSystem = oldPanel.openSystemTabs.filter(id => !next.openSystemTabs.includes(id))
        if (removed.length || removedSystem.length) {
          const closed = closedTabs.get(key) ?? new Set<string>()
          for (const tab of removed) closed.add(tab.id)
          for (const id of removedSystem) closed.add(`system:${id}`)
          closedTabs.set(key, closed)
        }
      }
    }
    for (const [key, panel] of state.byThread) {
      if (hydrated.has(key) && panel !== previous.byThread.get(key)) save(panel.scope, checkpoint(panel))
    }
  })
  async function hydrate(scope: PanelScope): Promise<void> {
    const key = panelScopeKey(scope)
    if (stopped || hydrated.has(key)) return
    const pending = inFlight.get(key)
    if (pending) return pending
    const generation = generations.get(key) ?? 0
    const initial = useDetailPanelStore.getState().getPanel(scope.threadId, scope.projectKey)
    const promise = api.read(scope).then(async raw => {
      if (stopped || generation !== (generations.get(key) ?? 0)) return
      const saved = normalizePanelCheckpoint(raw)
      const store = useDetailPanelStore.getState()
      const current = store.getPanel(scope.threadId, scope.projectKey)
      if (saved) {
        const changed = current !== initial
        const closed = closedTabs.get(key) ?? new Set<string>()
        const tabs = saved.tabs.filter(tab => !closed.has(tab.id))
        for (const tab of changed ? current.tabs : []) {
          const index = tabs.findIndex(item => item.id === tab.id)
          if (index < 0) tabs.push(tab)
          else tabs[index] = tab
        }
        const activeDetailTab = changed ? current.activeDetailTab : saved.activeTab
        store.patchPanel({
          tabs,
          activeTabId: activeDetailTab.kind === 'viewer' ? activeDetailTab.id : tabs.at(-1)?.id ?? null,
          activeDetailTab,
          openSystemTabs: [...new Set([...saved.systemTabs.filter(id => !closed.has(`system:${id}`)), ...current.openSystemTabs])],
          detailPanelPreferredVisible: changed ? current.detailPanelPreferredVisible : saved.visible,
          detailPanelFullView: changed ? current.detailPanelFullView : saved.fullView,
          detailPanelWidth: changed ? current.detailPanelWidth : saved.width,
          detailPanelWidthRatio: changed ? current.detailPanelWidthRatio : saved.width / 1676
        }, scope.threadId, scope.projectKey)
      }
      const restored = useDetailPanelStore.getState().getPanel(scope.threadId, scope.projectKey)
      const browserResults = await restoreBrowserState(scope, restored)
      if (stopped || generation !== (generations.get(key) ?? 0)) return
      for (const result of browserResults) {
        if (result.status === 'owned-by-another-window') continue
        useDetailPanelStore.getState().updateBrowserTab(scope.threadId, result.tabId, {
          browserStorageId: result.browserStorageId,
          suspended: result.suspended ?? result.status !== 'already-live',
          ...(result.currentUrl ? {
            currentUrl: result.currentUrl
          } : {}),
          ...(result.title ? {
            title: result.title
          } : {}),
          ...(result.faviconDataUrl ? {
            faviconDataUrl: result.faviconDataUrl
          } : {}),
          ...(result.canGoBack !== undefined ? {
            canGoBack: result.canGoBack
          } : {}),
          ...(result.canGoForward !== undefined ? {
            canGoForward: result.canGoForward
          } : {})
        })
      }
      hydrated.add(key)
      const latest = useDetailPanelStore.getState()
      const panel = latest.getPanel(scope.threadId, scope.projectKey)
      if (latest.byThread.has(key)) save(scope, checkpoint(panel))
    }).catch(report).finally(() => {
      if (inFlight.get(key) === promise) inFlight.delete(key)
    })
    inFlight.set(key, promise)
    return promise
  }
  return {
    hydrate,
    dispose() {
      stopped = true
      unsubscribe()
    }
  }
}
