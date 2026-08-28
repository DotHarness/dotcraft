import { create } from 'zustand'
import type { ThreadSummary, Thread, ThreadStatus, ThreadRuntimeSnapshot, ThreadGoal } from '../types/thread'
import { useViewerTabStore } from './viewerTabStore'
import { useComposerDraftStore } from './composerDraftStore'
import { discardRemovedVoiceOrigins } from '../voice/voiceOriginCleanupBridge'
import { getSubAgentParentThreadId, isSubAgentThread } from '../utils/subAgentThreads'
import { isInternalThread } from '../utils/internalThreads'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
export type { ThreadRuntimeSnapshot } from '../types/thread'

export interface ParkedApproval {
  bridgeId: string
  turnId: string | null
  rawParams: Record<string, unknown>
}

export interface ParkedUserInput {
  bridgeId: string
  turnId: string | null
  rawParams: Record<string, unknown>
}

interface ApplyRuntimeSnapshotOptions {
  isActive: boolean
  isDesktopOrigin: boolean
}

interface ThreadStoreState {
  threadList: ThreadSummary[]
  threadListProjectKey: string | null
  activeThreadId: string | null
  activeThread: Thread | null
  searchQuery: string
  loading: boolean
  /** Set of threadIds that currently have a running turn (for activity indicator). */
  runningTurnThreadIds: Set<string>
  /** Background-thread approvals waiting for user to return to that thread. */
  parkedApprovals: Map<string, ParkedApproval[]>
  parkedUserInputs: Map<string, ParkedUserInput>
  /** Lightweight per-thread runtime snapshot from workspace-level broadcasts. */
  runtimeSnapshots: Map<string, ThreadRuntimeSnapshot>
  /** Threads that should show "awaiting approval" badge in sidebar. */
  pendingApprovalThreadIds: Set<string>
  pendingUserInputThreadIds: Set<string>
  /** Threads with pending plan confirmation shortcut in conversation view. */
  pendingPlanConfirmationThreadIds: Set<string>
  /** Threads that completed in background and have not been visited yet. */
  unreadCompletedThreadIds: Set<string>
  /** Best-effort current goal snapshots, keyed by thread id. */
  goalSnapshots: Map<string, ThreadGoal>
  /** Desktop-local pinned top-level thread ids for the current workspace. */
  pinnedThreadIds: string[]
  pinnedThreadWorkspacePath: string | null
  activeHistoryCursors: { threadId: string; turnCursor: string | null } | null
}

interface ThreadStoreActions {
  setThreadList(threads: ThreadSummary[], projectKey?: string | null): void
  /** Prepend a new thread to the list (newest first). No-op if the same id already exists. */
  addThread(thread: ThreadSummary): void
  /** Insert or refresh thread summaries without replacing the whole list. */
  upsertThreads(threads: ThreadSummary[]): void
  updateThreadStatus(threadId: string, newStatus: ThreadStatus): void
  removeThread(threadId: string): void
  removeThreadTree(rootThreadId: string): void
  renameThread(threadId: string, displayName: string): void
  setActiveThreadId(id: string | null): void
  setActiveThread(thread: Thread | null): void
  setActiveHistoryCursors(threadId: string, turnCursor: string | null): void
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
  markPlanConfirmationPending(threadId: string): void
  clearPlanConfirmationPending(threadId: string): void
  markUnreadCompleted(threadId: string): void
  clearUnreadCompleted(threadId: string): void
  setThreadGoal(goal: ThreadGoal): void
  clearThreadGoal(threadId: string): void
  hydrateThreadGoal(threadId: string, goal: ThreadGoal | null | undefined): void
  hydratePinnedThreadIds(workspacePath: string, threadIds: string[]): void
  togglePinnedThread(threadId: string): void
  removePinnedThread(threadId: string): void
  prunePinnedThreadIds(): void
  reset(): void
}

export interface ThreadStore extends ThreadStoreState, ThreadStoreActions {}

const initialState: ThreadStoreState = {
  threadList: [],
  threadListProjectKey: null,
  activeThreadId: null,
  activeThread: null,
  searchQuery: '',
  loading: false,
  runningTurnThreadIds: new Set<string>(),
  parkedApprovals: new Map<string, ParkedApproval[]>(),
  parkedUserInputs: new Map<string, ParkedUserInput>(),
  runtimeSnapshots: new Map<string, ThreadRuntimeSnapshot>(),
  pendingApprovalThreadIds: new Set<string>(),
  pendingUserInputThreadIds: new Set<string>(),
  pendingPlanConfirmationThreadIds: new Set<string>(),
  unreadCompletedThreadIds: new Set<string>(),
  goalSnapshots: new Map<string, ThreadGoal>(),
  pinnedThreadIds: [],
  pinnedThreadWorkspacePath: null,
  activeHistoryCursors: null
}

function filterSetToThreadList(current: Set<string>, ids: Set<string>): Set<string> {
  return new Set([...current].filter((id) => ids.has(id)))
}

function filterMapToThreadList<T>(current: Map<string, T>, ids: Set<string>): Map<string, T> {
  return new Map([...current].filter(([id]) => ids.has(id)))
}

function parkedApprovalKey(approval: ParkedApproval): string {
  const requestId = typeof approval.rawParams.requestId === 'string' ? approval.rawParams.requestId : ''
  if (requestId) return `request:${requestId}`
  const itemId = typeof approval.rawParams.itemId === 'string' ? approval.rawParams.itemId : ''
  if (itemId) return `item:${itemId}`
  return `bridge:${approval.bridgeId}`
}

function upsertParkedApproval(queue: ParkedApproval[], approval: ParkedApproval): ParkedApproval[] {
  const key = parkedApprovalKey(approval)
  const index = queue.findIndex((candidate) => parkedApprovalKey(candidate) === key)
  if (index < 0) return [...queue, approval]
  return queue.map((candidate, i) => i === index ? approval : candidate)
}

function normalizeComparableWorkspacePath(path: string | null | undefined): string {
  return normalizeWorkspaceProjectKey(path)
}

function firstNonEmpty(...values: Array<string | null | undefined>): string | undefined {
  for (const value of values) {
    const trimmed = value?.trim()
    if (trimmed) return trimmed
  }
  return undefined
}

function hasOwnWorktreeProperty(thread: ThreadSummary): boolean {
  return Object.prototype.hasOwnProperty.call(thread, 'worktree')
}

function normalizeIncomingWorkspaceState<T extends ThreadSummary>(
  incoming: T,
  existing: ThreadSummary | null | undefined
): T {
  if (!existing?.worktree || hasOwnWorktreeProperty(incoming)) return incoming

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
    normalizeComparableWorkspacePath(effectiveWorkspacePath) !==
    normalizeComparableWorkspacePath(localWorkspacePath)
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

function collectThreadTreeIds(threads: ThreadSummary[], rootThreadId: string): Set<string> {
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
  useViewerTabStore.getState().onThreadDeleted(threadId, {
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
    set((state) => {
      const visibleThreads = threads.filter((thread) => !isInternalThread(thread))
      const threadListProjectKey = normalizeWorkspaceProjectKey(projectKey)
      const threadIds = new Set(visibleThreads.map((thread) => thread.id))
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
      const unreadCompletedThreadIds = filterSetToThreadList(state.unreadCompletedThreadIds, threadIds)
      const goalSnapshots = filterMapToThreadList(state.goalSnapshots, threadIds)

      for (const thread of visibleThreads) {
        if (thread.goal === null) {
          goalSnapshots.delete(thread.id)
        } else if (thread.goal) {
          goalSnapshots.set(thread.id, thread.goal)
        }

        const runtime = thread.runtime
        if (!runtime) continue

        const snapshot: ThreadRuntimeSnapshot = {
          running: runtime.running === true,
          busy: runtime.busy === true,
          waitingOnApproval: runtime.waitingOnApproval === true,
          waitingOnInput: runtime.waitingOnInput === true,
          waitingOnPlanConfirmation: runtime.waitingOnPlanConfirmation === true,
          maintenanceKind: runtime.maintenanceKind ?? null
        }
        const previous = runtimeSnapshots.get(thread.id)
        const isActive = state.activeThreadId === thread.id
        const isDesktopOrigin = thread.originChannel?.toLowerCase() === 'dotcraft-desktop'
        const isSubAgent = isSubAgentThread(thread)
        runtimeSnapshots.set(thread.id, snapshot)

        if (snapshot.running) {
          runningTurnThreadIds.add(thread.id)
          unreadCompletedThreadIds.delete(thread.id)
        } else {
          runningTurnThreadIds.delete(thread.id)
          if (!isActive && !isSubAgent && previous?.running === true) {
            unreadCompletedThreadIds.add(thread.id)
          }
        }

        if (snapshot.waitingOnApproval && !isActive && isDesktopOrigin) {
          pendingApprovalThreadIds.add(thread.id)
        } else {
          pendingApprovalThreadIds.delete(thread.id)
          if (!snapshot.waitingOnApproval) {
            parkedApprovals.delete(thread.id)
          }
        }

        if (snapshot.waitingOnInput && !isActive && isDesktopOrigin) {
          pendingUserInputThreadIds.add(thread.id)
        } else {
          pendingUserInputThreadIds.delete(thread.id)
          if (!snapshot.waitingOnInput) {
            parkedUserInputs.delete(thread.id)
          }
        }

        if (snapshot.waitingOnPlanConfirmation && !isActive && isDesktopOrigin) {
          pendingPlanConfirmationThreadIds.add(thread.id)
        } else {
          pendingPlanConfirmationThreadIds.delete(thread.id)
        }
      }

      return {
        threadList: visibleThreads,
        threadListProjectKey: threadListProjectKey || null,
        runtimeSnapshots,
        runningTurnThreadIds,
        parkedApprovals,
        parkedUserInputs,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds,
        goalSnapshots
      }
    })
  },

  addThread(thread) {
    set((state) => {
      if (isInternalThread(thread)) return state
      if (state.threadList.some((t) => t.id === thread.id)) return state
      const goalSnapshots = new Map(state.goalSnapshots)
      if (thread.goal === null) {
        goalSnapshots.delete(thread.id)
      } else if (thread.goal) {
        goalSnapshots.set(thread.id, thread.goal)
      }
      return { threadList: [thread, ...state.threadList], goalSnapshots }
    })
  },

  upsertThreads(threads) {
    const internalThreadIds = new Set(threads.filter((thread) => isInternalThread(thread)).map((thread) => thread.id))
    const visibleThreads = threads.filter((thread) => !isInternalThread(thread))
    if (visibleThreads.length === 0 && internalThreadIds.size === 0) return
    set((state) => {
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
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      const goalSnapshots = new Map(state.goalSnapshots)

      for (const id of internalThreadIds) {
        runtimeSnapshots.delete(id)
        parkedApprovals.delete(id)
        parkedUserInputs.delete(id)
        runningTurnThreadIds.delete(id)
        pendingApprovalThreadIds.delete(id)
        pendingUserInputThreadIds.delete(id)
        pendingPlanConfirmationThreadIds.delete(id)
        unreadCompletedThreadIds.delete(id)
        goalSnapshots.delete(id)
      }

      for (const thread of visibleThreads) {
        if (thread.goal === null) {
          goalSnapshots.delete(thread.id)
        } else if (thread.goal) {
          goalSnapshots.set(thread.id, thread.goal)
        }

        const runtime = thread.runtime
        if (!runtime) continue
        const snapshot: ThreadRuntimeSnapshot = {
          running: runtime.running === true,
          busy: runtime.busy === true,
          waitingOnApproval: runtime.waitingOnApproval === true,
          waitingOnInput: runtime.waitingOnInput === true,
          waitingOnPlanConfirmation: runtime.waitingOnPlanConfirmation === true,
          maintenanceKind: runtime.maintenanceKind ?? null
        }
        runtimeSnapshots.set(thread.id, snapshot)
        if (snapshot.running) {
          runningTurnThreadIds.add(thread.id)
          unreadCompletedThreadIds.delete(thread.id)
        } else {
          runningTurnThreadIds.delete(thread.id)
          if (isSubAgentThread(thread)) {
            unreadCompletedThreadIds.delete(thread.id)
          }
        }
      }

      return {
        threadList: [...missing, ...threadList],
        runtimeSnapshots,
        parkedApprovals,
        parkedUserInputs,
        runningTurnThreadIds,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds,
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
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.delete(threadId)
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
        unreadCompletedThreadIds,
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
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      const goalSnapshots = new Map(state.goalSnapshots)
      for (const id of treeIds) {
        parkedApprovals.delete(id)
        parkedUserInputs.delete(id)
        runtimeSnapshots.delete(id)
        pendingApprovalThreadIds.delete(id)
        pendingUserInputThreadIds.delete(id)
        pendingPlanConfirmationThreadIds.delete(id)
        unreadCompletedThreadIds.delete(id)
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
        unreadCompletedThreadIds,
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
    set((state) => {
      if (!id) {
        return { activeThreadId: id, activeHistoryCursors: null }
      }

      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.delete(id)
      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      pendingApprovalThreadIds.delete(id)
      const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
      pendingUserInputThreadIds.delete(id)
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.delete(id)
      return {
        activeThreadId: id,
        activeHistoryCursors: state.activeThreadId === id ? state.activeHistoryCursors : null,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds
      }
    })
  },

  setActiveThread(thread) {
    // Do not sync activeThreadId here — selection is user-driven; stale thread/read
    // responses must not redirect which thread is selected.
    set((state) => {
      if (!thread) return { activeThread: thread }
      const incomingTurns = Array.isArray(thread.turns) ? thread.turns : []
      const normalizedThread = normalizeIncomingWorkspaceState(thread, state.activeThread)
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
      return { activeThread, goalSnapshots }
    })
  },

  setActiveHistoryCursors(threadId, turnCursor) {
    set((state) => state.activeThreadId === threadId
      ? { activeHistoryCursors: { threadId, turnCursor } }
      : {})
  },

  setSearchQuery(query) {
    set({ searchQuery: query })
  },

  setLoading(loading) {
    set({ loading })
  },

  markTurnStarted(threadId) {
    set((state) => {
      const next = new Set(state.runningTurnThreadIds)
      next.add(threadId)
      return { runningTurnThreadIds: next }
    })
  },

  markTurnEnded(threadId) {
    set((state) => {
      const next = new Set(state.runningTurnThreadIds)
      next.delete(threadId)
      return { runningTurnThreadIds: next }
    })
  },

  parkApproval(threadId, approval) {
    set((state) => {
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.set(
        threadId,
        upsertParkedApproval(parkedApprovals.get(threadId) ?? [], approval)
      )
      return { parkedApprovals }
    })
  },

  consumeParkedApproval(threadId) {
    const state = _get()
    const approvals = state.parkedApprovals.get(threadId) ?? []
    const approval = approvals[0] ?? null
    if (!approval) return null
    const parkedApprovals = new Map(state.parkedApprovals)
    const remaining = approvals.slice(1)
    if (remaining.length > 0) {
      parkedApprovals.set(threadId, remaining)
    } else {
      parkedApprovals.delete(threadId)
    }
    set({ parkedApprovals })
    return approval
  },

  consumeParkedApprovals(threadId) {
    const state = _get()
    const approvals = state.parkedApprovals.get(threadId) ?? []
    if (approvals.length === 0) return []
    const parkedApprovals = new Map(state.parkedApprovals)
    parkedApprovals.delete(threadId)
    set({ parkedApprovals })
    return approvals
  },

  clearParkedApproval(threadId) {
    set((state) => {
      if (!state.parkedApprovals.has(threadId)) return state
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.delete(threadId)
      return { parkedApprovals }
    })
  },

  parkUserInput(threadId, request) {
    set((state) => {
      const parkedUserInputs = new Map(state.parkedUserInputs)
      parkedUserInputs.set(threadId, request)
      return { parkedUserInputs }
    })
  },

  consumeParkedUserInput(threadId) {
    const state = _get()
    const request = state.parkedUserInputs.get(threadId) ?? null
    if (!request) return null
    const parkedUserInputs = new Map(state.parkedUserInputs)
    parkedUserInputs.delete(threadId)
    set({ parkedUserInputs })
    return request
  },

  clearParkedUserInput(threadId) {
    set((state) => {
      if (!state.parkedUserInputs.has(threadId)) return state
      const parkedUserInputs = new Map(state.parkedUserInputs)
      parkedUserInputs.delete(threadId)
      return { parkedUserInputs }
    })
  },

  applyRuntimeSnapshot(threadId, runtime, options) {
    set((state) => {
      const previous = state.runtimeSnapshots.get(threadId)
      const runtimeSnapshots = new Map(state.runtimeSnapshots)
      runtimeSnapshots.set(threadId, runtime)

      const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
      if (runtime.running) {
        runningTurnThreadIds.add(threadId)
      } else {
        runningTurnThreadIds.delete(threadId)
      }

      const parkedApprovals = new Map(state.parkedApprovals)
      if (!runtime.waitingOnApproval) {
        parkedApprovals.delete(threadId)
      }
      const parkedUserInputs = new Map(state.parkedUserInputs)
      if (!runtime.waitingOnInput) {
        parkedUserInputs.delete(threadId)
      }

      const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
      if (runtime.waitingOnApproval && !options.isActive && options.isDesktopOrigin) {
        pendingApprovalThreadIds.add(threadId)
      } else {
        pendingApprovalThreadIds.delete(threadId)
      }

      const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
      if (runtime.waitingOnInput && !options.isActive && options.isDesktopOrigin) {
        pendingUserInputThreadIds.add(threadId)
      } else {
        pendingUserInputThreadIds.delete(threadId)
      }

      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      if (runtime.waitingOnPlanConfirmation && !options.isActive && options.isDesktopOrigin) {
        pendingPlanConfirmationThreadIds.add(threadId)
      } else {
        pendingPlanConfirmationThreadIds.delete(threadId)
      }

      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      if (options.isActive || runtime.running) {
        unreadCompletedThreadIds.delete(threadId)
      } else if (previous?.running === true) {
        const thread = state.threadList.find((entry) => entry.id === threadId)
        if (thread && isSubAgentThread(thread)) {
          unreadCompletedThreadIds.delete(threadId)
        } else {
          unreadCompletedThreadIds.add(threadId)
        }
      }

      return {
        runtimeSnapshots,
        runningTurnThreadIds,
        parkedApprovals,
        parkedUserInputs,
        pendingApprovalThreadIds,
        pendingUserInputThreadIds,
        pendingPlanConfirmationThreadIds,
        unreadCompletedThreadIds
      }
    })
  },

  markPlanConfirmationPending(threadId) {
    set((state) => {
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.add(threadId)
      return { pendingPlanConfirmationThreadIds }
    })
  },

  clearPlanConfirmationPending(threadId) {
    set((state) => {
      if (!state.pendingPlanConfirmationThreadIds.has(threadId)) return state
      const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
      pendingPlanConfirmationThreadIds.delete(threadId)
      return { pendingPlanConfirmationThreadIds }
    })
  },

  markUnreadCompleted(threadId) {
    set((state) => {
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.add(threadId)
      return { unreadCompletedThreadIds }
    })
  },

  clearUnreadCompleted(threadId) {
    set((state) => {
      if (!state.unreadCompletedThreadIds.has(threadId)) return state
      const unreadCompletedThreadIds = new Set(state.unreadCompletedThreadIds)
      unreadCompletedThreadIds.delete(threadId)
      return { unreadCompletedThreadIds }
    })
  },

  setThreadGoal(goal) {
    set((state) => {
      const goalSnapshots = new Map(state.goalSnapshots)
      goalSnapshots.set(goal.threadId, goal)
      return {
        goalSnapshots,
        threadList: state.threadList.map((thread) =>
          thread.id === goal.threadId ? { ...thread, goal } : thread
        ),
        activeThread:
          state.activeThread?.id === goal.threadId
            ? { ...state.activeThread, goal }
            : state.activeThread
      }
    })
  },

  clearThreadGoal(threadId) {
    set((state) => {
      const goalSnapshots = new Map(state.goalSnapshots)
      goalSnapshots.delete(threadId)
      return {
        goalSnapshots,
        threadList: state.threadList.map((thread) =>
          thread.id === threadId ? { ...thread, goal: null } : thread
        ),
        activeThread:
          state.activeThread?.id === threadId
            ? { ...state.activeThread, goal: null }
            : state.activeThread
      }
    })
  },

  hydrateThreadGoal(threadId, goal) {
    if (goal === undefined) return
    if (goal === null) {
      _get().clearThreadGoal(threadId)
      return
    }
    _get().setThreadGoal(goal)
  },

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
      unreadCompletedThreadIds: new Set<string>(),
      goalSnapshots: new Map<string, ThreadGoal>(),
      pinnedThreadIds: [],
      pinnedThreadWorkspacePath: null
    })
  }
}))

// Expose store to E2E / debug tooling via a window global (browser only)
if (typeof window !== 'undefined') {
  ;(window as unknown as Record<string, unknown>).__THREAD_STORE_STATE = () =>
    useThreadStore.getState()
}

/**
 * Archived threads are always hidden from the main sidebar list — on the archive
 * action itself and on a thread/statusChanged notification with newStatus 'archived'.
 */
export function selectFilteredThreads(state: ThreadStore): ThreadSummary[] {
  // Subagent threads are surfaced through the dock (running) and the Subagents
  // detail tab (Active/Done), never as sidebar thread entries.
  const visible = state.threadList.filter((t) => t.status !== 'archived' && !isSubAgentThread(t))
  if (!state.searchQuery.trim()) return visible
  const q = state.searchQuery.toLowerCase()
  return visible.filter((t) => (t.displayName ?? '').toLowerCase().includes(q))
}
