import { forwardRef, type ButtonHTMLAttributes, type JSX, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'

/**
 * Intent variants drive the action hierarchy; see Actions in specs/architecture/DESIGN.md.
 * Buttons are frameless by default and `outline` is the only bordered variant.
 */
export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'accent' | 'outline'

/**
 * Size families map to the shared control band (32px) plus compact / square options.
 * `toolbar` is the catalog top-bar band: shorter and rounder than the standard band,
 * shared by every control in that bar.
 */
export type ButtonSize = 'default' | 'sm' | 'icon' | 'iconSm' | 'prominent' | 'toolbar'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  /** Leading icon rendered before the label. Ignored for icon sizes (children is the glyph). */
  iconLeft?: ReactNode
  /** Show an in-control spinner and disable the button. */
  loading?: boolean
}

/**
 * Every text/icon action should route through this component so intent and size are
 * chosen by prop rather than re-derived per call site. The 1px border is always in the
 * box model but painted only by `outline`, so switching variants never shifts height.
 */
export const Button = forwardRef<HTMLButtonElement, ButtonProps>(function Button({
  variant = 'secondary',
  size = 'default',
  iconLeft,
  loading = false,
  disabled = false,
  type = 'button',
  className,
  children,
  ...props
}: ButtonProps, ref): JSX.Element {
  const isDisabled = disabled || loading
  const isIcon = size === 'icon' || size === 'iconSm'
  const spinner = (
    <span className="dc-button__spinner" aria-hidden="true">
      <Loader2 size={isIcon ? 15 : 14} className="animate-spin-custom" />
    </span>
  )
  return (
    <button
      ref={ref}
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
})
