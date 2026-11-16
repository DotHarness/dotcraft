import type { ReactNode } from 'react'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import { openBackgroundTask } from '../../utils/backgroundTaskNavigation'
import { findSubAgentChild } from '../../utils/subAgentIdentity'
import { RobotAvatar } from '../agents/RobotAvatar'
import { TASK_WAKE_ICONS, settledSubAgentWake, taskWakeLabel } from './taskWakeDisplay'
import type { TaskWake } from './turnActivityModel'
import { SubAgentEndingChips } from './SubAgentChips'
import type { ConversationItem } from '../../types/conversation'
import styles from './ResponseLeadingTurn.module.css'

export function ResponseLeadingTurn({ turnId, children }: { turnId: string; children: ReactNode }): JSX.Element {
  return <div className={styles.turn} data-turn-id={turnId}>{children}</div>
}

export function ResponseLeadingTurns({ children }: { children: ReactNode }): JSX.Element {
  return <div className={styles.turns}>{children}</div>
}

interface TaskWakeLineProps {
  threadId: string
  wake: TaskWake
  notice: ConversationItem
}

export function TaskWakeLine(props: TaskWakeLineProps): JSX.Element {
  return props.wake.kind === 'subagent' ? <SubAgentWakeLine {...props} /> : <WakeLine {...props} />
}

function SubAgentWakeLine({ threadId, wake, notice }: TaskWakeLineProps): JSX.Element {
  const { lookup } = useSubAgentLookup(threadId)
  const child = findSubAgentChild(lookup, null, wake.taskId, 'tree')
  const settled = settledSubAgentWake(wake, child)
  return wake.taskId && settled.status
    ? <SubAgentEndingChips items={[notice]} parentThreadId={threadId} />
    : <WakeLine threadId={threadId} wake={settled} subAgentThreadId={child?.childThreadId} />
}

function WakeLine({ threadId, wake, subAgentThreadId }: { threadId: string; wake: TaskWake; subAgentThreadId?: string }): JSX.Element {
  const locale = useLocale()
  const Icon = TASK_WAKE_ICONS[wake.kind]
  const taskId = wake.taskId
  const content = <>
    {wake.kind === 'subagent' && wake.label
      ? <RobotAvatar name={wake.label} size={16} />
      : <Icon size={16} strokeWidth={1.6} aria-hidden />}
    <span className={styles.label}>{taskWakeLabel(locale, wake)}</span>
  </>
  return taskId
    ? (
      <button
        type="button"
        className={styles.wake}
        onClick={() => openBackgroundTask(threadId, wake.kind, taskId, translate(locale, 'workflow.tab'), subAgentThreadId)}
      >
        {content}
      </button>
    )
    : <div className={styles.wake}>{content}</div>
}
