import { create } from 'zustand'
import type { SubAgentEntry } from '../types/toolCall'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { useConnectionStore } from './connectionStore'
import { useThreadStore } from './threadStore'
import {
  childFromWire, createPlaceholderChild,
  isSubAgentChildClosed, isSubAgentChildRunning, mergeExistingProgress,
  type SubAgentChild, type SubAgentChildWire
} from './subAgentChildren'

export { isSubAgentChildClosed, isSubAgentChildRunning, isTerminalSubAgentStatus } from './subAgentChildren'
export type { SubAgentChild } from './subAgentChildren'

export interface SubAgentDiscovery {
  status: 'idle' | 'loading' | 'ready' | 'error'
  discovered: boolean
}

export const undiscoveredSubAgents: SubAgentDiscovery = { status: 'idle', discovered: false }

interface SubAgentStoreState {
  childrenByParent: Map<string, SubAgentChild[]>
  discoveryByParent: Map<string, SubAgentDiscovery>
  staleProgressBlockedParents: Set<string>
  selectedChildByParent: Map<string, string>
}

interface FetchChildrenOptions {
  authoritative?: boolean
}

interface SetChildrenOptions {
  preserveRunningPlaceholders?: boolean
  blockStaleProgressWhenEmpty?: boolean
}

interface SubAgentStoreActions {
  setChildren(parentThreadId: string, children: SubAgentChild[], options?: SetChildrenOptions): void
  ensureChildren(parentThreadId: string): Promise<void>
  fetchChildren(parentThreadId: string, options?: FetchChildrenOptions): Promise<void>
  updateChildUsage(childThreadId: string, contextTokens: number): void
  interruptChild(child: SubAgentChild): Promise<void>
  updateProgress(parentThreadId: string, entries: SubAgentEntry[]): void
  updateChildRuntime(childThreadId: string, runtime: ThreadRuntimeSnapshot): void
  selectChild(parentThreadId: string, childThreadId: string | null): void
  clearParent(parentThreadId: string): void
  reset(): void
}

export interface SubAgentStore extends SubAgentStoreState, SubAgentStoreActions {}

const initialState: SubAgentStoreState = {
  childrenByParent: new Map(),
  discoveryByParent: new Map(),
  staleProgressBlockedParents: new Set(),
  selectedChildByParent: new Map()
}

interface ChildRequest {
  promise: Promise<void>
  refresh: boolean
  authoritative: boolean
}

const STATS_REFRESH_MS = 2_000

const requests = new Map<string, ChildRequest>()
const lifetimes = new Map<string, object>()
const statsRefreshTimers = new Map<string, ReturnType<typeof setTimeout>>()
function lifetime(parentThreadId: string): object {
  let token = lifetimes.get(parentThreadId)
  if (!token) {
    token = {}
    lifetimes.set(parentThreadId, token)
  }
  return token
}

export const useSubAgentStore = create<SubAgentStore>((set, get) => ({
  ...initialState,

  setChildren(parentThreadId, children, options) {
    const previous = get().childrenByParent.get(parentThreadId) ?? []
    set((state) => {
      const preserveRunningPlaceholders = options?.preserveRunningPlaceholders ?? true
      const blockStaleProgressWhenEmpty = options?.blockStaleProgressWhenEmpty === true
      if (
        children.length === 0
        && preserveRunningPlaceholders
        && previous.some((child) => child.isPlaceholder)
      ) {
        const runningPlaceholders = previous.filter((child) =>
          isSubAgentChildClosed(child) || (child.isPlaceholder === true && isSubAgentChildRunning(child))
        )
        const childrenByParent = new Map(state.childrenByParent)
        childrenByParent.set(parentThreadId, runningPlaceholders)
        return { childrenByParent }
      }

      const byId = new Map(previous.map((child) => [child.childThreadId, child]))
      const placeholderMatches = previous
        .map((child, index) => ({ child, index }))
        .filter((entry) => entry.child.isPlaceholder)
      const usedPlaceholderIndexes = new Set<number>()
      const merged = children.map((child) => {
        let existing = byId.get(child.childThreadId)
        if (!existing && !isSubAgentChildClosed(child)) {
          const nicknameMatch = placeholderMatches.find((entry) =>
            !usedPlaceholderIndexes.has(entry.index)
            && entry.child.nickname === child.nickname
          )
          const fallbackMatch = nicknameMatch
            ?? placeholderMatches.find((entry) => !usedPlaceholderIndexes.has(entry.index))
          if (fallbackMatch) {
            usedPlaceholderIndexes.add(fallbackMatch.index)
            existing = fallbackMatch.child
          }
        }
        return mergeExistingProgress(child, existing)
      })
      for (const child of previous) {
        if (isSubAgentChildClosed(child) && !merged.some((entry) => entry.childThreadId === child.childThreadId)) {
          merged.push(child)
        }
      }
      const childrenByParent = new Map(state.childrenByParent)
      childrenByParent.set(parentThreadId, merged)
      const staleProgressBlockedParents = new Set(state.staleProgressBlockedParents)
      let staleProgressChanged = false
      if (merged.length > 0 && staleProgressBlockedParents.delete(parentThreadId)) {
        staleProgressChanged = true
      } else if (merged.length === 0 && blockStaleProgressWhenEmpty && !staleProgressBlockedParents.has(parentThreadId)) {
        staleProgressBlockedParents.add(parentThreadId)
        staleProgressChanged = true
      }
      return {
        childrenByParent,
        ...(staleProgressChanged ? { staleProgressBlockedParents } : {})
      }
    })
  },

  ensureChildren(parentThreadId) {
    const pending = requests.get(parentThreadId)
    if (pending) return pending.promise
    if (get().discoveryByParent.has(parentThreadId)) return Promise.resolve()
    return get().fetchChildren(parentThreadId)
  },

  fetchChildren(parentThreadId, options) {
    if (!parentThreadId || useConnectionStore.getState().capabilities?.subAgentSessions !== true) {
      return Promise.resolve()
    }
    const pending = requests.get(parentThreadId)
    if (pending) {
      pending.refresh = true
      pending.authoritative ||= options?.authoritative === true
      return pending.promise
    }
    const token = lifetime(parentThreadId)
    const current = (): boolean => lifetimes.get(parentThreadId) === token
    const task: ChildRequest = {
      promise: Promise.resolve(), refresh: false, authoritative: options?.authoritative === true
    }
    requests.set(parentThreadId, task)
    const setDiscovery = (status: SubAgentDiscovery['status']): void => {
      if (!current()) return
      set((state) => {
        const discoveryByParent = new Map(state.discoveryByParent)
        discoveryByParent.set(parentThreadId, {
          status,
          discovered: status === 'ready' || state.discoveryByParent.get(parentThreadId)?.discovered === true
        })
        return { discoveryByParent }
      })
    }
    task.promise = (async () => {
      let failure: unknown
      do {
        task.refresh = false
        const authoritative = task.authoritative
        task.authoritative = false
        setDiscovery('loading')
        try {
          const result = await window.api.appServer.sendRequest('subagent/children/list', {
            parentThreadId, includeClosed: true, includeThreads: true
          }) as { data?: SubAgentChildWire[] }
          if (!current()) return
          const children = (result.data ?? [])
            .map((entry) => childFromWire(parentThreadId, entry))
            .filter((entry): entry is SubAgentChild => entry != null)
          useThreadStore.getState().upsertThreads(children
            .map((child) => child.threadSummary)
            .filter((thread): thread is ThreadSummary => thread != null))
          get().setChildren(parentThreadId, children, authoritative
            ? { preserveRunningPlaceholders: false, blockStaleProgressWhenEmpty: children.length === 0 }
            : undefined)
          setDiscovery('ready')
          failure = undefined
        } catch (error) {
          if (!current()) return
          setDiscovery('error')
          failure = error
        }
      } while (task.refresh && current())
      if (failure) throw failure
    })().finally(() => {
      if (requests.get(parentThreadId) === task) requests.delete(parentThreadId)
    })
    return task.promise
  },

  updateChildUsage(childThreadId, contextTokens) {
    const parents: string[] = []
    set((state) => {
      const childrenByParent = new Map(state.childrenByParent)
      for (const [parentThreadId, children] of childrenByParent) {
        if (!children.some((child) => child.childThreadId === childThreadId)) continue
        parents.push(parentThreadId)
        childrenByParent.set(parentThreadId, children.map((child) =>
          child.childThreadId === childThreadId ? { ...child, contextTokens } : child))
      }
      return parents.length > 0 ? { childrenByParent } : state
    })
    for (const parentThreadId of parents) {
      if (statsRefreshTimers.has(parentThreadId)) continue
      statsRefreshTimers.set(parentThreadId, setTimeout(() => {
        statsRefreshTimers.delete(parentThreadId)
        void get().fetchChildren(parentThreadId).catch(() => {})
      }, STATS_REFRESH_MS))
    }
  },

  async interruptChild(child) {
    const turnId = child.runtime?.activeTurnId
      ?? ((await window.api.appServer.sendRequest('thread/read', { threadId: child.childThreadId })) as { thread: ThreadSummary }).thread.runtime?.activeTurnId
    if (turnId) await window.api.appServer.sendRequest('turn/interrupt', { threadId: child.childThreadId, turnId })
  },

  updateProgress(parentThreadId, entries) {
    set((state) => {
      const current = state.childrenByParent.get(parentThreadId) ?? []
      const allowPlaceholderCreation = !state.staleProgressBlockedParents.has(parentThreadId)
      const closedNames = new Set(current.filter(isSubAgentChildClosed).map((child) => child.nickname))
      const activeNames = new Set(current.filter((child) => !isSubAgentChildClosed(child)).map((child) => child.nickname))
      const unmatched = entries.filter((entry) => !closedNames.has(entry.label) || activeNames.has(entry.label))
      const next = current.map((child) => {
        if (isSubAgentChildClosed(child)) return child
        const index = unmatched.findIndex((entry) => entry.label === child.nickname)
        const progress = index >= 0 ? unmatched.splice(index, 1)[0] : unmatched.shift()
        if (!progress) return child
        const isCompleted = child.runtime?.running === false || progress.isCompleted
        return {
          ...child,
          lastToolDisplay: progress.currentToolDisplay ?? progress.currentTool ?? child.lastToolDisplay,
          currentTool: isCompleted ? null : progress.currentTool ?? child.currentTool,
          isCompleted,
          runtime: child.runtime
            ? { ...child.runtime, running: child.runtime.running === false ? false : !isCompleted }
            : child.runtime
        }
      })
      if (allowPlaceholderCreation) {
        for (let index = 0; index < unmatched.length; index += 1) {
          next.push(createPlaceholderChild(parentThreadId, unmatched[index], current.length + index))
        }
      }
      if (next.length === 0 && current.length === 0) return state
      const childrenByParent = new Map(state.childrenByParent)
      childrenByParent.set(parentThreadId, next)
      const staleProgressBlockedParents = new Set(state.staleProgressBlockedParents)
      const staleProgressChanged = next.length > 0 && staleProgressBlockedParents.delete(parentThreadId)
      return {
        childrenByParent,
        ...(staleProgressChanged ? { staleProgressBlockedParents } : {})
      }
    })
  },

  updateChildRuntime(childThreadId, runtime) {
    set((state) => {
      let changed = false
      const childrenByParent = new Map(state.childrenByParent)
      for (const [parentThreadId, children] of childrenByParent) {
        const next = children.map((child) => {
          if (child.childThreadId !== childThreadId || isSubAgentChildClosed(child)) return child
          changed = true
          return {
            ...child,
            runtime,
            currentTool: runtime.running ? child.currentTool : null,
            isCompleted: !runtime.running
          }
        })
        childrenByParent.set(parentThreadId, next)
      }
      return changed ? { childrenByParent } : state
    })
  },

  selectChild(parentThreadId, childThreadId) {
    set((state) => {
      if ((state.selectedChildByParent.get(parentThreadId) ?? null) === childThreadId) return state
      const selectedChildByParent = new Map(state.selectedChildByParent)
      if (childThreadId) selectedChildByParent.set(parentThreadId, childThreadId)
      else selectedChildByParent.delete(parentThreadId)
      return { selectedChildByParent }
    })
  },

  clearParent(parentThreadId) {
    lifetimes.delete(parentThreadId)
    clearStatsRefresh(parentThreadId)
    requests.delete(parentThreadId)
    set((state) => {
      const childrenByParent = new Map(state.childrenByParent)
      const staleProgressBlockedParents = new Set(state.staleProgressBlockedParents)
      const discoveryByParent = new Map(state.discoveryByParent)
      const selectedChildByParent = new Map(state.selectedChildByParent)
      discoveryByParent.delete(parentThreadId)
      childrenByParent.delete(parentThreadId)
      staleProgressBlockedParents.delete(parentThreadId)
      selectedChildByParent.delete(parentThreadId)
      return { childrenByParent, staleProgressBlockedParents, discoveryByParent, selectedChildByParent }
    })
  },

  reset() {
    lifetimes.clear()
    requests.clear()
    for (const parentThreadId of [...statsRefreshTimers.keys()]) clearStatsRefresh(parentThreadId)
    set({
      childrenByParent: new Map(),
      discoveryByParent: new Map(),
      staleProgressBlockedParents: new Set(),
      selectedChildByParent: new Map()
    })
  }
}))

function clearStatsRefresh(parentThreadId: string): void {
  const timer = statsRefreshTimers.get(parentThreadId)
  if (timer !== undefined) clearTimeout(timer)
  statsRefreshTimers.delete(parentThreadId)
}
