import { useEffect, useMemo, type CSSProperties } from 'react'
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
import { RunningSpinner } from '../ui/RunningSpinner'
import { ActionTooltip } from '../ui/ActionTooltip'
import { formatSubAgentMeta, getSubAgentAccent } from '../../utils/subAgentPresentation'
import { formatRelativeTime } from '../../utils/relativeTime'

const EMPTY_CHILDREN: SubAgentChild[] = []

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
    void (async () => {
      await fetchChildren(activeThreadId, { authoritative: true })
      await fetchPreviews(activeThreadId)
    })()
  }, [fetchChildren, fetchPreviews, activeThreadId])

  // Load previews for any children that arrived via live progress/graph events
  // after the initial fetch (e.g. a subagent finishing while the tab is open).
  useEffect(() => {
    if (!activeThreadId) return
    if (children.some((child) => child.isPlaceholder !== true && child.lastMessagePreview == null)) {
      void fetchPreviews(activeThreadId)
    }
  }, [activeThreadId, children, fetchPreviews])

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
      <div style={emptyContainerStyle}>
        <p style={emptyTextStyle}>{t('subagentsPanel.empty')}</p>
      </div>
    )
  }

  return (
    <div style={scrollContainerStyle}>
      <SectionHeader label={t('subagentsPanel.active')} count={active.length} />
      {active.length === 0 ? (
        <p style={sectionEmptyStyle}>{t('subagentsPanel.noActive')}</p>
      ) : (
        <div style={rowsStyle}>
          {active.map((child) => (
            <SubagentRow key={child.childThreadId} child={child} />
          ))}
        </div>
      )}

      {done.length > 0 && (
        <>
          <SectionHeader label={t('subagentsPanel.done')} count={done.length} />
          <div style={rowsStyle}>
            {done.map((child) => (
              <SubagentRow key={child.childThreadId} child={child} />
            ))}
          </div>
        </>
      )}

      {closed.length > 0 && (
        <>
          <SectionHeader label={t('subagentsPanel.closed')} count={closed.length} />
          <div style={rowsStyle}>
            {closed.map((child) => (
              <SubagentRow key={child.childThreadId} child={child} />
            ))}
          </div>
        </>
      )}
    </div>
  )
}

function SectionHeader({ label, count }: { label: string; count: number }): JSX.Element {
  return (
    <div style={sectionHeaderStyle}>
      <span style={sectionHeaderLabelStyle}>
        {label}
        <span style={sectionHeaderCountStyle}>{count}</span>
      </span>
    </div>
  )
}

function SubagentRow({ child }: { child: SubAgentChild }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const running = isSubAgentChildRunning(child)
  const color = getSubAgentAccent(child.childThreadId || child.nickname)
  const meta = formatSubAgentMeta({
    agentRole: child.agentRole,
    profileName: child.profileName,
    runtimeType: child.runtimeType
  })
  const preview = resolvePreview(child, running, t)
  const timeLabel = child.threadSummary?.lastActiveAt
    ? formatRelativeTime(child.threadSummary.lastActiveAt, new Date(), locale)
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
      style={{ ...rowStyle, cursor: canOpen ? 'pointer' : 'default' }}
      onMouseEnter={(event) => {
        if (canOpen) (event.currentTarget as HTMLButtonElement).style.background = 'var(--bg-hover)'
      }}
      onMouseLeave={(event) => {
        ;(event.currentTarget as HTMLButtonElement).style.background = 'transparent'
      }}
    >
      <span style={iconSlotStyle}>
        {running ? (
          <RunningSpinner
            label={t('subAgentDock.running')}
            testId={`subagents-tab-running-${child.childThreadId}`}
          />
        ) : (
          <Bot size={15} strokeWidth={2} aria-hidden style={{ color, display: 'block' }} />
        )}
      </span>
      <span style={bodyCellStyle}>
        <span style={titleRowStyle}>
          <span style={{ ...nicknameStyle, color }}>{child.nickname}</span>
          {timeLabel && <span style={timeStyle}>{timeLabel}</span>}
        </span>
        <ActionTooltip label={preview} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden' }}>
          <span
            className={running ? 'tool-running-gradient-text' : undefined}
            style={{ ...previewStyle, display: 'block' }}
          >
            {meta && !running ? `${meta} · ${preview}` : preview}
          </span>
        </ActionTooltip>
      </span>
    </button>
  )
}

function resolvePreview(
  child: SubAgentChild,
  running: boolean,
  t: (key: string, vars?: Record<string, string | number>) => string
): string {
  if (running) {
    return child.lastToolDisplay?.trim() || t('subAgentDock.running')
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

const scrollContainerStyle: CSSProperties = {
  padding: '12px 12px 16px',
  overflowY: 'auto',
  height: '100%',
  minWidth: 0,
  maxWidth: '100%',
  boxSizing: 'border-box'
}

const emptyContainerStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '16px'
}

const emptyTextStyle: CSSProperties = {
  textAlign: 'center',
  color: 'var(--text-dimmed)',
  fontSize: '13px',
  lineHeight: 1.7
}

const sectionHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '10px 6px 6px',
  minHeight: '24px'
}

const sectionHeaderLabelStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  fontSize: '11px',
  fontWeight: 600,
  letterSpacing: '0.02em',
  textTransform: 'uppercase',
  color: 'var(--text-dimmed)'
}

const sectionHeaderCountStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  minWidth: '16px',
  height: '16px',
  padding: '0 4px',
  borderRadius: '8px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  fontSize: '10px',
  fontWeight: 500,
  letterSpacing: 0
}

const sectionEmptyStyle: CSSProperties = {
  margin: '0 0 4px',
  padding: '0 6px 6px',
  color: 'var(--text-dimmed)',
  fontSize: '12px'
}

const rowsStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '1px'
}

const rowStyle: CSSProperties = {
  width: '100%',
  display: 'grid',
  gridTemplateColumns: '22px minmax(0, 1fr)',
  alignItems: 'start',
  gap: '9px',
  padding: '8px 6px',
  border: 'none',
  borderRadius: '8px',
  background: 'transparent',
  textAlign: 'left',
  font: 'inherit',
  color: 'inherit',
  transition: 'background-color 100ms ease'
}

const iconSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: 22,
  height: 18
}

const bodyCellStyle: CSSProperties = {
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: '2px',
  overflow: 'hidden'
}

const titleRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'baseline',
  justifyContent: 'space-between',
  gap: '8px',
  minWidth: 0
}

const nicknameStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontSize: '13px',
  fontWeight: 600
}

const timeStyle: CSSProperties = {
  flexShrink: 0,
  color: 'var(--text-dimmed)',
  fontSize: '11px'
}

const previewStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  fontSize: '12px',
  lineHeight: 1.4,
  color: 'var(--text-secondary)'
}
