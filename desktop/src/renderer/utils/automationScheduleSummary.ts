import { translate, type AppLocale } from '../../shared/locales'
import type { AutomationSchedule } from '../types/automation'
export function automationScheduleSummary(
  schedule: AutomationSchedule,
  locale: AppLocale,
  options: { includeTimeZone?: boolean } = {}
): string {
  const t = (key: string) => translate(locale, key)
  const includeTimeZone = options.includeTimeZone ?? true
  let zone = schedule.timeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone
  try { new Intl.DateTimeFormat(locale, { timeZone: zone }) } catch { zone = 'UTC' }
  if (schedule.kind === 'at') {
    const date = schedule.at ? new Date(schedule.at) : null
    if (!date || !Number.isFinite(date.getTime())) return t('automation.schedule.at')
    const summary = `${t('automation.schedule.at')} · ${date.toLocaleString(locale, { timeZone: zone })}`
    return includeTimeZone ? `${summary} (${zone})` : summary
  }
  if (schedule.kind === 'every') return `${t('automation.schedule.every')} · ${(schedule.everyMs ?? 0) / 60000} ${t('automation.minuteUnit')}`
  const time = `${String(schedule.hour ?? 0).padStart(2, '0')}:${String(schedule.minute ?? 0).padStart(2, '0')}`
  const days = schedule.kind === 'weekly' ? ` · ${(schedule.days ?? []).map(day => t(`automation.day.${day}`)).join(', ')}` : ''
  const summary = `${t(`automation.schedule.${schedule.kind}`)}${days} · ${time}`
  return includeTimeZone ? `${summary} (${zone})` : summary
}
