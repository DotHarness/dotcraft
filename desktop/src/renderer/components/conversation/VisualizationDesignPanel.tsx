import { useEffect, useRef, useState, type KeyboardEvent } from 'react'
import { X } from 'lucide-react'
import { normalizeHexColor } from '../../../shared/themeSeed'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { requestColorPickerDialog } from '../ui/ColorPickerDialog'
import { IconButton } from '../ui/IconButton'
import { Input } from '../ui/Input'
import { PillSwitch } from '../ui/PillSwitch'
import { Select } from '../ui/Select'
import { Slider } from '../ui/Slider'
import {
  collectVisualizationDesignEdits,
  type VisualizationDesignControl,
  type VisualizationDesignValue
} from './visualizationDesignControls'
import type { VisualizationDesignState } from './useVisualizationDesignState'

interface Props {
  id: string
  state: VisualizationDesignState
  hidden?: boolean
  onClose: () => void
  onAddToMessage: () => void
}

export function VisualizationDesignPanel({ id, state, hidden, onClose, onAddToMessage }: Props): JSX.Element | null {
  const t = useT()
  const { controls, values, showingOriginal } = state
  if (!controls) return null
  const groups = controls.groups.filter(group => group.visible)
  const changed = collectVisualizationDesignEdits(controls, values).length > 0

  const onKeyDown = (event: KeyboardEvent<HTMLElement>): void => {
    if (event.key !== 'Escape' || event.defaultPrevented) return
    event.preventDefault()
    event.stopPropagation()
    onClose()
  }

  return (
    <section id={id} className="visualization-design" aria-label={t('visualization.designControls')} hidden={hidden} onKeyDown={onKeyDown}>
      <header className="visualization-design__header">
        <h3 className="visualization-design__title">{t('visualization.designControls')}</h3>
        <IconButton
          size={24}
          label={t('visualization.closeDesignControls')}
          tooltipLabel={t('visualization.closeDesignControls')}
          onClick={onClose}
          icon={<X size={14} aria-hidden />}
        />
      </header>
      <div className="visualization-design__body">
        {groups.length === 0 && <p className="visualization-design__empty">{t('visualization.noDesignControls')}</p>}
        {groups.map(group => (
          <div key={group.id} role="group" aria-label={group.label || undefined} className="visualization-design__group">
            {group.label && <div className="visualization-design__group-label" aria-hidden>{group.label}</div>}
            {group.controls.map(control => (
              <DesignControlRow
                key={control.id}
                control={control}
                value={showingOriginal ? control.initial : values[control.id] ?? control.initial}
                disabled={showingOriginal}
                onChange={value => state.change(control.id, value)}
              />
            ))}
          </div>
        ))}
      </div>
      <footer className="visualization-design__footer">
        <Button size="sm" variant="ghost" aria-pressed={showingOriginal} disabled={!changed} onClick={state.toggleOriginal}>
          {t('visualization.showOriginal')}
        </Button>
        <span className="visualization-design__actions">
          <Button size="sm" variant="ghost" disabled={!changed || showingOriginal} onClick={state.reset}>
            {t('visualization.resetDesign')}
          </Button>
          <Button size="sm" variant="primary" disabled={!changed || showingOriginal} onClick={onAddToMessage}>
            {t('visualization.addToMessage')}
          </Button>
        </span>
      </footer>
    </section>
  )
}

function DesignControlRow({ control, value, disabled, onChange }: {
  control: VisualizationDesignControl
  value: VisualizationDesignValue
  disabled: boolean
  onChange: (value: VisualizationDesignValue) => void
}): JSX.Element {
  const label = <span className="visualization-design__label">{control.label}</span>
  switch (control.kind) {
    case 'slider':
      return (
        <div className="visualization-design__row visualization-design__row--stacked">
          {label}
          <Slider
            value={Number(value)}
            min={control.min}
            max={control.max}
            step={control.step}
            ariaLabel={control.label}
            valueText={`${Number(value)}${control.unit ?? ''}`}
            disabled={disabled}
            onValueChange={onChange}
          />
        </div>
      )
    case 'color':
      return (
        <div className="visualization-design__row">
          {label}
          <ColorField label={control.label} value={String(value)} disabled={disabled} onChange={onChange} />
        </div>
      )
    case 'toggle':
      return (
        <div className="visualization-design__row">
          {label}
          <PillSwitch size="sm" checked={value === true} disabled={disabled} aria-label={control.label} onChange={onChange} />
        </div>
      )
    case 'select':
      return (
        <div className="visualization-design__row">
          {label}
          <Select
            value={String(value)}
            options={control.options}
            ariaLabel={control.label}
            disabled={disabled}
            adaptiveWidth={false}
            style={{ width: 140 }}
            onValueChange={onChange}
          />
        </div>
      )
  }
}

function ColorField({ label, value, disabled, onChange }: {
  label: string
  value: string
  disabled: boolean
  onChange: (value: string) => void
}): JSX.Element {
  const t = useT()
  const [draft, setDraft] = useState(value)
  const swatchRef = useRef<HTMLButtonElement>(null)
  const dismissRef = useRef<(() => void) | null>(null)
  useEffect(() => setDraft(value), [value])
  useEffect(() => () => dismissRef.current?.(), [])
  const normalized = normalizeHexColor(draft)
  const commit = (): void => {
    if (normalized && normalized !== value) onChange(normalized)
    else setDraft(value)
  }
  const choose = (): void => {
    const rect = swatchRef.current?.getBoundingClientRect()
    if (!rect) return
    const initial = value
    const picker = requestColorPickerDialog({ title: label, initialColor: initial }, {
      anchor: { x: rect.left, y: rect.top, width: rect.width, height: rect.height },
      onPreview: onChange
    })
    dismissRef.current = picker.dismiss
    void picker.result.then(result => {
      dismissRef.current = null
      onChange(result.kind === 'select' ? result.color : initial)
    })
  }
  return (
    <span className="visualization-design__color">
      <button
        ref={swatchRef}
        type="button"
        className="visualization-design__swatch"
        aria-label={label}
        disabled={disabled}
        style={{ background: value }}
        onClick={choose}
      />
      <Input
        mono
        aria-label={t('visualization.hexValue', { label })}
        className="visualization-design__hex"
        value={draft}
        disabled={disabled}
        invalid={normalized == null}
        spellCheck={false}
        maxLength={7}
        onChange={event => setDraft(event.currentTarget.value)}
        onBlur={commit}
        onKeyDown={event => { if (event.key === 'Enter') commit() }}
      />
    </span>
  )
}
