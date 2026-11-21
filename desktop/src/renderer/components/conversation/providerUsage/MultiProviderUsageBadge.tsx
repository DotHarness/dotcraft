import { useEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { CompactReading, MultiProviderUsagePanel, UsageProviderMark, usageProviderTitle, type UsageEntry } from './MultiProviderUsagePanel'
import { UsageHoverCard, useUsageHover } from './UsageHoverCard'
import type { ProviderSummary } from '../../../stores/providersStore'
import { readingSummary } from './usageReading'
import styles from './MultiProviderUsage.module.css'

const MARK_LIMIT = 3

export function MultiProviderUsageBadge({ main, lead, others }: { main: ProviderSummary; lead: UsageEntry | null; others: UsageEntry[] }): JSX.Element {
  const t = useT()
  const [open, setOpen] = useState(false)
  const hover = useUsageHover()
  const root = useRef<HTMLDivElement | null>(null)
  const entries = lead ? [lead, ...others] : others
  const marks = others.slice(0, others.length > MARK_LIMIT ? MARK_LIMIT - 1 : MARK_LIMIT)
  const hidden = others.slice(marks.length)
  const label = entries
    .map(({ provider, reading }) => `${usageProviderTitle(provider, t)}: ${readingSummary(reading, t)}`)
    .join(' · ')

  useEffect(() => {
    if (!open) return
    const outside = (event: PointerEvent): void => {
      if (!root.current?.contains(event.target as Node)) setOpen(false)
    }
    const escape = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') setOpen(false)
    }
    window.addEventListener('pointerdown', outside, true)
    window.addEventListener('keydown', escape)
    return () => {
      window.removeEventListener('pointerdown', outside, true)
      window.removeEventListener('keydown', escape)
    }
  }, [open])

  return (
    <div ref={root} className={styles.anchor} {...hover.handlers}>
      <button
        type="button"
        className={`dc-composer-pill ${styles.pill}`}
        aria-haspopup="dialog"
        aria-expanded={open}
        aria-label={label}
        onClick={() => {
          hover.dismiss()
          setOpen((current) => !current)
        }}
      >
        {lead ? (
          <>
            <span className={styles.leadMark}><UsageProviderMark provider={lead.provider} usage={lead.usage} size={14} /></span>
            <CompactReading reading={lead.reading} />
          </>
        ) : (
          <span className={styles.leadUnreported}><UsageProviderMark provider={main} usage={null} size={14} /></span>
        )}
        <span className={styles.stack} aria-hidden>
          {marks.map(({ provider, usage, reading }) => (
            <span key={provider.id} className={styles.chip} data-trouble={reading.tone === 'error' ? true : undefined}>
              <UsageProviderMark provider={provider} usage={usage} size={11} />
            </span>
          ))}
          {hidden.length > 0 && (
            <span className={`${styles.chip} ${styles.more}`}>+{hidden.length}</span>
          )}
        </span>
      </button>
      {hover.visible && !open && <UsageHoverCard entries={entries} />}
      {open && (
        <div className={styles.layer}>
          <MultiProviderUsagePanel entries={entries} />
        </div>
      )}
    </div>
  )
}
