import { useId } from 'react'
import { PillSwitch } from '../ui/PillSwitch'

interface ToggleSwitchProps {
  checked: boolean
  onChange: (checked: boolean) => void
  label?: string
  description?: string
  disabled?: boolean
}

/**
 * Labelled toggle with optional description. Thin wrapper over `PillSwitch`
 * that aligns the pill on the right while keeping the label/description on the left.
 */
export function ToggleSwitch({
  checked,
  onChange,
  label,
  description,
  disabled = false
}: ToggleSwitchProps): JSX.Element {
  const id = useId()
  const labelId = `${id}-label`

  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: '12px',
        opacity: disabled ? 0.65 : 1
      }}
    >
      {(label || description) && (
        <div style={{ flex: 1, minWidth: 0 }}>
          {label && (
            <span
              id={labelId}
              style={{
                display: 'block',
                fontSize: '13px',
                fontWeight: 600,
                color: 'var(--text-primary)',
                lineHeight: 1.4
              }}
            >
              {label}
            </span>
          )}
          {description && (
            <p
              style={{
                margin: '2px 0 0 0',
                fontSize: '12px',
                color: 'var(--text-secondary)',
                lineHeight: 1.4
              }}
            >
              {description}
            </p>
          )}
        </div>
      )}
      <PillSwitch
        checked={checked}
        onChange={onChange}
        disabled={disabled}
        size="md"
        aria-labelledby={label ? labelId : undefined}
      />
    </div>
  )
}
