import { useEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { useProviderUsage, type ProviderUsageSnapshot } from '../../../stores/providersStore'
import {
  shapeChatGptUsageWindows,
  type ChatGptUsageDisplayWindow,
  type ChatGptUsageWindowKind
} from '../../../utils/chatgptUsageWindows'
import { formatBalanceAmount, formatSpendAmount } from '../../../utils/providerBalance'
import { ProviderUsagePopover } from '../../conversation/ProviderUsagePopover'
import type { ProviderInfoWire } from '../providerInfo'
import { providerPresetMark } from './providerPresets'
import styles from './ProviderUsageMeter.module.css'

type Translate = ReturnType<typeof useT>

const POPOVER_MIN_SPACE_BELOW = 280

const WINDOW_LABEL_KEYS = {
  fiveHour: 'composer.chatgptBadge.sessionRemaining',
  weekly: 'composer.chatgptBadge.weeklyRemaining',
  primary: 'composer.chatgptBadge.primaryRemaining',
  secondary: 'composer.chatgptBadge.secondaryRemaining'
} as const satisfies Record<ChatGptUsageWindowKind, string>

export function ProviderUsageMeter({ provider }: { provider: ProviderInfoWire }): JSX.Element | null {
  const t = useT()
  const locale = useLocale()
  const usage = useProviderUsage(provider.id)
  const root = useRef<HTMLSpanElement>(null)
  const [position, setPosition] = useState<CSSProperties | null>(null)

  useEffect(() => {
    if (!position) return
    const close = (): void => setPosition(null)
    const outside = (event: PointerEvent): void => { if (!root.current?.contains(event.target as Node)) close() }
    const escape = (event: KeyboardEvent): void => { if (event.key === 'Escape') close() }
    window.addEventListener('pointerdown', outside, true)
    window.addEventListener('keydown', escape, true)
    window.addEventListener('scroll', close, true)
    return () => {
      window.removeEventListener('pointerdown', outside, true)
      window.removeEventListener('keydown', escape, true)
      window.removeEventListener('scroll', close, true)
    }
  }, [position])

  if (!usage?.available) return null
  const windows = shapeChatGptUsageWindows(usage)
  const amount = windows.length === 0 ? amountReading(usage, locale, t) : null
  if (windows.length === 0 && !amount) return null

  const summary = amount ?? windows.map((display) => t(WINDOW_LABEL_KEYS[display.kind], { percent: remainingPercent(display) })).join(', ')
  const toggle = (): void => {
    const rect = root.current?.getBoundingClientRect()
    if (position || !rect) {
      setPosition(null)
      return
    }
    const below = rect.bottom + POPOVER_MIN_SPACE_BELOW < window.innerHeight
    setPosition({
      right: window.innerWidth - rect.right,
      ...(below ? { top: rect.bottom + 6 } : { bottom: window.innerHeight - rect.top + 6 })
    })
  }

  return (
    <span
      ref={root}
      className={styles.anchor}
      onClick={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
    >
      <button
        type="button"
        className={styles.meter}
        aria-haspopup="dialog"
        aria-expanded={position != null}
        aria-label={`${provider.displayName}: ${summary}`}
        onClick={toggle}
      >
        {amount
          ? <span className={styles.amount} data-tone={usage.limitReachedKind ? 'error' : undefined}>{amount}</span>
          : windows.map((display) => <UsageWindow key={display.kind} display={display} t={t} />)}
      </button>
      {position && (
        <span className={styles.popover} style={position}>
          <ProviderUsagePopover
            title={provider.displayName}
            usage={usage}
            markKind={usage.pluginId ? undefined : providerPresetMark(provider) ?? undefined}
          />
        </span>
      )}
    </span>
  )
}

function UsageWindow({ display, t }: { display: ChatGptUsageDisplayWindow; t: Translate }): JSX.Element {
  const remaining = remainingPercent(display)
  return (
    <span className={styles.window}>
      <span className={styles.track}>
        <span style={{ width: `${remaining}%`, background: remainingColor(remaining) }} />
      </span>
      <span>{t(WINDOW_LABEL_KEYS[display.kind], { percent: remaining })}</span>
    </span>
  )
}

function amountReading(usage: ProviderUsageSnapshot, locale: string, t: Translate): string | null {
  if (usage.labels?.scope === 'apiKey') {
    const remaining = usage.values?.limit_remaining
    const amount = remaining ?? usage.values?.usage
    if (amount == null) return null
    const suffix = t(remaining != null ? 'settings.llm.openrouter.left' : 'settings.llm.openrouter.used')
    return `${formatSpendAmount(amount, usage.labels.currency, locale)} ${suffix}`
  }
  const balance = usage.balances?.[0]
  return balance ? formatBalanceAmount(balance.total, balance.currency, locale) : null
}

function remainingPercent(display: ChatGptUsageDisplayWindow): number {
  return Math.round(Math.max(0, Math.min(100, 100 - display.window.usedPercent)))
}

function remainingColor(remaining: number): string {
  if (remaining < 20) return 'var(--error)'
  if (remaining < 40) return 'var(--warning)'
  return 'var(--success)'
}
