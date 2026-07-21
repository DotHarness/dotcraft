import type { ButtonHTMLAttributes, JSX, ReactNode } from 'react'
import { Loader2 } from 'lucide-react'

/**
 * Intent variants drive the action hierarchy (see specs/architecture/DESIGN.md → Actions).
 * Buttons are frameless by default; a visible border is reserved for `outline`.
 * - `primary`   neutral inversion, the single immediate action in an area
 * - `secondary` frameless neutral fill (the common action)
 * - `ghost`     transparent tertiary control for inline / low-frequency commands
 * - `danger`    frameless semantic fill; destructive action (pair with Delete / Remove / Stop copy)
 * - `accent`    restrained brand accent; never the default for create/save/manage
 * - `outline`   the one bordered variant — only for special / important framed actions
 */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'accent' | 'outline'

/** Size families map to the shared control band (32px) plus compact / square options. */
export type ButtonSize = 'default' | 'sm' | 'icon' | 'iconSm'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  /** Leading icon rendered before the label. Ignored for icon sizes (children is the glyph). */
  iconLeft?: ReactNode
  /** Show an in-control spinner and disable the button. */
  loading?: boolean
}

/**
 * Shared action button. Every text/icon action should route through this component so intent
 * and size are chosen by prop rather than re-derived per call site. The 1px border is always
 * present in the box model (`.dc-button`) and only `secondary` paints it visibly, so switching a
 * button between filled and ghost never shifts height or alignment.
 */
export function Button({
  variant = 'secondary',
  size = 'default',
  iconLeft,
  loading = false,
  disabled = false,
  type = 'button',
  className,
  children,
  ...props
}: ButtonProps): JSX.Element {
  const isDisabled = disabled || loading
  const isIcon = size === 'icon' || size === 'iconSm'
  const spinner = (
    <span className="dc-button__spinner" aria-hidden="true">
      <Loader2 size={isIcon ? 15 : 14} className="animate-spin-custom" />
    </span>
  )
  return (
    <button
      type={type}
      data-variant={variant}
      data-size={size}
      disabled={isDisabled}
      className={className ? `dc-button ${className}` : 'dc-button'}
      {...props}
    >
      {loading
        ? spinner
        : iconLeft != null && !isIcon && (
            <span className="dc-button__icon" aria-hidden="true">
              {iconLeft}
            </span>
          )}
      {loading && isIcon ? null : children}
    </button>
  )
}
