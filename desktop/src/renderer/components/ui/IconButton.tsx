import { forwardRef, type ButtonHTMLAttributes, type CSSProperties, type JSX, type ReactNode } from 'react'
import { ActionTooltip } from './ActionTooltip'
import type { ShortcutSpec } from './shortcutKeys'

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  icon: ReactNode
  label: string
  size?: number
  /** Corner radius in px. Defaults to the standard 8px control-band radius. */
  radius?: number
  active?: boolean
  /** Active-state color treatment. Explorer/view toggles use neutral chrome. */
  activeTone?: 'accent' | 'neutral'
  /** Semantic treatment for destructive icon-only actions. */
  tone?: 'neutral' | 'danger'
  /** Paint a visible neutral border. Reserve for special / important framed controls. */
  bordered?: boolean
  tooltipLabel?: string
  shortcut?: ShortcutSpec
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
  tooltipWrapperStyle?: CSSProperties
  disabledReason?: string
}

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(function IconButton({
  icon,
  label,
  size = 32,
  radius = 8,
  active = false,
  activeTone = 'accent',
  tone = 'neutral',
  bordered = false,
  disabled = false,
  className,
  style,
  tooltipLabel,
  shortcut,
  tooltipPlacement,
  tooltipWrapperStyle,
  disabledReason,
  ...props
}: IconButtonProps, ref): JSX.Element {
  const button = (
    <button
      ref={ref}
      type="button"
      aria-label={label}
      disabled={disabled}
      className={className ? `dc-icon-button ${className}` : 'dc-icon-button'}
      data-active={active || undefined}
      data-active-tone={activeTone}
      data-bordered={bordered || undefined}
      data-tone={tone}
      style={{
        ...iconButtonSizeStyle(size, radius),
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
      wrapperStyle={tooltipWrapperStyle}
      disabledReason={disabled ? disabledReason : undefined}
    >
      {button}
    </ActionTooltip>
  )
})

// Sizing/layout only. Color, border, hover, active, and disabled states live in the
// shared `.dc-icon-button` class (tokens.css) so frameless icon buttons get real hover
// feedback and stay consistent app-wide.
function iconButtonSizeStyle(size: number, radius: number): CSSProperties {
  return {
    width: `${size}px`,
    height: `${size}px`,
    minWidth: `${size}px`,
    borderRadius: `${radius}px`,
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    padding: 0,
    flexShrink: 0
  }
}
