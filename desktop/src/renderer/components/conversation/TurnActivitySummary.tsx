import { useEffect, useRef, useState, type ReactNode } from 'react'
import { Bot, SquareTerminal, Workflow } from 'lucide-react'
import { getActivityElapsedMs, type TaskWake, type TaskWakeKind, type TurnActivityStatus } from './turnActivityModel'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import type { AppLocale } from '../../../shared/locales/types'
import { turnStopKey, useTurnStopStore } from '../../stores/turnStopStore'
import { useToolCallRevealTarget } from '../../stores/toolCallRevealStore'
import { openBackgroundTask } from '../../utils/backgroundTaskNavigation'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { ActionTooltip } from '../ui/ActionTooltip'
import styles from './TurnActivitySummary.module.css'

const SCROLL_ANCHOR_MS = 350

interface TurnActivitySummaryProps {
  status: TurnActivityStatus
  collapsible?: boolean
  children?: ReactNode
  threadId: string
  turnId: string
  startedAt?: string
  completedAt?: string
  taskWake?: TaskWake | null
}

export function TurnActivitySummary({ threadId, turnId, startedAt, completedAt, status, collapsible = false, taskWake, children }: TurnActivitySummaryProps): JSX.Element {
  const locale = useLocale()
  const byUser = useTurnStopStore(state => state.requested.has(turnStopKey(threadId, turnId)))
  const [expanded, setExpanded] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  const headerRef = useRef<HTMLButtonElement>(null)
  const open = !collapsible || expanded
  const revealNonce = useToolCallRevealTarget((target) =>
    target.threadId === threadId && target.turnId === turnId ? target.nonce : null)
  const mounted = useRef(open)
  if (open) mounted.current = true

  useEffect(() => {
    if (status !== 'working' || completedAt || !startedAt || !Number.isFinite(Date.parse(startedAt))) return
    setNow(Date.now())
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [status, startedAt, completedAt])

  useEffect(() => {
    if (revealNonce != null) setExpanded(true)
  }, [revealNonce])
  const elapsed = getActivityElapsedMs(startedAt, completedAt ?? (status === 'working' ? new Date(now).toISOString() : undefined))
  const key = status === 'stopped' ? byUser ? 'conversation.stopped' : 'conversation.interrupted'
    : status === 'working' ? 'conversation.working' : 'conversation.worked'
  const hasDuration = elapsed != null && (status !== 'working' || elapsed >= 1000)
  const label = translate(locale, hasDuration ? `${key}${status === 'stopped' ? 'After' : 'For'}` : key,
    { duration: elapsed == null ? '' : formatActivityDuration(elapsed, locale) })
  const origin = taskWake ? taskWakeLabel(locale, taskWake) : null
  const accessibleName = origin ? `${label} · ${origin}` : undefined
  const labelContent = origin
    ? <ActionTooltip label={origin} multiline><span>{label}</span></ActionTooltip>
    : <span>{label}</span>
  const OriginIcon = taskWake ? TASK_WAKE_ICONS[taskWake.kind] : null
  const taskId = taskWake?.taskId
  const originButton = taskWake && origin && OriginIcon ? (
    <ActionTooltip label={origin} multiline>
      <button
        type="button"
        className={styles.originButton}
        aria-label={translate(locale, TASK_WAKE_COPY[taskWake.kind].show)}
        disabled={!taskId}
        onClick={() => { if (taskId) openBackgroundTask(threadId, taskWake.kind, taskId, translate(locale, 'workflow.tab')) }}
      >
        <OriginIcon size={13} strokeWidth={1.8} aria-hidden />
      </button>
    </ActionTooltip>
  ) : null

  return <div className={styles.summary}>
    {collapsible && children ? (
      <div className={styles.header}>
        {originButton}
        <button
          ref={headerRef}
          type="button"
          className={styles.toggle}
          aria-expanded={expanded}
          aria-label={accessibleName}
          onClick={() => {
            if (headerRef.current) anchorScroll(headerRef.current)
            setExpanded(value => !value)
          }}
        >
          {labelContent}
          <span className={styles.chevron} aria-hidden><DisclosureChevron expanded={expanded} /></span>
        </button>
      </div>
    ) : (
      <div className={styles.header} role="status" aria-label={accessibleName}>{originButton}{labelContent}</div>
    )}
    {children ? (
      <div className={styles.body} data-open={open ? 'true' : undefined} inert={!open}>
        <div className={styles.bodyContent}>{mounted.current ? children : null}</div>
      </div>
    ) : null}
  </div>
}

const TASK_WAKE_ICONS: Record<TaskWakeKind, typeof SquareTerminal> = {
  terminal: SquareTerminal,
  subagent: Bot,
  workflow: Workflow
}

const TASK_WAKE_COPY: Record<TaskWakeKind, { param: string; show: string; started: string; generic: string; statuses: Record<string, string> }> = {
  terminal: {
    param: 'command',
    show: 'conversation.showBackgroundCommand',
    started: 'conversation.startedByBackgroundCommand',
    generic: 'conversation.startedByBackgroundCommandGeneric',
    statuses: {
      completed: 'conversation.backgroundCommandFinished',
      failed: 'conversation.backgroundCommandFailed',
      killed: 'conversation.backgroundCommandStopped',
      waitingForInput: 'conversation.backgroundCommandWaitingForInput'
    }
  },
  subagent: {
    param: 'name',
    show: 'conversation.showSubagent',
    started: 'conversation.startedBySubagent',
    generic: 'conversation.startedBySubagentGeneric',
    statuses: {
      completed: 'conversation.subagentFinished',
      failed: 'conversation.subagentFailed',
      cancelled: 'conversation.subagentStopped'
    }
  },
  workflow: {
    param: 'name',
    show: 'conversation.showWorkflow',
    started: 'conversation.startedByWorkflow',
    generic: 'conversation.startedByWorkflowGeneric',
    statuses: {
      succeeded: 'conversation.workflowFinished',
      failed: 'conversation.workflowFailed',
      cancelled: 'conversation.workflowFailed',
      interrupted: 'conversation.workflowFailed',
      stopped: 'conversation.workflowStopped'
    }
  }
}

function taskWakeLabel(locale: AppLocale, { kind, label, status }: TaskWake): string {
  const copy = TASK_WAKE_COPY[kind]
  if (!label) return translate(locale, copy.generic)
  return translate(locale, (status && copy.statuses[status]) || copy.started, { [copy.param]: label })
}

function anchorScroll(element: HTMLElement): void {
  let scroller = element.parentElement
  while (scroller && !/(auto|scroll)/.test(getComputedStyle(scroller).overflowY)) scroller = scroller.parentElement
  if (!scroller) return
  const container = scroller
  const top = element.getBoundingClientRect().top
  const end = performance.now() + SCROLL_ANCHOR_MS
  let frame = 0
  const stop = (): void => {
    cancelAnimationFrame(frame)
    for (const type of ['wheel', 'touchstart', 'keydown']) container.removeEventListener(type, stop)
  }
  const step = (): void => {
    container.scrollTop += element.getBoundingClientRect().top - top
    if (performance.now() < end) frame = requestAnimationFrame(step)
    else stop()
  }
  for (const type of ['wheel', 'touchstart', 'keydown']) container.addEventListener(type, stop, { passive: true })
  frame = requestAnimationFrame(step)
}

function formatActivityDuration(elapsed: number, locale: string): string {
  const seconds = Math.floor(elapsed / 1000)
  const units: Array<[string, number]> = [
    ['hour', Math.floor(seconds / 3600)],
    ['minute', Math.floor(seconds % 3600 / 60)],
    ['second', seconds % 60]
  ]
  return units.filter(([unit, value]) => value > 0 || (unit === 'second' && seconds === 0))
    .map(([unit, value]) => new Intl.NumberFormat(locale, { style: 'unit', unit, unitDisplay: 'narrow' }).format(value))
    .join(' ')
}
