import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ThinkingGlyph } from './ThinkingGlyph'
import type { TurnThinkingLabel } from './turnThinkingStatus'
import styles from './TurnWorkingLine.module.css'

const EXIT_DELAY_MS = 300

interface ShownLabel { text: string; live: boolean }

export function TurnWorkingLine({ working, label, keepSpace, afterTool }: {
  working: boolean
  label: TurnThinkingLabel | null
  keepSpace: boolean
  afterTool: boolean
}): JSX.Element | null {
  const t = useT()
  const [engaged, setEngaged] = useState(working)
  const [settled, setSettled] = useState(false)
  const [exited, setExited] = useState(false)
  if (working && !engaged) setEngaged(true)
  if (working && (settled || exited)) {
    setSettled(false)
    setExited(false)
  }

  const current: ShownLabel | null = label == null ? null
    : label.kind === 'waitingForAnswer' ? { text: t('conversation.thinking.waitingForAnswer'), live: true }
    : { text: label.heading || t('conversation.thinking.streaming'), live: true }
  const [shown, setShown] = useState(current)
  if (current && (current.text !== shown?.text || current.live !== shown.live)) setShown(current)

  useEffect(() => {
    if (!settled) return undefined
    const timer = setTimeout(() => setExited(true), EXIT_DELAY_MS)
    return () => clearTimeout(timer)
  }, [settled])

  if (!engaged || (!working && !keepSpace)) return null
  return (
    <div className={styles.line} data-after-tool={afterTool || undefined} data-exited={exited || undefined} data-testid="turn-working-line">
      <span className={styles.glyph}>
        <ThinkingGlyph thinking={working} onSettled={() => setSettled(true)} />
      </span>
      {shown ? (
        <span className={styles.label} data-visible={current ? 'true' : 'false'} aria-hidden={!current}>
          <RunningShimmer key={`${shown.text}:${shown.live}`} active={shown.live} className={styles.text}>
            {shown.text}
          </RunningShimmer>
        </span>
      ) : null}
    </div>
  )
}
