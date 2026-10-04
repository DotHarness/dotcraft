import type { PendingRequest } from '../../core/state'
import type { I18n } from '../../i18n'

type Approval = Extract<PendingRequest, { kind: 'approval' }>

export function approvalTitle(request: Approval, t: I18n['t']): string {
  if (request.approvalType === 'shell') return t('approval.runCommand')
  if (request.approvalType === 'file') {
    if (request.operation === 'read' || request.operation === 'list') return t('approval.readFile')
    return t('approval.changeFile')
  }
  return t('approval.other')
}

export function subjectOf(request: Approval): string {
  if (request.approvalType === 'shell') return request.operation
  if (request.approvalType === 'file') return request.target
  return [request.operation, request.targetLabel ?? request.target].filter(Boolean).join(' · ')
}
