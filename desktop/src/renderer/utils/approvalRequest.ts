import type { PendingApproval } from '../stores/conversationStore'
import type { ApprovalType } from '../types/conversation'

export function approvalRequestKey(request: PendingApproval): string {
  return `${request.source ?? 'tool'}:${request.requestId || request.itemId || request.bridgeId}`
}

export function approvalRequestTarget(request: PendingApproval): {
  bridgeId: string
  threadId: string | null
  turnId: string | null
  requestId: string
  itemId: string
} {
  return {
    bridgeId: request.bridgeId,
    threadId: request.threadId,
    turnId: request.turnId,
    requestId: request.requestId,
    itemId: request.itemId
  }
}

export function approvalQuestionKey(type: ApprovalType): string {
  if (type === 'file') return 'approval.question.file'
  if (type === 'remoteResource') return 'approval.question.remoteResource'
  if (type === 'skill') return 'approval.question.skill'
  return 'approval.question.shell'
}
