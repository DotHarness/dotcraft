import { useCallback, useEffect, useRef, useState } from 'react'
import type { AutomationRun } from '../../types/automation'
import type { ThreadSummary } from '../../types/thread'
import { useConnectionStore } from '../../stores/connectionStore'
import { useThreadStore } from '../../stores/threadStore'

export interface RunThread extends ThreadSummary { turns?: { status: string }[] }
export function runThreadBusy(thread: RunThread | undefined): boolean {
  return !!thread && (!!thread.runtime?.running || !!thread.runtime?.busy || !!thread.runtime?.waitingOnApproval
    || !!thread.runtime?.waitingOnInput || !!thread.runtime?.waitingOnPlanConfirmation
    || !!thread.turns?.some(turn => ['running', 'waitingApproval', 'waitingInput'].includes(turn.status)))
}

export function useAutomationRunThreads(runs: AutomationRun[]) {
  const [threads, setThreads] = useState<Record<string, RunThread>>({})
  const [error, setError] = useState<string | null>(null)
  const ids = JSON.stringify([...new Set(runs.flatMap(run => run.threadId ? [run.threadId] : []))].sort())
  const connection = useConnectionStore(state => state.status)
  const generation = useRef(0)
  const known = useRef<Record<string, RunThread>>({})
  const refresh = useCallback(async () => {
    const current = ++generation.current
    const next: Record<string, RunThread> = {}
    const pendingIds = JSON.parse(ids) as string[]
    let failure: string | null = null
    for (let start = 0; start < pendingIds.length; start += 8) {
      await Promise.all(pendingIds.slice(start, start + 8).map(async threadId => {
        try {
          const result = await window.api.appServer.sendRequest('thread/read', { threadId })
          const thread = (result as unknown as { thread?: RunThread }).thread
          if (!thread) throw new Error('automation.runUnavailable')
          next[threadId] = thread
        } catch (reason) { failure = String(reason) }
      }))
    }
    if (current === generation.current) {
      const restored = Object.values(next).filter(thread => known.current[thread.id]?.status === 'archived' && thread.status !== 'archived')
      if (restored.length) useThreadStore.getState().upsertThreads(restored)
      known.current = next
      setThreads(next); setError(failure)
    }
    return next
  }, [ids, connection])
  useEffect(() => {
    void refresh()
    const off = window.api.appServer.onNotification?.(payload => {
      if (payload.foreground === false) return
      const params = payload.params as { threadId?: string } | undefined
      if (payload.method === 'thread/statusChanged' && params?.threadId && (JSON.parse(ids) as string[]).includes(params.threadId)) void refresh()
    })
    return () => { generation.current++; off?.() }
  }, [refresh, ids])
  const runtime = useThreadStore(state => state.runtimeSnapshots)
  const currentThreads = Object.fromEntries(Object.entries(threads).map(([id, thread]) => [id,
    runtime.has(id) ? { ...thread, runtime: runtime.get(id), turns: undefined } : thread]))
  return { threads: currentThreads, refresh, error }
}
