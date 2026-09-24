import { forwardRef, type ButtonHTMLAttributes } from 'react'
import { MoreHorizontal } from 'lucide-react'
import { IconButton } from './IconButton'

interface MoreActionsButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children' | 'aria-label'> {
  label: string
  open?: boolean
  size?: number
  radius?: number
  iconSize?: number
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
}

export const MoreActionsButton = forwardRef<HTMLButtonElement, MoreActionsButtonProps>(function MoreActionsButton(
  { label, open, size, radius, iconSize = 16, tooltipPlacement = 'bottom', ...props },
  ref
): JSX.Element {
  return (
    <IconButton
      ref={ref}
      {...props}
      size={size}
      radius={radius}
      label={label}
      tooltipLabel={label}
      tooltipPlacement={tooltipPlacement}
      aria-haspopup="menu"
      aria-expanded={open}
      icon={<MoreHorizontal size={iconSize} aria-hidden />}
    />
  )
})
