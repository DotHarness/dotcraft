import type { CSSProperties } from 'react'
import { Cloud, File, SquareTerminal } from 'lucide-react'
import type { ConversationItem, ApprovalState, ApprovalType } from '../../types/conversation'
import { useT } from '../../contexts/LocaleContext'

interface ApprovalCardProps {
  item: ConversationItem
  /** Whether this is the active pending approval for the current turn. */
  isActive: boolean
}

const RESOLVED_LABELS: Record<ApprovalState, { labelKey: string; color: string } | null> = {
  pending: null,
  accepted: { labelKey: 'approval.resolved.accepted', color: 'var(--success)' },
  acceptedForSession: { labelKey: 'approval.resolved.acceptedForSession', color: 'var(--success)' },
  acceptedAlways: { labelKey: 'approval.resolved.acceptedAlways', color: 'var(--success)' },
  declined: { labelKey: 'approval.resolved.declined', color: 'var(--error)' },
  cancelled: { labelKey: 'approval.resolved.cancelled', color: 'var(--text-dimmed)' },
  timedOut: { labelKey: 'approval.resolved.timedOut', color: 'var(--warning)' }
}

/**
 * Inline approval status rendered inside the conversation stream.
 *
 * Pending approvals are handled by ApprovalDecisionComposer near the input
 * composer; this stream item remains a lightweight status marker.
 */
export function ApprovalCard({ item, isActive }: ApprovalCardProps): JSX.Element {
  const t = useT()
  const approvalType = item.approvalType ?? 'shell'
  const typeLabel = t(approvalTypeLabelKey(approvalType))
  const TypeIcon = approvalTypeIcon(approvalType)
  const operation = item.approvalOperation?.trim() ?? ''
  const target = item.approvalTarget?.trim() ?? ''
  const approvalState = item.approvalState ?? 'pending'
  const isPending = approvalState === 'pending'

  if (isPending) {
    const runningLabel = t('approval.running')
    const summary = approvalSummary(operation, target)
    return (
      <div
        role="status"
        aria-label={runningLabel}
        style={pendingStatusStyle}
      >
        <span style={{ color: 'var(--text-dimmed)', flexShrink: 0 }}>
          <TypeIcon size={15} strokeWidth={1.6} aria-hidden style={{ flexShrink: 0 }} />
        </span>
        <span className={isActive ? 'tool-running-gradient-text' : undefined}>
          {runningLabel}
        </span>
        {summary && (
          <>
            <span style={{ color: 'var(--text-dimmed)' }}>-</span>
            <span style={pendingSummaryStyle}>{summary}</span>
          </>
        )}
      </div>
    )
  }

  const resolved = RESOLVED_LABELS[approvalState]
  return (
    <div style={resolvedStatusStyle}>
      <span style={{ color: 'var(--text-dimmed)', flexShrink: 0 }}>
        <TypeIcon size={16} strokeWidth={1.5} aria-hidden style={{ flexShrink: 0 }} />
      </span>
      <span style={{ fontWeight: 500, color: 'var(--text-primary)' }}>{typeLabel}</span>
      {operation && (
        <>
          <span style={{ color: 'var(--text-dimmed)' }}>-</span>
          <span style={resolvedOperationStyle}>
            {operation}
          </span>
        </>
      )}
      {resolved && (
        <span style={{ color: resolved.color, fontWeight: 500, flexShrink: 0, marginLeft: 'auto' }}>
          {t(resolved.labelKey)}
        </span>
      )}
    </div>
  )
}

function approvalTypeLabelKey(type: ApprovalType): string {
  if (type === 'file') return 'approval.type.file'
  if (type === 'remoteResource') return 'approval.type.remoteResource'
  if (type === 'skill') return 'approval.kind.skill'
  return 'approval.type.shell'
}

function approvalTypeIcon(type: ApprovalType): typeof SquareTerminal {
  if (type === 'shell') return SquareTerminal
  if (type === 'remoteResource') return Cloud
  return File
}

function approvalSummary(operation: string, target: string): string {
  if (operation && target) return `${operation} (${target})`
  return operation || target
}

const pendingStatusStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minWidth: 0,
  padding: '4px 8px',
  color: 'var(--text-dimmed)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const pendingSummaryStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  color: 'var(--text-dimmed)'
}

const resolvedStatusStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '6px 10px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-secondary)',
  fontSize: '12px',
  color: 'var(--text-secondary)'
}

const resolvedOperationStyle: CSSProperties = {
  fontFamily: 'var(--font-mono)',
  fontSize: '11px',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  flex: 1
}
