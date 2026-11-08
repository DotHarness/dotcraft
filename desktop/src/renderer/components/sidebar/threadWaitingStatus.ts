import { getThreadConversation, selectLatestCreatePlanTurnId } from '../../stores/conversationStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadRuntimeSnapshot } from '../../types/thread'

export type ThreadWaitingStatus = 'approval' | 'input' | 'plan' | null

type WaitingRuntime = Pick<ThreadRuntimeSnapshot,
  'waitingOnApproval' | 'waitingOnInput' | 'waitingOnPlanConfirmation'>

function threadWaitingStatus(runtime: Partial<WaitingRuntime> | undefined): ThreadWaitingStatus {
  if (runtime?.waitingOnApproval) return 'approval'
  if (runtime?.waitingOnInput) return 'input'
  if (runtime?.waitingOnPlanConfirmation) return 'plan'
  return null
}

export function useThreadWaitingStatus(
  threadId: string,
  runtime: Partial<WaitingRuntime> | undefined,
  hidePlan = false
): ThreadWaitingStatus {
  const dismissed = useUIStore(state => state.planApprovalDismissed)
  const status = threadWaitingStatus(runtime)
  if (status !== 'plan') return status
  if (hidePlan) return null
  const conversation = getThreadConversation(threadId)?.getState()
  const planTurnId = conversation ? selectLatestCreatePlanTurnId(conversation) : null
  return planTurnId && dismissed[planTurnId] ? null : status
}
