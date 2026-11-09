import { create } from 'zustand'
import type { ThreadSummary, Thread, ThreadStatus, ThreadRuntimeSnapshot, ThreadGoal, ThreadReadState } from '../types/thread'
import { useDetailPanelStore } from './detailPanelStore'
import { useComposerDraftStore } from './composerDraftStore'
import { discardRemovedVoiceOrigins } from '../voice/voiceOriginCleanupBridge'
import { getSubAgentParentThreadId, isSubAgentThread } from '../utils/subAgentThreads'
import { isHiddenThread } from '../../shared/hiddenThreads'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { ApplyRuntimeSnapshotOptions } from './threadRuntimeState'
import { retainedThreadActions, type ParkedApproval, type ParkedUserInput } from './retainedThreadState'
import { retainedThreadIds } from './threadConnectionStore'
import { useSubAgentStore } from './subAgentStore'
export { readStateChangedSince, readStateMark } from './retainedThreadState'
export type { ParkedApproval, ParkedUserInput } from './retainedThreadState'
import { normalizeThreadSummaryRuntime, syncActiveThreadRuntime } from '../utils/threadRuntime'
export type { ThreadRuntimeSnapshot } from '../types/thread'

interface ThreadStoreState {
  threadList: ThreadSummary[]
  threadListProjectKey: string | null
  activeThreadId: string | null
  activeThread: Thread | null
  activeThreadLoadError: { threadId: string; message: string } | null
  searchQuery: string
  loading: boolean

  runningTurnThreadIds: Set<string>

  parkedApprovals: Map<string, ParkedApproval[]>
  parkedUserInputs: Map<string, ParkedUserInput>

  runtimeSnapshots: Map<string, ThreadRuntimeSnapshot>

  pendingApprovalThreadIds: Set<string>
  pendingUserInputThreadIds: Set<string>

  pendingPlanConfirmationThreadIds: Set<string>

  goalSnapshots: Map<string, ThreadGoal>
  hiddenThreadIds: Set<string>

  pinnedThreadIds: string[]
  pinnedThreadWorkspacePath: string | null
}

interface ThreadStoreActions {
  setThreadList(threads: ThreadSummary[], projectKey?: string | null): void
  addThread(thread: ThreadSummary): void
  upsertThreads(threads: ThreadSummary[], runtimeBaseline?: ReadonlyMap<string, ThreadRuntimeSnapshot>): void
  updateThreadStatus(threadId: string, newStatus: ThreadStatus): void
  removeThread(threadId: string): void
  removeThreadTree(rootThreadId: string): void
  renameThread(threadId: string, displayName: string): void
  setActiveThreadId(id: string | null): void
  setActiveThread(thread: Thread | null): void
  setActiveThreadLoadError(threadId: string, message: string): void
  setSearchQuery(query: string): void
  setLoading(loading: boolean): void
  markTurnStarted(threadId: string): void
  markTurnEnded(threadId: string): void
  parkApproval(threadId: string, approval: ParkedApproval): void
  consumeParkedApproval(threadId: string): ParkedApproval | null
  consumeParkedApprovals(threadId: string): ParkedApproval[]
  clearParkedApproval(threadId: string): void
  parkUserInput(threadId: string, request: ParkedUserInput): void
  consumeParkedUserInput(threadId: string): ParkedUserInput | null
  clearParkedUserInput(threadId: string): void
  applyRuntimeSnapshot(threadId: string, runtime: ThreadRuntimeSnapshot, options: ApplyRuntimeSnapshotOptions): void
  setThreadReadState(threadId: string, readState: ThreadReadState): void
  setThreadGoal(goal: ThreadGoal): void
  clearThreadGoal(threadId: string): void
  hydrateThreadGoal(threadId: string, goal: ThreadGoal | null | undefined): void
  hydratePinnedThreadIds(workspacePath: string, threadIds: string[]): void
  togglePinnedThread(threadId: string): void
  removePinnedThread(threadId: string): void
  prunePinnedThreadIds(): void
  resetForeground(): void
  releaseRetainedThread(threadId: string): void
  reset(): void
}

export interface ThreadStore extends ThreadStoreState, ThreadStoreActions {}

const initialState: ThreadStoreState = {
  threadList: [],
  threadListProjectKey: null,
  activeThreadId: null,
  activeThread: null,
  activeThreadLoadError: null,
  searchQuery: '',
  loading: false,
  runningTurnThreadIds: new Set<string>(),
  parkedApprovals: new Map<string, ParkedApproval[]>(),
  parkedUserInputs: new Map<string, ParkedUserInput>(),
  runtimeSnapshots: new Map<string, ThreadRuntimeSnapshot>(),
  pendingApprovalThreadIds: new Set<string>(),
  pendingUserInputThreadIds: new Set<string>(),
  pendingPlanConfirmationThreadIds: new Set<string>(),
  goalSnapshots: new Map<string, ThreadGoal>(),
  hiddenThreadIds: new Set<string>(),
  pinnedThreadIds: [],
  pinnedThreadWorkspacePath: null
}

function trackHiddenThreads(current: Set<string>, threads: readonly ThreadSummary[]): Set<string> {
  let next = current
  for (const thread of threads) {
    const hidden = isHiddenThread(thread)
    if (hidden === next.has(thread.id)) continue
    if (next === current) next = new Set(current)
    if (hidden) next.add(thread.id)
    else next.delete(thread.id)
  }
  return next
}

function filterSetToThreadList(current: Set<string>, ids: Set<string>): Set<string> {
  return new Set([...current].filter((id) => ids.has(id)))
}

function filterMapToThreadList<T>(current: Map<string, T>, ids: Set<string>): Map<string, T> {
  return new Map([...current].filter(([id]) => ids.has(id)))
}

function firstNonEmpty(...values: Array<string | null | undefined>): string | undefined {
  for (const value of values) {
    const trimmed = value?.trim()
    if (trimmed) return trimmed
  }
  return undefined
}

function normalizeIncomingWorkspaceState<T extends ThreadSummary>(
  incoming: T,
  existing: ThreadSummary | null | undefined
): T {
  if (!existing?.worktree || Object.hasOwn(incoming, 'worktree')) return incoming

  const incomingWorkspacePath = firstNonEmpty(incoming.workspacePath)
  const incomingEffectiveWorkspacePath = firstNonEmpty(incoming.effectiveWorkspacePath)
  if (!incomingWorkspacePath && !incomingEffectiveWorkspacePath) return incoming

  const localWorkspacePath = firstNonEmpty(
    incomingWorkspacePath,
    existing.worktree.workspacePath,
    existing.worktree.sourceWorkspacePath,
    existing.workspacePath
  )
  const effectiveWorkspacePath = firstNonEmpty(incomingEffectiveWorkspacePath, incomingWorkspacePath)
  if (!localWorkspacePath || !effectiveWorkspacePath) return incoming

  if (
    normalizeWorkspaceProjectKey(effectiveWorkspacePath) !==
    normalizeWorkspaceProjectKey(localWorkspacePath)
  ) {
    return incoming
  }

  return {
    ...incoming,
    effectiveWorkspacePath,
    worktree: null
  }
}

function mergeThreadSummary(existing: ThreadSummary, incoming: ThreadSummary): ThreadSummary {
  return { ...existing, ...normalizeIncomingWorkspaceState(incoming, existing) }
}

export function collectThreadTreeIds(threads: ThreadSummary[], rootThreadId: string): Set<string> {
  const ids = new Set<string>([rootThreadId])
  let changed = true
  while (changed) {
    changed = false
    for (const thread of threads) {
      if (ids.has(thread.id)) continue
      const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
      if (parentId && ids.has(parentId)) {
        ids.add(thread.id)
        changed = true
      }
    }
  }
  return ids
}

function normalizePinnedThreadIds(threadIds: Iterable<string>): string[] {
  const seen = new Set<string>()
  const normalized: string[] = []
  for (const value of threadIds) {
    const id = value.trim()
    if (!id || seen.has(id)) continue
    seen.add(id)
    normalized.push(id)
  }
  return normalized
}

function getPinnableThreadIds(threads: ThreadSummary[]): Set<string> {
  return new Set(
    threads
      .filter((thread) => thread.status !== 'archived' && !isSubAgentThread(thread))
      .map((thread) => thread.id)
  )
}

function persistPinnedThreadIds(workspacePath: string | null, threadIds: string[]): void {
  const workspace = workspacePath?.trim()
  if (!workspace || typeof window === 'undefined') return
  const workspaceKey = normalizeWorkspaceProjectKey(workspace)
  if (!workspaceKey) return

  void window.api?.settings
    ?.set({ pinnedThreadIdsByWorkspace: { [workspaceKey]: threadIds } })
    .catch((err: unknown) => console.error('settings:set pinnedThreadIdsByWorkspace failed:', err))
}

function disposeViewerTabsForThread(threadId: string): void {
  useDetailPanelStore.getState().onThreadDeleted(threadId, {
    onBrowserTabRemoved: (tab) => {
      void window.api.workspace.viewer.browser.destroy({ tabId: tab.id })
    },
    onTerminalTabRemoved: (tab) => {
      void window.api.workspace.viewer.terminal.dispose({ tabId: tab.id })
    }
  })
}

export const useThreadStore = create<ThreadStore>((set, _get) => ({
  ...initialState,

  setThreadList(threads, projectKey = null) {
    const nextProjectKey = normalizeWorkspaceProjectKey(projectKey)
    set((state) => {
      const visibleThreads = threads.filter((thread) => !isHiddenThread(thread)).map(normalizeThreadSummaryRuntime)
      const threadListProjectKey = nextProjectKey
      const retained = new Set(retainedThreadIds())
      const childrenByParent = useSubAgentStore.getState().childrenByParent
      for (const parentId of retained) {
        for (const child of childrenByParent.get(parentId) ?? []) retained.add(child.childThreadId)
      }
      const threadIds = new Set([...visibleThreads.map((thread) => thread.id), ...retained])
      if (state.threadListProjectKey === (nextProjectKey || null)) {
        for (const child of state.threadList) {
          const rootId = child.source?.subAgent?.rootThreadId ?? getSubAgentParentThreadId(child)
          if (isSubAgentThread(child) && rootId && threadIds.has(rootId) && !threadIds.has(child.id)) {
            visibleThreads.push(child)
            threadIds.add(child.id)
          }
        }
      }
      const runtimeSnapshots = filterMapToThreadList(state.runtimeSnapshots, threadIds)
      const parkedApprovals = filterMapToThreadList(state.parkedApprovals, threadIds)
      const parkedUserInputs = filterMapToThreadList(state.parkedUserInputs, threadIds)
      const runningTurnThreadIds = filterSetToThreadList(state.runningTurnThreadIds, threadIds)
      const pendingApprovalThreadIds = filterSetToThreadList(state.pendingApprovalThreadIds, threadIds)
      const pendingUserInputThreadIds = filterSetToThreadList(state.pendingUserInputThreadIds, threadIds)
      const pendingPlanConfirmationThreadIds = filterSetToThreadList(
        state.pendingPlanConfirmationThreadIds,
        threadIds
      )
      const goalSnapshots = filterMapToThreadList(state.goalSnapshots, threadIds)

      for (const thread of visibleThreads) {
        if (retained.has(thread.id)) continue
        if (thread.goal === null) {
          goalSnapshots.delete(thread.id)
        } else if (thread.goal) {
          goalSnapshots.set(thread.id, thread.goal)
        }

        const snapshot = thread.runtime
        if (!snapshot) continue
        const isDesktopOrigin = thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
        runtimeSnapshots.set(thread.id, snapshot)

        if (snapshot.running) {
          runningTurnThreadIds.add(thread.id)
        } else {
          runningTurnThreadIds.delete(thread.id)
        }

        if (snapshot.waitingOnApproval && isDesktopOrigin) {
          pendingApprovalThreadIds.add(thread.id)
        } else {
          pendingApprovalThreadIds.delete(thread.id)
          if (!snapshot.waitingOnApproval) {
            parkedApprovals.delete(thread.id)
          }
        }

        if (snapshot.waitingOnInput && isDesktopOrigin) {
          pendingUserInputThreadIds.add(thread.id)
        } else {
          pendingUserInputThreadIds.delete(thread.id)
          if (!snapshot.waitingOnInput) {
            parkedUserInputs.delete(thread.id)
          }
        }

        if (snapshot.waitingOnPlanConfirmation && isDesktopOrigin) {
          pendingPlanConfirmationThreadIds.add(thread.id)
        } else {
          pendingPlanConfirmationThreadIds.delete(thread.id)
        }
      }

      return {
        threadList: visibleThreads,
        activeThread: syncActiveThreadRuntime(state.activeThread, visibleThreads),
        threadListProjectKey: threadListProjectKey || null,
        hiddenThreadIds: trackHiddenThreads(state.hiddenThreadIds, threads),
        runtimeSnapshots,
        runningTurnThreadIds,
        parkedApprovals,
        parkedUserInputs,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        goalSnapshots
      }
    })
  },

  addThread(thread) {
    thread = normalizeThreadSummaryRuntime(thread)
    set((state) => {
      const hiddenThreadIds = trackHiddenThreads(state.hiddenThreadIds, [thread])
      if (isHiddenThread(thread) || state.threadList.some((t) => t.id === thread.id)) {
        return hiddenThreadIds === state.hiddenThreadIds ? state : { hiddenThreadIds }
      }
      const goalSnapshots = new Map(state.goalSnapshots)
      if (thread.goal === null) {
        goalSnapshots.delete(thread.id)
      } else if (thread.goal) {
        goalSnapshots.set(thread.id, thread.goal)
      }
      const runtime = state.runtimeSnapshots.get(thread.id) ?? thread.runtime
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      if (runtime) runtimeSnapshots.set(thread.id, runtime)
      return { threadList: [{ ...thread, runtime }, ...state.threadList], runtimeSnapshots, goalSnapshots, hiddenThreadIds }
    })
  },

  upsertThreads(threads, runtimeBaseline) {
    const internalThreadIds = new Set(threads.filter((thread) => isHiddenThread(thread)).map((thread) => thread.id))
    if (threads.length === 0) return
    set((state) => {
      const visibleThreads = threads.filter((thread) => !isHiddenThread(thread)).map((thread) => {
        const current = state.runtimeSnapshots.get(thread.id)
        return normalizeThreadSummaryRuntime(runtimeBaseline && current !== runtimeBaseline.get(thread.id)
          ? { ...thread, runtime: current }
          : thread)
      })
      const incoming = new Map(visibleThreads.map((thread) => [thread.id, thread]))
      const seen = new Set<string>()
      const existingThreadList = internalThreadIds.size > 0
        ? state.threadList.filter((thread) => !internalThreadIds.has(thread.id))
        : state.threadList
      const threadList = existingThreadList.map((thread) => {
        const next = incoming.get(thread.id)
        if (!next) return thread
        seen.add(thread.id)
        return mergeThreadSummary(thread, next)
      })
      const missing = visibleThreads.filter((thread) => !seen.has(thread.id))
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      const parkedApprovals = new Map(state.parkedApprovals)
      const parkedUserInputs = new Map(state.parkedUserInputs)
      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      const goalSnapshots = new Map(state.goalSnapshots)

      for (const id of internalThreadIds) {
        runtimeSnapshots.delete(id)
        parkedApprovals.delete(id)
        parkedUserInputs.delete(id)
        runningTurnThreadIds.delete(id)
        pendingApprovalThreadIds.delete(id)
        pendingUserInputThreadIds.delete(id)
        pendingPlanConfirmationThreadIds.delete(id)
        goalSnapshots.delete(id)
      }

      for (const thread of visibleThreads) {
        if (thread.goal === null) {
          goalSnapshots.delete(thread.id)
        } else if (thread.goal) {
          goalSnapshots.set(thread.id, thread.goal)
        }

        const snapshot = thread.runtime
        if (!snapshot) continue
        runtimeSnapshots.set(thread.id, snapshot)
        if (snapshot.running) {
          runningTurnThreadIds.add(thread.id)
        } else {
          runningTurnThreadIds.delete(thread.id)
        }
      }

      return {
        threadList: [...missing, ...threadList],
        activeThread: syncActiveThreadRuntime(state.activeThread, visibleThreads),
        hiddenThreadIds: trackHiddenThreads(state.hiddenThreadIds, threads),
        runtimeSnapshots,
        parkedApprovals,
        parkedUserInputs,
        runningTurnThreadIds,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        goalSnapshots
      }
    })
  },

  updateThreadStatus(threadId, newStatus) {
    set((state) => ({
      threadList: state.threadList.map((t) =>
        t.id === threadId ? { ...t, status: newStatus } : t
      ),
      activeThread:
        state.activeThread?.id === threadId
          ? { ...state.activeThread, status: newStatus }
          : state.activeThread
    }))
  },

  removeThread(threadId) {
    discardRemovedVoiceOrigins([threadId])
    disposeViewerTabsForThread(threadId)
    useComposerDraftStore.getState().clearDraft(threadId)
    _get().removePinnedThread(threadId)
    set((state) => {
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.delete(threadId)
      const parkedUserInputs = new Map(state.parkedUserInputs)
      parkedUserInputs.delete(threadId)
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      runtimeSnapshots.delete(threadId)
      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      pendingApprovalThreadIds.delete(threadId)
      const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
      pendingUserInputThreadIds.delete(threadId)
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.delete(threadId)
      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      runningTurnThreadIds.delete(threadId)
      const goalSnapshots = new Map(state.goalSnapshots)
      goalSnapshots.delete(threadId)
      return {
        threadList: state.threadList.filter((t) => t.id !== threadId),
        activeThreadId:
          state.activeThreadId === threadId ? null : state.activeThreadId,
        activeThread:
          state.activeThread?.id === threadId ? null : state.activeThread,
        runningTurnThreadIds,
        parkedApprovals,
        parkedUserInputs,
        runtimeSnapshots,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        goalSnapshots
      }
    })
  },

  removeThreadTree(rootThreadId) {
    const pinnedTreeIds = collectThreadTreeIds(_get().threadList, rootThreadId)
    discardRemovedVoiceOrigins([...pinnedTreeIds])
    for (const id of pinnedTreeIds) {
      _get().removePinnedThread(id)
      useComposerDraftStore.getState().clearDraft(id)
    }

    set((state) => {
      const treeIds = collectThreadTreeIds(state.threadList, rootThreadId)
      for (const id of treeIds) {
        disposeViewerTabsForThread(id)
      }

      const parkedApprovals = new Map(state.parkedApprovals)
      const parkedUserInputs = new Map(state.parkedUserInputs)
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      const goalSnapshots = new Map(state.goalSnapshots)
      for (const id of treeIds) {
        parkedApprovals.delete(id)
        parkedUserInputs.delete(id)
        runtimeSnapshots.delete(id)
        pendingApprovalThreadIds.delete(id)
        pendingUserInputThreadIds.delete(id)
        pendingPlanConfirmationThreadIds.delete(id)
        runningTurnThreadIds.delete(id)
        goalSnapshots.delete(id)
      }

      return {
        threadList: state.threadList.filter((t) => !treeIds.has(t.id)),
        activeThreadId:
          state.activeThreadId && treeIds.has(state.activeThreadId) ? null : state.activeThreadId,
        activeThread:
          state.activeThread && treeIds.has(state.activeThread.id) ? null : state.activeThread,
        runningTurnThreadIds,
        parkedApprovals,
        parkedUserInputs,
        runtimeSnapshots,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        goalSnapshots
      }
    })
  },

  renameThread(threadId, displayName) {
    set((state) => ({
      threadList: state.threadList.map((t) =>
        t.id === threadId ? { ...t, displayName } : t
      ),
      activeThread:
        state.activeThread?.id === threadId
          ? { ...state.activeThread, displayName }
          : state.activeThread
    }))
  },

  setActiveThreadId(id) {
    set({ activeThreadId: id, activeThreadLoadError: null })
  },

  setActiveThread(thread) {
    // Do not sync activeThreadId here — selection is user-driven; stale thread/read
    // responses must not redirect which thread is selected.
    set((state) => {
      if (!thread) return { activeThread: thread }
      const incomingTurns = Array.isArray(thread.turns) ? thread.turns : []
      const normalizedThread = normalizeThreadSummaryRuntime(normalizeIncomingWorkspaceState(thread, state.activeThread))
      const activeThread = state.activeThread?.id === thread.id
        && incomingTurns.length === 0
        && state.activeThread.turns.length > 0
        ? {
            ...state.activeThread,
            ...normalizedThread,
            turns: state.activeThread.turns,
            queuedInputs: normalizedThread.queuedInputs ?? state.activeThread.queuedInputs
          }
        : { ...normalizedThread, turns: incomingTurns }
      const goalSnapshots = new Map(state.goalSnapshots)
      if (activeThread.goal === null) {
        goalSnapshots.delete(activeThread.id)
      } else if (activeThread.goal) {
        goalSnapshots.set(activeThread.id, activeThread.goal)
      }
      return {
        activeThread,
        activeThreadLoadError: state.activeThreadLoadError?.threadId === thread.id ? null : state.activeThreadLoadError,
        goalSnapshots,
        hiddenThreadIds: trackHiddenThreads(state.hiddenThreadIds, [thread])
      }
    })
  },

  setActiveThreadLoadError(threadId, message) {
    set((state) => (state.activeThreadId === threadId ? { activeThreadLoadError: { threadId, message } } : {}))
  },

  setSearchQuery(query) {
    set({ searchQuery: query })
  },

  setLoading(loading) {
    set({ loading })
  },

  ...retainedThreadActions(set, _get),

  hydratePinnedThreadIds(workspacePath, threadIds) {
    set({
      pinnedThreadWorkspacePath: workspacePath,
      pinnedThreadIds: normalizePinnedThreadIds(threadIds)
    })
  },

  togglePinnedThread(threadId) {
    const id = threadId.trim()
    if (!id) return

    let workspacePath: string | null = null
    let nextPinnedThreadIds: string[] | null = null
    set((state) => {
      if (!state.pinnedThreadWorkspacePath) return state
      if (!getPinnableThreadIds(state.threadList).has(id)) return state

      const current = normalizePinnedThreadIds(state.pinnedThreadIds)
      const pinnedThreadIds = current.includes(id)
        ? current.filter((existing) => existing !== id)
        : [id, ...current]
      workspacePath = state.pinnedThreadWorkspacePath
      nextPinnedThreadIds = pinnedThreadIds
      return { pinnedThreadIds }
    })

    if (nextPinnedThreadIds) {
      persistPinnedThreadIds(workspacePath, nextPinnedThreadIds)
    }
  },

  removePinnedThread(threadId) {
    const id = threadId.trim()
    if (!id) return

    let workspacePath: string | null = null
    let nextPinnedThreadIds: string[] | null = null
    set((state) => {
      if (!state.pinnedThreadIds.includes(id)) return state
      const pinnedThreadIds = state.pinnedThreadIds.filter((existing) => existing !== id)
      workspacePath = state.pinnedThreadWorkspacePath
      nextPinnedThreadIds = pinnedThreadIds
      return { pinnedThreadIds }
    })

    if (nextPinnedThreadIds) {
      persistPinnedThreadIds(workspacePath, nextPinnedThreadIds)
    }
  },

  prunePinnedThreadIds() {
    let workspacePath: string | null = null
    let nextPinnedThreadIds: string[] | null = null
    set((state) => {
      const pinnableThreadIds = getPinnableThreadIds(state.threadList)
      const pinnedThreadIds = state.pinnedThreadIds.filter((id) => pinnableThreadIds.has(id))
      if (pinnedThreadIds.length === state.pinnedThreadIds.length) return state
      workspacePath = state.pinnedThreadWorkspacePath
      nextPinnedThreadIds = pinnedThreadIds
      return { pinnedThreadIds }
    })

    if (nextPinnedThreadIds) {
      persistPinnedThreadIds(workspacePath, nextPinnedThreadIds)
    }
  },

  resetForeground() {
    set({ threadList: [], threadListProjectKey: null, activeThreadId: null, activeThread: null,
      activeThreadLoadError: null, searchQuery: '', loading: false,
      pinnedThreadIds: [], pinnedThreadWorkspacePath: null })
  },

  reset() {
    set({
      ...initialState,
      runningTurnThreadIds: new Set<string>(),
      parkedApprovals: new Map<string, ParkedApproval[]>(),
      parkedUserInputs: new Map<string, ParkedUserInput>(),
      runtimeSnapshots: new Map<string, ThreadRuntimeSnapshot>(),
      pendingApprovalThreadIds: new Set<string>(),
      pendingUserInputThreadIds: new Set<string>(),
      pendingPlanConfirmationThreadIds: new Set<string>(),
      goalSnapshots: new Map<string, ThreadGoal>(),
      hiddenThreadIds: new Set<string>(),
      pinnedThreadIds: [],
      pinnedThreadWorkspacePath: null
    })
  }
}))

export function selectFilteredThreads(state: ThreadStore): ThreadSummary[] {
  const visible = state.threadList.filter((t) => t.status !== 'archived' && !isSubAgentThread(t))
  if (!state.searchQuery.trim()) return visible
  const q = state.searchQuery.toLowerCase()
  return visible.filter((t) => (t.displayName ?? '').toLowerCase().includes(q))
}
