import { useEffect, useMemo, useState } from 'react'
import { Square } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  isSubAgentChildClosed,
  isSubAgentChildRunning,
  useSubAgentStore,
  type SubAgentChild
} from '../../stores/subAgentStore'
import { useSubAgentLookup } from '../../hooks/useSubAgentLookup'
import { useThreadStore } from '../../stores/threadStore'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { IconButton } from '../ui/IconButton'
import { Spinner } from '../ui/Spinner'
import { addToast } from '../../stores/toastStore'
import { formatCompactCount } from '../../utils/formatCompactCount'
import { formatSubAgentMeta } from '../../utils/subAgentPresentation'
import { RobotAvatar } from '../agents/RobotAvatar'
import { formatRelativeTime } from '../../utils/relativeTime'
import { formatSubAgentElapsed } from '../../utils/formatSubAgentElapsed'
import { SubagentDetail } from './SubagentDetail'
import styles from './SubagentsTab.module.css'

const EMPTY_CHILDREN: SubAgentChild[] = []


export function SubagentsTab(): JSX.Element {
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  useSubAgentLookup(activeThreadId ?? '')
  const selectedChild = useSubAgentStore((s) => {
    if (!activeThreadId) return null
    const selectedId = s.selectedChildByParent.get(activeThreadId)
    return selectedId ? findChild(s.childrenByParent, activeThreadId, selectedId) : null
  })

  if (activeThreadId && selectedChild) {
    return (
      <SubagentDetail
        child={selectedChild}
        onBack={() => useSubAgentStore.getState().selectChild(activeThreadId, null)}
      />
    )
  }
  return <SubagentList parentThreadId={activeThreadId} />
}

function findChild(
  childrenByParent: Map<string, SubAgentChild[]>,
  parentThreadId: string,
  childThreadId: string
): SubAgentChild | null {
  const own = childrenByParent.get(parentThreadId)?.find((child) => child.childThreadId === childThreadId)
  if (own) return own
  for (const children of childrenByParent.values()) {
    const match = children.find((child) => child.childThreadId === childThreadId)
    if (match) return match
  }
  return null
}

function SubagentList({ parentThreadId }: { parentThreadId: string | null }): JSX.Element {
  const t = useT()
  const children = useSubAgentStore((s) =>
    parentThreadId ? s.childrenByParent.get(parentThreadId) ?? EMPTY_CHILDREN : EMPTY_CHILDREN
  )
  const fetchUsage = useSubAgentStore((s) => s.fetchUsage)
  const [finishedOpen, setFinishedOpen] = useState(false)
  const [closedOpen, setClosedOpen] = useState(false)

  useEffect(() => {
    if (!parentThreadId) return
    if (children.some((child) => child.isPlaceholder !== true && child.usedTokens == null)) {
      void fetchUsage(parentThreadId)
    }
  }, [parentThreadId, children, fetchUsage])

  const hasRunningSubagent = children.some(isSubAgentChildRunning)
  const elapsedNowMs = useElapsedNow(hasRunningSubagent)

  const { active, done, closed } = useMemo(() => {
    const running: SubAgentChild[] = []
    const finished: SubAgentChild[] = []
    const closedChildren: SubAgentChild[] = []
    for (const child of children) {
      if (isSubAgentChildClosed(child)) closedChildren.push(child)
      else if (isSubAgentChildRunning(child)) running.push(child)
      else finished.push(child)
    }
    return { active: running, done: finished, closed: closedChildren }
  }, [children])

  if (children.length === 0) {
    return (
      <div className={styles.emptyContainer}>
        <p className={styles.emptyText}>{t('subagentsPanel.empty')}</p>
      </div>
    )
  }

  const open = (child: SubAgentChild): void => {
    if (parentThreadId) useSubAgentStore.getState().selectChild(parentThreadId, child.childThreadId)
  }

  return (
    <div className={`${styles.scrollContainer} dc-scrollbar-stable`}>
      <SectionHeader label={t('subagentsPanel.running')} count={active.length} />
      {active.length === 0 ? (
        <p className={styles.sectionEmpty}>{t('subagentsPanel.noRunning')}</p>
      ) : (
        <div className={styles.rows}>
          {active.map((child) => (
            <SubagentRow key={child.childThreadId} child={child} elapsedNowMs={elapsedNowMs} onOpen={open} />
          ))}
        </div>
      )}

      {done.length > 0 && (
        <>
          <SectionHeader label={t('subagentsPanel.finished')} count={done.length} open={finishedOpen} onToggle={setFinishedOpen} />
          {finishedOpen && (
            <div className={styles.rows}>
              {done.map((child) => (
                <SubagentRow key={child.childThreadId} child={child} elapsedNowMs={elapsedNowMs} onOpen={open} />
              ))}
            </div>
          )}
        </>
      )}

      {closed.length > 0 && (
        <>
          <SectionHeader label={t('subagentsPanel.closed')} count={closed.length} open={closedOpen} onToggle={setClosedOpen} />
          {closedOpen && (
            <div className={styles.rows}>
              {closed.map((child) => (
                <SubagentRow key={child.childThreadId} child={child} elapsedNowMs={elapsedNowMs} onOpen={open} />
              ))}
            </div>
          )}
        </>
      )}
    </div>
  )
}

function SectionHeader({
  label,
  count,
  open,
  onToggle
}: {
  label: string
  count: number
  open?: boolean
  onToggle?: (open: boolean) => void
}): JSX.Element {
  if (!onToggle) {
    return (
      <div className={styles.sectionHeader}>
        <span className={styles.sectionHeaderLabel}>{label} · {count}</span>
      </div>
    )
  }
  return (
    <button
      type="button"
      className={`${styles.sectionHeader} ${styles.sectionToggle}`}
      aria-expanded={open === true}
      onClick={() => onToggle(!open)}
    >
      <span className={styles.sectionHeaderLabel}>{label} · {count}</span>
      <span className={styles.sectionChevron} aria-hidden><DisclosureChevron expanded={open === true} /></span>
    </button>
  )
}

function SubagentRow({
  child,
  elapsedNowMs,
  onOpen
}: {
  child: SubAgentChild
  elapsedNowMs: number
  onOpen: (child: SubAgentChild) => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [stopping, setStopping] = useState(false)
  const running = isSubAgentChildRunning(child)
  const meta = formatSubAgentMeta({
    agentRole: child.agentRole,
    profileName: child.profileName,
    runtimeType: child.runtimeType
  })
  const ended = running ? null : endedStatus(child, t)
  const timeLabel = running
    ? formatRunningElapsed(child.runtime?.activeTurnStartedAt, elapsedNowMs)
    : ended ?? (child.threadSummary?.lastActiveAt
      ? formatRelativeTime(child.threadSummary.lastActiveAt, new Date(elapsedNowMs), locale)
      : '')
  const canOpen = child.isPlaceholder !== true
  const canStop = running && child.isPlaceholder !== true
  const stop = (): void => {
    setStopping(true)
    useSubAgentStore.getState().interruptChild(child).then(
      () => setStopping(false),
      () => {
        setStopping(false)
        addToast(t('subagentsPanel.stopFailed'), 'error')
      }
    )
  }

  return (
    <div className={styles.row} data-stoppable={canStop || undefined}>
      <button
        type="button"
        onClick={() => onOpen(child)}
        disabled={!canOpen}
        aria-label={t('subagentsPanel.openAria', { name: child.nickname })}
        className={styles.rowButton}
      >
        <span className={styles.avatar}>
          <RobotAvatar name={child.nickname} size={32} />
        </span>
        <span className={styles.bodyCell}>
          <span className={styles.titleRow}>
            <span className={styles.nameGroup}>
              <span className={styles.nickname}>{child.nickname}</span>
              {meta && <span className={styles.meta}>({meta})</span>}
            </span>
            {timeLabel && <span className={styles.time} data-ended={ended ? true : undefined}>{timeLabel}</span>}
          </span>
          {(child.model || child.usedTokens != null) && (
            <span className={styles.usage}>
              {child.model}
              {child.model && child.usedTokens != null && ' · '}
              {child.usedTokens != null && (
                <span>
                  <span className={styles.usageCount}>{formatCompactCount(child.usedTokens)}</span>
                  {' '}{t('subagentsPanel.tokens')}
                </span>
              )}
            </span>
          )}
        </span>
      </button>
      {canStop && (
        <span className={styles.stop}>
          <IconButton
            size={24}
            icon={stopping ? <Spinner size={14} /> : <Square size={14} strokeWidth={1.75} />}
            label={t('subagentsPanel.stop', { name: child.nickname })}
            tooltipLabel={t('subagentsPanel.stop', { name: child.nickname })}
            disabled={stopping}
            aria-busy={stopping || undefined}
            onClick={stop}
          />
        </span>
      )}
    </div>
  )
}

function useElapsedNow(enabled: boolean): number {
  const [nowMs, setNowMs] = useState(() => Date.now())
  useEffect(() => {
    if (!enabled) return
    const timer = window.setInterval(() => setNowMs(Date.now()), 1_000)
    return () => window.clearInterval(timer)
  }, [enabled])
  return nowMs
}

function formatRunningElapsed(startedAt: string | null | undefined, nowMs: number): string {
  if (!startedAt) return ''
  const startedAtMs = Date.parse(startedAt)
  if (!Number.isFinite(startedAtMs)) return ''
  return formatSubAgentElapsed(nowMs - startedAtMs)
}

function endedStatus(
  child: SubAgentChild,
  t: (key: string, vars?: Record<string, string | number>) => string
): string | null {
  const normalized = child.status.trim().toLowerCase()
  if (normalized === 'failed') return t('subAgentDock.failed')
  if (normalized === 'cancelled' || normalized === 'canceled') return t('subAgentDock.cancelled')
  return null
}
