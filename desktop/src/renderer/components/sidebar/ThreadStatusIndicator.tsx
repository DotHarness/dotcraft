import { useT } from '../../contexts/LocaleContext'
import { useThreadStore } from '../../stores/threadStore'
import { isThreadUnread } from '../../utils/threadReadState'
import { isThreadRuntimeActive } from '../../utils/threadRuntime'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Spinner } from '../ui/Spinner'
import { ThreadWaitingBadge } from './ThreadWaitingBadge'
import { useThreadWaitingStatus } from './threadWaitingStatus'

export function ThreadStatusIndicator({ threadId, active }: { threadId: string; active: boolean }): JSX.Element | null {
  const t = useT()
  const thread = useThreadStore((s) => s.threadList.find((candidate) => candidate.id === threadId))
  const turnStarted = useThreadStore((s) => s.runningTurnThreadIds.has(threadId))
  const snapshot = useThreadStore((s) => s.runtimeSnapshots.get(threadId))
  const waitingOnApproval = useThreadStore((s) => s.pendingApprovalThreadIds.has(threadId))
  const waitingOnInput = useThreadStore((s) => s.pendingUserInputThreadIds.has(threadId))
  const waitingOnPlanConfirmation = useThreadStore((s) => s.pendingPlanConfirmationThreadIds.has(threadId))
  const waiting = useThreadWaitingStatus(threadId, { waitingOnApproval, waitingOnInput, waitingOnPlanConfirmation }, active)
  const running = turnStarted || isThreadRuntimeActive(snapshot ?? thread?.runtime)

  if (waiting) return <ThreadWaitingBadge status={waiting} quiet={active} />
  if (running) {
    return (
      <span className="dc-status-indicator">
        <Spinner label={t('threadEntry.turnRunning')} />
      </span>
    )
  }
  if (!thread || active || thread.status !== 'active' || !isThreadUnread(thread)) return null
  return (
    <ActionTooltip label={t('threadEntry.unread')}>
      <span className="dc-status-indicator" role="img" aria-label={t('threadEntry.unread')}>
        <span className="dc-status-indicator__dot" data-tone="success" />
      </span>
    </ActionTooltip>
  )
}
