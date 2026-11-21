import type { ProviderUsageSnapshot } from '../../../stores/providersStore'
import {
  shapeChatGptUsageWindows,
  type ChatGptUsageDisplayWindow,
  type ChatGptUsageWindowKind
} from '../../../utils/chatgptUsageWindows'
import { formatBalanceAmount, formatSpendAmount } from '../../../utils/providerBalance'

export type Translate = (key: string, vars?: Record<string, string | number>) => string
export type UsageTone = 'ok' | 'warning' | 'error'

export type UsageReading =
  | { kind: 'windows'; remaining: number; windows: ChatGptUsageDisplayWindow[]; tone: UsageTone }
  | { kind: 'amount'; text: string; tone: UsageTone }

const WINDOW_REMAINING_KEYS: Record<ChatGptUsageWindowKind, string> = {
  fiveHour: 'composer.chatgptBadge.sessionRemaining',
  weekly: 'composer.chatgptBadge.weeklyRemaining',
  primary: 'composer.chatgptBadge.primaryRemaining',
  secondary: 'composer.chatgptBadge.secondaryRemaining'
}

export function windowRemaining(display: ChatGptUsageDisplayWindow): number {
  return Math.round(Math.max(0, Math.min(100, 100 - display.window.usedPercent)))
}

export function toneForRemaining(remaining: number): UsageTone {
  if (remaining < 20) return 'error'
  if (remaining < 40) return 'warning'
  return 'ok'
}

export function toneColor(tone: UsageTone): string {
  if (tone === 'error') return 'var(--error)'
  if (tone === 'warning') return 'var(--warning)'
  return 'var(--success)'
}

export function readProviderUsage(usage: ProviderUsageSnapshot | null, locale: string, t: Translate): UsageReading | null {
  if (!usage?.available) return null
  const windows = shapeChatGptUsageWindows(usage)
  if (windows.length > 0) {
    const remaining = Math.min(...windows.map(windowRemaining))
    return { kind: 'windows', remaining, windows, tone: toneForRemaining(remaining) }
  }
  if (usage.labels?.scope === 'apiKey') {
    const left = usage.values?.limit_remaining
    const amount = left ?? usage.values?.usage
    if (amount == null) return null
    const suffix = t(left != null ? 'settings.llm.openrouter.left' : 'settings.llm.openrouter.used')
    return { kind: 'amount', text: `${formatSpendAmount(amount, usage.labels.currency, locale)} ${suffix}`, tone: usage.limitReachedKind ? 'error' : 'ok' }
  }
  const balance = usage.balances?.[0]
  if (!balance) return null
  return { kind: 'amount', text: formatBalanceAmount(balance.total, balance.currency, locale), tone: usage.limitReachedKind ? 'error' : 'ok' }
}

const TONE_RANK: Record<UsageTone, number> = { error: 0, warning: 1, ok: 2 }

export function orderBySeverity<T extends { reading: { tone: UsageTone } }>(entries: T[]): T[] {
  return entries
    .map((entry, index) => ({ entry, index }))
    .sort((a, b) => TONE_RANK[a.entry.reading.tone] - TONE_RANK[b.entry.reading.tone] || a.index - b.index)
    .map(({ entry }) => entry)
}

export function windowReadings(windows: ChatGptUsageDisplayWindow[], t: Translate): Array<{ kind: ChatGptUsageWindowKind; text: string; tone: UsageTone }> {
  return windows.map((display) => {
    const remaining = windowRemaining(display)
    return { kind: display.kind, text: t(WINDOW_REMAINING_KEYS[display.kind], { percent: remaining }), tone: toneForRemaining(remaining) }
  })
}

export function readingSummary(reading: UsageReading, t: Translate): string {
  if (reading.kind === 'amount') return reading.text
  return windowReadings(reading.windows, t).map((window) => window.text).join(', ')
}
