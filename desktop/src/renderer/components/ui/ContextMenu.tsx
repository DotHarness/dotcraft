import { useEffect, useRef, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

export interface ContextMenuItem {
  label: string
  onClick: () => void
  icon?: ReactNode
  danger?: boolean
  disabled?: boolean
}

export interface ContextMenuPosition {
  x: number
  y: number
}

interface ContextMenuProps {
  items: ContextMenuItem[]
  position: ContextMenuPosition
  onClose: () => void
}

/**
 * Generic positioned context menu rendered via a portal.
 * Closes on outside click or Escape key.
 * Spec §10
 */
export function ContextMenu({ items, position, onClose }: ContextMenuProps): JSX.Element {
  const menuRef = useRef<HTMLDivElement>(null)

  // Clamp to viewport on mount
  const menuWidth = 160
  const menuItemHeight = 30
  const menuPadding = 8
  const estimatedHeight = items.length * menuItemHeight + menuPadding * 2

  const left = Math.min(position.x, window.innerWidth - menuWidth - 8)
  const top = Math.min(position.y, window.innerHeight - estimatedHeight - 8)

  useEffect(() => {
    function handleMouseDown(e: MouseEvent): void {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        onClose()
      }
    }
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', handleMouseDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleMouseDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [onClose])

  const menu = (
    <div
      ref={menuRef}
      role="menu"
      style={{
        position: 'fixed',
        top,
        left,
        width: menuWidth,
        background: 'var(--glass-surface-strong)',
        border: '1px solid var(--glass-border)',
        borderRadius: '10px',
        boxShadow: 'var(--glass-shadow-soft)',
        backdropFilter: 'var(--glass-blur)',
        WebkitBackdropFilter: 'var(--glass-blur)',
        zIndex: 9999,
        padding: `${menuPadding}px 0`,
        overflow: 'hidden'
      }}
    >
      {items.map((item, i) => (
        <button
          key={i}
          role="menuitem"
          disabled={item.disabled}
          onClick={() => {
            if (!item.disabled) {
              item.onClick()
              onClose()
            }
          }}
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: '8px',
            width: '100%',
            padding: '6px 14px',
            textAlign: 'left',
            background: 'none',
            border: 'none',
            fontSize: '13px',
            color: item.danger
              ? 'var(--error)'
              : item.disabled
                ? 'var(--text-dimmed)'
                : 'var(--text-primary)',
            cursor: item.disabled ? 'default' : 'pointer',
            transition: 'background-color 80ms ease'
          }}
          onMouseEnter={(e) => {
            if (!item.disabled) {
              ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--glass-surface-soft)'
            }
          }}
          onMouseLeave={(e) => {
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
          }}
        >
          {item.icon && (
            <span
              aria-hidden="true"
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                justifyContent: 'center',
                width: 16,
                height: 16,
                flexShrink: 0
              }}
            >
              {item.icon}
            </span>
          )}
          {item.label}
        </button>
      ))}
    </div>
  )

  return createPortal(menu, document.body) as JSX.Element
}
