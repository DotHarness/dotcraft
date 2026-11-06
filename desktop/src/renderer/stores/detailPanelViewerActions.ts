import type { StateCreator } from 'zustand'
import type { DetailPanelStore } from './detailPanelState'
import type { FilesViewerTab, FileViewerTab, BrowserViewerTab, TerminalViewerTab, WorkflowViewerTab, AutomationViewerTab, SideChatViewerTab } from '../../shared/viewer/types'
import { normalizeBrowserUrl } from '../../shared/viewer/linkResolver'
import { sameFilePath } from '../../shared/workspaceProjectKey'
import { computeLabels, applyFileNavigationHint, revealRequest } from './viewerTabLabels'
let counter = 0
let _runNonce = 0
const nextTabId = (): string => `vtab-${Date.now()}-${++counter}`

export const createViewerActions: StateCreator<DetailPanelStore, [], [], Pick<DetailPanelStore,
  'openFiles' | 'openFile' | 'openBrowser' | 'openTerminal' | 'runInTerminal' | 'openWorkflow' | 'openAutomation' | 'openSideChat' | 'focusBrowserTabByUrl'>> = (set, get) => ({
    openFiles({ threadId, initialLabel }) {
      const threadState = get().getThreadState(threadId)
      const existing = threadState.tabs.find((tab): tab is FilesViewerTab => tab.kind === 'files')
      if (existing) {
        set((s) => {
          const next = new Map(s.byThread)
          next.set(get().scopeKey(threadId), {
            ...threadState,
            activeTabId: existing.id
          })
          return {
            byThread: next
          }
        })
        return existing.id
      }
      const newTab: FilesViewerTab = {
        id: nextTabId(),
        kind: 'files',
        label: initialLabel
      }
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: [...threadState.tabs, newTab],
          activeTabId: newTab.id
        })
        return {
          byThread: next
        }
      })
      return newTab.id
    },

    openFile({
      threadId,
      absolutePath,
      relativePath,
      contentClass,
      sizeBytes,
      forceNew = false,
      navigationHint,
      reveal
    }) {
      const state = get()
      const threadState = state.getThreadState(threadId)
      const existing = forceNew
        ? undefined
        : threadState.tabs.find((t): t is FileViewerTab => t.kind === 'file' && sameFilePath(t.absolutePath, absolutePath))
      const activeFilesTab = !forceNew
        ? threadState.tabs.find((tab) => tab.id === threadState.activeTabId && tab.kind === 'files')
        : undefined
      if (existing) {
        const nextTabs = computeLabels(threadState.tabs.map((tab) => {
          if (tab.id !== existing.id || tab.kind !== 'file') return tab
          return applyFileNavigationHint(tab, navigationHint, reveal)
        }).filter((tab) => tab.id !== activeFilesTab?.id))
        set((s) => {
          const next = new Map(s.byThread)
          next.set(get().scopeKey(threadId), {
            ...threadState,
            tabs: nextTabs,
            activeTabId: existing.id
          })
          return {
            byThread: next
          }
        })
        return existing.id
      }
      const newTab: FileViewerTab = {
        id: activeFilesTab?.id ?? nextTabId(),
        kind: 'file',
        absolutePath,
        relativePath,
        label: relativePath,
        contentClass,
        navigationRevision: 1,
        ...(sizeBytes !== undefined ? {
          sizeBytes
        } : {}),
        ...(navigationHint ? {
          navigationHint: {
            ...navigationHint
          }
        } : {}),
        ...(reveal ? {
          pendingReveal: revealRequest(reveal)
        } : {})
      }
      const newTabs = computeLabels(activeFilesTab
        ? threadState.tabs.map((tab) => tab.id === activeFilesTab.id ? newTab : tab)
        : [...threadState.tabs, newTab])
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: newTabs,
          activeTabId: newTab.id
        })
        return {
          byThread: next
        }
      })
      return newTab.id
    },

    openBrowser({
      threadId,
      tabId,
      target,
      initialUrl = 'about:blank',
      initialLabel = 'New Tab',
      activate = true,
      projectKey,
      browserStorageId
    }) {
      const state = get()
      const threadState = state.getThreadState(threadId, projectKey)
      const requestedId = tabId ?? nextTabId()
      const existing = threadState.tabs.find((tab) => tab.id === requestedId)
      if (existing?.kind === 'browser') {
        const updatedTabs = computeLabels(threadState.tabs.map((tab) => {
          if (tab.id !== requestedId || tab.kind !== 'browser') return tab
          return {
            ...tab,
            target: target ?? tab.target,
            ...(browserStorageId ? { browserStorageId } : {}),
            currentUrl: initialUrl,
            title: initialLabel || tab.title,
            label: initialLabel || tab.label
          }
        }))
        set((s) => {
          const next = new Map(s.byThread)
          next.set(get().scopeKey(threadId, projectKey), {
            ...threadState,
            tabs: updatedTabs,
            activeTabId: activate ? requestedId : threadState.activeTabId
          })
          return {
            byThread: next
          }
        })
        return requestedId
      }
      const newTab: BrowserViewerTab = {
        id: requestedId,
        kind: 'browser',
        browserStorageId: browserStorageId ?? `browser:${crypto.randomUUID()}`,
        target: target ?? `browser-${Date.now()}-${Math.floor(Math.random() * 1_000_000)}`,
        label: initialLabel,
        currentUrl: initialUrl,
        loading: false,
        canGoBack: false,
        canGoForward: false
      }
      const newTabs = computeLabels([...threadState.tabs, newTab])
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId, projectKey), {
          ...threadState,
          tabs: newTabs,
          activeTabId: activate ? newTab.id : threadState.activeTabId
        })
        return {
          byThread: next
        }
      })
      return newTab.id
    },

    openTerminal({ threadId, cwd, initialLabel = 'Terminal' }) {
      const state = get()
      const threadState = state.getThreadState(threadId)
      const newTab: TerminalViewerTab = {
        id: nextTabId(),
        kind: 'terminal',
        label: initialLabel,
        cwd,
        hasStarted: false
      }
      const newTabs = computeLabels([...threadState.tabs, newTab])
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: newTabs,
          activeTabId: newTab.id
        })
        return {
          byThread: next
        }
      })
      return newTab.id
    },

    runInTerminal({ threadId, tabId, cwd, title, command, env }) {
      const threadState = get().getThreadState(threadId)
      const pendingRun = {
        command,
        ...(env ? {
          env
        } : {}),
        nonce: ++_runNonce
      }
      const existing = threadState.tabs.find((tab) => tab.id === tabId && tab.kind === 'terminal')
      const tabs = existing
        ? threadState.tabs.map((tab) => (tab === existing ? {
          ...existing,
          cwd,
          title,
          pendingRun
        } : tab))
        : [...threadState.tabs, {
          id: tabId,
          kind: 'terminal' as const,
          label: title,
          cwd,
          title,
          pendingRun,
          hasStarted: false
        }]
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: computeLabels(tabs),
          activeTabId: tabId
        })
        return {
          byThread: next
        }
      })
      return tabId
    },

    openWorkflow({ threadId, runId, initialLabel }) {
      const threadState = get().getThreadState(threadId)
      const existing = threadState.tabs.find((tab): tab is WorkflowViewerTab =>
        tab.kind === 'workflow' && tab.runId === runId)
      if (existing) {
        set((state) => {
          const next = new Map(state.byThread)
          next.set(get().scopeKey(threadId), {
            ...threadState,
            activeTabId: existing.id
          })
          return {
            byThread: next
          }
        })
        return existing.id
      }
      const newTab: WorkflowViewerTab = {
        id: nextTabId(),
        kind: 'workflow',
        label: initialLabel,
        threadId,
        runId
      }
      set((state) => {
        const next = new Map(state.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: [...threadState.tabs, newTab],
          activeTabId: newTab.id
        })
        return {
          byThread: next
        }
      })
      return newTab.id
    },

    openAutomation({ threadId, automationId, label }) {
      const threadState = get().getThreadState(threadId)
      const existing = threadState.tabs.find((tab): tab is AutomationViewerTab =>
        tab.kind === 'automation' && tab.automationId === automationId)
      const tab: AutomationViewerTab = existing ?? {
        id: nextTabId(),
        kind: 'automation',
        label,
        threadId,
        automationId
      }
      set((state) => {
        const next = new Map(state.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: existing ? threadState.tabs : [...threadState.tabs, tab],
          activeTabId: tab.id
        })
        return {
          byThread: next
        }
      })
      return tab.id
    },

    openSideChat({ threadId, sideChatKey, label }) {
      const threadState = get().getThreadState(threadId)
      const newTab: SideChatViewerTab = {
        id: nextTabId(),
        kind: 'sideChat',
        label,
        threadId,
        sideChatKey
      }
      set((state) => {
        const next = new Map(state.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          tabs: [...threadState.tabs, newTab],
          activeTabId: newTab.id
        })
        return {
          byThread: next
        }
      })
      return newTab.id
    },

    focusBrowserTabByUrl({ threadId, url }) {
      const state = get()
      const threadState = state.getThreadState(threadId)
      const target = normalizeBrowserUrl(url)
      if (!target) return null
      const existing = threadState.tabs.find((tab) => {
        if (tab.kind !== 'browser') return false
        return normalizeBrowserUrl(tab.currentUrl) === target
      })
      if (!existing) return null
      set((s) => {
        const next = new Map(s.byThread)
        next.set(get().scopeKey(threadId), {
          ...threadState,
          activeTabId: existing.id
        })
        return {
          byThread: next
        }
      })
      return existing.id
    },
  })
