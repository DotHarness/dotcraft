import type { ThreadRuntimeSnapshot } from '../types/thread'
import type { ThreadStore } from './threadStore'
import { normalizeThreadRuntime } from '../utils/threadRuntime'

export interface ApplyRuntimeSnapshotOptions {
  isDesktopOrigin: boolean
  baseline?: ReadonlyMap<string, ThreadRuntimeSnapshot>
}

type RuntimeCollections = Pick<ThreadStore,
  | 'runtimeSnapshots' | 'runningTurnThreadIds'
  | 'parkedApprovals' | 'parkedUserInputs'
  | 'pendingApprovalThreadIds' | 'pendingUserInputThreadIds'
  | 'pendingPlanConfirmationThreadIds'
>

type RuntimeState = RuntimeCollections & Pick<ThreadStore, 'threadList' | 'activeThread' | 'hiddenThreadIds'>

// Collections must be cloned by the caller before hydration.
export function hydrateThreadRuntimeCollections(
  state: RuntimeCollections,
  threadId: string,
  runtime: ThreadRuntimeSnapshot,
  isDesktopOrigin: boolean
): void {
  state.runtimeSnapshots.set(threadId, runtime)
  if (runtime.running) state.runningTurnThreadIds.add(threadId)
  else state.runningTurnThreadIds.delete(threadId)
  if (!runtime.waitingOnApproval) state.parkedApprovals.delete(threadId)
  if (!runtime.waitingOnInput) state.parkedUserInputs.delete(threadId)

  if (runtime.waitingOnApproval && isDesktopOrigin) state.pendingApprovalThreadIds.add(threadId)
  else state.pendingApprovalThreadIds.delete(threadId)
  if (runtime.waitingOnInput && isDesktopOrigin) state.pendingUserInputThreadIds.add(threadId)
  else state.pendingUserInputThreadIds.delete(threadId)
  if (runtime.waitingOnPlanConfirmation && isDesktopOrigin) state.pendingPlanConfirmationThreadIds.add(threadId)
  else state.pendingPlanConfirmationThreadIds.delete(threadId)
}

export function applyThreadRuntimeSnapshot(
  state: RuntimeState,
  threadId: string,
  incoming: ThreadRuntimeSnapshot,
  options: ApplyRuntimeSnapshotOptions
): Partial<RuntimeState> {
  if (state.hiddenThreadIds.has(threadId)
    || (options.baseline && state.runtimeSnapshots.get(threadId) !== options.baseline.get(threadId))) return state
  const runtime = normalizeThreadRuntime(incoming)
  const collections: RuntimeCollections = {
    runtimeSnapshots: new Map(state.runtimeSnapshots),
    runningTurnThreadIds: new Set(state.runningTurnThreadIds),
    parkedApprovals: new Map(state.parkedApprovals),
    parkedUserInputs: new Map(state.parkedUserInputs),
    pendingApprovalThreadIds: new Set(state.pendingApprovalThreadIds),
    pendingUserInputThreadIds: new Set(state.pendingUserInputThreadIds),
    pendingPlanConfirmationThreadIds: new Set(state.pendingPlanConfirmationThreadIds)
  }
  hydrateThreadRuntimeCollections(collections, threadId, runtime, options.isDesktopOrigin)
  return {
    threadList: state.threadList.map((thread) => thread.id === threadId ? { ...thread, runtime } : thread),
    activeThread: state.activeThread?.id === threadId ? { ...state.activeThread, runtime } : state.activeThread,
    ...collections
  }
}
