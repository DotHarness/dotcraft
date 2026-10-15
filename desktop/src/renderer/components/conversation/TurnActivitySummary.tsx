import { useEffect, useRef, useState, type ReactNode } from 'react'
import { getActivityElapsedMs, type TurnActivityStatus } from './turnActivityModel'
import { useLocale } from '../../contexts/LocaleContext'
import { translate } from '../../../shared/locales'
import { turnStopKey, useTurnStopStore } from '../../stores/turnStopStore'
import { DisclosureChevron } from '../ui/DisclosureChevron'
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
}

export function TurnActivitySummary({ threadId, turnId, startedAt, completedAt, status, collapsible = false, children }: TurnActivitySummaryProps): JSX.Element {
  const locale = useLocale()
  const byUser = useTurnStopStore(state => state.requested.has(turnStopKey(threadId, turnId)))
  const [expanded, setExpanded] = useState(false)
  const [now, setNow] = useState(() => Date.now())
  const headerRef = useRef<HTMLButtonElement>(null)
  const open = !collapsible || expanded
  const mounted = useRef(open)
  if (open) mounted.current = true

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

  return <div className={styles.summary}>
    {collapsible && children ? (
      <button
        ref={headerRef}
        type="button"
        className={styles.header}
        aria-expanded={expanded}
        onClick={() => {
          if (headerRef.current) anchorScroll(headerRef.current)
          setExpanded(value => !value)
        }}
      >
        <span>{label}</span>
        <span className={styles.chevron} aria-hidden><DisclosureChevron expanded={expanded} /></span>
      </button>
    ) : (
      <div className={styles.header} role="status"><span>{label}</span></div>
    )}
    {children ? (
      <div className={styles.body} data-open={open ? 'true' : undefined} inert={!open}>
        <div className={styles.bodyContent}>{mounted.current ? children : null}</div>
      </div>
    ) : null}
  </div>
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
