import { useRef, useState, type JSX } from 'react'
import { ChevronDown, Loader2 } from 'lucide-react'
import { ContextMenu, type ContextMenuEntry } from './ContextMenu'

export type StatusMenuTone = 'neutral' | 'success' | 'warning' | 'error'

interface StatusMenuButtonProps {
  label: string
  ariaLabel?: string
  tone?: StatusMenuTone
  items: ContextMenuEntry[]
  loading?: boolean
  disabled?: boolean
  className?: string
}

/** Compact state label whose trailing chevron opens the shared action menu. */
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
        data-tone={tone}
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
        {loading
          ? <Loader2 size={13} className="animate-spin-custom" aria-hidden />
          : <span className="dc-status-menu-button__dot" aria-hidden />}
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
