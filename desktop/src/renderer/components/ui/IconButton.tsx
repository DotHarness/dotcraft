import { forwardRef, type ButtonHTMLAttributes, type CSSProperties, type JSX, type ReactNode } from 'react'
import { ActionTooltip } from './ActionTooltip'
import type { ShortcutSpec } from './shortcutKeys'

export interface IconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children'> {
  icon: ReactNode
  label: string
  /** Square edge in px. Omit to follow the surface's control band. */
  size?: number
  /** Corner radius in px. Omit to follow the surface's control band. */
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
  size,
  radius,
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

// Per-call overrides only: the defaults and every visual state live on `.dc-icon-button`.
function iconButtonSizeStyle(size?: number, radius?: number): CSSProperties {
  return {
    ...(size != null ? { width: `${size}px`, height: `${size}px`, minWidth: `${size}px` } : null),
    ...(radius != null ? { borderRadius: `${radius}px` } : null)
  }
}
