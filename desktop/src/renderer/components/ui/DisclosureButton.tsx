import type { ButtonHTMLAttributes, ReactNode } from 'react'
import { DisclosureChevron } from './DisclosureChevron'

interface DisclosureButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'type' | 'children'> {
  expanded: boolean
  children: ReactNode
}

export function DisclosureButton({ expanded, children, className, ...props }: DisclosureButtonProps): JSX.Element {
  return (
    <button
      {...props}
      type="button"
      className={className ? `dc-disclosure-button ${className}` : 'dc-disclosure-button'}
      aria-expanded={expanded}
    >
      {children}
      <DisclosureChevron expanded={expanded} direction="reveal" />
    </button>
  )
}
