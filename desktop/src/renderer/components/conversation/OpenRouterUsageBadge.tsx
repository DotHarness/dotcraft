import { useEffect, useRef, useState, type JSX } from 'react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import { useProviderUsage, useProvidersStore, type ProviderSummary } from '../../stores/providersStore'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ProviderMark } from '../ui/ProviderMark'
import { ProviderUsagePopover } from './ProviderUsagePopover'
import styles from './OpenRouterUsageBadge.module.css'

export function OpenRouterUsageBadge({ provider }: { provider: ProviderSummary }): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const usage = useProviderUsage(provider.id)
  const refresh = useProvidersStore((state) => state.refreshUsage)
  const [open, setOpen] = useState(false)
  const root = useRef<HTMLDivElement>(null)
  const remaining = usage?.values?.limit_remaining
  const amount = remaining ?? usage?.values?.usage
  const formatted = amount != null ? new Intl.NumberFormat(locale, {
    style: 'currency', currency: 'USD', maximumFractionDigits: 6
  }).format(amount) : null
  const suffix = t(remaining != null ? 'settings.llm.openrouter.left' : 'settings.llm.openrouter.used')
  const label = formatted ? `${formatted} ${suffix}` : t('settings.llm.openrouter.usage')

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

  return <div ref={root} className={styles.root}>
    <ActionTooltip label={open ? '' : `${provider.displayName}: ${label}`} placement="top">
      <button type="button" className={`dc-composer-pill ${styles.badge}`} aria-haspopup="dialog"
        aria-expanded={open} aria-label={`${provider.displayName}: ${label}`} onClick={() => setOpen(!open)}>
        <ProviderMark kind="openrouter" size={16} />
        {formatted ? <><span>{formatted}</span><span className={styles.suffix}>{suffix}</span></> : <span>{label}</span>}
      </button>
    </ActionTooltip>
    {open && <div className={styles.popover}>
      <ProviderUsagePopover title={provider.displayName} usage={usage} markKind="openrouter" />
    </div>}
  </div>
}
