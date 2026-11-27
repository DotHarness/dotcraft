import { create } from 'zustand'
import { useOwnedThreadsStore } from './ownedThreadsStore'
import { useThreadStore } from './threadStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'

interface OwnedThreadUnreadEntry {
  count: number
  lastRevision: string | null
}

interface OwnedThreadActivityState {
  byThreadId: Readonly<Record<string, OwnedThreadUnreadEntry>>
  syncFromStores(): void
  reset(): void
}

const MAX_TRACKED_COUNT = 100

export function findThreadAcrossWorkspaces(
  threadId: string,
  foregroundThreads: readonly ThreadSummary[],
  chatThreads: readonly ThreadSummary[] | undefined,
  projects: readonly { threads: unknown[] }[]
): ThreadSummary | undefined {
  const foreground = foregroundThreads.find((candidate) => candidate.id === threadId)
  if (foreground) return foreground
  const inChat = chatThreads?.find((candidate) => candidate.id === threadId)
  if (inChat) return inChat
  for (const project of projects) {
    const match = (project.threads as ThreadSummary[]).find((candidate) => candidate.id === threadId)
    if (match) return match
  }
  return undefined
}

export function useThreadAcrossWorkspaces(threadId: string | null | undefined): ThreadSummary | undefined {
  const foreground = useThreadStore((s) => (threadId ? s.threadList.find((candidate) => candidate.id === threadId) : undefined))
  const inWorkspace = useWorkspaceProjectsStore((s) => {
    if (!threadId) return undefined
    const chatMatch = (s.chat?.threads as ThreadSummary[] | undefined)?.find((candidate) => candidate.id === threadId)
    if (chatMatch) return chatMatch
    for (const project of s.projects) {
      const match = (project.threads as ThreadSummary[]).find((candidate) => candidate.id === threadId)
      if (match) return match
    }
    return undefined
  })
  return foreground ?? inWorkspace
}

export const useOwnedThreadActivityStore = create<OwnedThreadActivityState>((set, get) => ({
  byThreadId: {},

  syncFromStores() {
    const records = useOwnedThreadsStore.getState().records
    const threadState = useThreadStore.getState()
    const workspaceState = useWorkspaceProjectsStore.getState()
    const current = get().byThreadId
    let changed = false
    const next: Record<string, OwnedThreadUnreadEntry> = {}

    for (const record of records) {
      const thread = findThreadAcrossWorkspaces(
        record.threadId,
        threadState.threadList,
        workspaceState.chat?.threads as ThreadSummary[] | undefined,
        workspaceState.projects
      )
      const prev = current[record.threadId]
      const readState = thread?.readState
      if (!readState) {
        if (prev) next[record.threadId] = prev
        continue
      }
      if (readState.firstUnread == null) {
        if (prev && (prev.count !== 0 || prev.lastRevision !== readState.revision)) changed = true
        next[record.threadId] = { count: 0, lastRevision: readState.revision }
        continue
      }
      if (!prev) {
        changed = true
        next[record.threadId] = { count: 1, lastRevision: readState.revision }
      } else if (prev.lastRevision !== readState.revision) {
        changed = true
        next[record.threadId] = {
          count: Math.min(MAX_TRACKED_COUNT, Math.max(1, prev.count + 1)),
          lastRevision: readState.revision
        }
      } else {
        const count = Math.max(1, prev.count)
        if (count !== prev.count) changed = true
        next[record.threadId] = { count, lastRevision: prev.lastRevision }
      }
    }

    if (!changed && Object.keys(current).length === Object.keys(next).length) return
    set({ byThreadId: next })
  },

  reset() {
    set({ byThreadId: {} })
  }
}))

export function startOwnedThreadActivityTracking(): () => void {
  const sync = (): void => useOwnedThreadActivityStore.getState().syncFromStores()
  sync()
  const stops = [
    useOwnedThreadsStore.subscribe(sync),
    useThreadStore.subscribe(sync),
    useWorkspaceProjectsStore.subscribe(sync)
  ]
  return () => {
    stops.forEach((stop) => stop())
  }
}

export function useOwnedThreadUnreadCount(threadId: string | null | undefined): number {
  return useOwnedThreadActivityStore((s) => (threadId ? s.byThreadId[threadId]?.count ?? 0 : 0))
}
