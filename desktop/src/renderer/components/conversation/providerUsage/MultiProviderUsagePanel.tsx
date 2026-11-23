import { Fragment, useLayoutEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import type { ProviderSummary, ProviderUsageSnapshot } from '../../../stores/providersStore'
import { ProviderMark, type ProviderMarkKind } from '../../ui/ProviderMark'
import { providerPresetMark } from '../../settings/providers/providerPresets'
import { PluginUsageIcon } from '../PluginUsageIcon'
import { ProviderUsageCard, ProviderUsageContent } from '../ProviderUsagePopover'
import { toneColor, type UsageReading } from './usageReading'
import styles from './MultiProviderUsage.module.css'

export interface UsageEntry {
  provider: ProviderSummary
  usage: ProviderUsageSnapshot
  reading: UsageReading
}

export function usageProviderMarkKind(provider: ProviderSummary, usage: ProviderUsageSnapshot | null): ProviderMarkKind | null {
  if (usage?.pluginId) return null
  return provider.webSearchBackend === 'deepseek' ? 'deepseek' : providerPresetMark(provider)
}

export function UsageProviderMark({ provider, usage, size }: { provider: ProviderSummary; usage: ProviderUsageSnapshot | null; size: number }): JSX.Element {
  const kind = usageProviderMarkKind(provider, usage)
  return kind
    ? <ProviderMark kind={kind} size={size} />
    : <PluginUsageIcon pluginId={usage?.pluginId ?? provider.pluginId ?? null} size={size} />
}

export function usageProviderTitle(provider: ProviderSummary, t: (key: string) => string): string {
  return provider.authMethod === 'chatgptOAuth' ? t('composer.chatgptUsage.title') : provider.displayName
}

export function CompactReading({ reading }: { reading: UsageReading }): JSX.Element {
  if (reading.kind === 'amount') {
    return <span className={styles.amount} data-tone={reading.tone === 'error' ? 'error' : undefined}>{reading.text}</span>
  }
  return (
    <span className={styles.miniTrack}><span style={{ width: `${reading.remaining}%`, background: toneColor(reading.tone) }} /></span>
  )
}

const VIEWPORT_MARGIN = 8

export function packColumns(heights: number[], chrome: number, between: number, limit: number): number[][] {
  const columns: number[][] = []
  let current: number[] = []
  let used = chrome
  heights.forEach((height, index) => {
    if (current.length > 0 && used + between + height > limit) {
      columns.push(current)
      current = []
      used = chrome
    }
    used += (current.length > 0 ? between : 0) + height
    current.push(index)
  })
  if (current.length > 0) columns.push(current)
  return columns
}

function UsageSections({ entries }: { entries: UsageEntry[] }): JSX.Element {
  const t = useT()
  return (
    <>
      {entries.map(({ provider, usage }, index) => (
        <Fragment key={provider.id}>
          {index > 0 && <div className={styles.separator} />}
          <div data-usage-section>
            <ProviderUsageContent
              title={usageProviderTitle(provider, t)}
              usage={usage}
              markKind={usageProviderMarkKind(provider, usage) ?? undefined}
              pluginId={usage.pluginId ?? provider.pluginId ?? null}
              showFetched={false}
            />
          </div>
        </Fragment>
      ))}
    </>
  )
}

export function MultiProviderUsagePanel({ entries }: { entries: UsageEntry[] }): JSX.Element {
  const t = useT()
  const root = useRef<HTMLDivElement | null>(null)
  const probe = useRef<HTMLDivElement | null>(null)
  const [columns, setColumns] = useState<number[][] | null>(null)
  const [shift, setShift] = useState(0)
  const content = entries.map(({ provider, usage }) => `${provider.id}:${usage.fetchedAt ?? ''}`).join('|')

  useLayoutEffect(() => {
    const card = probe.current?.firstElementChild
    if (!card) return
    const sections = [...card.querySelectorAll('[data-usage-section]')].map((section) => section.getBoundingClientRect())
    const heights = sections.map((section) => section.height)
    const between = sections.length > 1 ? sections[1].top - sections[0].bottom : 0
    const chrome = card.getBoundingClientRect().height - heights.reduce((sum, height) => sum + height, 0) - between * (sections.length - 1)
    const next = packColumns(heights, chrome, between, Math.min(window.innerHeight * 0.7, 520))
    setColumns((current) => JSON.stringify(current) === JSON.stringify(next) ? current : next)
  }, [content])

  useLayoutEffect(() => {
    const rect = root.current?.getBoundingClientRect()
    if (!rect) return
    const overflow = rect.right + shift - (window.innerWidth - VIEWPORT_MARGIN)
    setShift(Math.ceil(Math.max(0, Math.min(overflow, rect.left + shift - VIEWPORT_MARGIN))))
  }, [columns])

  return (
    <div
      ref={root}
      className={styles.columns}
      role="dialog"
      aria-label={t('composer.providerUsage.dialog')}
      style={shift > 0 ? { transform: `translateX(${-shift}px)` } : undefined}
    >
      <div ref={probe} className={styles.probe} aria-hidden>
        <ProviderUsageCard><UsageSections entries={entries} /></ProviderUsageCard>
      </div>
      {columns?.map((column) => (
        <ProviderUsageCard key={entries[column[0]]?.provider.id ?? column[0]}>
          <UsageSections entries={column.flatMap((index) => entries[index] ?? [])} />
        </ProviderUsageCard>
      ))}
    </div>
  )
}
