import { create } from 'zustand'
import type { SubAgentEntry } from '../types/toolCall'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { useConnectionStore } from './connectionStore'
import { useThreadStore } from './threadStore'
import { getThreadConnection, requestThreadConnection } from './threadConnectionStore'
import { syncSubAgentTaskActivity, clearSubAgentTaskActivity, resetSubAgentTaskActivity, useSubAgentTaskActivity } from './subAgentTaskActivity'
import {
  childFromWire, createPlaceholderChild,
  isSubAgentChildClosed, isTerminalSubAgentStatus, mergeExistingProgress,
  type SubAgentChild, type SubAgentChildWire
} from './subAgentChildren'

export { isSubAgentChildClosed, isTerminalSubAgentStatus } from './subAgentChildren'
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

interface ChildLifetime {
  connectionKey: string | null
  connectionId: string | null
}

interface ChildRequest {
  promise: Promise<void>
  refresh: boolean
  authoritative: boolean
  lifetime: ChildLifetime
}

const requests = new Map<string, ChildRequest>()
const lifetimes = new Map<string, ChildLifetime>()
function subAgentConnectionThread(threadId: string): string {
  const graph = useSubAgentStore.getState().childrenByParent
  let current = threadId
  while (!getThreadConnection(current)) {
    const parent = [...graph].find(([, children]) => children.some(child => child.childThreadId === current))?.[0]
    if (!parent) return threadId
    current = parent
  }
  return current
}

function childConnection(parentThreadId: string): ChildLifetime {
  const source = getThreadConnection(subAgentConnectionThread(parentThreadId)) ?? useConnectionStore.getState()
  return { connectionKey: source.connectionKey, connectionId: source.connectionId }
}

function lifetime(parentThreadId: string): ChildLifetime {
  let token = lifetimes.get(parentThreadId)
  const source = childConnection(parentThreadId)
  if (!token || token.connectionKey !== source.connectionKey || token.connectionId !== source.connectionId) {
    token = source
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
    const current = get().childrenByParent.get(parentThreadId) ?? []
    for (const child of previous) {
      if (!current.some(next => next.childThreadId === child.childThreadId && !isSubAgentChildClosed(next))
        && get().childrenByParent.has(child.childThreadId)) get().clearParent(child.childThreadId)
    }
  },

  ensureChildren(parentThreadId) {
    const previous = lifetimes.get(parentThreadId)
    const token = lifetime(parentThreadId)
    const pending = requests.get(parentThreadId)
    if (pending?.lifetime === token) return pending.promise
    if (previous === token && get().discoveryByParent.has(parentThreadId)) {
      return syncSubAgentTaskActivity(parentThreadId, get().childrenByParent.get(parentThreadId) ?? [], subAgentConnectionThread(parentThreadId))
    }
    return get().fetchChildren(parentThreadId)
  },

  fetchChildren(parentThreadId, options) {
    const connection = getThreadConnection(subAgentConnectionThread(parentThreadId))
    const capabilities = connection ? connection.capabilities : useConnectionStore.getState().capabilities
    if (!parentThreadId || capabilities?.subAgentSessions !== true) return Promise.resolve()
    const token = lifetime(parentThreadId)
    const pending = requests.get(parentThreadId)
    if (pending?.lifetime === token) {
      pending.refresh = true
      pending.authoritative ||= options?.authoritative === true
      return pending.promise
    }
    const current = (): boolean => {
      const source = childConnection(parentThreadId)
      return lifetimes.get(parentThreadId) === token
        && source.connectionKey === token.connectionKey && source.connectionId === token.connectionId
    }
    const task: ChildRequest = {
      promise: Promise.resolve(), refresh: false, authoritative: options?.authoritative === true, lifetime: token
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
        const runtimeBaseline = useThreadStore.getState().runtimeSnapshots
        try {
          const result = await requestThreadConnection(subAgentConnectionThread(parentThreadId), 'subagent/children/list', {
            parentThreadId, includeClosed: true, includeThreads: true
          }) as { data?: SubAgentChildWire[] }
          if (!current()) return
          const children = (result.data ?? [])
            .map((entry) => childFromWire(parentThreadId, entry))
            .filter((entry): entry is SubAgentChild => entry != null)
          const threads = children.map((child) => child.threadSummary)
            .filter((thread): thread is ThreadSummary => thread != null)
          const threadStore = useThreadStore.getState()
          if (!connection || threadStore.threadListProjectKey === connection.workspaceKey) {
            threadStore.upsertThreads(threads, runtimeBaseline)
          } else {
            for (const thread of threads) {
              if (thread.runtime) threadStore.applyRuntimeSnapshot(thread.id, thread.runtime, {
                isDesktopOrigin: false, baseline: runtimeBaseline
              })
            }
          }
          get().setChildren(parentThreadId, children, authoritative
            ? { preserveRunningPlaceholders: false, blockStaleProgressWhenEmpty: children.length === 0 }
            : undefined)
          syncSubAgentTaskActivity(parentThreadId, get().childrenByParent.get(parentThreadId) ?? [], subAgentConnectionThread(parentThreadId))
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
    set((state) => {
      const childrenByParent = new Map(state.childrenByParent)
      let changed = false
      for (const [parentThreadId, children] of childrenByParent) {
        if (!children.some((child) => child.childThreadId === childThreadId)) continue
        changed = true
        childrenByParent.set(parentThreadId, children.map((child) =>
          child.childThreadId === childThreadId ? { ...child, contextTokens } : child))
      }
      return changed ? { childrenByParent } : state
    })
  },

  async interruptChild(child) {
    const turnId = subAgentChildRuntime(child)?.activeTurnId
      ?? ((await requestThreadConnection(subAgentConnectionThread(child.parentThreadId), 'thread/read', { threadId: child.childThreadId })) as { thread: ThreadSummary }).thread.runtime?.activeTurnId
    if (turnId) await requestThreadConnection(subAgentConnectionThread(child.parentThreadId), 'turn/interrupt', { threadId: child.childThreadId, turnId })
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
        return {
          ...child,
          status: child.isPlaceholder ? progress.isCompleted ? 'completed' : 'open' : child.status,
          lastToolDisplay: progress.currentToolDisplay ?? progress.currentTool ?? child.lastToolDisplay,
          currentTool: subAgentChildRuntime(child)?.running === false || progress.isCompleted
            ? null : progress.currentTool ?? child.currentTool
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
    const descendants = get().childrenByParent.get(parentThreadId) ?? []
    clearSubAgentTaskActivity(parentThreadId)
    lifetimes.delete(parentThreadId)
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
    for (const child of descendants) {
      if (get().childrenByParent.has(child.childThreadId)) get().clearParent(child.childThreadId)
    }
  },

  reset() {
    resetSubAgentTaskActivity()
    lifetimes.clear()
    requests.clear()
    set({
      childrenByParent: new Map(),
      discoveryByParent: new Map(),
      staleProgressBlockedParents: new Set(),
      selectedChildByParent: new Map()
    })
  }
}))

export function subAgentChildRuntime(child: SubAgentChild): ThreadRuntimeSnapshot | undefined {
  return useThreadStore.getState().runtimeSnapshots.get(child.childThreadId)
}

export function isSubAgentChildRunning(child: SubAgentChild): boolean {
  if (isSubAgentChildClosed(child)) return false
  return subAgentChildRuntime(child)?.running
    ?? (child.isPlaceholder === true && !isTerminalSubAgentStatus(child.status))
}

export function isSubAgentChildWorking(child: SubAgentChild): boolean {
  if (isSubAgentChildClosed(child)) return false
  return isSubAgentChildRunning(child)
    || useSubAgentTaskActivity.getState().tasksByThread.has(child.childThreadId)
    || (useSubAgentStore.getState().childrenByParent.get(child.childThreadId) ?? []).some(isSubAgentChildWorking)
}
