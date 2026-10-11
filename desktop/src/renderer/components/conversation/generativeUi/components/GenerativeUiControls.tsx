import { useContext, useId, type ReactNode } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { Button } from '../../../ui/Button'
import { Checkbox } from '../../../ui/Checkbox'
import { Input, Textarea } from '../../../ui/Input'
import { Select, type SelectOption } from '../../../ui/Select'
import { Slider } from '../../../ui/Slider'
import { SegmentedControl } from '../../../settings/ui/SegmentedControl'
import { GenerativeUiFormContext, useGenerativeUiFieldValue, useGenerativeUiInert, useGenerativeUiInvoke } from '../GenerativeUiContext'
import type { GenerativeUiProps } from '../generativeUiSchema'
import { GenerativeUiIconGlyph } from './GenerativeUiContent'
import styles from './GenerativeUiControls.module.css'

type ControlProps<Name extends 'Button' | 'Input' | 'Textarea' | 'Select' | 'Checkbox' | 'RadioGroup' | 'SegmentedControl' | 'Slider'> = {
  props: GenerativeUiProps<Name>
  children?: ReactNode
}

function Field({ id, label, hint, error, children }: { id?: string; label?: string; hint?: string; error?: string; children: ReactNode }): JSX.Element {
  return (
    <div className={styles.field}>
      {label && (id ? <label className={styles.label} htmlFor={id}>{label}</label> : <span className={styles.label}>{label}</span>)}
      {children}
      {error ? <span className={styles.error} role="alert">{error}</span> : hint && <span className={styles.hint}>{hint}</span>}
    </div>
  )
}

export function GenerativeUiButton({ props, children }: ControlProps<'Button'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const invoke = useGenerativeUiInvoke()
  const form = useContext(GenerativeUiFormContext)
  const submit = props.submit === true && form !== null
  return (
    <Button
      type={submit ? 'submit' : 'button'}
      variant={props.variant ?? 'secondary'}
      size={props.size === 'sm' ? 'sm' : 'default'}
      disabled={props.disabled || inert}
      className={styles.button}
      data-full-width={props.fullWidth ? '' : undefined}
      iconLeft={props.icon ? <GenerativeUiIconGlyph name={props.icon} size={14} /> : undefined}
      onClick={() => invoke(props.onClick, [])}
    >
      {props.label ?? children}
    </Button>
  )
}

export function GenerativeUiInput({ props }: ControlProps<'Input'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const id = useId()
  const invoke = useGenerativeUiInvoke()
  const numeric = props.type === 'number'
  const [value, setValue] = useGenerativeUiFieldValue<string | number>({
    value: props.value,
    defaultValue: props.defaultValue,
    fallback: '',
    name: props.name,
    onChange: props.onChange
  })
  return (
    <Field id={id} label={props.label} hint={props.hint} error={props.error}>
      <Input
        id={id}
        type={props.type ?? 'text'}
        value={value}
        placeholder={props.placeholder}
        disabled={props.disabled || inert}
        required={props.required}
        invalid={Boolean(props.error)}
        onChange={event => {
          const raw = event.currentTarget.value
          setValue(numeric && raw.trim() !== '' && Number.isFinite(Number(raw)) ? Number(raw) : raw)
        }}
        onKeyDown={event => {
          if (event.key === 'Enter' && !event.nativeEvent.isComposing && props.onSubmit) invoke(props.onSubmit, [value])
        }}
      />
    </Field>
  )
}

export function GenerativeUiTextarea({ props }: ControlProps<'Textarea'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const id = useId()
  const [value, setValue] = useGenerativeUiFieldValue<string>({
    value: props.value,
    defaultValue: props.defaultValue,
    fallback: '',
    name: props.name,
    onChange: props.onChange
  })
  return (
    <Field id={id} label={props.label} hint={props.hint} error={props.error}>
      <Textarea
        id={id}
        rows={props.rows ?? 3}
        value={value}
        placeholder={props.placeholder}
        disabled={props.disabled || inert}
        invalid={Boolean(props.error)}
        onChange={event => setValue(event.currentTarget.value)}
      />
    </Field>
  )
}

export function GenerativeUiSelect({ props }: ControlProps<'Select'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const id = useId()
  const [value, setValue] = useGenerativeUiFieldValue<string>({
    value: props.value,
    defaultValue: props.defaultValue,
    fallback: props.placeholder ? '' : props.options[0].value,
    name: props.name,
    onChange: props.onChange
  })
  const options: SelectOption[] = props.placeholder && !props.options.some(option => option.value === value)
    ? [{ value, label: props.placeholder, disabled: true }, ...props.options]
    : props.options
  return (
    <Field id={id} label={props.label}>
      <div className={styles.select}>
        <Select id={id} value={value} options={options} disabled={props.disabled || inert} ariaLabel={props.label} adaptiveWidth={false} onValueChange={setValue} />
      </div>
    </Field>
  )
}

export function GenerativeUiCheckbox({ props, children }: ControlProps<'Checkbox'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const [checked, setChecked] = useGenerativeUiFieldValue<boolean>({
    value: props.checked,
    defaultValue: props.defaultChecked,
    fallback: false,
    name: props.name,
    onChange: props.onChange
  })
  return <Checkbox checked={checked} disabled={props.disabled || inert} label={props.label ?? children} onChange={setChecked} />
}

export function GenerativeUiRadioGroup({ props }: ControlProps<'RadioGroup'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const name = useId()
  const [value, setValue] = useGenerativeUiFieldValue<string>({
    value: props.value,
    defaultValue: props.defaultValue,
    fallback: '',
    name: props.name,
    onChange: props.onChange
  })
  return (
    <Field label={props.label}>
      <div role="radiogroup" aria-label={props.label} className={styles.radioGroup} data-direction={props.direction ?? 'column'}>
        {props.options.map(option => (
          <label key={option.value} className={styles.radio} data-disabled={props.disabled || inert || option.disabled ? '' : undefined}>
            <input
              type="radio"
              className={styles.radioInput}
              name={name}
              value={option.value}
              checked={value === option.value}
              disabled={props.disabled || inert || option.disabled}
              onChange={() => setValue(option.value)}
            />
            <span className={styles.radioControl} aria-hidden="true" />
            <span>{option.label}</span>
          </label>
        ))}
      </div>
    </Field>
  )
}

export function GenerativeUiSegmentedControl({ props }: ControlProps<'SegmentedControl'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const [value, setValue] = useGenerativeUiFieldValue<string>({
    value: props.value,
    defaultValue: props.defaultValue,
    fallback: props.options[0].value,
    name: props.name,
    onChange: props.onChange
  })
  return (
    <Field label={props.label}>
      <div className={styles.segmented}>
        <SegmentedControl value={value} options={props.options} ariaLabel={props.label ?? props.options.map(option => option.label).join(', ')} disabled={props.disabled || inert} onChange={setValue} />
      </div>
    </Field>
  )
}

export function GenerativeUiSlider({ props }: ControlProps<'Slider'>): JSX.Element {
  const inert = useGenerativeUiInert()
  const t = useT()
  const [value, setValue] = useGenerativeUiFieldValue<number>({
    value: props.value,
    defaultValue: props.defaultValue,
    fallback: props.min,
    name: props.name,
    onChange: props.onChange
  })
  return (
    <Field label={props.label}>
      <div className={styles.slider}>
        <Slider
          value={value}
          min={props.min}
          max={props.max}
          step={props.step}
          ariaLabel={props.label ?? t('generativeUi.sliderValue')}
          valueText={props.valueText ?? String(value)}
          disabled={props.disabled || inert}
          onValueChange={setValue}
        />
      </div>
    </Field>
  )
}
