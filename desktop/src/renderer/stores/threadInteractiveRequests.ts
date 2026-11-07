import { getThreadConversation } from './conversationStore'
import { useThreadStore } from './threadStore'

export function activateParkedInteractiveRequests(threadId: string): void {
  const threads = useThreadStore.getState()
  if (threads.activeThreadId !== threadId) return
  const conversation = getThreadConversation(threadId)?.getState()
  if (!conversation) return
  for (const request of threads.consumeParkedApprovals(threadId)) {
    conversation.onApprovalRequest(request.bridgeId, request.rawParams)
  }
  const input = threads.consumeParkedUserInput(threadId)
  if (input) conversation.onUserInputRequest(input.bridgeId, input.rawParams)
}

export function parkThreadInteractiveRequests(threadId: string): void {
  const conversation = getThreadConversation(threadId)?.getState()
  if (!conversation) return
  const threads = useThreadStore.getState()
  for (const approval of conversation.pendingApprovals) {
    threads.parkApproval(threadId, {
      bridgeId: approval.bridgeId, turnId: approval.turnId,
      rawParams: { ...approval, threadId }
    })
  }
  const input = conversation.pendingUserInput
  if (input) threads.parkUserInput(threadId, {
    bridgeId: input.bridgeId, turnId: input.turnId,
    rawParams: { ...input }
  })
}

export function resolveParkedApproval(threadId: string | null | undefined, requestId: string | null | undefined): void {
  if (!threadId || !requestId) return
  const threads = useThreadStore.getState()
  for (const approval of threads.consumeParkedApprovals(threadId)) {
    if (approval.rawParams.requestId !== requestId) threads.parkApproval(threadId, approval)
  }
}
