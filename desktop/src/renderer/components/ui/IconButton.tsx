import type { ButtonHTMLAttributes, CSSProperties, JSX, ReactNode } from 'react'
import { ActionTooltip } from './ActionTooltip'
import type { ShortcutSpec } from './shortcutKeys'

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  icon: ReactNode
  label: string
  size?: number
  active?: boolean
  tooltipLabel?: string
  shortcut?: ShortcutSpec
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
  disabledReason?: string
}

export function IconButton({
  icon,
  label,
  size = 32,
  active = false,
  disabled = false,
  style,
  tooltipLabel,
  shortcut,
  tooltipPlacement,
  disabledReason,
  ...props
}: IconButtonProps): JSX.Element {
  const button = (
    <button
      type="button"
      aria-label={label}
      disabled={disabled}
      style={{
        ...iconButtonStyle(size, active, disabled),
        ...style
      }}
      {...props}
    >
      {icon}
    </button>
  )

  if (!tooltipLabel && !shortcut && !disabledReason) return button

  return (
    <ActionTooltip
      label={tooltipLabel ?? label}
      shortcut={shortcut}
      placement={tooltipPlacement}
      disabledReason={disabled ? disabledReason : undefined}
    >
      {button}
    </ActionTooltip>
  )
}

function iconButtonStyle(size: number, active: boolean, disabled: boolean): CSSProperties {
  return {
    width: `${size}px`,
    height: `${size}px`,
    minWidth: `${size}px`,
    borderRadius: active ? '10px' : '9px',
    border: active ? '1px solid color-mix(in srgb, var(--accent) 55%, transparent)' : '1px solid var(--border-default)',
    background: active ? 'color-mix(in srgb, var(--accent) 14%, var(--bg-secondary))' : 'var(--bg-secondary)',
    color: disabled ? 'var(--text-dimmed)' : active ? 'var(--accent)' : 'var(--text-secondary)',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    cursor: disabled ? 'default' : 'pointer',
    transition: 'background-color 120ms ease, border-color 120ms ease, color 120ms ease, transform 120ms ease',
    opacity: disabled ? 0.65 : 1,
    padding: 0,
    outline: 'none',
    flexShrink: 0
  }
}
