import type { StoreApi } from 'zustand'
import type { ThreadStore } from './threadStore'
import { applyThreadRuntimeSnapshot } from './threadRuntimeState'

export interface ParkedApproval {
  bridgeId: string
  turnId: string | null
  rawParams: Record<string, unknown>
  connectionId?: string
}

export interface ParkedUserInput {
  bridgeId: string
  turnId: string | null
  rawParams: Record<string, unknown>
  connectionId?: string
}

function parkedApprovalKey(approval: ParkedApproval): string {
  const requestId = typeof approval.rawParams.requestId === 'string' ? approval.rawParams.requestId : ''
  if (requestId) return `request:${requestId}`
  const itemId = typeof approval.rawParams.itemId === 'string' ? approval.rawParams.itemId : ''
  if (itemId) return `item:${itemId}`
  return `bridge:${approval.bridgeId}`
}

function upsertParkedApproval(queue: ParkedApproval[], approval: ParkedApproval): ParkedApproval[] {
  const key = parkedApprovalKey(approval)
  const index = queue.findIndex((candidate) => parkedApprovalKey(candidate) === key)
  if (index < 0) return [...queue, approval]
  return queue.map((candidate, i) => (i === index ? approval : candidate))
}

let readStateSequence = 0
const readStateChangedAt = new Map<string, number>()

export function readStateMark(): number {
  return readStateSequence
}

export function readStateChangedSince(threadId: string, mark: number): boolean {
  return (readStateChangedAt.get(threadId) ?? 0) > mark
}

type RetainedActions = Pick<
  ThreadStore,
  | 'markTurnStarted'
  | 'markTurnEnded'
  | 'parkApproval'
  | 'consumeParkedApproval'
  | 'consumeParkedApprovals'
  | 'clearParkedApproval'
  | 'parkUserInput'
  | 'consumeParkedUserInput'
  | 'clearParkedUserInput'
  | 'applyRuntimeSnapshot'
  | 'setThreadReadState'
  | 'setThreadGoal'
  | 'clearThreadGoal'
  | 'hydrateThreadGoal'
  | 'releaseRetainedThread'
>

export function retainedThreadActions(
  set: StoreApi<ThreadStore>['setState'],
  _get: StoreApi<ThreadStore>['getState']
): RetainedActions {
  return {
    markTurnStarted(threadId) {
      set((state) => {
        if (state.hiddenThreadIds.has(threadId)) return state
        const next = new Set(state.runningTurnThreadIds)
        next.add(threadId)
        return { runningTurnThreadIds: next }
      })
    },

    markTurnEnded(threadId) {
      set((state) => {
        const next = new Set(state.runningTurnThreadIds)
        next.delete(threadId)
        return { runningTurnThreadIds: next }
      })
    },

    parkApproval(threadId, approval) {
      set((state) => {
        const parkedApprovals = new Map(state.parkedApprovals)
        parkedApprovals.set(threadId, upsertParkedApproval(parkedApprovals.get(threadId) ?? [], approval))
        return { parkedApprovals }
      })
    },

    consumeParkedApproval(threadId) {
      const state = _get()
      const approvals = state.parkedApprovals.get(threadId) ?? []
      const approval = approvals[0] ?? null
      if (!approval) return null
      const parkedApprovals = new Map(state.parkedApprovals)
      const remaining = approvals.slice(1)
      if (remaining.length > 0) {
        parkedApprovals.set(threadId, remaining)
      } else {
        parkedApprovals.delete(threadId)
      }
      set({ parkedApprovals })
      return approval
    },

    consumeParkedApprovals(threadId) {
      const state = _get()
      const approvals = state.parkedApprovals.get(threadId) ?? []
      if (approvals.length === 0) return []
      const parkedApprovals = new Map(state.parkedApprovals)
      parkedApprovals.delete(threadId)
      set({ parkedApprovals })
      return approvals
    },

    clearParkedApproval(threadId) {
      set((state) => {
        if (!state.parkedApprovals.has(threadId)) return state
        const parkedApprovals = new Map(state.parkedApprovals)
        parkedApprovals.delete(threadId)
        return { parkedApprovals }
      })
    },

    parkUserInput(threadId, request) {
      set((state) => {
        const parkedUserInputs = new Map(state.parkedUserInputs)
        parkedUserInputs.set(threadId, request)
        return { parkedUserInputs }
      })
    },

    consumeParkedUserInput(threadId) {
      const state = _get()
      const request = state.parkedUserInputs.get(threadId) ?? null
      if (!request) return null
      const parkedUserInputs = new Map(state.parkedUserInputs)
      parkedUserInputs.delete(threadId)
      set({ parkedUserInputs })
      return request
    },

    clearParkedUserInput(threadId) {
      set((state) => {
        if (!state.parkedUserInputs.has(threadId)) return state
        const parkedUserInputs = new Map(state.parkedUserInputs)
        parkedUserInputs.delete(threadId)
        return { parkedUserInputs }
      })
    },

    applyRuntimeSnapshot(threadId, runtime, options) {
      set((state) => applyThreadRuntimeSnapshot(state, threadId, runtime, options))
    },

    setThreadReadState(threadId, readState) {
      readStateChangedAt.set(threadId, ++readStateSequence)
      set((state) => ({
        threadList: state.threadList.map((thread) => (thread.id === threadId ? { ...thread, readState } : thread)),
        activeThread: state.activeThread?.id === threadId ? { ...state.activeThread, readState } : state.activeThread
      }))
    },

    setThreadGoal(goal) {
      set((state) => {
        const goalSnapshots = new Map(state.goalSnapshots)
        goalSnapshots.set(goal.threadId, goal)
        return {
          goalSnapshots,
          threadList: state.threadList.map((thread) => (thread.id === goal.threadId ? { ...thread, goal } : thread)),
          activeThread: state.activeThread?.id === goal.threadId ? { ...state.activeThread, goal } : state.activeThread
        }
      })
    },

    clearThreadGoal(threadId) {
      set((state) => {
        const goalSnapshots = new Map(state.goalSnapshots)
        goalSnapshots.delete(threadId)
        return {
          goalSnapshots,
          threadList: state.threadList.map((thread) => (thread.id === threadId ? { ...thread, goal: null } : thread)),
          activeThread: state.activeThread?.id === threadId ? { ...state.activeThread, goal: null } : state.activeThread
        }
      })
    },

    hydrateThreadGoal(threadId, goal) {
      if (goal === undefined) return
      if (goal === null) {
        _get().clearThreadGoal(threadId)
        return
      }
      _get().setThreadGoal(goal)
    },

    releaseRetainedThread(threadId) {
      set((state) => {
        const parkedApprovals = new Map(state.parkedApprovals)
        const parkedUserInputs = new Map(state.parkedUserInputs)
        const runtimeSnapshots = new Map(state.runtimeSnapshots)
        const goalSnapshots = new Map(state.goalSnapshots)
        const runningTurnThreadIds = new Set(state.runningTurnThreadIds)
        const pendingApprovalThreadIds = new Set(state.pendingApprovalThreadIds)
        const pendingUserInputThreadIds = new Set(state.pendingUserInputThreadIds)
        const pendingPlanConfirmationThreadIds = new Set(state.pendingPlanConfirmationThreadIds)
        for (const map of [parkedApprovals, parkedUserInputs, runtimeSnapshots, goalSnapshots]) map.delete(threadId)
        for (const values of [
          runningTurnThreadIds,
          pendingApprovalThreadIds,
          pendingUserInputThreadIds,
          pendingPlanConfirmationThreadIds
        ])
          values.delete(threadId)
        return {
          parkedApprovals,
          parkedUserInputs,
          runtimeSnapshots,
          goalSnapshots,
          runningTurnThreadIds,
          pendingApprovalThreadIds,
          pendingUserInputThreadIds,
          pendingPlanConfirmationThreadIds
        }
      })
    }
  }
}
