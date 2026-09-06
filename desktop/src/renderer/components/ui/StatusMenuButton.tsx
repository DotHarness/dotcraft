import { useRef, useState, type JSX } from 'react'
import { ChevronDown } from 'lucide-react'
import { StatusIndicator, type StatusTone } from './StatusIndicator'
import { ContextMenu, type ContextMenuEntry } from './ContextMenu'

export type StatusMenuTone = StatusTone

interface StatusMenuButtonProps {
  label: string
  ariaLabel?: string
  tone?: StatusMenuTone
  items: ContextMenuEntry[]
  loading?: boolean
  disabled?: boolean
  className?: string
}

export function StatusMenuButton({
  label,
  ariaLabel,
  tone = 'neutral',
  items,
  loading = false,
  disabled = false,
  className
}: StatusMenuButtonProps): JSX.Element {
  const buttonRef = useRef<HTMLButtonElement>(null)
  const [position, setPosition] = useState<{ x: number; y: number } | null>(null)
  const unavailable = disabled || loading || items.length === 0

  function closeMenu(restoreFocus = true): void {
    setPosition(null)
    if (restoreFocus) window.setTimeout(() => buttonRef.current?.focus(), 0)
  }

  function toggleMenu(): void {
    if (position != null) {
      closeMenu(false)
      return
    }
    const rect = buttonRef.current?.getBoundingClientRect()
    if (!rect) return
    setPosition({ x: rect.right - 200, y: rect.bottom + 4 })
  }

  return (
    <>
      <button
        ref={buttonRef}
        type="button"
        className={className ? `dc-status-menu-button ${className}` : 'dc-status-menu-button'}
        data-open={position != null || undefined}
        aria-label={ariaLabel ?? label}
        aria-haspopup="menu"
        aria-expanded={position != null}
        disabled={unavailable}
        onClick={toggleMenu}
        onKeyDown={(event) => {
          if (event.key !== 'Enter' && event.key !== ' ') return
          event.preventDefault()
          toggleMenu()
        }}
      >
        <StatusIndicator tone={loading ? 'pending' : tone} />
        <span>{label}</span>
        <ChevronDown size={13} aria-hidden />
      </button>
      {position && (
        <ContextMenu
          position={position}
          items={items}
          onClose={() => closeMenu(true)}
        />
      )}
    </>
  )
}
