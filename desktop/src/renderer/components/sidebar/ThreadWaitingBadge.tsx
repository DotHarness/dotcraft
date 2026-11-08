import { useT } from '../../contexts/LocaleContext'
import type { ThreadWaitingStatus } from './threadWaitingStatus'

export function ThreadWaitingBadge({
  status,
  quiet = false
}: {
  status: NonNullable<ThreadWaitingStatus>
  quiet?: boolean
}): JSX.Element {
  const t = useT()
  const label = status === 'approval' ? 'threadEntry.pendingApproval'
    : status === 'input' ? 'threadEntry.pendingUserInput'
      : 'threadEntry.pendingPlanConfirmation'
  return (
    <span
      className="dc-status-badge"
      data-size="compact"
      data-tone={status === 'plan' ? 'info' : 'warning'}
      data-quiet={quiet ? 'true' : undefined}
    >
      <span className="dc-status-badge__label">{t(label)}</span>
    </span>
  )
}
