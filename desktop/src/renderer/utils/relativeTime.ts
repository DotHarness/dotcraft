import { translate } from '../../shared/locales/catalog'
import type { AppLocale } from '../../shared/locales/types'

/**
 * Compact by design: the output shares a sidebar row with the thread title, so
 * every locale uses a number plus a short unit rather than Intl's "3 hours ago".
 */
export function formatRelativeTime(
  isoDate: string,
  now: Date = new Date(),
  locale: AppLocale = 'en'
): string {
  const date = new Date(isoDate)
  const diffSec = Math.floor((now.getTime() - date.getTime()) / 1000)
  const diffMin = Math.floor(diffSec / 60)
  const diffHours = Math.floor(diffMin / 60)
  const diffDays = Math.floor(diffHours / 24)
  const diffWeeks = Math.floor(diffDays / 7)
  const diffMonths = Math.floor(diffDays / 30)

  if (diffSec < 60) return translate(locale, 'relativeTime.justNow')
  if (diffMin < 60) return translate(locale, 'relativeTime.minutes', { count: diffMin })
  if (diffHours < 24) return translate(locale, 'relativeTime.hours', { count: diffHours })
  if (diffDays < 7) return translate(locale, 'relativeTime.days', { count: diffDays })
  if (diffDays < 30) return translate(locale, 'relativeTime.weeks', { count: diffWeeks })
  return translate(locale, 'relativeTime.months', { count: diffMonths })
}
