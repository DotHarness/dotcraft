import { translate, type AppLocale } from '../../shared/locales'

export function automationNextRun(nextRunAt: string | null | undefined, now: number, locale: AppLocale): string | null {
  if (!nextRunAt) return null
  const remaining = Date.parse(nextRunAt) - now
  if (!Number.isFinite(remaining)) return null
  if (remaining <= 0) return translate(locale, 'automation.dueNow')
  const [size, unit] = remaining >= 86400000 ? [86400000, 'day'] as const
    : remaining >= 3600000 ? [3600000, 'hour'] as const : [60000, 'minute'] as const
  const time = new Intl.RelativeTimeFormat(locale, { numeric: 'always' }).format(Math.ceil(remaining / size), unit)
  return translate(locale, 'automation.nextRunRelative', { time })
}
