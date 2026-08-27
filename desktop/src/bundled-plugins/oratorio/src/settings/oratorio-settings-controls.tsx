import { useEffect, useLayoutEffect, useMemo, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronDown, ChevronUp, Clock } from 'lucide-react'
import { Input, Select } from '../ui'
import { useOratorioSettingsT } from './oratorio-settings-i18n'

export function FieldControl({ children }: { children: ReactNode }) {
  return <div className="ora-field-control"><div className="ora-field-control__input">{children}</div></div>
}
export function NumberStepper({ value, min, max, label, onChange, disabled = false }: {
  value: number
  min: number
  max: number
  label: string
  onChange: (value: number) => void
  disabled?: boolean
}) {
  const t = useOratorioSettingsT()
  const [text, setText] = useState(String(value))
  useEffect(() => setText(String(value)), [value])
  const draftNumber = Number.parseInt(text, 10)
  const draftInRange = Number.isFinite(draftNumber) && draftNumber >= min && draftNumber <= max

  function commit(raw = text): void {
    const parsed = Number.parseInt(raw, 10)
    const next = Number.isFinite(parsed) ? Math.min(max, Math.max(min, parsed)) : value
    setText(String(next))
    if (next !== value) onChange(next)
  }

  function step(delta: number): void {
    const next = Math.min(max, Math.max(min, value + delta))
    setText(String(next))
    if (next !== value) onChange(next)
  }

  return <div className="ora-number-stepper-wrap"><div className="ora-number-stepper">
    <Input
      bare
      value={text}
      disabled={disabled}
      inputMode="numeric"
      role="spinbutton"
      aria-label={label}
      aria-valuemin={min}
      aria-valuemax={max}
      aria-valuenow={draftInRange ? draftNumber : value}
      aria-invalid={!draftInRange}
      onChange={(event) => setText(event.target.value.replace(/[^0-9]/g, ''))}
      onBlur={() => commit()}
      onKeyDown={(event) => {
        if (event.key === 'ArrowUp') { event.preventDefault(); step(1) }
        if (event.key === 'ArrowDown') { event.preventDefault(); step(-1) }
        if (event.key === 'Enter') { event.preventDefault(); commit() }
      }}
    />
    <span className="ora-number-stepper__buttons">
      <button type="button" tabIndex={-1} disabled={disabled || value >= max} aria-label={`${label}: ${t('increase')}`} onClick={() => step(1)}><ChevronUp size={11} /></button>
      <button type="button" tabIndex={-1} disabled={disabled || value <= min} aria-label={`${label}: ${t('decrease')}`} onClick={() => step(-1)}><ChevronDown size={11} /></button>
    </span>
  </div>{!draftInRange ? <small className="ora-number-stepper__error" role="alert">{`${t('rangeRequired')} ${min}–${max}`}</small> : null}</div>
}

type DurationUnit = 'seconds' | 'minutes' | 'hours'
const unitSeconds: Record<DurationUnit, number> = { seconds: 1, minutes: 60, hours: 3600 }

function bestUnit(seconds: number): DurationUnit {
  if (seconds >= 3600 && seconds % 3600 === 0) return 'hours'
  if (seconds >= 60 && seconds % 60 === 0) return 'minutes'
  return 'seconds'
}

function durationSummary(seconds: number, unit: DurationUnit, labels: Record<DurationUnit, string>): string {
  return `${seconds / unitSeconds[unit]} ${labels[unit].toLocaleLowerCase()}`
}

export function DurationPicker({ valueSeconds, minSeconds, maxSeconds, label, onChange }: {
  valueSeconds: number
  minSeconds: number
  maxSeconds: number
  label: string
  onChange: (seconds: number) => void
}) {
  const t = useOratorioSettingsT()
  const [unit, setUnit] = useState<DurationUnit>(() => bestUnit(valueSeconds))
  useEffect(() => {
    const next = bestUnit(valueSeconds)
    if (valueSeconds % unitSeconds[unit] !== 0) setUnit(next)
  }, [unit, valueSeconds])
  const labels = useMemo(() => ({ seconds: t('seconds'), minutes: t('minutes'), hours: t('hours') }), [t])
  const factor = unitSeconds[unit]
  const min = Math.max(1, Math.ceil(minSeconds / factor))
  const max = Math.max(min, Math.floor(maxSeconds / factor))
  const amount = Math.min(max, Math.max(min, Math.round(valueSeconds / factor)))

  return <PickerPopover label={durationSummary(valueSeconds, unit, labels)} ariaLabel={label}>
    <div className="ora-duration-menu">
      <strong>{label}</strong>
      <div className="ora-duration-menu__controls">
        <NumberStepper value={amount} min={min} max={max} label={label} onChange={(next) => onChange(next * factor)} />
        <Select<DurationUnit>
          ariaLabel={`${label}: ${t('unit')}`}
          value={unit}
          onValueChange={(next) => {
            setUnit(next)
          }}
          options={(Object.keys(unitSeconds) as DurationUnit[]).map((value) => {
            const candidate = valueSeconds / unitSeconds[value]
            return { value, label: labels[value], disabled: !Number.isInteger(candidate) || candidate < 1 }
          })}
        />
      </div>
      <small>{`${minSeconds}–${maxSeconds} ${t('seconds').toLocaleLowerCase()}`}</small>
    </div>
  </PickerPopover>
}

export function IntervalPicker({ valueSeconds, label, onChange, disabled = false }: {
  valueSeconds: number | null
  label: string
  onChange: (seconds: number | null) => void
  disabled?: boolean
}) {
  const t = useOratorioSettingsT()
  const preset = valueSeconds === null ? 'off' : valueSeconds === 900 ? '15m' : valueSeconds === 3600 ? '1h' : 'custom'
  const summary = preset === 'off' ? t('off') : preset === '15m' ? t('every15m') : preset === '1h' ? t('everyHour') : `${t('customInterval')} · ${Math.round((valueSeconds ?? 900) / 60)}m`
  return <PickerPopover label={summary} ariaLabel={label} disabled={disabled}>
    {(close) => <>
      <strong>{label}</strong>
      <PickerOption selected={preset === 'off'} onClick={() => { onChange(null); close() }}>{t('off')}</PickerOption>
      <PickerOption selected={preset === '15m'} onClick={() => { onChange(900); close() }}>{t('every15m')}</PickerOption>
      <PickerOption selected={preset === '1h'} onClick={() => { onChange(3600); close() }}>{t('everyHour')}</PickerOption>
      <PickerOption selected={preset === 'custom'} onClick={() => onChange(valueSeconds ?? 1800)}>{t('customInterval')}</PickerOption>
      {preset === 'custom' ? <div className="ora-duration-menu__custom"><NumberStepper value={Math.max(1, Math.round((valueSeconds ?? 1800) / 60))} min={1} max={1440} label={t('customInterval')} onChange={(value) => onChange(value * 60)} /><span>{t('minutes')}</span></div> : null}
    </>}
  </PickerPopover>
}

function PickerPopover({ label, ariaLabel, children, disabled = false }: { label: string; ariaLabel: string; children: ReactNode | ((close: () => void) => ReactNode); disabled?: boolean }) {
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const close = (restoreFocus = true): void => { setOpen(false); if (restoreFocus) window.queueMicrotask(() => triggerRef.current?.focus()) }
  useEffect(() => {
    if (!open || !position || document.activeElement !== triggerRef.current) return
    panelRef.current?.querySelector<HTMLElement>('input, button, [role="combobox"]')?.focus()
  }, [open, position])
  useLayoutEffect(() => {
    if (!open) return
    const trigger = triggerRef.current; const panel = panelRef.current
    if (!trigger || !panel) return
    const rect = trigger.getBoundingClientRect(); const left = Math.max(8, Math.min(rect.left, innerWidth - panel.offsetWidth - 8)); const top = rect.bottom + 6 + panel.offsetHeight > innerHeight ? rect.top - panel.offsetHeight - 6 : rect.bottom + 6
    setPosition({ left, top: Math.max(8, top) })
    const focusable = panel.querySelector<HTMLElement>('input, button, [role="combobox"]'); focusable?.focus()
    const onPointer = (event: MouseEvent): void => { const target = event.target as Node; if (!panel.contains(target) && !trigger.contains(target)) close(false) }
    const onKey = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') { event.preventDefault(); close(); return }
      if (event.key !== 'Tab') return
      const nodes = Array.from(panel.querySelectorAll<HTMLElement>('input, button, [role="combobox"], [tabindex]:not([tabindex="-1"])')).filter((node) => !node.hasAttribute('disabled'))
      const first = nodes[0]; const last = nodes[nodes.length - 1]
      if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last?.focus() }
      if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first?.focus() }
    }
    document.addEventListener('mousedown', onPointer, true); document.addEventListener('keydown', onKey, true)
    return () => { document.removeEventListener('mousedown', onPointer, true); document.removeEventListener('keydown', onKey, true) }
  }, [open])
  return <><button ref={triggerRef} type="button" className="ora-picker-trigger" disabled={disabled} aria-label={ariaLabel} aria-haspopup="dialog" aria-expanded={open} onClick={() => setOpen((value) => !value)}><Clock size={13} aria-hidden /><span>{label}</span><ChevronDown size={12} aria-hidden /></button>{open ? createPortal(<div ref={panelRef} role="dialog" aria-modal="false" aria-label={ariaLabel} className="ora-picker-panel" style={{ top: position?.top ?? 0, left: position?.left ?? 0, visibility: position ? 'visible' : 'hidden' }}>{typeof children === 'function' ? children(() => close()) : children}</div>, document.body) : null}</>
}

function PickerOption({ selected, onClick, children }: { selected: boolean; onClick: () => void; children: ReactNode }) {
  return <button type="button" className="ora-picker-option" aria-pressed={selected} onClick={onClick}>{children}</button>
}
