import { useEffect, useRef, useState, type JSX } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useProviderUsage, useProvidersStore, type ProviderSummary } from '../../stores/providersStore'
import { formatSpendAmount } from '../../utils/providerBalance'
import { ProviderMark, type ProviderMarkKind } from '../ui/ProviderMark'
import { PluginUsageIcon } from './PluginUsageIcon'
import { ProviderUsagePopover } from './ProviderUsagePopover'
import { UsageHoverCard, useUsageHover } from './providerUsage/UsageHoverCard'
import { readProviderUsage } from './providerUsage/usageReading'
import styles from './ApiKeyUsageBadge.module.css'

export function ApiKeyUsageBadge({ provider, markKind }: { provider: ProviderSummary; markKind: ProviderMarkKind | null }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const usage = useProviderUsage(provider.id)
  const refresh = useProvidersStore((state) => state.refreshUsage)
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const remaining = usage?.values?.limit_remaining
  const amount = remaining ?? usage?.values?.usage
  const formatted = amount != null ? formatSpendAmount(amount, usage?.labels?.currency, locale) : null
  const suffix = t(remaining != null ? 'settings.llm.openrouter.left' : 'settings.llm.openrouter.used')
  const label = formatted ? `${formatted} ${suffix}` : t('settings.llm.openrouter.usage')
  const hover = useUsageHover()
  const reading = readProviderUsage(usage, locale, t)

  useEffect(() => { void refresh(provider.id) }, [provider.id, refresh])
  useEffect(() => {
    if (!open) return
    void refresh(provider.id)
    const outside = (event: PointerEvent): void => { if (!root.current?.contains(event.target as Node)) setOpen(false) }
    const escape = (event: KeyboardEvent): void => { if (event.key === 'Escape') setOpen(false) }
    document.addEventListener('pointerdown', outside)
    document.addEventListener('keydown', escape)
    return () => {
      document.removeEventListener('pointerdown', outside)
      document.removeEventListener('keydown', escape)
    }
  }, [open, provider.id, refresh])

  return <div ref={root} className={styles.root} {...hover.handlers}>
    <button type="button" className={`dc-composer-pill ${styles.badge}`} aria-haspopup="dialog"
      aria-expanded={open} aria-label={`${provider.displayName}: ${label}`} onClick={() => { hover.dismiss(); setOpen(!open) }}>
      {markKind
        ? <ProviderMark kind={markKind} size={16} />
        : <PluginUsageIcon pluginId={usage?.pluginId ?? provider.pluginId ?? null} size={16} />}
      {formatted ? <><span>{formatted}</span><span className={styles.suffix}>{suffix}</span></> : <span>{label}</span>}
    </button>
    {hover.visible && !open && usage && reading && <UsageHoverCard entries={[{ provider, usage, reading }]} />}
    {open && <div className={styles.popover}>
      <ProviderUsagePopover title={provider.displayName} usage={usage} markKind={markKind ?? undefined} pluginId={usage?.pluginId ?? provider.pluginId ?? null} />
    </div>}
  </div>
}
