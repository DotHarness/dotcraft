import type { PendingApproval, PendingUserInputRequest, UserInputQuestion } from '../stores/conversationStore'
import { normalizeApprovalShell, type ApprovalDecision, type ApprovalType } from '../types/conversation'

const APPROVAL_DECISIONS: readonly string[] = ['accept', 'acceptForSession', 'acceptAlways', 'decline', 'cancel']

function normalizeApprovalDecision(value: unknown): ApprovalDecision | null {
  return typeof value === 'string' && APPROVAL_DECISIONS.includes(value) ? value as ApprovalDecision : null
}

function normalizeApprovalType(value: unknown): ApprovalType {
  return value === 'file' || value === 'remoteResource' || value === 'computerUse'
    ? value
    : 'shell'
}

export function parsePendingApproval(
  bridgeId: string,
  params: Record<string, unknown>,
  fallbackTurnId: string | null
): PendingApproval {
  const targetLabel = typeof params.targetLabel === 'string' && params.targetLabel.trim()
    ? params.targetLabel
    : undefined
  return {
    bridgeId,
    threadId: typeof params.threadId === 'string' ? params.threadId : null,
    turnId: typeof params.turnId === 'string' ? params.turnId : fallbackTurnId,
    requestId: typeof params.requestId === 'string' ? params.requestId : '',
    locallySubmittedDecision: normalizeApprovalDecision(params.locallySubmittedDecision),
    itemId: typeof params.itemId === 'string' && params.itemId.trim().length > 0
      ? params.itemId
      : `approval-${bridgeId}`,
    approvalType: normalizeApprovalType(params.approvalType),
    operation: (params.operation as string) ?? '',
    target: (params.target as string) ?? '',
    targetLabel,
    reason: (params.reason as string) ?? '',
    shell: normalizeApprovalShell(params.shell)
  }
}

export function parseUserInputRequest(
  bridgeId: string,
  params: Record<string, unknown>
): PendingUserInputRequest | null {
  if (
    typeof params.threadId !== 'string'
    || typeof params.turnId !== 'string'
    || typeof params.requestId !== 'string'
    || typeof params.isBlocking !== 'boolean'
  ) return null

  const rawQuestions = Array.isArray(params.questions) ? params.questions : []
  const questions: UserInputQuestion[] = rawQuestions
    .map((raw) => {
      const q = raw as Record<string, unknown>
      const rawOptions = Array.isArray(q.options) ? q.options : []
      return {
        id: typeof q.id === 'string' ? q.id : '',
        header: typeof q.header === 'string' ? q.header : '',
        question: typeof q.question === 'string' ? q.question : '',
        isOther: q.isOther !== false,
        isSecret: q.isSecret === true,
        options: rawOptions
          .map((rawOption) => {
            const option = rawOption as Record<string, unknown>
            return {
              label: typeof option.label === 'string' ? option.label : '',
              description: typeof option.description === 'string' ? option.description : ''
            }
          })
          .filter((option) => option.label.trim().length > 0)
      }
    })
    .filter((q) => q.id.trim().length > 0 && q.question.trim().length > 0)

  return {
    bridgeId,
    threadId: params.threadId,
    requestId: params.requestId,
    turnId: params.turnId,
    isBlocking: params.isBlocking,
    questions
  }
}
