import { create, type StateCreator } from 'zustand'
import { panelScopeKey, type PanelScope } from '../../shared/viewer/panelState'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { createViewerActions } from './detailPanelViewerActions'
import { createTabUpdates } from './detailPanelTabUpdates'
import type { DetailPanelStore, PanelSession, ActiveDetailTab, SystemDetailTab } from './detailPanelState'

export type { DetailPanelStore, PanelSession, ActiveDetailTab, SystemDetailTab, FileReveal } from './detailPanelState'

const SYSTEM_ORDER: SystemDetailTab[] = ['changes', 'plan', 'subagents', 'board']

const defaults = new Map<string, PanelSession>()

function emptyPanel(scope: PanelScope, workspacePath: string): PanelSession {
  const key = panelScopeKey(scope)
  let panel = defaults.get(key)
  if (!panel) {
    panel = {
      scope,
      workspacePath,
      browserWorkspacePath: workspacePath,
      tabs: [],
      activeTabId: null,
      activeDetailTab: {
        kind: 'launcher'
      },
      openSystemTabs: [],
      lastActiveSystemTab: 'changes',
      detailPanelPreferredVisible: false,
      detailPanelFullView: false,
      detailPanelWidth: 600,
      detailPanelWidthRatio: 600 / 1676,
      autoShowReasons: new Set()
    }
    defaults.set(key, panel)
  }
  return panel
}

function reconcilePanel(previous: PanelSession, incoming: PanelSession): PanelSession {
  const panel = {
    ...previous,
    ...incoming
  }
  if (incoming.activeTabId !== previous.activeTabId) {
    panel.activeDetailTab = incoming.activeTabId
      ? {
        kind: 'viewer',
        id: incoming.activeTabId
      }
      : panel.openSystemTabs.length
        ? {
          kind: 'system',
          id: panel.openSystemTabs.includes(panel.lastActiveSystemTab) ? panel.lastActiveSystemTab : panel.openSystemTabs.at(-1)!
        }
        : {
          kind: 'launcher'
        }
  }
  if (previous.tabs.length && !panel.tabs.length && !panel.openSystemTabs.length) panel.detailPanelPreferredVisible = false
  return panel
}

export const useDetailPanelStore = create<DetailPanelStore>((set, get, api) => {
  const setViewer: Parameters<StateCreator<DetailPanelStore>>[0] = (update) => {
    set((state) => {
      const patch = typeof update === 'function' ? update(state) : update
      if (!patch.byThread) return patch
      const byThread = new Map(patch.byThread)
      for (const [key, incoming] of byThread) {
        const previous = state.byThread.get(key)
        if (previous === incoming) continue
        const owner = incoming.scope
        byThread.set(key, reconcilePanel(previous ?? emptyPanel(owner, state.currentWorkspacePath ?? ''), incoming))
      }
      return {
        ...patch,
        byThread
      }
    })
  }
  return {
    byThread: new Map(),
    deletedScopes: new Map(),
    currentThreadId: null,
    welcomeScopeId: null,
    currentProjectKey: '',
    currentWorkspacePath: null,
    ...createViewerActions(setViewer, get, api),
    ...createTabUpdates(setViewer, get, api),
    scopeKey(threadId, projectKey = get().currentProjectKey) {
      return panelScopeKey({
        projectKey,
        threadId
      })
    },

    getPanel(threadId = get().currentThreadId ?? get().welcomeScopeId ?? '', projectKey = get().currentProjectKey) {
      const scope = {
        projectKey,
        threadId
      }
      return get().byThread.get(panelScopeKey(scope)) ?? emptyPanel(scope, projectKey === get().currentProjectKey ? get().currentWorkspacePath ?? '' : projectKey)
    },

    getThreadState(threadId, projectKey) {
      return get().getPanel(threadId, projectKey)
    },

    getCurrentPanel() {
      return get().getPanel()
    },

    getCurrentScope() {
      return get().currentThreadId || get().welcomeScopeId ? get().getPanel().scope : null
    },

    getScopeForTab(tabId) {
      for (const panel of get().byThread.values()) if (panel.tabs.some(tab => tab.id === tabId)) return panel.scope
      return null
    },

    getCurrentTabs() {
      return get().getPanel().tabs
    },

    getCurrentActiveTabId() {
      return get().getPanel().activeTabId
    },

    patchPanel(patch, threadId, projectKey) {
      const panel = get().getPanel(threadId, projectKey)
      set(state => ({
        byThread: new Map(state.byThread).set(panelScopeKey(panel.scope), {
          ...panel,
          ...patch
        })
      }))
    },

    onThreadSwitched(threadId) {
      set({
        currentThreadId: threadId
      })
    },

    setWelcomeScope(welcomeScopeId) {
      set({
        welcomeScopeId
      })
    },

    onWorkspaceSwitched(currentWorkspacePath, projectKey = currentWorkspacePath) {
      set({
        currentWorkspacePath,
        currentProjectKey: normalizeWorkspaceProjectKey(projectKey)
      })
    },

    onThreadDeleted(threadId, options) {
      const key = get().scopeKey(threadId, options?.projectKey)
      const scope = {
        projectKey: options?.projectKey ?? get().currentProjectKey,
        threadId
      }
      const panel = get().byThread.get(key)
      for (const tab of panel?.tabs ?? []) {
        if (tab.kind === 'browser') options?.onBrowserTabRemoved?.(tab)
        if (tab.kind === 'terminal') options?.onTerminalTabRemoved?.(tab)
      }
      set(state => {
        const byThread = new Map(state.byThread)
        byThread.delete(key)
        return {
          byThread,
          deletedScopes: new Map(state.deletedScopes).set(key, scope)
        }
      })
    },

    toggleDetailPanel() {
      get().setDetailPanelVisible(!get().getPanel().detailPanelPreferredVisible)
    },

    setDetailPanelVisible(detailPanelPreferredVisible) {
      get().patchPanel({
        detailPanelPreferredVisible
      })
    },

    toggleDetailPanelFullView() {
      get().setDetailPanelFullView(!get().getPanel().detailPanelFullView)
    },

    setDetailPanelFullView(detailPanelFullView) {
      const panel = get().getPanel()
      get().patchPanel({
        detailPanelFullView: detailPanelFullView && panel.activeDetailTab.kind !== 'launcher',
        ...(detailPanelFullView ? {
          detailPanelPreferredVisible: true
        } : {})
      })
    },

    setDetailPanelWidth(width, mainSurfaceWidth) {
      const detailPanelWidth = Math.max(300, width)
      get().patchPanel({
        detailPanelWidth,
        ...(mainSurfaceWidth && mainSurfaceWidth > 0 ? {
          detailPanelWidthRatio: detailPanelWidth / mainSurfaceWidth
        } : {})
      })
    },

    setActiveDetailTab(tab, options) {
      const panel = get().getPanel()
      get().patchPanel({
        activeDetailTab: {
          kind: 'system',
          id: tab
        },
        openSystemTabs: SYSTEM_ORDER.filter(id => id === tab || panel.openSystemTabs.includes(id)),
        lastActiveSystemTab: tab,
        ...(options?.reveal === false ? {} : {
          detailPanelPreferredVisible: true
        })
      })
    },

    setActiveViewerTab(tabId, options) {
      const owner = get().getScopeForTab(tabId)
      get().patchPanel({
        activeDetailTab: {
          kind: 'viewer',
          id: tabId
        },
        ...(options?.reveal === false ? {} : {
          detailPanelPreferredVisible: true
        })
      }, owner?.threadId, owner?.projectKey)
    },

    closeSystemTab(tab, fallbackViewerId) {
      const panel = get().getPanel()
      const openSystemTabs = panel.openSystemTabs.filter(id => id !== tab)
      if (panel.activeDetailTab.kind !== 'system' || panel.activeDetailTab.id !== tab) {
        get().patchPanel({
          openSystemTabs
        })
        return
      }
      const activeDetailTab: ActiveDetailTab = openSystemTabs.length ? {
        kind: 'system',
        id: openSystemTabs.at(-1)!
      } : fallbackViewerId ? {
        kind: 'viewer',
        id: fallbackViewerId
      } : {
        kind: 'launcher'
      }
      get().patchPanel({
        openSystemTabs,
        activeDetailTab,
        ...(activeDetailTab.kind === 'launcher' ? {
          detailPanelPreferredVisible: false,
          detailPanelFullView: false
        } : {})
      })
    },

    closeViewerTab(options) {
      const panel = get().getPanel()
      const activeDetailTab: ActiveDetailTab = panel.openSystemTabs.length ? {
        kind: 'system',
        id: panel.openSystemTabs.includes(panel.lastActiveSystemTab) ? panel.lastActiveSystemTab : panel.openSystemTabs.at(-1)!
      } : {
        kind: 'launcher'
      }
      get().patchPanel({
        activeDetailTab,
        ...(activeDetailTab.kind === 'launcher' ? {
          detailPanelPreferredVisible: false,
          detailPanelFullView: false
        } : options?.reveal === false ? {} : {
          detailPanelPreferredVisible: true
        })
      })
    },

    maybeAutoShowForReason(reason) {
      const normalized = reason.trim(), panel = get().getPanel()
      if (!normalized || panel.autoShowReasons.has(normalized)) return false
      get().patchPanel({
        autoShowReasons: new Set([...panel.autoShowReasons, normalized]),
        detailPanelPreferredVisible: true
      })
      return true
    },

    resetAutoShowReasons() {
      get().patchPanel({
        autoShowReasons: new Set()
      })
    },

    handOffWelcomePanel(welcomeScopeId, threadId) {
      const scope = get().getPanel(welcomeScopeId).scope
      const fromKey = panelScopeKey(scope), toScope = {
        ...scope,
        threadId
      }
      const panel = get().byThread.get(fromKey)
      if (!panel) return null
      set(state => {
        const byThread = new Map(state.byThread)
        byThread.delete(fromKey)
        byThread.set(panelScopeKey(toScope), {
          ...panel,
          scope: toScope
        })
        return {
          byThread,
          currentThreadId: state.currentThreadId === welcomeScopeId ? threadId : state.currentThreadId
        }
      })
      return panel
    },

    reset() {
      defaults.clear()
      set({
        byThread: new Map(),
        deletedScopes: new Map(),
        currentThreadId: null,
        welcomeScopeId: null,
        currentWorkspacePath: null,
        currentProjectKey: ''
      })
    }
  }
})
