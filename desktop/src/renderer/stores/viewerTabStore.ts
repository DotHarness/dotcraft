/**
 * Per-thread viewer tab state store.
 *
 * Each thread independently manages its own list of viewer tabs.
 * Thread lifecycle:
 *  - `openFile`            — opens (or focuses) a file tab in the active thread.
 *  - `closeTab`            — removes a tab; nearest-neighbor fallback for active tab.
 *  - `setActiveTab`        — sets active tab within a thread.
 *  - `onThreadSwitched`    — records the new active thread ID.
 *  - `onThreadDeleted`     — discards all tabs for the deleted thread.
 *  - `onWorkspaceSwitched` — clears all tab state (new workspace = fresh start).
 *
 * Label collision resolution (§5.4):
 *   When multiple open tabs share the same basename, we walk backward up the
 *   relative path, appending parent directory segments, until all labels are unique.
 */
import { create } from 'zustand'
import type {
  ViewerTab,
  FileViewerTab,
  BrowserViewerTab,
  TerminalViewerTab,
  ViewerContentClass,
  ViewerKind,
  PerThreadViewerState,
  FileNavigationHint
} from '../../shared/viewer/types'
import { normalizeBrowserUrl } from '../../shared/viewer/linkResolver'

// ─── Label deduplication helpers ────────────────────────────────────────────

function computeLabels(tabs: ViewerTab[]): ViewerTab[] {
  if (tabs.length === 0) return tabs

  const fileTabs = tabs
    .map((tab, index) => ({ tab, index }))
    .filter((entry): entry is { tab: FileViewerTab; index: number } => entry.tab.kind === 'file')

  if (fileTabs.length === 0) {
    return tabs.map((tab, index) => {
      if (tab.kind === 'browser') return { ...tab, label: browserDefaultLabel(tab) }
      if (tab.kind === 'terminal') return { ...tab, label: terminalDefaultLabel(tabs, index) }
      return tab
    })
  }

  // Build a map: basename → [tab indices with that basename]
  const basenameMap = new Map<string, number[]>()
  for (const { tab, index } of fileTabs) {
    // Normalize separators for consistent splitting
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    const base = parts[parts.length - 1] ?? tab.relativePath
    const existing = basenameMap.get(base)
    if (existing) {
      existing.push(index)
    } else {
      basenameMap.set(base, [index])
    }
  }

  // For non-colliding tabs, the label is simply the basename.
  // For colliding tabs, we extend with parent path segments.
  const labels = tabs.map((tab) => {
    if (tab.kind === 'browser') return browserDefaultLabel(tab)
    if (tab.kind === 'terminal') return tab.label
    const parts = tab.relativePath.replace(/\\/g, '/').split('/')
    return parts[parts.length - 1] ?? tab.relativePath
  })

  for (const [, indices] of basenameMap.entries()) {
    if (indices.length <= 1) continue

    // Resolve collision: keep adding parent segments until all are unique
    let depth = 1 // 0 = basename, 1 = parent/basename, ...
    const maxDepth = Math.max(
      ...indices.map((i) => tabs[i]!.relativePath.replace(/\\/g, '/').split('/').length - 1)
    )

    while (depth <= maxDepth) {
      const candidates = indices.map((i) => {
        const parts = tabs[i]!.relativePath.replace(/\\/g, '/').split('/')
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

    // Fallback: use the full relative path if still not unique after max depth
    if (depth > maxDepth) {
      for (const i of indices) {
        labels[i] = tabs[i]!.relativePath
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

// ─── Store interface ────────────────────────────────────────────────────────

interface ViewerTabStoreState {
  /** Map from threadId → per-thread viewer state. */
  byThread: Map<string, PerThreadViewerState>
  /** Currently active thread ID (mirrors threadStore.activeThreadId). */
  currentThreadId: string | null
  /** Current workspace path — used to scope tab identity. */
  currentWorkspacePath: string | null
}

interface ViewerTabStoreActions {
  /**
   * Opens a file tab in the given thread.
   * If an identical tab (same absolutePath) already exists, focuses it and returns its id.
   * Otherwise, creates a new tab and activates it.
   * Returns the tab ID.
   */
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

  /** Opens a browser tab in the given thread. */
  openBrowser(params: {
    threadId: string
    tabId?: string
    target?: string
    initialUrl?: string
    initialLabel?: string
    forceNew?: boolean
    activate?: boolean
  }): string

  /** Opens a terminal tab in the given thread. */
  openTerminal(params: {
    threadId: string
    cwd: string
    initialLabel?: string
  }): string

  /** Focuses an existing browser tab in the thread by normalized current URL. */
  focusBrowserTabByUrl(params: { threadId: string; url: string }): string | null

  /** Applies browser state updates to an existing browser tab. */
  updateBrowserTab(threadId: string, tabId: string, patch: Partial<BrowserViewerTab>): void

  /** Applies terminal state updates to an existing terminal tab. */
  updateTerminalTab(threadId: string, tabId: string, patch: Partial<TerminalViewerTab>): void

  /** Closes the tab with `tabId` in `threadId` and selects the nearest neighbor. */
  closeTab(threadId: string, tabId: string): void

  /** Activates an existing tab in `threadId`. */
  setActiveTab(threadId: string, tabId: string): void

  /** Sets the active thread (does not alter tab state). */
  onThreadSwitched(newThreadId: string | null): void

  /** Removes all viewer tabs for the given thread (e.g., thread deleted). */
  onThreadDeleted(
    threadId: string,
    options?: {
      onBrowserTabRemoved?: (tab: BrowserViewerTab) => void
      onTerminalTabRemoved?: (tab: TerminalViewerTab) => void
    }
  ): void

  /** Clears all per-thread state when the workspace changes. */
  onWorkspaceSwitched(
    newWorkspacePath: string,
    options?: {
      onBrowserTabRemoved?: (tab: BrowserViewerTab) => void
      onTerminalTabRemoved?: (tab: TerminalViewerTab) => void
    }
  ): void

  /** Returns the per-thread viewer state for the given thread (lazy-initialised). */
  getThreadState(threadId: string): PerThreadViewerState

  /** Returns current thread's tabs (convenience selector for UI components). */
  getCurrentTabs(): ViewerTab[]

  /** Returns current thread's active tab ID. */
  getCurrentActiveTabId(): string | null
}

type ViewerTabStore = ViewerTabStoreState & ViewerTabStoreActions

// Stable empty references for selectors (avoid new object/array per read).
const EMPTY_TABS: ViewerTab[] = Object.freeze([]) as unknown as ViewerTab[]
const EMPTY_THREAD_STATE: PerThreadViewerState = Object.freeze({
  tabs: EMPTY_TABS,
  activeTabId: null
}) as PerThreadViewerState

// ─── Counter for unique IDs ───────────────────────────────────────────────────

let _tabIdCounter = 0
function nextTabId(): string {
  return `vtab-${Date.now()}-${++_tabIdCounter}`
}

// ─── Store implementation ────────────────────────────────────────────────────

export const useViewerTabStore = create<ViewerTabStore>((set, get) => ({
  byThread: new Map(),
  currentThreadId: null,
  currentWorkspacePath: null,

  openFile({
    threadId,
    absolutePath,
    relativePath,
    contentClass,
    sizeBytes,
    kind = 'file',
    forceNew = false,
    navigationHint
  }) {
    const state = get()
    const threadState = state.getThreadState(threadId)

    // Deduplication: if a tab with the same absolutePath already exists, focus it
    const existing = forceNew
      ? undefined
      : threadState.tabs.find((t) => t.kind === kind && t.absolutePath === absolutePath)
    if (existing && !forceNew) {
      set((s) => {
        const next = new Map(s.byThread)
        next.set(threadId, { ...threadState, activeTabId: existing.id })
        return { byThread: next }
      })
      return existing.id
    }

    // Create new tab
    const newTab: ViewerTab = {
      id: nextTabId(),
      kind: kind === 'browser' ? 'file' : kind,
      absolutePath,
      relativePath,
      label: relativePath, // will be recomputed by computeLabels
      contentClass,
      ...(sizeBytes !== undefined ? { sizeBytes } : {}),
      ...(navigationHint ? { navigationHint } : {})
    }

    const newTabs = computeLabels([...threadState.tabs, newTab])
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
