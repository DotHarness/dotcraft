export interface WorkspaceThreadCacheUpdateResult {
  threads: unknown[]
  changed: boolean
  refreshThreadList: boolean
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
  if (getThreadStatus(thread) === 'archived') return removeWorkspaceThreadTree(threads, id)

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
  } else if (method === 'thread/statusChanged') {
    const threadId = stringField(p.threadId)
    const newStatus = stringField(p.newStatus).toLowerCase()
    const previousStatus = stringField(p.previousStatus).toLowerCase()
    if (newStatus === 'archived') {
      next = removeWorkspaceThreadTree(threads, threadId)
    } else if (hasThread(threads, threadId)) {
      next = updateWorkspaceThread(threads, threadId, { status: newStatus })
    } else if (threadId && previousStatus === 'archived') {
      refreshThreadList = true
    }
  } else if (method === 'thread/runtimeChanged') {
    next = updateWorkspaceThread(threads, stringField(p.threadId), { runtime: p.runtime })
  }

  return {
    threads: next,
    changed: next !== threads,
    refreshThreadList
  }
}
