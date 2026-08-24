import { useEffect, useMemo, useState, type CSSProperties } from 'react'
import { Bot } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import {
  isSubAgentChildClosed,
  isSubAgentChildRunning,
  useSubAgentStore,
  type SubAgentChild
} from '../../stores/subAgentStore'
import { useThreadStore } from '../../stores/threadStore'
import { useUIStore } from '../../stores/uiStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { formatSubAgentMeta, getSubAgentAccent, getSubAgentIdentitySeed } from '../../utils/subAgentPresentation'
import { formatRelativeTime } from '../../utils/relativeTime'
import { formatSubAgentElapsed } from '../../utils/formatSubAgentElapsed'
import styles from './SubagentsTab.module.css'

const EMPTY_CHILDREN: SubAgentChild[] = []

/** Refresh interval for running subagents' live message preview while the tab is open. */
const RUNNING_PREVIEW_POLL_MS = 3000

/**
 * Subagents tab — collects the active thread's subagents into Active (running),
 * Done (finished, edge still open) and Closed (closed by the main agent or by
 * residency reclaim) sections. Subagents are not shown in the dock (running-only)
 * or the sidebar, so this panel is the durable place to review and reopen them —
 * including closed ones, whose conversations remain readable. Rows are entirely
 * clickable and open the subagent conversation; there are no destructive actions
 * here so history is always preserved.
 */
export function SubagentsTab(): JSX.Element {
  const t = useT()
  const activeThreadId = useThreadStore((s) => s.activeThreadId)
  const children = useSubAgentStore((s) =>
    activeThreadId ? s.childrenByParent.get(activeThreadId) ?? EMPTY_CHILDREN : EMPTY_CHILDREN
  )
  const fetchChildren = useSubAgentStore((s) => s.fetchChildren)
  const fetchPreviews = useSubAgentStore((s) => s.fetchPreviews)

  useEffect(() => {
    if (!activeThreadId) return
    void fetchChildren(activeThreadId, { authoritative: true, includeClosed: true })
  }, [fetchChildren, activeThreadId])

  // Load previews for any children that arrived via live progress/graph events
  // after the initial fetch (e.g. a subagent finishing while the tab is open).
  useEffect(() => {
    if (!activeThreadId) return
    if (children.some((child) =>
      child.isPlaceholder !== true
      && (
        child.lastMessagePreview == null
        || (isSubAgentChildRunning(child) && child.activeTurnStartedAt == null)
      )
    )) {
      void fetchPreviews(activeThreadId)
    }
  }, [activeThreadId, children, fetchPreviews])

  const hasRunningSubagent = children.some(isSubAgentChildRunning)
  const elapsedNowMs = useElapsedNow(hasRunningSubagent)

  // While the tab is open and any subagent is running, poll its latest agent
  // message so the Active rows show live progress. Stops when nothing is running
  // or the tab unmounts, so idle tabs never poll.
  useEffect(() => {
    if (!activeThreadId || !hasRunningSubagent) return
    const timer = setInterval(() => {
      void fetchPreviews(activeThreadId, { runningOnly: true })
    }, RUNNING_PREVIEW_POLL_MS)
    return () => clearInterval(timer)
  }, [activeThreadId, hasRunningSubagent, fetchPreviews])

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

  return (
    <div className={`${styles.scrollContainer} dc-scrollbar-stable`}>
      <SectionHeader label={t('subagentsPanel.active')} count={active.length} />
      {active.length === 0 ? (
        <p className={styles.sectionEmpty}>{t('subagentsPanel.noActive')}</p>
      ) : (
        <div className={styles.rows}>
          {active.map((child) => (
            <SubagentRow key={child.childThreadId} child={child} elapsedNowMs={elapsedNowMs} />
          ))}
        </div>
      )}

      {done.length > 0 && (
        <>
          <SectionHeader label={t('subagentsPanel.done')} count={done.length} />
          <div className={styles.rows}>
            {done.map((child) => (
              <SubagentRow key={child.childThreadId} child={child} elapsedNowMs={elapsedNowMs} />
            ))}
          </div>
        </>
      )}

      {closed.length > 0 && (
        <>
          <SectionHeader label={t('subagentsPanel.closed')} count={closed.length} />
          <div className={styles.rows}>
            {closed.map((child) => (
              <SubagentRow key={child.childThreadId} child={child} elapsedNowMs={elapsedNowMs} />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function SectionHeader({ label, count }: { label: string; count: number }): JSX.Element {
  return (
    <div className={styles.sectionHeader}>
      <span className={styles.sectionHeaderLabel}>{label} · {count}</span>
    </div>
  )
}

function SubagentRow({ child, elapsedNowMs }: { child: SubAgentChild; elapsedNowMs: number }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const running = isSubAgentChildRunning(child)
  const color = getSubAgentAccent(getSubAgentIdentitySeed(child))
  const meta = formatSubAgentMeta({
    agentRole: child.agentRole,
    profileName: child.profileName,
    runtimeType: child.runtimeType
  })
  const preview = resolvePreview(child, running, t)
  const timeLabel = running
    ? formatRunningElapsed(child.activeTurnStartedAt, elapsedNowMs)
    : child.threadSummary?.lastActiveAt
      ? formatRelativeTime(child.threadSummary.lastActiveAt, new Date(elapsedNowMs), locale)
      : ''
  const canOpen = child.isPlaceholder !== true

  const openThread = (): void => {
    if (!canOpen) return
    useThreadStore.getState().setActiveThreadId(child.childThreadId)
    useUIStore.getState().setActiveMainView('conversation')
  }

  return (
    <button
      type="button"
      onClick={openThread}
      disabled={!canOpen}
      aria-label={t('subagentsPanel.openAria', { name: child.nickname })}
      className={styles.row}
      style={{ '--subagent-accent': color } as CSSProperties}
    >
      <span className={styles.iconSlot}>
        {/* Both active and finished rows use the Bot glyph; the running state is
            conveyed by the gradient preview text below, not a separate spinner. */}
        <Bot size={15} strokeWidth={2} aria-hidden className={styles.icon} />
      </span>
      <span className={styles.bodyCell}>
        <span className={styles.titleRow}>
          <span className={styles.nameGroup}>
            <span className={styles.nickname}>{child.nickname}</span>
            {meta && <span className={styles.meta}>({meta})</span>}
          </span>
          {timeLabel && <span className={styles.time}>{timeLabel}</span>}
        </span>
        <ActionTooltip label={preview} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden' }}>
          <span className={`${styles.preview}${running ? ' tool-running-gradient-text' : ''}`}>
            {preview}
          </span>
        </ActionTooltip>
      </span>
    </button>
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

function resolvePreview(
  child: SubAgentChild,
  running: boolean,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  if (running) {
    // Prefer the live agent message the subagent is producing (refreshed by the
    // panel's poll); fall back to the current tool activity, then to "Running".
    return child.lastMessagePreview?.trim()
      || child.lastToolDisplay?.trim()
      || t('subAgentDock.running')
  }
  const preview = child.lastMessagePreview?.trim()
  if (preview) return preview
  return formatDoneStatus(child, t)
}

function formatDoneStatus(
  child: SubAgentChild,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  const normalized = child.status.trim().toLowerCase()
  if (normalized === 'closed') return t('subagentsPanel.closedStatus')
  if (normalized === 'failed') return t('subAgentDock.failed')
  if (normalized === 'cancelled' || normalized === 'canceled') return t('subAgentDock.cancelled')
  return t('subAgentDock.completed')
}
