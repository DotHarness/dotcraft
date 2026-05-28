import { useEffect, useState, type CSSProperties } from 'react'
import { useT } from '../../contexts/LocaleContext'
import type { AutomationSchedule } from '../../stores/automationsStore'

/**
 * Schedule preset buttons + per-preset detail inputs used by the New Task dialog.
 * Stays compact until a preset is selected.
 */
export type SchedulePreset = 'once' | 'hourly' | 'daily' | 'weekly' | 'custom'

interface Props {
  value: AutomationSchedule | null
  onChange(schedule: AutomationSchedule | null): void
}

const HOUR_MS = 60 * 60 * 1000

function toPreset(s: AutomationSchedule | null): SchedulePreset {
  if (!s || s.kind === 'once') return 'once'
  if (s.kind === 'daily') return 'daily'
  if (s.kind === 'every' && s.everyMs === HOUR_MS) return 'hourly'
  if (s.kind === 'every' && s.everyMs === 7 * 24 * HOUR_MS) return 'weekly'
  if (s.kind === 'every') return 'custom'
  return 'once'
}

function resolveTz(s?: AutomationSchedule | null): string {
  if (s?.tz) return s.tz
  try {
    return Intl.DateTimeFormat().resolvedOptions().timeZone ?? 'UTC'
  } catch {
    return 'UTC'
  }
}

export function SchedulePicker({ value, onChange }: Props): JSX.Element {
  const t = useT()
  const preset = toPreset(value)
  const [customMinutes, setCustomMinutes] = useState<number>(
    value?.everyMs ? Math.max(1, Math.round(value.everyMs / 60_000)) : 30
  )

  useEffect(() => {
    if (preset !== 'custom' || !value?.everyMs) return

    const nextCustomMinutes = Math.max(1, Math.round(value.everyMs / 60_000))
    if (nextCustomMinutes !== customMinutes) {
      setCustomMinutes(nextCustomMinutes)
    }
  }, [preset, value?.everyMs, customMinutes])

  const hour = value?.dailyHour ?? 9
  const minute = value?.dailyMinute ?? 0

  function select(next: SchedulePreset): void {
    const tz = resolveTz(value)
    if (next === 'once') onChange(null)
    else if (next === 'hourly')
      onChange({ kind: 'every', everyMs: HOUR_MS })
    else if (next === 'daily')
      onChange({ kind: 'daily', dailyHour: hour, dailyMinute: minute, tz })
    else if (next === 'weekly')
      onChange({ kind: 'every', everyMs: 7 * 24 * HOUR_MS })
    else onChange({ kind: 'every', everyMs: customMinutes * 60_000 })
  }

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        flexWrap: 'wrap',
        gap: '6px'
      }}
    >
      <PresetBtn active={preset === 'once'} onClick={() => select('once')}>
        {t('auto.newTask.scheduleOnce')}
      </PresetBtn>
      <PresetBtn active={preset === 'hourly'} onClick={() => select('hourly')}>
        {t('auto.newTask.scheduleEveryHour')}
      </PresetBtn>
      <PresetBtn active={preset === 'daily'} onClick={() => select('daily')}>
        {t('auto.newTask.scheduleDaily')}
      </PresetBtn>
      <PresetBtn active={preset === 'weekly'} onClick={() => select('weekly')}>
        {t('auto.newTask.scheduleWeekly')}
      </PresetBtn>
      <PresetBtn active={preset === 'custom'} onClick={() => select('custom')}>
        {t('auto.newTask.scheduleCustom')}
      </PresetBtn>

      {preset === 'daily' && (
        <input
          type="time"
          aria-label={t('auto.newTask.time')}
          value={`${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`}
          onChange={(e) => {
            const [h, m] = e.target.value.split(':').map((n) => parseInt(n, 10))
            onChange({
              kind: 'daily',
              dailyHour: isFinite(h) ? h : 9,
              dailyMinute: isFinite(m) ? m : 0,
              tz: resolveTz(value)
            })
          }}
          style={inputStyle}
        />
      )}

      {preset === 'custom' && (
        <div style={{ display: 'inline-flex', alignItems: 'center', gap: '6px' }}>
          <input
            type="number"
            min={1}
            aria-label={t('auto.newTask.scheduleCustom')}
            value={customMinutes}
            onChange={(e) => {
              const n = Math.max(1, parseInt(e.target.value, 10) || 1)
              setCustomMinutes(n)
              onChange({ kind: 'every', everyMs: n * 60_000 })
            }}
            style={{ ...inputStyle, width: '72px' }}
          />
          <span style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>min</span>
        </div>
      )}
    </div>
  )
}

const inputStyle: CSSProperties = {
  padding: '5px 8px',
  borderRadius: '6px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-secondary)',
  color: 'var(--text-primary)',
  fontSize: '12px',
  outline: 'none'
}

function PresetBtn({
  active,
  onClick,
  children
}: {
  active: boolean
  onClick(): void
  children: React.ReactNode
}): JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      style={{
        padding: '4px 10px',
        borderRadius: '999px',
        border: active ? '1px solid var(--accent)' : '1px solid var(--border-default)',
        backgroundColor: active ? 'color-mix(in srgb, var(--accent) 14%, transparent)' : 'transparent',
        color: active ? 'var(--accent)' : 'var(--text-secondary)',
        fontSize: '12px',
        fontWeight: 500,
        cursor: 'pointer'
      }}
    >
      {children}
    </button>
  )
}
