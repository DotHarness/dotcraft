import { isHiddenThread } from '../shared/hiddenThreads'
import { sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'

export interface WorkspaceThreadCacheUpdateResult {
  threads: unknown[]
  changed: boolean
  refreshThreadList: boolean
}

export interface WorkspaceThreadListRefreshState {
  threads: unknown[]
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

function withReadStates(result: WorkspaceThreadListResult): unknown[] {
  const threads = Array.isArray(result.data) ? result.data : []
  const readStates = isRecord(result.readStates) ? result.readStates : null
  if (!readStates) return threads
  return threads.map((thread) => {
    const readState = readStates[getThreadId(thread)]
    return isRecord(thread) && isRecord(readState) ? { ...thread, readState } : thread
  })
}

export function applyWorkspaceThreadListRefreshSuccess(
  entry: WorkspaceThreadListRefreshState,
  result: WorkspaceThreadListResult
): void {
  advanceThreadListRevision(entry)
  cancelWorkspaceThreadListRetry(entry)
  entry.threads = withReadStates(result)
  entry.errorMessage = undefined
}

function isCompleteWorkspaceThreadListRequest(
  entry: WorkspaceThreadListRefreshState & { workspacePath: string },
  method: string,
  params: unknown
): boolean {
  if (method !== 'thread/list' || !isRecord(params)) return false
  const identity = isRecord(params.identity) ? params.identity : null
  if (!sameWorkspaceProjectKey(stringField(identity?.workspacePath), entry.workspacePath)) return false
  if (params.scope !== 'workspace' || params.includeSubAgents !== true) return false
  if (params.includeArchived === true || params.includeInternal === true) return false
  if (params.limit != null || stringField(params.cursor) || stringField(params.query) || stringField(params.channelName)) return false
  return true
}

export function beginWorkspaceThreadListRequest(
  entry: WorkspaceThreadListRefreshState & { workspacePath: string },
  method: string,
  params: unknown
): number | undefined {
  if (!isCompleteWorkspaceThreadListRequest(entry, method, params)) return undefined
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
  if (!isCompleteWorkspaceThreadListRequest(entry, method, params)) return false
  if (!isRecord(result) || !Array.isArray(result.data) || result.nextCursor != null) return false

  applyWorkspaceThreadListRefreshSuccess(entry, result)
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
  try {
    const result = await load()
    if (threadListRevisions.get(entry) !== revision) return false
    applyWorkspaceThreadListRefreshSuccess(entry, result)
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
