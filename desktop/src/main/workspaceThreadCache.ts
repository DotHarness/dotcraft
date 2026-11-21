import { isHiddenThread } from '../shared/hiddenThreads'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'

export interface WorkspaceThreadCacheUpdateResult {
  threads: unknown[]
  changed: boolean
  refreshThreadList: boolean
}

export interface WorkspaceThreadListRefreshState {
  threads: unknown[]
  threadListLoaded?: boolean
  errorMessage?: string
}

export const WORKSPACE_THREAD_LIST_RETRY_DELAYS_MS: readonly number[] = [2_000, 5_000, 15_000, 30_000]

export interface WorkspaceThreadListRefreshOptions {
  retryDelaysMs?: readonly number[]
  isActive?: () => boolean
  onRetryChanged?: () => void
}

const threadListRevisions = new WeakMap<WorkspaceThreadListRefreshState, number>()
const threadListRetryTimers = new WeakMap<WorkspaceThreadListRefreshState, ReturnType<typeof setTimeout>>()
const readStateClocks = new WeakMap<WorkspaceThreadListRefreshState, { sequence: number; changedAt: Map<string, number> }>()
interface WorkspaceThreadDirectory {
  cursor: string
  requestedAtMark: number
  seenIds: Set<string>
  baselineIds: Set<string>
  removedIds: Set<string>
  updates: Map<string, Record<string, unknown>>
}
const threadDirectories = new WeakMap<WorkspaceThreadListRefreshState, WorkspaceThreadDirectory>()

function readStateClock(entry: WorkspaceThreadListRefreshState): { sequence: number; changedAt: Map<string, number> } {
  let clock = readStateClocks.get(entry)
  if (!clock) {
    clock = { sequence: 0, changedAt: new Map() }
    readStateClocks.set(entry, clock)
  }
  return clock
}

export function noteWorkspaceReadStateChange(entry: WorkspaceThreadListRefreshState, threadId: string): void {
  const clock = readStateClock(entry)
  clock.changedAt.set(threadId, ++clock.sequence)
}

function advanceThreadListRevision(entry: WorkspaceThreadListRefreshState): number {
  const revision = (threadListRevisions.get(entry) ?? 0) + 1
  threadListRevisions.set(entry, revision)
  return revision
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object'
}

function stringField(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function getThreadId(thread: unknown): string {
  return isRecord(thread) ? stringField(thread.id) : ''
}

function getThreadStatus(thread: unknown): string {
  return isRecord(thread) ? stringField(thread.status).toLowerCase() : ''
}

function isSubAgentThread(thread: unknown): boolean {
  if (!isRecord(thread)) return false
  const source = isRecord(thread.source) ? thread.source : null
  const sourceKind = stringField(source?.kind).toLowerCase()
  const originChannel = stringField(thread.originChannel).toLowerCase()
  return sourceKind === 'subagent' || originChannel === 'subagent'
}

function getSubAgentParentThreadId(thread: unknown): string {
  if (!isSubAgentThread(thread) || !isRecord(thread)) return ''

  const source = isRecord(thread.source) ? thread.source : null
  const subAgent = isRecord(source?.subAgent) ? source.subAgent : null
  const sourceParent = stringField(subAgent?.parentThreadId)
  if (sourceParent) return sourceParent

  const context = stringField(thread.channelContext)
  if (context) return context

  const metadata = isRecord(thread.metadata) ? thread.metadata : null
  return stringField(metadata?.channelContext)
}

function removeWorkspaceThreadTree(threads: unknown[], rootThreadId: string): unknown[] {
  const rootId = rootThreadId.trim()
  if (!rootId) return threads

  const removed = new Set<string>([rootId])
  let expanded = true
  while (expanded) {
    expanded = false
    for (const thread of threads) {
      const id = getThreadId(thread)
      if (!id || removed.has(id)) continue
      const parentId = getSubAgentParentThreadId(thread)
      if (parentId && removed.has(parentId)) {
        removed.add(id)
        expanded = true
      }
    }
  }

  let changed = false
  const next = threads.filter((thread) => {
    const id = getThreadId(thread)
    const remove = Boolean(id && removed.has(id))
    if (remove) changed = true
    return !remove
  })
  return changed ? next : threads
}

function upsertWorkspaceThread(threads: unknown[], thread: unknown): unknown[] {
  const id = getThreadId(thread)
  if (!id || !isRecord(thread)) return threads
  if (getThreadStatus(thread) === 'archived' || isHiddenThread(thread)) return removeWorkspaceThreadTree(threads, id)

  const existing = threads.findIndex((candidate) => getThreadId(candidate) === id)
  if (existing >= 0) {
    const next = [...threads]
    next[existing] = {
      ...(isRecord(next[existing]) ? next[existing] : {}),
      ...thread
    }
    return next
  }

  return [thread, ...threads]
}

function updateWorkspaceThread(
  threads: unknown[],
  threadId: string,
  patch: Record<string, unknown>
): unknown[] {
  const id = threadId.trim()
  if (!id) return threads

  let changed = false
  const next = threads.map((thread) => {
    if (getThreadId(thread) !== id || !isRecord(thread)) return thread
    changed = true
    return { ...thread, ...patch }
  })
  return changed ? next : threads
}

function hasThread(threads: unknown[], threadId: string): boolean {
  const id = threadId.trim()
  return Boolean(id && threads.some((thread) => getThreadId(thread) === id))
}

export function applyWorkspaceThreadNotificationToCache(
  threads: unknown[],
  method: string,
  params: unknown
): WorkspaceThreadCacheUpdateResult {
  const p = isRecord(params) ? params : {}
  let next = threads
  let refreshThreadList = false

  if (method === 'thread/started') {
    next = upsertWorkspaceThread(threads, p.thread)
  } else if (method === 'thread/renamed') {
    const threadId = stringField(p.threadId)
    const displayName = stringField(p.displayName)
    next = updateWorkspaceThread(threads, threadId, { displayName })
  } else if (method === 'thread/deleted') {
    next = removeWorkspaceThreadTree(threads, stringField(p.threadId))
  } else if (method === 'thread/archived') {
    next = removeWorkspaceThreadTree(threads, stringField(p.threadId))
  } else if (method === 'thread/unarchived') {
    const threadId = stringField(p.threadId)
    if (hasThread(threads, threadId)) {
      next = updateWorkspaceThread(threads, threadId, { status: 'active' })
    } else if (threadId) {
      refreshThreadList = true
    }
  } else if (method === 'thread/status/changed') {
    next = updateWorkspaceThread(threads, stringField(p.threadId), { runtime: p.runtime })
  } else if (method === 'thread/readState/changed' && isRecord(p.readState)) {
    next = updateWorkspaceThread(threads, stringField(p.threadId), { readState: p.readState })
  }

  return {
    threads: next,
    changed: next !== threads,
    refreshThreadList
  }
}

export interface WorkspaceThreadListResult {
  data?: unknown[]
  readStates?: Record<string, unknown>
}

function withReadStates(
  entry: WorkspaceThreadListRefreshState,
  result: WorkspaceThreadListResult,
  requestedAtMark: number
): unknown[] {
  const threads = Array.isArray(result.data) ? result.data : []
  const readStates = isRecord(result.readStates) ? result.readStates : null
  const { changedAt } = readStateClock(entry)
  return threads.map((thread) => {
    const threadId = getThreadId(thread)
    const cached = (changedAt.get(threadId) ?? 0) > requestedAtMark
      ? entry.threads.find((candidate) => getThreadId(candidate) === threadId)
      : undefined
    const readState = (isRecord(cached) ? cached.readState : undefined) ?? readStates?.[threadId]
    return isRecord(thread) && isRecord(readState) ? { ...thread, readState } : thread
  })
}

export function noteWorkspaceThreadNotification(
  entry: WorkspaceThreadListRefreshState,
  method: string,
  params: unknown
): void {
  const p = isRecord(params) ? params : {}
  const id = stringField(p.threadId)
  if (method === 'thread/readState/changed' && id) noteWorkspaceReadStateChange(entry, id)
  const directory = threadDirectories.get(entry)
  if (!directory) return
  if (method === 'thread/deleted' || method === 'thread/archived') {
    directory.removedIds.add(id)
  } else if (method === 'thread/unarchived') {
    directory.removedIds.delete(id)
  } else {
    let patch: Record<string, unknown> | undefined
    let threadId = id
    if (method === 'thread/started' && isRecord(p.thread)) {
      threadId = getThreadId(p.thread)
      directory.baselineIds.delete(threadId)
      patch = p.thread
    } else if (method === 'thread/renamed') {
      patch = { displayName: p.displayName }
    } else if (method === 'thread/status/changed') {
      patch = { runtime: p.runtime }
    } else if (method === 'thread/readState/changed') {
      patch = { readState: p.readState }
    }
    if (threadId && patch) directory.updates.set(threadId, { ...directory.updates.get(threadId), ...patch })
  }
}

function createThreadDirectory(entry: WorkspaceThreadListRefreshState): WorkspaceThreadDirectory {
  const directory: WorkspaceThreadDirectory = {
    cursor: '', requestedAtMark: readStateClock(entry).sequence, seenIds: new Set(),
    baselineIds: new Set(entry.threads.map(getThreadId)), removedIds: new Set(), updates: new Map()
  }
  threadDirectories.set(entry, directory)
  return directory
}

function mergeThreadDirectoryPage(
  entry: WorkspaceThreadListRefreshState,
  directory: WorkspaceThreadDirectory,
  result: WorkspaceThreadListResult,
  complete: boolean
): unknown[] {
  const incoming = withReadStates(entry, result, directory.requestedAtMark)
    .filter(thread => !directory.seenIds.has(getThreadId(thread)))
    .filter(thread => {
      const source = isRecord(thread) && isRecord(thread.source) ? thread.source : null
      const subAgent = isRecord(source?.subAgent) ? source.subAgent : null
      return ![getThreadId(thread), getSubAgentParentThreadId(thread), stringField(subAgent?.rootThreadId)]
        .some(id => id && directory.removedIds.has(id))
    })
    .map(thread => isRecord(thread) ? { ...thread, ...directory.updates.get(getThreadId(thread)) } : thread)
  const merged = new Map(entry.threads.map(thread => [getThreadId(thread), thread]))
  for (const thread of incoming) {
    const id = getThreadId(thread)
    directory.seenIds.add(id)
    merged.set(id, thread)
  }
  return [...merged.values()].filter(thread => {
    const id = getThreadId(thread)
    return !complete || directory.seenIds.has(id) || !directory.baselineIds.has(id)
  })
}

function applyWorkspaceThreadListRefreshSuccess(
  entry: WorkspaceThreadListRefreshState,
  directory: WorkspaceThreadDirectory,
  result: WorkspaceThreadListResult
): void {
  advanceThreadListRevision(entry)
  cancelWorkspaceThreadListRetry(entry)
  entry.threads = mergeThreadDirectoryPage(entry, directory, result, true)
  entry.threadListLoaded = true
  threadDirectories.delete(entry)
  entry.errorMessage = undefined
}

function isWorkspaceThreadDirectoryRequest(
  entry: WorkspaceThreadListRefreshState & { workspacePath: string },
  method: string,
  params: unknown
): params is Record<string, unknown> {
  if (method !== 'thread/list' || !isRecord(params)) return false
  const identity = isRecord(params.identity) ? params.identity : null
  if (!sameWorkspaceProjectKey(stringField(identity?.workspacePath), entry.workspacePath)) return false
  if (params.scope !== 'workspace' || params.includeSubAgents !== true) return false
  if (params.includeArchived === true || params.includeInternal === true) return false
  if (stringField(params.query) || stringField(params.channelName)) return false
  return true
}

export function beginWorkspaceThreadListRequest(
  entry: WorkspaceThreadListRefreshState & { workspacePath: string },
  method: string,
  params: unknown
): number | undefined {
  if (!isWorkspaceThreadDirectoryRequest(entry, method, params)) return undefined
  const cursor = stringField(params.cursor)
  let directory = threadDirectories.get(entry)
  if (cursor) {
    if (!directory || directory.cursor !== cursor) return undefined
  } else {
    directory = createThreadDirectory(entry)
    entry.threadListLoaded = false
  }
  directory.requestedAtMark = readStateClock(entry).sequence
  return advanceThreadListRevision(entry)
}

export function applyWorkspaceThreadListRequestResult(
  entry: WorkspaceThreadListRefreshState & { workspacePath: string },
  method: string,
  params: unknown,
  result: unknown,
  requestGeneration: number | undefined
): boolean {
  if (requestGeneration == null || threadListRevisions.get(entry) !== requestGeneration) return false
  if (!isWorkspaceThreadDirectoryRequest(entry, method, params)) return false
  if (!isRecord(result) || !Array.isArray(result.data)) return false
  if (result.nextCursor != null && (typeof result.nextCursor !== 'string' || params.limit == null)) return false
  const directory = threadDirectories.get(entry)
  if (!directory || directory.cursor !== stringField(params.cursor)) return false
  directory.cursor = stringField(result.nextCursor)
  if (directory.cursor) {
    cancelWorkspaceThreadListRetry(entry)
    entry.threads = mergeThreadDirectoryPage(entry, directory, result, false)
    entry.errorMessage = undefined
  } else {
    applyWorkspaceThreadListRefreshSuccess(entry, directory, result)
  }
  return true
}

export function refreshWorkspaceThreadListCache(
  entry: WorkspaceThreadListRefreshState,
  load: () => Promise<WorkspaceThreadListResult>,
  options: WorkspaceThreadListRefreshOptions = {}
): Promise<boolean> {
  return refreshWorkspaceThreadListAttempt(entry, load, options, 0)
}

function cancelWorkspaceThreadListRetry(entry: WorkspaceThreadListRefreshState): void {
  const timer = threadListRetryTimers.get(entry)
  if (timer == null) return
  clearTimeout(timer)
  threadListRetryTimers.delete(entry)
}

async function refreshWorkspaceThreadListAttempt(
  entry: WorkspaceThreadListRefreshState,
  load: () => Promise<WorkspaceThreadListResult>,
  options: WorkspaceThreadListRefreshOptions,
  attempt: number
): Promise<boolean> {
  cancelWorkspaceThreadListRetry(entry)
  const revision = advanceThreadListRevision(entry)
  const directory = createThreadDirectory(entry)
  try {
    const result = await load()
    if (threadListRevisions.get(entry) !== revision) return false
    applyWorkspaceThreadListRefreshSuccess(entry, directory, result)
  } catch (error) {
    if (threadListRevisions.get(entry) !== revision) return false
    const retryDelayMs = options.retryDelaysMs?.[attempt]
    if (retryDelayMs == null) {
      applyWorkspaceThreadListRefreshFailure(entry, error)
      return true
    }
    threadListRetryTimers.set(entry, setTimeout(() => {
      threadListRetryTimers.delete(entry)
      if (options.isActive?.() === false) return
      void refreshWorkspaceThreadListAttempt(entry, load, options, attempt + 1).then((changed) => {
        if (changed) options.onRetryChanged?.()
      })
    }, retryDelayMs))
    return false
  }
  return true
}

export function applyWorkspaceThreadListRefreshFailure(
  entry: WorkspaceThreadListRefreshState,
  error: unknown
): void {
  entry.errorMessage = error instanceof Error ? error.message : String(error)
}
