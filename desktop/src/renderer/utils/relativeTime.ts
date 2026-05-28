import type { AppLocale } from '../../shared/locales/types'

/**
 * Formats an ISO 8601 date string as a compact relative time label.
 * Used in the sidebar thread list (spec §9.5).
 *
 * English (`en`): compact legacy form (e.g. "3h", "just now").
 * Chinese (`zh-Hans`): `Intl.RelativeTimeFormat` for natural phrasing.
 */
export function formatRelativeTime(
  isoDate: string,
  now: Date = new Date(),
  locale: AppLocale = 'en'
): string {
  const date = new Date(isoDate)
  const diffMs = now.getTime() - date.getTime()
  const diffSec = Math.floor(diffMs / 1000)

  if (locale === 'zh-Hans') {
    const rtf = new Intl.RelativeTimeFormat('zh-Hans', { numeric: 'auto' })
    if (diffSec < 60) return rtf.format(-diffSec, 'second')
    const diffMin = Math.floor(diffSec / 60)
    if (diffMin < 60) return rtf.format(-diffMin, 'minute')
    const diffHours = Math.floor(diffMin / 60)
    if (diffHours < 24) return rtf.format(-diffHours, 'hour')
    const diffDays = Math.floor(diffHours / 24)
    if (diffDays < 7) return rtf.format(-diffDays, 'day')
    const diffWeeks = Math.floor(diffDays / 7)
    if (diffDays < 30) return rtf.format(-diffWeeks, 'week')
    const diffMonths = Math.floor(diffDays / 30)
    return rtf.format(-diffMonths, 'month')
  }

  const diffMin = Math.floor(diffSec / 60)
  const diffHours = Math.floor(diffMin / 60)
  const diffDays = Math.floor(diffHours / 24)
  const diffWeeks = Math.floor(diffDays / 7)
  const diffMonths = Math.floor(diffDays / 30)

  if (diffSec < 60) return 'just now'
  if (diffMin < 60) return `${diffMin}m`
  if (diffHours < 24) return `${diffHours}h`
  if (diffDays < 7) return `${diffDays}d`
  if (diffDays < 30) return `${diffWeeks}w`
  return `${diffMonths}mo`
}
