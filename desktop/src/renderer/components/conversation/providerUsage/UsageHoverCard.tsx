import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { UsageProviderMark, usageProviderTitle, type UsageEntry } from './MultiProviderUsagePanel'
import { toneColor, windowReadings, type Translate, type UsageReading } from './usageReading'
import styles from './MultiProviderUsage.module.css'

const HOVER_DELAY_MS = 300

interface UsageHover {
  visible: boolean
  dismiss: () => void
  handlers: { onPointerEnter: () => void; onPointerLeave: () => void; onFocus: () => void; onBlur: () => void }
}

export function useUsageHover(): UsageHover {
  const [visible, setVisible] = useState(false)
  const timer = useRef<number | null>(null)
  const clear = useCallback(() => {
    if (timer.current != null) window.clearTimeout(timer.current)
    timer.current = null
  }, [])
  const show = (): void => {
    clear()
    timer.current = window.setTimeout(() => setVisible(true), HOVER_DELAY_MS)
  }
  const dismiss = (): void => {
    clear()
    setVisible(false)
  }
  useEffect(() => clear, [clear])
  return { visible, dismiss, handlers: { onPointerEnter: show, onPointerLeave: dismiss, onFocus: show, onBlur: dismiss } }
}

function HoverReading({ reading, t }: { reading: UsageReading; t: Translate }): JSX.Element {
  if (reading.kind === 'amount') {
    return <span className={styles.hoverValue} data-tone={reading.tone === 'error' ? 'error' : undefined}>{reading.text}</span>
  }
  return (
    <span className={styles.hoverValue}>
      {windowReadings(reading.windows, t).map((window, index) => (
        <span key={window.kind}>
          {index > 0 && <span className={styles.hoverSeparator}> · </span>}
          <span style={window.tone === 'ok' ? undefined : { color: toneColor(window.tone) }}>{window.text}</span>
        </span>
      ))}
    </span>
  )
}

export function UsageHoverCard({ entries }: { entries: UsageEntry[] }): JSX.Element {
  const t = useT()
  return (
    <div className={styles.hover} role="tooltip">
      {entries.map(({ provider, usage, reading }) => (
        <div key={provider.id} className={styles.hoverRow}>
          <UsageProviderMark provider={provider} usage={usage} size={14} />
          <span className={styles.hoverName}>{usageProviderTitle(provider, t)}</span>
          <HoverReading reading={reading} t={t} />
        </div>
      ))}
    </div>
  )
}
