/**
 * Label collision resolution (§5.4): when multiple open tabs share the same
 * basename, walk backward up the relative path, appending parent directory
 * segments, until all labels are unique.
 */
import { create } from 'zustand'
import type {
  ViewerTab,
  FilesViewerTab,
  FileViewerTab,
  BrowserViewerTab,
  TerminalViewerTab,
  WorkflowViewerTab,
  ViewerContentClass,
  ViewerKind,
  PerThreadViewerState,
  FileNavigationHint
} from '../../shared/viewer/types'
import { normalizeBrowserUrl } from '../../shared/viewer/linkResolver'

function computeLabels(tabs: ViewerTab[]): ViewerTab[] {
  if (tabs.length === 0) return tabs

  const fileTabs = tabs
    .map((tab, index) => ({ tab, index }))
    .filter((entry): entry is { tab: FileViewerTab; index: number } => entry.tab.kind === 'file')

  if (fileTabs.length === 0) {
    return tabs.map((tab, index) => {
      if (tab.kind === 'browser') return { ...tab, label: browserDefaultLabel(tab) }
      if (tab.kind === 'terminal') return { ...tab, label: terminalDefaultLabel(tabs, index) }
      if (tab.kind === 'workflow') return tab
      return tab
    })
  }

  const basenameMap = new Map<string, number[]>()
  for (const { tab, index } of fileTabs) {
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    const base = parts[parts.length - 1] ?? tab.relativePath
    const existing = basenameMap.get(base)
    if (existing) {
      existing.push(index)
    } else {
      basenameMap.set(base, [index])
    }
  }

  const labels = tabs.map((tab) => {
    if (tab.kind === 'files') return tab.label
    if (tab.kind === 'browser') return browserDefaultLabel(tab)
    if (tab.kind === 'terminal') return tab.label
    if (tab.kind === 'workflow') return tab.label
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    return parts[parts.length - 1] ?? tab.relativePath
  })

  for (const [, indices] of basenameMap.entries()) {
    if (indices.length <= 1) continue

    let depth = 1 // 0 = basename, 1 = parent/basename, ...
    const maxDepth = Math.max(
      ...indices.map((i) => {
        const tab = tabs[i] as FileViewerTab
        return tab.relativePath.replace(/\\/g, '/').split('/').length - 1
      })
    )

    while (depth <= maxDepth) {
      const candidates = indices.map((i) => {
        const tab = tabs[i] as FileViewerTab
        const parts = tab.relativePath.replace(/\\/g, '/').split('/')
        const start = Math.max(0, parts.length - 1 - depth)
        return parts.slice(start).join('/')
      })

      const unique = new Set(candidates).size === candidates.length
      if (unique) {
        for (let j = 0; j < indices.length; j++) {
          labels[indices[j]!] = candidates[j]!
        }
        break
      }
      depth++
    }

    if (depth > maxDepth) {
      for (const i of indices) {
        const tab = tabs[i] as FileViewerTab
        labels[i] = tab.relativePath
      }
    }
  }

  return tabs.map((tab, i) => {
    if (tab.kind === 'browser') {
      return { ...tab, label: labels[i] ?? browserDefaultLabel(tab) }
    }
    if (tab.kind === 'terminal') {
      return { ...tab, label: terminalDefaultLabel(tabs, i) }
    }
    if (tab.kind === 'workflow') return tab
    if (tab.kind === 'files') return tab
    return { ...tab, label: labels[i] ?? tab.label }
  })
}

function browserDefaultLabel(tab: ViewerTab): string {
  if (tab.kind !== 'browser') return tab.label
  if (tab.title?.trim()) return tab.title.trim()
  const url = tab.currentUrl.trim()
  if (!url) return 'New Tab'
  try {
    const parsed = new URL(url)
    return parsed.host || 'New Tab'
  } catch {
    return 'New Tab'
  }
}

function terminalDefaultLabel(tabs: ViewerTab[], tabIndex: number): string {
  const tab = tabs[tabIndex]
  if (!tab || tab.kind !== 'terminal') return 'Terminal'
  const terminalTabs = tabs.filter((item): item is TerminalViewerTab => item.kind === 'terminal')
  const position = terminalTabs.findIndex((item) => item.id === tab.id)
  return position >= 0 ? `Terminal ${position + 1}` : 'Terminal'
}

function applyFileNavigationHint(tab: FileViewerTab, navigationHint?: FileNavigationHint): FileViewerTab {
  const next: FileViewerTab = { ...tab }
  if (navigationHint) {
    next.navigationHint = { ...navigationHint }
  } else {
    delete next.navigationHint
  }
  return next
}

interface ViewerTabStoreState {
  byThread: Map<string, PerThreadViewerState>
  /** Currently active thread ID (mirrors threadStore.activeThreadId). */
  currentThreadId: string | null
  /** Current workspace path — used to scope tab identity. */
  currentWorkspacePath: string | null
}

interface ViewerTabStoreActions {
  /** Opens or focuses the thread's single empty workspace file viewer. */
  openFiles(params: { threadId: string; initialLabel: string }): string

  /** Focuses and returns the existing tab when one already has the same absolutePath. */
  openFile(params: {
    threadId: string
    absolutePath: string
    relativePath: string
    contentClass: ViewerContentClass
    sizeBytes?: number
    kind?: ViewerKind
    forceNew?: boolean
    navigationHint?: FileNavigationHint
  }): string

  openBrowser(params: {
    threadId: string
    tabId?: string
    target?: string
    initialUrl?: string
    initialLabel?: string
    forceNew?: boolean
    activate?: boolean
  }): string

  openTerminal(params: {
    threadId: string
    cwd: string
    initialLabel?: string
  }): string

  openWorkflow(params: { threadId: string; runId: string; initialLabel: string }): string

  /** Focuses an existing browser tab in the thread by normalized current URL. */
  focusBrowserTabByUrl(params: { threadId: string; url: string }): string | null

  updateBrowserTab(threadId: string, tabId: string, patch: Partial<BrowserViewerTab>): void

  updateTerminalTab(threadId: string, tabId: string, patch: Partial<TerminalViewerTab>): void

  /** Closes the tab with `tabId` in `threadId` and selects the nearest neighbor. */
  closeTab(threadId: string, tabId: string): void

  setActiveTab(threadId: string, tabId: string): void

  setWordWrap(threadId: string, tabId: string, wordWrap: boolean): void

  /** Sets the active thread (does not alter tab state). */
  onThreadSwitched(newThreadId: string | null): void

  onThreadDeleted(
    threadId: string,
    options?: {
      onBrowserTabRemoved?: (tab: BrowserViewerTab) => void
      onTerminalTabRemoved?: (tab: TerminalViewerTab) => void
    }
  ): void

  onWorkspaceSwitched(
    newWorkspacePath: string,
    options?: {
      onBrowserTabRemoved?: (tab: BrowserViewerTab) => void
      onTerminalTabRemoved?: (tab: TerminalViewerTab) => void
    }
  ): void

  /** Returns the per-thread viewer state for the given thread (lazy-initialised). */
  getThreadState(threadId: string): PerThreadViewerState

  getCurrentTabs(): ViewerTab[]

  getCurrentActiveTabId(): string | null
}

type ViewerTabStore = ViewerTabStoreState & ViewerTabStoreActions

// Stable empty references for selectors (avoid new object/array per read).
const EMPTY_TABS: ViewerTab[] = Object.freeze([]) as unknown as ViewerTab[]
const EMPTY_THREAD_STATE: PerThreadViewerState = Object.freeze({
  tabs: EMPTY_TABS,
  activeTabId: null
}) as PerThreadViewerState

let _tabIdCounter = 0
function nextTabId(): string {
  return `vtab-${Date.now()}-${++_tabIdCounter}`
}

export const useViewerTabStore = create<ViewerTabStore>((set, get) => ({
  byThread: new Map(),
  currentThreadId: null,
  currentWorkspacePath: null,

  openFiles({ threadId, initialLabel }) {
    const threadState = get().getThreadState(threadId)
    const existing = threadState.tabs.find((tab): tab is FilesViewerTab => tab.kind === 'files')
    if (existing) {
      set((s) => {
        const next = new Map(s.byThread)
        next.set(threadId, { ...threadState, activeTabId: existing.id })
        return { byThread: next }
      })
      return existing.id
    }

    const newTab: FilesViewerTab = { id: nextTabId(), kind: 'files', label: initialLabel }
    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { tabs: [...threadState.tabs, newTab], activeTabId: newTab.id })
      return { byThread: next }
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
    navigationHint
  }) {
    const state = get()
    const threadState = state.getThreadState(threadId)

    const existing = forceNew
      ? undefined
      : threadState.tabs.find((t): t is FileViewerTab => t.kind === 'file' && t.absolutePath === absolutePath)
    const activeFilesTab = !forceNew
      ? threadState.tabs.find((tab) => tab.id === threadState.activeTabId && tab.kind === 'files')
      : undefined
    if (existing && !forceNew) {
      const nextTabs = computeLabels(threadState.tabs.map((tab) => {
        if (tab.id !== existing.id || tab.kind !== 'file') return tab
        return applyFileNavigationHint(tab, navigationHint)
      }).filter((tab) => tab.id !== activeFilesTab?.id))
      set((s) => {
        const next = new Map(s.byThread)
        next.set(threadId, { tabs: nextTabs, activeTabId: existing.id })
        return { byThread: next }
      })
      return existing.id
    }

    const newTab: FileViewerTab = {
      id: activeFilesTab?.id ?? nextTabId(),
      kind: 'file',
      absolutePath,
      relativePath,
      label: relativePath, // will be recomputed by computeLabels
      contentClass,
      ...(sizeBytes !== undefined ? { sizeBytes } : {}),
      ...(navigationHint ? { navigationHint: { ...navigationHint } } : {})
    }

    const newTabs = computeLabels(activeFilesTab
      ? threadState.tabs.map((tab) => tab.id === activeFilesTab.id ? newTab : tab)
      : [...threadState.tabs, newTab])
    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { tabs: newTabs, activeTabId: newTab.id })
      return { byThread: next }
    })

    return newTab.id
  },

  openBrowser({
    threadId,
    tabId,
    target,
    initialUrl = 'about:blank',
    initialLabel = 'New Tab',
    activate = true
  }) {
    const state = get()
    const threadState = state.getThreadState(threadId)
    const requestedId = tabId ?? nextTabId()
    const existing = threadState.tabs.find((tab) => tab.id === requestedId)
    if (existing?.kind === 'browser') {
      const updatedTabs = computeLabels(threadState.tabs.map((tab) => {
        if (tab.id !== requestedId || tab.kind !== 'browser') return tab
        return {
          ...tab,
          target: target ?? tab.target,
          currentUrl: initialUrl,
          title: initialLabel || tab.title,
          label: initialLabel || tab.label
        }
      }))
      set((s) => {
        const next = new Map(s.byThread)
        next.set(threadId, {
          tabs: updatedTabs,
          activeTabId: activate ? requestedId : threadState.activeTabId
        })
        return { byThread: next }
      })
      return requestedId
    }

    const newTab: BrowserViewerTab = {
      id: requestedId,
      kind: 'browser',
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
      next.set(threadId, {
        tabs: newTabs,
        activeTabId: activate ? newTab.id : threadState.activeTabId
      })
      return { byThread: next }
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
      next.set(threadId, { tabs: newTabs, activeTabId: newTab.id })
      return { byThread: next }
    })
    return newTab.id
  },

  openWorkflow({ threadId, runId, initialLabel }) {
    const threadState = get().getThreadState(threadId)
    const existing = threadState.tabs.find((tab): tab is WorkflowViewerTab =>
      tab.kind === 'workflow' && tab.runId === runId)
    if (existing) {
      set((state) => {
        const next = new Map(state.byThread)
        next.set(threadId, { ...threadState, activeTabId: existing.id })
        return { byThread: next }
      })
      return existing.id
    }
    const newTab: WorkflowViewerTab = {
      id: nextTabId(), kind: 'workflow', label: initialLabel, threadId, runId
    }
    set((state) => {
      const next = new Map(state.byThread)
      next.set(threadId, { tabs: [...threadState.tabs, newTab], activeTabId: newTab.id })
      return { byThread: next }
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
      next.set(threadId, { ...threadState, activeTabId: existing.id })
      return { byThread: next }
    })
    return existing.id
  },

  updateBrowserTab(threadId, tabId, patch) {
    const state = get()
    const threadState = state.getThreadState(threadId)
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
      next.set(threadId, { ...threadState, tabs: relabeled })
      return { byThread: next }
    })
  },

  updateTerminalTab(threadId, tabId, patch) {
    const state = get()
    const threadState = state.getThreadState(threadId)
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
      next.set(threadId, { ...threadState, tabs: relabeled })
      return { byThread: next }
    })
  },

  closeTab(threadId, tabId) {
    const state = get()
    const threadState = state.getThreadState(threadId)
    const tabs = threadState.tabs
    const idx = tabs.findIndex((t) => t.id === tabId)
    if (idx === -1) return

    const newTabs = computeLabels(tabs.filter((_, i) => i !== idx))

    let newActiveTabId: string | null = threadState.activeTabId

    if (threadState.activeTabId === tabId) {
      // Nearest-neighbor: prefer left, then right, then fall back to null
      if (idx > 0) {
        newActiveTabId = tabs[idx - 1]!.id
      } else if (idx < tabs.length - 1) {
        newActiveTabId = tabs[idx + 1]!.id
      } else {
        // No more viewer tabs — signal caller to return to last system tab
        newActiveTabId = null
      }
    }

    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { tabs: newTabs, activeTabId: newActiveTabId })
      return { byThread: next }
    })
  },

  setActiveTab(threadId, tabId) {
    const state = get()
    const threadState = state.getThreadState(threadId)
    if (!threadState.tabs.find((t) => t.id === tabId)) return

    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { ...threadState, activeTabId: tabId })
      return { byThread: next }
    })
  },

  setWordWrap(threadId, tabId, wordWrap) {
    const state = get()
    const threadState = state.getThreadState(threadId)
    const idx = threadState.tabs.findIndex((t) => t.id === tabId && t.kind === 'file')
    if (idx === -1) return
    const current = threadState.tabs[idx] as FileViewerTab
    if (current.wordWrap === wordWrap) return

    const nextTabs = [...threadState.tabs]
    nextTabs[idx] = { ...current, wordWrap }
    set((s) => {
      const next = new Map(s.byThread)
      next.set(threadId, { ...threadState, tabs: nextTabs })
      return { byThread: next }
    })
  },

  onThreadSwitched(newThreadId) {
    set({ currentThreadId: newThreadId })
  },

  onThreadDeleted(threadId, options) {
    const existing = get().byThread.get(threadId)
    if (existing?.tabs.length && (options?.onBrowserTabRemoved || options?.onTerminalTabRemoved)) {
      for (const tab of existing.tabs) {
        if (tab.kind === 'browser') {
          options.onBrowserTabRemoved?.(tab)
        } else if (tab.kind === 'terminal') {
          options.onTerminalTabRemoved?.(tab)
        }
      }
    }
    set((s) => {
      const next = new Map(s.byThread)
      next.delete(threadId)
      return { byThread: next }
    })
  },

  onWorkspaceSwitched(newWorkspacePath, options) {
    if (options?.onBrowserTabRemoved || options?.onTerminalTabRemoved) {
      for (const threadState of get().byThread.values()) {
        for (const tab of threadState.tabs) {
          if (tab.kind === 'browser') {
            options.onBrowserTabRemoved?.(tab)
          } else if (tab.kind === 'terminal') {
            options.onTerminalTabRemoved?.(tab)
          }
        }
      }
    }
    set({
      byThread: new Map(),
      currentWorkspacePath: newWorkspacePath
    })
  },

  getThreadState(threadId) {
    const existing = get().byThread.get(threadId)
    if (existing) return existing
    return EMPTY_THREAD_STATE
  },

  getCurrentTabs() {
    const { currentThreadId } = get()
    if (!currentThreadId) return EMPTY_TABS
    return get().getThreadState(currentThreadId).tabs
  },

  getCurrentActiveTabId() {
    const { currentThreadId } = get()
    if (!currentThreadId) return null
    return get().getThreadState(currentThreadId).activeTabId
  }
}))
