import type { ButtonHTMLAttributes, CSSProperties, JSX, ReactNode } from 'react'
import { ActionTooltip } from './ActionTooltip'
import type { ShortcutSpec } from './shortcutKeys'

interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  icon: ReactNode
  label: string
  size?: number
  active?: boolean
  /** Paint a visible neutral border. Reserve for special / important framed controls. */
  bordered?: boolean
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
  bordered = false,
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
      className="dc-icon-button"
      data-active={active || undefined}
      data-bordered={bordered || undefined}
      style={{
        ...iconButtonSizeStyle(size),
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

// Sizing/layout only. Color, border, hover, active, and disabled states live in the
// shared `.dc-icon-button` class (tokens.css) so frameless icon buttons get real hover
// feedback and stay consistent app-wide.
function iconButtonSizeStyle(size: number): CSSProperties {
  return {
    width: `${size}px`,
    height: `${size}px`,
    minWidth: `${size}px`,
    borderRadius: '8px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 0,
    flexShrink: 0
  }
}
