import { getThreadConversation, selectLatestCreatePlanTurnId } from '../../stores/conversationStore'
import { useThreadAwaitsOfferAnswer } from '../../stores/threadTasksStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadRuntimeSnapshot } from '../../types/thread'
import { turnNextStep } from '../../utils/threadToolDisplay'

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
  const awaitsOfferAnswer = useThreadAwaitsOfferAnswer(threadId)
  const awaitsAnswer = awaitsOfferAnswer || awaitsNextStepAnswer(threadId)
  const status = threadWaitingStatus(awaitsAnswer ? { ...runtime, waitingOnInput: true } : runtime)
  if (status !== 'plan') return status
  if (hidePlan) return null
  const conversation = getThreadConversation(threadId)?.getState()
  const planTurnId = conversation ? selectLatestCreatePlanTurnId(conversation) : null
  return planTurnId && dismissed[planTurnId] ? null : status
}

function awaitsNextStepAnswer(threadId: string): boolean {
  const latest = getThreadConversation(threadId)?.getState().turns.at(-1)
  return latest?.status === 'completed' && turnNextStep(latest.items)?.blocked === true
}
