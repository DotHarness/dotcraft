import type { Thread } from '../types/thread'
import type { ThreadResumeResult } from '../stores/applyThreadRestoration'
import { runtimeSnapshotFromThread } from '../utils/threadRuntime'

export function makeRestoreResult(thread: Thread): ThreadResumeResult {
  const latest = thread.turns[thread.turns.length - 1]
  return {
    thread: { ...thread, runtime: thread.runtime ?? runtimeSnapshotFromThread(thread), turns: [] },
    instructionSources: [],
    currentState: {
      turnId: latest?.id,
      turnStatus: latest?.status,
      itemCount: latest?.items?.length ?? 0,
      pendingRequests: []
    },
    initialTurnsPage: { data: [...thread.turns].reverse(), nextCursor: null }
  } as unknown as ThreadResumeResult
}
