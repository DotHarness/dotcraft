import { useEffect, useLayoutEffect, useMemo, useRef, useState, type CSSProperties } from 'react'
import { Clock } from 'lucide-react'
import { useLocale, useT } from '../../contexts/LocaleContext'
import type { AutomationSchedule } from '../../stores/automationsStore'
import { MenuHeading, MenuOption, PillDropdown } from '../ui/PillDropdown'

/**
 * Schedule control for the New Task dialog: a compact pill that opens a recurrence
 * menu. "Daily" reveals a flexible time field (type a time or pick a 15-minute slot);
 * "Custom" reveals an interval input.
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

function fmtTime(hour: number, minute: number): string {
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
}

/** Locale-aware display ("9:00 AM" / "09:00") for summaries and slot rows. */
function fmtTimeLocale(locale: string, hour: number, minute: number): string {
  try {
    return new Intl.DateTimeFormat(locale, { hour: 'numeric', minute: '2-digit' }).format(
      new Date(2000, 0, 1, hour, minute)
    )
  } catch {
    return fmtTime(hour, minute)
  }
}

export function SchedulePicker({ value, onChange }: Props): JSX.Element {
  const t = useT()
  const locale = useLocale()
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
    else if (next === 'hourly') onChange({ kind: 'every', everyMs: HOUR_MS })
    else if (next === 'daily') onChange({ kind: 'daily', dailyHour: hour, dailyMinute: minute, tz })
    else if (next === 'weekly') onChange({ kind: 'every', everyMs: 7 * 24 * HOUR_MS })
    else onChange({ kind: 'every', everyMs: customMinutes * 60_000 })
  }

  const summary =
    preset === 'hourly'
      ? t('auto.newTask.scheduleEveryHour')
      : preset === 'daily'
        ? t('auto.newTask.scheduleDailyAt', { time: fmtTimeLocale(locale, hour, minute) })
        : preset === 'weekly'
          ? t('auto.newTask.scheduleWeekly')
          : preset === 'custom'
            ? t('auto.newTask.scheduleEveryMinutes', { minutes: customMinutes })
            : t('auto.newTask.scheduleOnce')

  return (
    <PillDropdown
      ariaLabel={t('auto.newTask.scheduleLabel')}
      label={summary}
      icon={<Clock size={13} strokeWidth={1.8} aria-hidden />}
      panelMinWidth={220}
      panelMaxHeight={420}
    >
      {(close) => (
        <>
          <MenuHeading>{t('auto.newTask.scheduleLabel')}</MenuHeading>
          <MenuOption selected={preset === 'once'} onClick={() => { select('once'); close() }}>
            {t('auto.newTask.scheduleOnce')}
          </MenuOption>
          <MenuOption selected={preset === 'hourly'} onClick={() => { select('hourly'); close() }}>
            {t('auto.newTask.scheduleEveryHour')}
          </MenuOption>
          <MenuOption selected={preset === 'daily'} onClick={() => select('daily')}>
            {t('auto.newTask.scheduleDaily')}
          </MenuOption>
          <MenuOption selected={preset === 'weekly'} onClick={() => { select('weekly'); close() }}>
            {t('auto.newTask.scheduleWeekly')}
          </MenuOption>
          <MenuOption selected={preset === 'custom'} onClick={() => select('custom')}>
            {t('auto.newTask.scheduleCustom')}
          </MenuOption>

          {preset === 'daily' && (
            <div style={detailRowStyle}>
              <span style={detailLabelStyle}>{t('auto.newTask.atLabel')}</span>
              <TimeField
                hour={hour}
                minute={minute}
                onChange={(h, m) =>
                  onChange({ kind: 'daily', dailyHour: h, dailyMinute: m, tz: resolveTz(value) })
                }
              />
            </div>
          )}

          {preset === 'custom' && (
            <div style={detailRowStyle}>
              <input
                type="number"
                min={1}
                className="dc-plain-number"
                aria-label={t('auto.newTask.scheduleCustom')}
                value={customMinutes}
                onChange={(e) => {
                  const n = Math.max(1, parseInt(e.target.value, 10) || 1)
                  setCustomMinutes(n)
                  onChange({ kind: 'every', everyMs: n * 60_000 })
                }}
                style={{ ...fieldInputStyle, width: '72px' }}
              />
              <span style={detailLabelStyle}>{t('auto.newTask.minutesShort')}</span>
            </div>
          )}
        </>
      )}
    </PillDropdown>
  )
}

/**
 * Flexible time entry: type a time directly (`9:30`, `21:30`, `9:30 pm`), or open a
 * scrollable list of 15-minute slots. The selected slot is centered when the list opens.
 */
function TimeField({
  hour,
  minute,
  onChange
}: {
  hour: number
  minute: number
  onChange(hour: number, minute: number): void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const [open, setOpen] = useState(false)
  const [text, setText] = useState(fmtTime(hour, minute))
  const listRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    setText(fmtTime(hour, minute))
  }, [hour, minute])

  const slots = useMemo(() => {
    const out: Array<[number, number]> = []
    for (let h = 0; h < 24; h++) {
      for (let m = 0; m < 60; m += 15) out.push([h, m])
    }
    return out
  }, [])
  const selectedIndex = slots.findIndex(([h, m]) => h === hour && m === minute)

  function commit(raw: string): void {
    const match = /^\s*(\d{1,2})\s*:\s*(\d{1,2})\s*([ap]\.?m\.?)?\s*$/i.exec(raw)
    if (!match) {
      setText(fmtTime(hour, minute))
      return
    }
    let h = Math.max(0, Math.min(23, parseInt(match[1], 10)))
    const m = Math.max(0, Math.min(59, parseInt(match[2], 10)))
    const meridiem = match[3]?.toLowerCase()
    if (meridiem?.startsWith('p') && h < 12) h += 12
    if (meridiem?.startsWith('a') && h === 12) h = 0
    onChange(h, m)
    setText(fmtTime(h, m))
  }

  useLayoutEffect(() => {
    if (!open || !listRef.current || selectedIndex < 0) return
    const row = listRef.current.children[selectedIndex] as HTMLElement | undefined
    if (row) listRef.current.scrollTop = row.offsetTop - listRef.current.clientHeight / 2 + row.offsetHeight / 2
  }, [open, selectedIndex])

  return (
    <div style={{ display: 'inline-flex', flexDirection: 'column', minWidth: 0 }}>
      <div style={{ display: 'inline-flex', alignItems: 'center', ...fieldInputStyle, padding: '0 6px 0 8px' }}>
        <input
          value={text}
          onChange={(e) => setText(e.target.value)}
          onFocus={(e) => {
            e.target.select()
            setOpen(true)
          }}
          onKeyDown={(e) => {
            if (e.key === 'Enter') {
              e.preventDefault()
              commit(text)
              setOpen(false)
            } else if (e.key === 'Escape') {
              setOpen(false)
            }
          }}
          onBlur={() => {
            commit(text)
            setOpen(false)
          }}
          placeholder="HH:MM"
          inputMode="numeric"
          aria-label={t('auto.newTask.time')}
          style={{
            width: '54px',
            border: 'none',
            background: 'transparent',
            color: 'var(--text-primary)',
            fontSize: '12px',
            outline: 'none',
            fontVariantNumeric: 'tabular-nums'
          }}
        />
        <button
          type="button"
          tabIndex={-1}
          aria-label={t('auto.newTask.time')}
          onMouseDown={(e) => e.preventDefault()}
          onClick={() => setOpen((v) => !v)}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            width: '18px',
            height: '18px',
            border: 'none',
            background: 'transparent',
            color: 'var(--text-dimmed)',
            cursor: 'pointer',
            padding: 0
          }}
        >
          <Clock size={12} strokeWidth={1.8} aria-hidden />
        </button>
      </div>

      {open && (
        <div
          ref={listRef}
          onMouseDown={(e) => e.preventDefault()}
          style={{
            marginTop: '4px',
            maxHeight: '168px',
            overflowY: 'auto',
            display: 'flex',
            flexDirection: 'column',
            gap: '1px'
          }}
        >
          {slots.map(([h, m], i) => (
            <MenuOption
              key={i}
              selected={i === selectedIndex}
              onClick={() => {
                onChange(h, m)
                setText(fmtTime(h, m))
                setOpen(false)
              }}
            >
              {fmtTimeLocale(locale, h, m)}
            </MenuOption>
          ))}
        </div>
      )}
    </div>
  )
}

const detailRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '6px 9px 2px'
}

const detailLabelStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-secondary)'
}

const fieldInputStyle: CSSProperties = {
  height: '28px',
  padding: '0 8px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  backgroundColor: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '12px',
  outline: 'none'
}
