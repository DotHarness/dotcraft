import { Children, forwardRef, type ButtonHTMLAttributes, type JSX, type ReactNode } from 'react'
import { Loader2 } from 'lucide-react'

export type ButtonVariant = 'primary' | 'secondary' | 'ghost' | 'danger' | 'accent' | 'outline'

export type ButtonSize = 'default' | 'sm' | 'icon' | 'iconSm' | 'prominent' | 'toolbar'

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  iconLeft?: ReactNode
  loading?: boolean
}

export function ButtonLabel({ children }: { children: ReactNode }): JSX.Element {
  return <span className="dc-button__label">{children}</span>
}

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
      data-loading={loading || undefined}
      aria-busy={loading || undefined}
      className={className ? `dc-button ${className}` : 'dc-button'}
      {...props}
    >
      {loading && spinner}
      {iconLeft != null && !isIcon && <span className="dc-button__icon" aria-hidden="true">{iconLeft}</span>}
      {loading && isIcon ? null : isIcon ? children : Children.map(children, child =>
        typeof child === 'string' || typeof child === 'number' ? <ButtonLabel>{child}</ButtonLabel> : child)}
    </button>
  )
})
