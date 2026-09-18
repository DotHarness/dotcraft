import type { CSSProperties } from 'react'
import type { ConversationItem } from '../../types/conversation'
import { useT } from '../../contexts/LocaleContext'

interface ApprovalCardProps {
  item: ConversationItem
}

export function ApprovalCard({ item }: ApprovalCardProps): JSX.Element | null {
  const t = useT()
  if ((item.approvalState ?? 'pending') !== 'pending') return null
  const label = t('approval.running')
  return (
    <div role="status" aria-label={label} style={pendingStatusStyle}>
      {label}
    </div>
  )
}

const pendingStatusStyle: CSSProperties = {
  padding: '4px 8px',
  color: 'var(--text-dimmed)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}
