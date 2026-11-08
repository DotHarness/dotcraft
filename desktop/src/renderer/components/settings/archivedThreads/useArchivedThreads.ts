import { useCallback, useEffect, useRef, useState } from 'react'
import type { ThreadSummary } from '../../../types/thread'
import { archivedThreadsFromList, type ArchivedThreadSource } from './archivedThreadsModel'

export interface ArchivedSourceState {
  status: 'loading' | 'ready' | 'error'
  threads: ThreadSummary[]
  error?: string
}

export interface ArchivedThreadsLoader {
  states: ReadonlyMap<string, ArchivedSourceState>
  reload(sourceKey?: string): void
  removeThread(sourceKey: string, threadId: string): void
}

export function useArchivedThreads(sources: ArchivedThreadSource[]): ArchivedThreadsLoader {
  const [states, setStates] = useState<Map<string, ArchivedSourceState>>(() => new Map())
  const liveRef = useRef(new Map<string, ArchivedThreadSource>())
  const inflightRef = useRef(new Map<string, { pending: boolean }>())
  const live = sources.filter((source) => source.live)
  const liveSignature = live.map((source) => `${source.key}\u0000${source.identityWorkspacePath}`).join('\u0001')
  liveRef.current = new Map(live.map((source) => [source.key, source]))

  const loadSource = useCallback(async (key: string): Promise<void> => {
    const inflight = inflightRef.current.get(key)
    if (inflight) {
      inflight.pending = true
      return
    }
    const source = liveRef.current.get(key)
    if (!source) return
    const marker = { pending: false }
    inflightRef.current.set(key, marker)
    setStates((current) => withState(current, key, { status: 'loading', threads: current.get(key)?.threads ?? [] }))
    try {
      const result = await window.api.appServer.sendConnectionRequest(key, 'thread/list', {
        identity: desktopIdentity(source.identityWorkspacePath),
        scope: 'workspace',
        includeArchived: true
      })
      const threads = archivedThreadsFromList((result as { data?: ThreadSummary[] }).data ?? [])
      if (liveRef.current.has(key)) setStates((current) => withState(current, key, { status: 'ready', threads }))
    } catch (error) {
      if (liveRef.current.has(key)) {
        setStates((current) => withState(current, key, {
          status: 'error',
          threads: [],
          error: error instanceof Error ? error.message : String(error)
        }))
      }
    } finally {
      inflightRef.current.delete(key)
      if (marker.pending) void loadSource(key)
    }
  }, [])

  useEffect(() => {
    const keys = new Set(liveRef.current.keys())
    setStates((current) => {
      const next = new Map([...current].filter(([key]) => keys.has(key)))
      return next.size === current.size ? current : next
    })
    for (const key of keys) void loadSource(key)
  }, [liveSignature, loadSource])

  const removeThread = useCallback((sourceKey: string, threadId: string) => {
    setStates((current) => {
      const state = current.get(sourceKey)
      if (!state?.threads.some((thread) => thread.id === threadId)) return current
      return withState(current, sourceKey, { ...state, threads: state.threads.filter((thread) => thread.id !== threadId) })
    })
  }, [])

  useEffect(() => window.api.appServer.onNotificationRaw(({ method, params }) => {
    if (method === 'thread/archived') {
      for (const key of liveRef.current.keys()) void loadSource(key)
      return
    }
    if (method !== 'thread/unarchived' && method !== 'thread/deleted') return
    const threadId = (params as { threadId?: unknown } | null)?.threadId
    if (typeof threadId !== 'string') return
    for (const key of liveRef.current.keys()) removeThread(key, threadId)
  }), [loadSource, removeThread])

  const reload = useCallback((sourceKey?: string) => {
    const keys = sourceKey ? [sourceKey] : [...liveRef.current.keys()]
    for (const key of keys) void loadSource(key)
  }, [loadSource])

  return { states, reload, removeThread }
}

function withState(
  current: Map<string, ArchivedSourceState>,
  key: string,
  state: ArchivedSourceState
): Map<string, ArchivedSourceState> {
  const next = new Map(current)
  next.set(key, state)
  return next
}

function desktopIdentity(workspacePath: string) {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}
