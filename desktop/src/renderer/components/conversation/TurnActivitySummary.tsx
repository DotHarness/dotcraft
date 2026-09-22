import { useEffect, useState, type ReactNode } from 'react'
import { ToolDisclosure } from './ToolDisclosure'
import { getActivityElapsedMs, type TurnActivityStatus } from './turnActivityModel'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { turnStopKey, useTurnStopStore } from '../../stores/turnStopStore'
import styles from './TurnActivitySummary.module.css'

interface TurnActivitySummaryProps {
  status: TurnActivityStatus
  children?: ReactNode
  threadId: string
  turnId: string
  startedAt?: string
  completedAt?: string
}

export function TurnActivitySummary({ threadId, turnId, startedAt, completedAt, status, children }: TurnActivitySummaryProps): JSX.Element {
  const locale = useLocale()
  const byUser = useTurnStopStore(state => state.requested.has(turnStopKey(threadId, turnId)))
  const [expanded, setExpanded] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  useEffect(() => {
    if (status !== 'working' || completedAt || !startedAt || !Number.isFinite(Date.parse(startedAt))) return
    setNow(Date.now())
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [status, startedAt, completedAt])
  const elapsed = getActivityElapsedMs(startedAt, completedAt ?? (status === 'working' ? new Date(now).toISOString() : undefined))
  const key = status === 'stopped' ? byUser ? 'conversation.stopped' : 'conversation.interrupted'
    : status === 'working' ? 'conversation.working' : 'conversation.worked'
  const hasDuration = elapsed != null && (status !== 'working' || elapsed >= 1000)
  const label = translate(locale, hasDuration ? `${key}${status === 'stopped' ? 'After' : 'For'}` : key,
    { duration: elapsed == null ? '' : formatActivityDuration(elapsed, locale) })

  if (status === 'worked' && children) {
    return <div className={styles.summary}>
      <ToolDisclosure expanded={expanded} onToggle={() => setExpanded(value => !value)} title={label} variant="turn">
        {children}
      </ToolDisclosure>
    </div>
  }
  return <div className={styles.status} role="status">
    <span>{label}</span>
    <div className={styles.divider} />
  </div>
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
