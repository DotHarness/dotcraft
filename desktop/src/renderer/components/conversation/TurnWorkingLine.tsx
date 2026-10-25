import { useEffect, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { RunningShimmer } from '../ui/RunningShimmer'
import { ThinkingGlyph } from './ThinkingGlyph'
import type { TurnThinkingLabel } from './turnThinkingStatus'
import styles from './TurnWorkingLine.module.css'

const FADE_MS = 180

interface ShownLabel { text: string; live: boolean }

export function TurnWorkingLine({ working, label, keepSpace, afterTool }: {
  working: boolean
  label: TurnThinkingLabel | null
  keepSpace: boolean
  afterTool: boolean
}): JSX.Element | null {
  const t = useT()
  const [engaged, setEngaged] = useState(working)
  const [faded, setFaded] = useState(!working)
  if (working && !engaged) setEngaged(true)
  if (working && faded) setFaded(false)

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

  if (!engaged || (!working && !keepSpace)) return null
  return (
    <div className={styles.line} data-after-tool={afterTool || undefined} data-exited={!working || undefined} data-testid="turn-working-line">
      <span className={styles.glyph}>
        <ThinkingGlyph thinking={!faded} />
      </span>
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
