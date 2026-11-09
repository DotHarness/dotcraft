import type { ThreadRuntimeSnapshot } from '../types/thread'
import type { ThreadStore } from './threadStore'
import { normalizeThreadRuntime } from '../utils/threadRuntime'

export interface ApplyRuntimeSnapshotOptions {
  isDesktopOrigin: boolean
  baseline?: ReadonlyMap<string, ThreadRuntimeSnapshot>
}

type RuntimeState = Pick<ThreadStore,
  | 'threadList' | 'activeThread' | 'hiddenThreadIds'
  | 'runtimeSnapshots' | 'runningTurnThreadIds'
  | 'parkedApprovals' | 'parkedUserInputs'
  | 'pendingApprovalThreadIds' | 'pendingUserInputThreadIds'
  | 'pendingPlanConfirmationThreadIds'
>

export function applyThreadRuntimeSnapshot(
  state: RuntimeState,
  threadId: string,
  incoming: ThreadRuntimeSnapshot,
  options: ApplyRuntimeSnapshotOptions
): Partial<RuntimeState> {
  if (state.hiddenThreadIds.has(threadId)
    || (options.baseline && state.runtimeSnapshots.get(threadId) !== options.baseline.get(threadId))) return state
  const runtime = normalizeThreadRuntime(incoming)
  const runtimeSnapshots = new Map(state.runtimeSnapshots)
  runtimeSnapshots.set(threadId, runtime)

  const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
  if (runtime.running) runningTurnThreadIds.add(threadId)
  else runningTurnThreadIds.delete(threadId)

  const parkedApprovals = new Map(state.parkedApprovals)
  if (!runtime.waitingOnApproval) parkedApprovals.delete(threadId)
  const parkedUserInputs = new Map(state.parkedUserInputs)
  if (!runtime.waitingOnInput) parkedUserInputs.delete(threadId)

  const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
  if (runtime.waitingOnApproval && options.isDesktopOrigin) {
    pendingApprovalThreadIds.add(threadId)
  } else {
    pendingApprovalThreadIds.delete(threadId)
  }

  const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
  if (runtime.waitingOnInput && options.isDesktopOrigin) {
    pendingUserInputThreadIds.add(threadId)
  } else {
    pendingUserInputThreadIds.delete(threadId)
  }

  const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
  if (runtime.waitingOnPlanConfirmation && options.isDesktopOrigin) {
    pendingPlanConfirmationThreadIds.add(threadId)
  } else {
    pendingPlanConfirmationThreadIds.delete(threadId)
  }

  return {
    threadList: state.threadList.map((thread) => thread.id === threadId ? { ...thread, runtime } : thread),
    activeThread: state.activeThread?.id === threadId ? { ...state.activeThread, runtime } : state.activeThread,
    runtimeSnapshots,
    runningTurnThreadIds,
    parkedApprovals,
    parkedUserInputs,
    pendingApprovalThreadIds,
    pendingUserInputThreadIds,
    pendingPlanConfirmationThreadIds
  }
}
