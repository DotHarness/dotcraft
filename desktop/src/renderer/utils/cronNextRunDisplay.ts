/**
 * Next-run labels for Automations Cron cards (local time + optional relative).
 */

import { translate, type AppLocale, type MessageKey } from '../../shared/locales'

export interface NextRunDisplay {
  absolute: string
  relative: string | null
}

function tr(
  locale: AppLocale,
  key: MessageKey | string,
  vars?: Record<string, string | number>
): string {
  return translate(locale, key, vars)
}

function formatRelativeUntil(ms: number, locale: AppLocale): string {
  const diff = ms - Date.now()
  if (diff <= 0) return tr(locale, 'cron.next.now')
  const seconds = Math.floor(diff / 1000)
  if (seconds < 60) return tr(locale, 'cron.next.inSec', { n: seconds })
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return tr(locale, 'cron.next.inMin', { n: minutes })
  const hours = Math.floor(minutes / 60)
  if (hours < 48) return tr(locale, 'cron.next.inHour', { n: hours })
  const days = Math.floor(hours / 24)
  return tr(locale, 'cron.next.inDay', { n: days })
}

/**
 * @param nextRunAtMs UTC epoch ms from server
 * @param enabled when false, absolute line still shows the scheduled instant; relative is omitted
 */
export function formatNextRun(
  nextRunAtMs: number | null | undefined,
  enabled: boolean,
  locale: AppLocale
): NextRunDisplay {
  if (nextRunAtMs == null) {
    return { absolute: tr(locale, 'cron.nextNotScheduled'), relative: null }
  }
  const d = new Date(nextRunAtMs)
  const absolute = Number.isNaN(d.getTime())
    ? '—'
    : d.toLocaleString(undefined, {
        month: 'short',
        day: 'numeric',
        year: d.getFullYear() !== new Date().getFullYear() ? 'numeric' : undefined,
        hour: '2-digit',
        minute: '2-digit'
      })
  const relative = enabled ? formatRelativeUntil(nextRunAtMs, locale) : null
  return { absolute, relative }
}
