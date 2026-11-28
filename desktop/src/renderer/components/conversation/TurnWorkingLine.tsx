import { useEffect, useState } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ThinkingGlyph } from './ThinkingGlyph'
import { useRunningWork, type RunningWork } from './summary/store'
import { desktopSummaryServices } from './summary/summaryServices'
import { formatRunningWorkCompact, formatRunningWorkDetailed, openRunningWork } from './runningWorkFormat'
import type { TurnThinkingLabel } from './turnThinkingStatus'
import { useTurnTasksClaimed } from './turnTaskClaims'
import styles from './TurnWorkingLine.module.css'

const FADE_MS = 180

interface ShownLabel { text: string; live: boolean }

export function TurnWorkingLine({ turnId, working, label, keepSpace, afterTool, tasksThreadId }: {
  turnId: string
  working: boolean
  label: TurnThinkingLabel | null
  keepSpace: boolean
  afterTool: boolean
  tasksThreadId: string | null
}): JSX.Element | null {
  const t = useT()
  const [engaged, setEngaged] = useState(working)
  const [faded, setFaded] = useState(!working)
  if (working && !engaged) setEngaged(true)
  if (working && faded) setFaded(false)
  const work = useRunningWork(desktopSummaryServices, tasksThreadId, working)
  const tasksClaimed = useTurnTasksClaimed(turnId)

  const current: ShownLabel | null = label == null ? null
    : label.kind === 'waitingForAnswer' ? { text: t('conversation.thinking.waitingForAnswer'), live: true }
    : { text: label.title || t('conversation.thinking.streaming'), live: true }
  const [shown, setShown] = useState(current)
  if (current && (current.text !== shown?.text || current.live !== shown.live)) setShown(current)

  useEffect(() => {
    if (working) return undefined
    const timer = setTimeout(() => setFaded(true), FADE_MS)
    return () => clearTimeout(timer)
  }, [working])

  const showTasks = work.total > 0 && keepSpace && !tasksClaimed
  if (!showTasks && (!engaged || (!working && !keepSpace))) return null
  return (
    <div className={styles.line} data-after-tool={afterTool || undefined} data-exited={!working || undefined} data-testid="turn-working-line">
      {showTasks && faded ? <RunningTasksIndicator work={work} /> : (
        <>
          <span className={styles.glyph}><ThinkingGlyph thinking={!faded} /></span>
          {showTasks ? <RunningTasksButton work={work} detailed={false} /> : null}
        </>
      )}
      {showTasks && current ? <span className={styles.separator} aria-hidden="true">·</span> : null}
      {shown ? (
        <span className={styles.label} data-visible={current ? 'true' : 'false'} aria-hidden={!current}>
          <RunningShimmer active={shown.live} className={styles.text}>
            {shown.text}
          </RunningShimmer>
        </span>
      ) : null}
    </div>
  )
}

export function RunningTasksIndicator({ work }: { work: RunningWork }): JSX.Element {
  return (
    <span className={styles.taskIndicator}>
      <span className={styles.pulseSlot} aria-hidden="true"><span className={styles.pulse} /></span>
      <RunningTasksButton work={work} detailed />
    </span>
  )
}

function RunningTasksButton({ work, detailed }: { work: RunningWork; detailed: boolean }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const label = detailed ? formatRunningWorkDetailed(t, locale, work) : formatRunningWorkCompact(t, work)
  return (
    <button type="button" className={`dc-quiet-action ${styles.tasks}`} onClick={() => openRunningWork(work)}>
      {label}
    </button>
  )
}
