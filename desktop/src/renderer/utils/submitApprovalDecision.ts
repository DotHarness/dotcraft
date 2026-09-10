import { useConversationStore, type PendingApproval } from '../stores/conversationStore'
import type { ApprovalDecision } from '../types/conversation'
import { approvalRequestTarget } from './approvalRequest'

export async function submitApprovalDecision(request: PendingApproval, value: string): Promise<void> {
  if (request.submit) {
    await request.submit(value)
    return
  }
  const decision = value as ApprovalDecision
  const target = approvalRequestTarget(request)
  useConversationStore.getState().onApprovalSubmitStarted(decision, target)
  try {
    await window.api.appServer.sendServerResponse(request.bridgeId, { decision })
    useConversationStore.getState().onApprovalDecision(decision, target)
  } catch (error) {
    useConversationStore.getState().onApprovalSubmitFailed(target)
    throw error
  }
}
