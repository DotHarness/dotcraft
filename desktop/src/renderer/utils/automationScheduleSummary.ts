import { translate, type AppLocale } from '../../shared/locales'
import type { AutomationSchedule } from '../types/automation'
export function automationScheduleSummary(schedule: AutomationSchedule, locale: AppLocale): string {
  const t = (key: string) => translate(locale, key)
  let zone = schedule.timeZone ?? Intl.DateTimeFormat().resolvedOptions().timeZone
  try { new Intl.DateTimeFormat(locale, { timeZone: zone }) } catch { zone = 'UTC' }
  if (schedule.kind === 'at') {
    const date = schedule.at ? new Date(schedule.at) : null
    return date && Number.isFinite(date.getTime()) ? `${t('automation.schedule.at')} · ${date.toLocaleString(locale, { timeZone: zone })} (${zone})` : t('automation.schedule.at')
  }
  if (schedule.kind === 'every') return `${t('automation.schedule.every')} · ${(schedule.everyMs ?? 0) / 60000} ${t('automation.minuteUnit')}`
  const time = `${String(schedule.hour ?? 0).padStart(2, '0')}:${String(schedule.minute ?? 0).padStart(2, '0')}`
  const days = schedule.kind === 'weekly' ? ` · ${(schedule.days ?? []).map(day => t(`automation.day.${day}`)).join(', ')}` : ''
  return `${t(`automation.schedule.${schedule.kind}`)}${days} · ${time} (${zone})`
}
