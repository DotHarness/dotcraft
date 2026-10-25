import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ThinkingGlyph } from './ThinkingGlyph'
import { useRunningTaskCount } from './summary/store'
import { desktopSummaryServices } from './summary/summaryServices'
import { setSummaryShown } from './summary/view'
import type { TurnThinkingLabel } from './turnThinkingStatus'
import styles from './TurnWorkingLine.module.css'

const FADE_MS = 180

interface ShownLabel { text: string; live: boolean }

export function TurnWorkingLine({ working, label, keepSpace, afterTool, tasksThreadId }: {
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
  const tasks = useRunningTaskCount(desktopSummaryServices, tasksThreadId)

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

  const showTasks = tasks > 0 && keepSpace
  if (!showTasks && (!engaged || (!working && !keepSpace))) return null
  return (
    <div className={styles.line} data-after-tool={afterTool || undefined} data-exited={!working || undefined} data-testid="turn-working-line">
      {showTasks && faded ? (
        <span className={styles.pulseSlot} aria-hidden="true"><span className={styles.pulse} /></span>
      ) : (
        <span className={styles.glyph}>
          <ThinkingGlyph thinking={!faded} />
        </span>
      )}
      {showTasks ? <RunningTasksButton count={tasks} /> : null}
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

function RunningTasksButton({ count }: { count: number }): JSX.Element {
  const t = useT()
  return (
    <button type="button" className={`dc-quiet-action ${styles.tasks}`} onClick={() => setSummaryShown(true)}>
      {t(count === 1 ? 'conversation.runningTasks.one' : 'conversation.runningTasks.other', { count })}
    </button>
  )
}
