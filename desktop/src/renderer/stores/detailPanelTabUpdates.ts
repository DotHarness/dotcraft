import type { StateCreator } from 'zustand'
import type { DetailPanelStore } from './detailPanelState'
import type { FileViewerTab, BrowserViewerTab, TerminalViewerTab } from '../../shared/viewer/types'
import { computeLabels } from './viewerTabLabels'

export const createTabUpdates: StateCreator<DetailPanelStore, [], [], Pick<DetailPanelStore,
  'updateBrowserTab' | 'updateTerminalTab' | 'closeTab' | 'setActiveTab' | 'setWordWrap' | 'clearPendingReveal'>> = (set, get) => ({
    updateBrowserTab(threadId, tabId, patch) {
      const state = get()
      const owner = state.getScopeForTab(tabId)
      const threadState = state.getThreadState(threadId, owner?.projectKey)
      const idx = threadState.tabs.findIndex((t) => t.id === tabId && t.kind === 'browser')
      if (idx === -1) return
      const current = threadState.tabs[idx] as BrowserViewerTab
      const nextTab: BrowserViewerTab = {
        ...current,
        ...patch,
        id: current.id,
        kind: 'browser'
      }
      const nextTabs = [...threadState.tabs]
      nextTabs[idx] = nextTab
      const relabeled = computeLabels(nextTabs)
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId, owner?.projectKey), {
          ...threadState,
          tabs: relabeled
        })
        return {
          byThread: next
        }
      })
    },

    updateTerminalTab(threadId, tabId, patch) {
      const state = get()
      const owner = state.getScopeForTab(tabId)
      const threadState = state.getThreadState(threadId, owner?.projectKey)
      const idx = threadState.tabs.findIndex((t) => t.id === tabId && t.kind === 'terminal')
      if (idx === -1) return
      const current = threadState.tabs[idx] as TerminalViewerTab
      const patchEntries = Object.entries(patch) as Array<[keyof TerminalViewerTab, unknown]>
      if (patchEntries.length && patchEntries.every(([key, value]) => {
        const existing = current[key]
        if (Object.is(existing, value)) return true
        if (typeof existing === 'object' && existing !== null && typeof value === 'object' && value !== null) {
          return JSON.stringify(existing) === JSON.stringify(value)
        }
        return false
      })) {
        return
      }
      const nextTab: TerminalViewerTab = {
        ...current,
        ...patch,
        id: current.id,
        kind: 'terminal'
      }
      const nextTabs = [...threadState.tabs]
      nextTabs[idx] = nextTab
      const relabeled = computeLabels(nextTabs)
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId, owner?.projectKey), {
          ...threadState,
          tabs: relabeled
        })
        return {
          byThread: next
        }
      })
    },

    closeTab(threadId, tabId) {
      const state = get()
      const owner = state.getScopeForTab(tabId)
      const threadState = state.getThreadState(threadId, owner?.projectKey)
      const tabs = threadState.tabs
      const idx = tabs.findIndex((t) => t.id === tabId)
      if (idx === -1) return
      const newTabs = computeLabels(tabs.filter((_, i) => i !== idx))
      const nextViewerId = tabs[idx - 1]?.id ?? tabs[idx + 1]?.id ?? null
      let activeDetailTab = threadState.activeDetailTab
      const closingSelected = activeDetailTab.kind === 'viewer' && activeDetailTab.id === tabId
      const activeTabId = closingSelected || threadState.activeTabId === tabId
        ? nextViewerId
        : threadState.activeTabId
      if (closingSelected) {
        activeDetailTab = nextViewerId
          ? { kind: 'viewer', id: nextViewerId }
          : threadState.openSystemTabs.length
            ? {
                kind: 'system',
                id: threadState.openSystemTabs.includes(threadState.lastActiveSystemTab)
                  ? threadState.lastActiveSystemTab
                  : threadState.openSystemTabs.at(-1)!
              }
            : { kind: 'launcher' }
      }
      state.patchPanel({
        tabs: newTabs,
        activeTabId,
        activeDetailTab,
        ...(!newTabs.length && !threadState.openSystemTabs.length
          ? { detailPanelPreferredVisible: false, detailPanelFullView: false }
          : {})
      }, threadId, owner?.projectKey)
    },

    setActiveTab(threadId, tabId) {
      const state = get()
      const owner = state.getScopeForTab(tabId)
      const threadState = state.getThreadState(threadId, owner?.projectKey)
      if (!threadState.tabs.find((t) => t.id === tabId)) return
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId, owner?.projectKey), {
          ...threadState,
          activeTabId: tabId
        })
        return {
          byThread: next
        }
      })
    },

    setWordWrap(threadId, tabId, wordWrap) {
      const state = get()
      const owner = state.getScopeForTab(tabId)
      const threadState = state.getThreadState(threadId, owner?.projectKey)
      const idx = threadState.tabs.findIndex((t) => t.id === tabId && t.kind === 'file')
      if (idx === -1) return
      const current = threadState.tabs[idx] as FileViewerTab
      if (current.wordWrap === wordWrap) return
      const nextTabs = [...threadState.tabs]
      nextTabs[idx] = {
        ...current,
        wordWrap
      }
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId, owner?.projectKey), {
          ...threadState,
          tabs: nextTabs
        })
        return {
          byThread: next
        }
      })
    },

    clearPendingReveal(threadId, tabId, revision) {
      const owner = get().getScopeForTab(tabId)
      const threadState = get().getThreadState(threadId, owner?.projectKey)
      const current = threadState.tabs.find((tab): tab is FileViewerTab => tab.id === tabId && tab.kind === 'file')
      if (current?.pendingReveal?.revision !== revision) return
      const rest: FileViewerTab = {
        ...current
      }
      delete rest.pendingReveal
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId, owner?.projectKey), {
          ...threadState,
          tabs: threadState.tabs.map((tab) => (tab === current ? rest : tab))
        })
        return {
          byThread: next
        }
      })
    },
  })
