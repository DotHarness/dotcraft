import type { ReactNode } from 'react'
import type { AutomationInput, AutomationSchedule } from '../../types/automation'
import { useT } from '../../contexts/LocaleContext'
import { Input } from '../ui/Input'
import { Select } from '../ui/Select'
import { TimeSelect } from '../ui/TimeSelect'
import { resolveSystemTimeZone } from '../../utils/automationTimeZone'

export function AutomationScheduleEditor({
  value,
  onChange,
  notificationPolicy,
  onNotificationPolicyChange
}: {
  value: AutomationSchedule
  onChange(value: AutomationSchedule): void
  notificationPolicy: AutomationInput['notificationPolicy']
  onNotificationPolicyChange(value: AutomationInput['notificationPolicy']): void
}): JSX.Element {
  const t = useT()
  const set = (patch: Partial<AutomationSchedule>) => onChange({ ...value, ...patch })
  const scheduleOptions = (['at', 'every', 'daily', 'weekdays', 'weekly'] as const)
    .map((kind) => ({ value: kind, label: t(`automation.schedule.${kind}`) }))
  const notificationOptions = (['important', 'all', 'failures'] as const)
    .map((policy) => ({ value: policy, label: t(`automation.notify.${policy}`) }))

  return (
    <section className="dc-automation-section">
      <h3>{t('automation.timeSection')}</h3>
      <div className="dc-automation-fields">
        <SettingRow label={t('automation.repeat')}>
          <Select
            appearance="frameless"
            adaptiveWidth={false}
            value={value.kind}
            options={scheduleOptions}
            ariaLabel={t('automation.repeat')}
            onValueChange={(kind) => onChange({
              kind,
              timeZone: value.timeZone ?? resolveSystemTimeZone(),
              hour: value.hour ?? 9,
              minute: value.minute ?? 0,
              everyMs: value.everyMs ?? 3600000,
              days: value.days ?? [1],
              at: value.at ?? new Date(Date.now() + 3600000).toISOString()
            })}
          />
        </SettingRow>
        {value.kind === 'at' ? (
          <SettingRow label={t('automation.date')}>
            <Input className="dc-automation-inline-input" aria-label={t('automation.date')} type="datetime-local" value={localDate(value.at)} onChange={(event) => { if (event.target.value) set({ at: new Date(event.target.value).toISOString() }) }} />
          </SettingRow>
        ) : null}
        {value.kind === 'every' ? (
          <SettingRow label={t('automation.minutes')}>
            <Input className="dc-automation-inline-input dc-plain-number" aria-label={t('automation.minutes')} type="number" min="1" value={(value.everyMs ?? 3600000) / 60000} onChange={(event) => set({ everyMs: Number(event.target.value) * 60000 })} />
          </SettingRow>
        ) : null}
        {value.kind === 'weekly' ? (
          <SettingRow label={t('automation.on')}>
            <div className="dc-automation-weekdays">
              {[1, 2, 3, 4, 5, 6, 7].map((day) => (
                <label key={day}>
                  <input type="checkbox" checked={value.days?.includes(day) ?? false} onChange={(event) => set({ days: event.target.checked ? [...(value.days ?? []), day] : value.days?.filter((value) => value !== day) })} />
                  <span>{t(`automation.day.${day}`)}</span>
                </label>
              ))}
            </div>
          </SettingRow>
        ) : null}
        {!['every', 'at'].includes(value.kind) ? (
          <SettingRow label={t('automation.time')}>
            <TimeSelect hour={value.hour ?? 9} minute={value.minute ?? 0} ariaLabel={t('automation.time')} onChange={(hour, minute) => set({ hour, minute })} />
          </SettingRow>
        ) : null}
        <SettingRow label={t('automation.notifications')}>
          <Select appearance="frameless" adaptiveWidth={false} value={notificationPolicy} options={notificationOptions} ariaLabel={t('automation.notifications')} onValueChange={onNotificationPolicyChange} />
        </SettingRow>
      </div>
    </section>
  )
}

function SettingRow({ label, children }: { label: string; children: ReactNode }): JSX.Element {
  return <div className="dc-automation-setting-row"><span>{label}</span><div>{children}</div></div>
}

function localDate(value?: string | null): string {
  if (!value) return ''
  const date = new Date(value)
  return new Date(date.getTime() - date.getTimezoneOffset() * 60000).toISOString().slice(0, 16)
}

export function validSchedule(value: AutomationSchedule): boolean {
  if (value.kind === 'at') return !!value.at && Number.isFinite(Date.parse(value.at))
  if (value.kind === 'every') return Number.isFinite(value.everyMs) && (value.everyMs ?? 0) >= 60000
  try { new Intl.DateTimeFormat('en', { timeZone: value.timeZone ?? '' }).format() } catch { return false }
  return (value.hour ?? -1) >= 0 && (value.hour ?? 24) < 24 && (value.minute ?? -1) >= 0 && (value.minute ?? 60) < 60 && (value.kind !== 'weekly' || (value.days?.length ?? 0) > 0)
}
