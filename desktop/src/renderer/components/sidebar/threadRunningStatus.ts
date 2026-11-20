import { useThreadStore } from '../../stores/threadStore'
import type { ThreadSummary } from '../../types/thread'
import { isThreadRuntimeActive } from '../../utils/threadRuntime'

export function useThreadShowsRunning(thread: ThreadSummary): boolean {
  const turnStarted = useThreadStore((s) => s.runningTurnThreadIds.has(thread.id))
  const snapshot = useThreadStore((s) => s.runtimeSnapshots.get(thread.id))
  return turnStarted || isThreadRuntimeActive(snapshot ?? thread.runtime)
}
