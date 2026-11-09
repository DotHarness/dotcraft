import type { PendingRequest } from '../../core/state'
import type { I18n } from '../../i18n'

type Approval = Extract<PendingRequest, { kind: 'approval' }>

export function approvalTitle(request: Approval, t: I18n['t']): string {
  if (request.approvalType === 'shell') return t('approval.runCommand')
  if (request.approvalType === 'file') return t('approval.fileOperation')
  if (request.approvalType === 'remoteResource') return t('approval.remoteResource')
  if (request.approvalType === 'computerUse') return t('approval.computerUse', { app: request.targetLabel ?? request.target })
  return t('approval.other')
}

export function subjectOf(request: Approval): string {
  if (request.approvalType === 'shell') return request.operation
  if (request.approvalType === 'file') return request.target
  return [request.operation, request.targetLabel ?? request.target].filter(Boolean).join(' · ')
}
