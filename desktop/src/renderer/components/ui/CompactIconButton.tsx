import { useState, type ButtonHTMLAttributes, type CSSProperties, type JSX, type ReactNode } from 'react'
import { ActionTooltip } from './ActionTooltip'

interface CompactIconButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'children' | 'aria-label'> {
  icon: ReactNode
  label: string
  active?: boolean
  activeColor?: CSSProperties['color']
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
}

export function CompactIconButton({
  icon,
  label,
  active = false,
  activeColor = 'var(--text-primary)',
  tooltipPlacement = 'top',
  style,
  ...props
}: CompactIconButtonProps): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const chromeVisible = hovered || focused

  return (
    <ActionTooltip label={label} placement={tooltipPlacement}>
      <button
        type="button"
        aria-label={label}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        style={{
          width: '24px',
          height: '24px',
          padding: 0,
          borderRadius: '6px',
          border: '1px solid transparent',
          background: chromeVisible ? 'var(--bg-tertiary)' : 'transparent',
          color: active ? activeColor : chromeVisible ? 'var(--text-primary)' : 'var(--text-secondary)',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: 'pointer',
          transition: 'color 120ms ease, background 120ms ease',
          ...style
        }}
        {...props}
      >
        {icon}
      </button>
    </ActionTooltip>
  )
}
