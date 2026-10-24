import type { Thread, ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'

export function normalizeThreadRuntime(runtime: Partial<ThreadRuntimeSnapshot>): ThreadRuntimeSnapshot {
  return {
    running: runtime.running === true,
    activeTurnId: runtime.activeTurnId ?? null,
    activeTurnStartedAt: runtime.activeTurnStartedAt ?? null,
    busy: runtime.busy === true,
    waitingOnApproval: runtime.waitingOnApproval === true,
    waitingOnInput: runtime.waitingOnInput === true,
    waitingOnPlanConfirmation: runtime.waitingOnPlanConfirmation === true,
    maintenanceKind: runtime.maintenanceKind ?? null
  }
}

export function normalizeThreadSummaryRuntime<T extends ThreadSummary>(thread: T): T {
  return thread.runtime ? { ...thread, runtime: normalizeThreadRuntime(thread.runtime) } : thread
}

export function runtimeSnapshotFromThread(thread: Thread): ThreadRuntimeSnapshot {
  if (thread.runtime) return normalizeThreadRuntime(thread.runtime)
  const activeTurn = thread.turns?.find((turn) =>
    turn.status === 'running' || turn.status === 'waitingApproval' || turn.status === 'waitingInput'
  )
  return normalizeThreadRuntime({
    running: activeTurn != null,
    activeTurnId: activeTurn?.id,
    activeTurnStartedAt: activeTurn?.createdAt,
    busy: activeTurn != null,
    waitingOnApproval: activeTurn?.status === 'waitingApproval',
    waitingOnInput: activeTurn?.status === 'waitingInput'
  })
}

export function syncActiveThreadRuntime(activeThread: Thread | null, threads: ThreadSummary[]): Thread | null {
  if (!activeThread) return activeThread
  const runtime = threads.find((thread) => thread.id === activeThread.id)?.runtime
  return runtime ? { ...activeThread, runtime } : activeThread
}
