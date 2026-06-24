import { create } from 'zustand'
import type { SubAgentEntry } from '../types/toolCall'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { useConnectionStore } from './connectionStore'
import { useThreadStore } from './threadStore'

export interface SubAgentEdgeWire {
  parentThreadId?: string
  childThreadId?: string
  parentTurnId?: string | null
  depth?: number
  agentPath?: string | null
  taskName?: string | null
  agentNickname?: string | null
  agentRole?: string | null
  agentType?: string | null
  agent_type?: string | null
  role?: string | null
  profileName?: string | null
  runtimeType?: string | null
  supportsSendInput?: boolean
  supportsResume?: boolean
  supportsSendMessage?: boolean
  supportsFollowupTask?: boolean
  supportsClose?: boolean
  status?: string
}

interface SubAgentChildWire {
  edge?: SubAgentEdgeWire
  thread?: ThreadSummary | null
}

export interface SubAgentChild {
  childThreadId: string
  parentThreadId: string
  agentPath?: string | null
  taskName?: string | null
  nickname: string
  agentRole: string | null
  profileName: string | null
  runtimeType: string | null
  supportsSendInput: boolean
  supportsResume: boolean
  supportsSendMessage?: boolean
  supportsFollowupTask?: boolean
  supportsClose: boolean
  status: string
  lastToolDisplay: string | null
  currentTool: string | null
  inputTokens: number
  outputTokens: number
  isCompleted: boolean
  isPlaceholder?: boolean
  runtime?: ThreadRuntimeSnapshot
  threadSummary?: ThreadSummary | null
}

interface SubAgentStoreState {
  childrenByParent: Map<string, SubAgentChild[]>
  collapsedByParent: Map<string, boolean>
  userCollapsedByParent: Map<string, boolean>
  loadingParents: Set<string>
  staleProgressBlockedParents: Set<string>
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
  fetchChildren(parentThreadId: string, options?: FetchChildrenOptions): Promise<void>
  updateProgress(parentThreadId: string, entries: SubAgentEntry[]): void
  updateChildRuntime(childThreadId: string, runtime: ThreadRuntimeSnapshot): void
  setParentCollapsed(parentThreadId: string, collapsed: boolean, userInitiated?: boolean): void
  clearParent(parentThreadId: string): void
  reset(): void
}

export interface SubAgentStore extends SubAgentStoreState, SubAgentStoreActions {}

const initialState: SubAgentStoreState = {
  childrenByParent: new Map(),
  collapsedByParent: new Map(),
  userCollapsedByParent: new Map(),
  loadingParents: new Set(),
  staleProgressBlockedParents: new Set()
}

function normalizeText(value: unknown): string | null {
  return typeof value === 'string' && value.trim().length > 0 ? value.trim() : null
}

function lastAgentPathSegment(agentPath: string | null | undefined): string | null {
  if (!agentPath) return null
  const parts = agentPath.split('/').filter((part) => part.length > 0)
  return parts.length > 0 ? parts[parts.length - 1] : null
}

function normalizeRole(source: {
  agentRole?: unknown
  agentType?: unknown
  agent_type?: unknown
  role?: unknown
} | null | undefined): string | null {
  return normalizeText(source?.agentRole)
    ?? normalizeText(source?.agentType)
    ?? normalizeText(source?.agent_type)
    ?? normalizeText(source?.role)
}

export function isTerminalSubAgentStatus(status: string | null | undefined): boolean {
  const normalized = status?.trim().toLowerCase()
  return normalized === 'closed'
    || normalized === 'completed'
    || normalized === 'failed'
    || normalized === 'cancelled'
    || normalized === 'canceled'
}

export function isSubAgentChildRunning(child: SubAgentChild): boolean {
  if (child.runtime?.running === true) return true
  if (child.runtime?.running === false) return false
  if (child.isPlaceholder === true) return !child.isCompleted && !isTerminalSubAgentStatus(child.status)
  if (child.isCompleted || isTerminalSubAgentStatus(child.status)) return false
  return false
}

function childFromWire(parentThreadId: string, wire: SubAgentChildWire): SubAgentChild | null {
  const edge = wire.edge ?? {}
  const childThreadId = normalizeText(edge.childThreadId) ?? normalizeText(wire.thread?.id)
  if (!childThreadId) return null
  const cachedThread = useThreadStore.getState().threadList.find((thread) => thread.id === childThreadId)
  const threadSummary = wire.thread ?? cachedThread ?? null
  const source = threadSummary?.source?.subAgent
  const runtime = threadSummary?.runtime
  const status = normalizeText(edge.status) ?? 'open'
  const agentPath = normalizeText(edge.agentPath) ?? normalizeText(source?.agentPath)
  const taskName = normalizeText(edge.taskName) ?? normalizeText(source?.taskName)
  const isCompleted = runtime?.running === true
    ? false
    : runtime?.running === false || isTerminalSubAgentStatus(status)
  const nickname =
    normalizeText(wire.thread?.displayName)
    ?? normalizeText(edge.agentNickname)
    ?? normalizeText(source?.agentNickname)
    ?? taskName
    ?? lastAgentPathSegment(agentPath)
    ?? childThreadId
  return {
    childThreadId,
    parentThreadId: normalizeText(edge.parentThreadId) ?? parentThreadId,
    agentPath,
    taskName,
    nickname,
    agentRole: normalizeRole(edge) ?? normalizeRole(source),
    profileName: normalizeText(edge.profileName) ?? normalizeText(source?.profileName),
    runtimeType: normalizeText(edge.runtimeType) ?? normalizeText(source?.runtimeType),
    supportsSendInput: edge.supportsSendInput ?? source?.supportsSendInput ?? true,
    supportsResume: edge.supportsResume ?? source?.supportsResume ?? true,
    supportsSendMessage: edge.supportsSendMessage ?? source?.supportsSendMessage ?? false,
    supportsFollowupTask: edge.supportsFollowupTask ?? source?.supportsFollowupTask ?? false,
    supportsClose: edge.supportsClose ?? source?.supportsClose ?? true,
    status,
    lastToolDisplay: null,
    currentTool: null,
    inputTokens: 0,
    outputTokens: 0,
    isCompleted,
    isPlaceholder: false,
    runtime,
    threadSummary
  }
}

function mergeExistingProgress(next: SubAgentChild, existing: SubAgentChild | undefined): SubAgentChild {
  if (!existing) return next
  let runtime = next.runtime ?? existing.runtime
  const nextCompleted = next.runtime?.running === true
    ? false
    : next.isCompleted || next.runtime?.running === false || isTerminalSubAgentStatus(next.status)
  if (nextCompleted && runtime) {
    runtime = { ...runtime, running: false }
  }
  const isCompleted = nextCompleted
    ? true
    : existing.runtime?.running === true
      ? false
      : existing.isCompleted
  return {
    ...next,
    agentPath: next.agentPath ?? existing.agentPath,
    taskName: next.taskName ?? existing.taskName,
    agentRole: next.agentRole ?? existing.agentRole,
    profileName: next.profileName ?? existing.profileName,
    runtimeType: next.runtimeType ?? existing.runtimeType,
    lastToolDisplay: existing.lastToolDisplay,
    currentTool: isCompleted ? null : existing.currentTool,
    inputTokens: existing.inputTokens,
    outputTokens: existing.outputTokens,
    isCompleted,
    isPlaceholder: false,
    runtime
  }
}

function createPlaceholderChild(
  parentThreadId: string,
  progress: SubAgentEntry,
  index: number
): SubAgentChild {
  const label = normalizeText(progress.label) ?? `Agent ${index + 1}`
  const task = normalizeText((progress as SubAgentEntry & { task?: string }).task)
  const display = normalizeText(progress.currentToolDisplay)
    ?? normalizeText(progress.currentTool)
    ?? task
  const isCompleted = progress.isCompleted === true
  return {
    childThreadId: `subagent-placeholder:${parentThreadId}:${index}:${label}`,
    parentThreadId,
    agentPath: null,
    taskName: null,
    nickname: label,
    agentRole: null,
    profileName: null,
    runtimeType: null,
    supportsSendInput: false,
    supportsResume: false,
    supportsSendMessage: false,
    supportsFollowupTask: false,
    supportsClose: false,
    status: isCompleted ? 'completed' : 'open',
    lastToolDisplay: display,
    currentTool: isCompleted ? null : progress.currentTool,
    inputTokens: progress.inputTokens,
    outputTokens: progress.outputTokens,
    isCompleted,
    isPlaceholder: true,
    runtime: {
      running: !isCompleted,
      waitingOnApproval: false,
      waitingOnPlanConfirmation: false
    },
    threadSummary: null
  }
}

function ensureDefaultCollapsed(
  state: SubAgentStoreState,
  parentThreadId: string,
  children: SubAgentChild[]
): Map<string, boolean> | null {
  if (children.length === 0 || state.collapsedByParent.has(parentThreadId)) return null

  const collapsedByParent = new Map(state.collapsedByParent)
  collapsedByParent.set(parentThreadId, true)
  return collapsedByParent
}

export const useSubAgentStore = create<SubAgentStore>((set, get) => ({
  ...initialState,

  setChildren(parentThreadId, children, options) {
    set((state) => {
      const previous = state.childrenByParent.get(parentThreadId) ?? []
      const preserveRunningPlaceholders = options?.preserveRunningPlaceholders ?? true
      const blockStaleProgressWhenEmpty = options?.blockStaleProgressWhenEmpty === true
      if (
        children.length === 0
        && preserveRunningPlaceholders
        && previous.some((child) => child.isPlaceholder)
      ) {
        const runningPlaceholders = previous.filter((child) =>
          child.isPlaceholder === true && isSubAgentChildRunning(child)
        )
        const childrenByParent = new Map(state.childrenByParent)
        childrenByParent.set(parentThreadId, runningPlaceholders)
        const collapsedByParent = ensureDefaultCollapsed(state, parentThreadId, runningPlaceholders)
        return collapsedByParent ? { childrenByParent, collapsedByParent } : { childrenByParent }
      }

      const byId = new Map(previous.map((child) => [child.childThreadId, child]))
      const placeholderMatches = previous
        .map((child, index) => ({ child, index }))
        .filter((entry) => entry.child.isPlaceholder)
      const usedPlaceholderIndexes = new Set<number>()
      const merged = children.map((child) => {
        let existing = byId.get(child.childThreadId)
        if (!existing) {
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
      const childrenByParent = new Map(state.childrenByParent)
      childrenByParent.set(parentThreadId, merged)
      const collapsedByParent = ensureDefaultCollapsed(state, parentThreadId, merged)
      const staleProgressBlockedParents = cloneSet(state.staleProgressBlockedParents)
      let staleProgressChanged = false
      if (merged.length > 0 && staleProgressBlockedParents.delete(parentThreadId)) {
        staleProgressChanged = true
      } else if (merged.length === 0 && blockStaleProgressWhenEmpty && !staleProgressBlockedParents.has(parentThreadId)) {
        staleProgressBlockedParents.add(parentThreadId)
        staleProgressChanged = true
      }
      return {
        childrenByParent,
        ...(collapsedByParent ? { collapsedByParent } : {}),
        ...(staleProgressChanged ? { staleProgressBlockedParents } : {})
      }
    })
  },

  async fetchChildren(parentThreadId, options) {
    if (!parentThreadId) return
    if (useConnectionStore.getState().capabilities?.subAgentSessions !== true) return
    set((state) => {
      const loadingParents = new Set(state.loadingParents)
      loadingParents.add(parentThreadId)
      return { loadingParents }
    })
    try {
      const result = await window.api.appServer.sendRequest('subagent/children/list', {
        parentThreadId,
        includeClosed: false,
        includeThreads: true
      }) as { data?: SubAgentChildWire[] }
      const children = (result.data ?? [])
        .map((entry) => childFromWire(parentThreadId, entry))
        .filter((entry): entry is SubAgentChild => entry != null)
      const childThreads = children
        .map((child) => child.threadSummary)
        .filter((thread): thread is ThreadSummary => thread != null)
      useThreadStore.getState().upsertThreads(childThreads)
      get().setChildren(parentThreadId, children, options?.authoritative === true
        ? { preserveRunningPlaceholders: false, blockStaleProgressWhenEmpty: children.length === 0 }
        : undefined)
    } finally {
      set((state) => {
        const loadingParents = new Set(state.loadingParents)
        loadingParents.delete(parentThreadId)
        return { loadingParents }
      })
    }
  },

  updateProgress(parentThreadId, entries) {
    set((state) => {
      const current = state.childrenByParent.get(parentThreadId) ?? []
      const allowPlaceholderCreation = !state.staleProgressBlockedParents.has(parentThreadId)
      const unmatched = [...entries]
      const next = current.map((child) => {
        const index = unmatched.findIndex((entry) => entry.label === child.nickname)
        const progress = index >= 0 ? unmatched.splice(index, 1)[0] : unmatched.shift()
        if (!progress) return child
        const isCompleted = child.runtime?.running === false || progress.isCompleted
        return {
          ...child,
          lastToolDisplay: progress.currentToolDisplay ?? progress.currentTool ?? child.lastToolDisplay,
          currentTool: isCompleted ? null : progress.currentTool ?? child.currentTool,
          inputTokens: progress.inputTokens,
          outputTokens: progress.outputTokens,
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
      const collapsedByParent = ensureDefaultCollapsed(state, parentThreadId, next)
      const staleProgressBlockedParents = cloneSet(state.staleProgressBlockedParents)
      const staleProgressChanged = next.length > 0 && staleProgressBlockedParents.delete(parentThreadId)
      return {
        childrenByParent,
        ...(collapsedByParent ? { collapsedByParent } : {}),
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
          if (child.childThreadId !== childThreadId) return child
          changed = true
          return {
            ...child,
            runtime,
            currentTool: runtime.running ? child.currentTool : null,
            isCompleted: !runtime.running
          }
        })
        childrenByParent.set(parentThreadId, next)
        const collapsedByParent = ensureDefaultCollapsed(state, parentThreadId, next)
        if (collapsedByParent) {
          return { childrenByParent, collapsedByParent }
        }
      }
      return changed ? { childrenByParent } : state
    })
  },

  setParentCollapsed(parentThreadId, collapsed, userInitiated = true) {
    set((state) => {
      const collapsedByParent = new Map(state.collapsedByParent)
      collapsedByParent.set(parentThreadId, collapsed)
      if (!userInitiated) {
        return { collapsedByParent }
      }

      const userCollapsedByParent = new Map(state.userCollapsedByParent)
      if (collapsed) {
        userCollapsedByParent.set(parentThreadId, true)
      } else {
        userCollapsedByParent.delete(parentThreadId)
      }
      return { collapsedByParent, userCollapsedByParent }
    })
  },

  clearParent(parentThreadId) {
    set((state) => {
      const childrenByParent = new Map(state.childrenByParent)
      const collapsedByParent = new Map(state.collapsedByParent)
      const userCollapsedByParent = new Map(state.userCollapsedByParent)
      const staleProgressBlockedParents = cloneSet(state.staleProgressBlockedParents)
      childrenByParent.delete(parentThreadId)
      collapsedByParent.delete(parentThreadId)
      userCollapsedByParent.delete(parentThreadId)
      staleProgressBlockedParents.delete(parentThreadId)
      return { childrenByParent, collapsedByParent, userCollapsedByParent, staleProgressBlockedParents }
    })
  },

  reset() {
    set({
      childrenByParent: new Map(),
      collapsedByParent: new Map(),
      userCollapsedByParent: new Map(),
      loadingParents: new Set(),
      staleProgressBlockedParents: new Set()
    })
  }
}))

function cloneSet<T>(source: Set<T>): Set<T> {
  return new Set(source)
}
