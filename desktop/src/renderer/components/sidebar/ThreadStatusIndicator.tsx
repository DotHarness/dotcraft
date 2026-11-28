import { Pause } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useLinkedChats } from '../../stores/linkedChatsStore'
import { useOwnedThread } from '../../stores/ownedThreadsStore'
import { useOwnedThreadUnreadCount, useThreadAcrossWorkspaces } from '../../stores/ownedThreadActivityStore'
import { useThreadStore } from '../../stores/threadStore'
import { isThreadUnread } from '../../utils/threadReadState'
import { isThreadRuntimeActive } from '../../utils/threadRuntime'
import { ActionTooltip } from '../ui/ActionTooltip'
import { Spinner } from '../ui/Spinner'
import { ThreadWaitingBadge } from './ThreadWaitingBadge'
import { useThreadWaitingStatus, type ThreadWaitingStatus } from './threadWaitingStatus'

export function ThreadStatusIndicator({
  threadId,
  active,
  unreadVariant = 'dot',
  includeLinkedChats = false
}: {
  threadId: string
  active: boolean
  unreadVariant?: 'dot' | 'count'
  includeLinkedChats?: boolean
}): JSX.Element | null {
  const t = useT()
  const ownedThread = useOwnedThread(threadId)
  const thread = useThreadAcrossWorkspaces(threadId)
  const turnStarted = useThreadStore((s) => s.runningTurnThreadIds.has(threadId))
  const snapshot = useThreadStore((s) => s.runtimeSnapshots.get(threadId))
  const waitingOnApproval = useThreadStore((s) => s.pendingApprovalThreadIds.has(threadId))
  const waitingOnInput = useThreadStore((s) => s.pendingUserInputThreadIds.has(threadId))
  const waitingOnPlanConfirmation = useThreadStore((s) => s.pendingPlanConfirmationThreadIds.has(threadId))
  const linkedChats = useLinkedChats(includeLinkedChats ? threadId : null)
  const unreadCount = useOwnedThreadUnreadCount(unreadVariant === 'count' ? threadId : null)

  const runtime = snapshot ?? thread?.runtime
  const ownWaiting = useThreadWaitingStatus(
    threadId,
    {
      waitingOnApproval: waitingOnApproval || runtime?.waitingOnApproval === true,
      waitingOnInput: waitingOnInput || runtime?.waitingOnInput === true,
      waitingOnPlanConfirmation: waitingOnPlanConfirmation || runtime?.waitingOnPlanConfirmation === true
    },
    active
  )
  const linkedWaiting = includeLinkedChats ? resolveLinkedWaitingStatus(linkedChats) : null
  const waiting = ownWaiting ?? linkedWaiting
  const linkedFailed = includeLinkedChats && linkedChats.some((chat) => chat.status === 'failed')
  const running = turnStarted || isThreadRuntimeActive(runtime)

  if (ownedThread?.paused) {
    return (
      <ActionTooltip label={t('conductor.paused')}>
        <span
          className="dc-status-indicator"
          role="img"
          aria-label={t('conductor.paused')}
          data-testid={`thread-paused-${threadId}`}
        >
          <Pause size={13} strokeWidth={2} className="dc-status-indicator__icon" aria-hidden />
        </span>
      </ActionTooltip>
    )
  }

  if (waiting) return <ThreadWaitingBadge status={waiting} quiet={active} />
  if (linkedFailed) {
    return (
      <span className="dc-status-indicator" role="img" aria-label={t('common.error')}>
        <span className="dc-status-indicator__dot" data-tone="error" />
      </span>
    )
  }
  if (running) {
    return (
      <span className="dc-status-indicator">
        <Spinner label={t('threadEntry.turnRunning')} />
      </span>
    )
  }
  if (!thread || active || thread.status !== 'active' || !isThreadUnread(thread)) return null
  if (unreadVariant === 'count') {
    const count = Math.max(1, unreadCount)
    const display = count > 99 ? '99+' : String(count)
    return (
      <ActionTooltip label={t('threadEntry.unread')}>
        <span
          className="dc-unread-count-pill"
          role="img"
          aria-label={t('threadEntry.unread')}
          data-testid={`thread-unread-count-${threadId}`}
        >
          {display}
        </span>
      </ActionTooltip>
    )
  }
  return (
    <ActionTooltip label={t('threadEntry.unread')}>
      <span className="dc-status-indicator" role="img" aria-label={t('threadEntry.unread')}>
        <span className="dc-status-indicator__dot" data-tone="unread" />
      </span>
    </ActionTooltip>
  )
}

function resolveLinkedWaitingStatus(
  chats: readonly { status: string; waitingFor?: string }[]
): ThreadWaitingStatus {
  const waiting = chats.find((chat) => chat.status === 'waitingForInput')
  if (!waiting) return null
  if (waiting.waitingFor === 'input') return 'input'
  if (waiting.waitingFor === 'planConfirmation') return 'plan'
  return 'approval'
}
