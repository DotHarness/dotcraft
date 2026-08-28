import { Check } from 'lucide-react'
import { useId, type CSSProperties, type JSX, type ReactNode } from 'react'

export interface CheckboxProps {
  id?: string
  checked: boolean
  onChange: (checked: boolean) => void
  disabled?: boolean
  label?: ReactNode
  ariaLabel?: string
  style?: CSSProperties
}

export function Checkbox({
  id,
  checked,
  onChange,
  disabled = false,
  label,
  ariaLabel,
  style
}: CheckboxProps): JSX.Element {
  const generatedId = useId()
  const inputId = id ?? generatedId
  const control = (
    <span className="dc-checkbox__control" aria-hidden="true">
      {checked && <Check size={13} strokeWidth={2.5} />}
    </span>
  )

  return (
    <label
      htmlFor={inputId}
      className="dc-checkbox"
      data-disabled={disabled || undefined}
      style={style}
    >
      <input
        id={inputId}
        type="checkbox"
        checked={checked}
        disabled={disabled}
        aria-label={ariaLabel}
        onChange={(event) => onChange(event.target.checked)}
        className="dc-checkbox__input"
      />
      {control}
      {label != null && <span className="dc-checkbox__label">{label}</span>}
    </label>
  )
}
