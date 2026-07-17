import { create } from 'zustand'
import type { SubAgentEntry } from '../types/toolCall'
import type { ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { wireTurnToConversationTurn } from '../types/conversation'
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
  /**
   * Preview of the subagent's most recent agent message, used by the Subagents
   * panel for finished subagents (running ones prefer live tool progress).
   * Loaded lazily via {@link SubAgentStore.fetchPreviews}; null until read.
   */
  lastMessagePreview: string | null
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
  /**
   * Loads a short preview of each child's most recent agent message via
   * `thread/read`, populating {@link SubAgentChild.lastMessagePreview}. Skips
   * children that already have a preview unless `force` is set. When
   * `runningOnly` is set, only running children are read and they are always
   * refreshed (ignoring the cached preview) — used by the panel's live poll so a
   * running subagent's message keeps updating.
   */
  fetchPreviews(parentThreadId: string, options?: { force?: boolean; runningOnly?: boolean }): Promise<void>
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

const PREVIEW_MAX_LENGTH = 140

/**
 * Extracts a short single-line preview from the most recent agent message across
 * the given raw turns (newest last). Returns null when no agent text is present.
 */
function extractLastAgentMessagePreview(rawTurns: Array<Record<string, unknown>>): string | null {
  for (let turnIndex = rawTurns.length - 1; turnIndex >= 0; turnIndex -= 1) {
    let turn
    try {
      turn = wireTurnToConversationTurn(rawTurns[turnIndex])
    } catch {
      continue
    }
    for (let itemIndex = turn.items.length - 1; itemIndex >= 0; itemIndex -= 1) {
      const item = turn.items[itemIndex]
      if (item.type !== 'agentMessage') continue
      const text = item.text?.replace(/\s+/g, ' ').trim()
      if (!text) continue
      return text.length > PREVIEW_MAX_LENGTH ? `${text.slice(0, PREVIEW_MAX_LENGTH)}…` : text
    }
  }
  return null
}

export function isTerminalSubAgentStatus(status: string | null | undefined): boolean {
  const normalized = status?.trim().toLowerCase()
  return normalized === 'closed'
    || normalized === 'completed'
    || normalized === 'failed'
    || normalized === 'cancelled'
    || normalized === 'canceled'
}

/** True when the subagent's spawn edge has been closed (by CloseAgent or residency reclaim). */
export function isSubAgentChildClosed(child: SubAgentChild): boolean {
  return child.status.trim().toLowerCase() === 'closed'
}

export function isSubAgentChildRunning(child: SubAgentChild): boolean {
  // A closed edge is terminal even if a stale runtime snapshot still says running.
  if (isSubAgentChildClosed(child)) return false
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
    lastMessagePreview: null,
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
    lastMessagePreview: existing.lastMessagePreview,
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
    lastMessagePreview: null,
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
        // Include closed edges so the Subagents panel can still surface subagents
        // the main agent closed (or that residency auto-reclaimed) for read-only
        // review. Running/done vs closed is distinguished on the client.
        includeClosed: true,
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

  async fetchPreviews(parentThreadId, options) {
    if (!parentThreadId) return
    const runningOnly = options?.runningOnly === true
    // runningOnly polls always refresh (the message is still changing); the
    // default pass only fills children that have no cached preview yet.
    const force = options?.force === true || runningOnly
    const children = get().childrenByParent.get(parentThreadId) ?? []
    const targets = children.filter((child) =>
      child.isPlaceholder !== true
      && (!runningOnly || isSubAgentChildRunning(child))
      && (force || child.lastMessagePreview == null)
    )
    if (targets.length === 0) return

    const previews = new Map<string, string>()
    await Promise.all(targets.map(async (child) => {
      try {
        const result = await window.api.appServer.sendRequest('thread/read', {
          threadId: child.childThreadId,
          includeTurns: true
        }) as { thread?: { turns?: Array<Record<string, unknown>> } }
        const preview = extractLastAgentMessagePreview(result.thread?.turns ?? [])
        if (preview) previews.set(child.childThreadId, preview)
      } catch {
        // Best-effort preview; leave null so the row falls back to a status label.
      }
    }))
    if (previews.size === 0) return

    set((state) => {
      const current = state.childrenByParent.get(parentThreadId)
      if (!current) return state
      const next = current.map((child) => {
        const preview = previews.get(child.childThreadId)
        return preview && preview !== child.lastMessagePreview
          ? { ...child, lastMessagePreview: preview }
          : child
      })
      const childrenByParent = new Map(state.childrenByParent)
      childrenByParent.set(parentThreadId, next)
      return { childrenByParent }
    })
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
