import { useEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronRight } from 'lucide-react'
import { ActionTooltip } from './ActionTooltip'

export interface ContextMenuItem {
  type?: 'item'
  label: string
  onClick: () => void
  icon?: ReactNode
  /** Native tooltip describing what the item does (shown on hover). */
  title?: string
  danger?: boolean
  disabled?: boolean
  submenu?: ContextMenuEntry[]
}

export interface ContextMenuSeparator {
  type: 'separator'
}

export type ContextMenuEntry = ContextMenuItem | ContextMenuSeparator

export interface ContextMenuPosition {
  x: number
  y: number
}

interface ContextMenuProps {
  items: ContextMenuEntry[]
  position: ContextMenuPosition
  onClose: () => void
}

interface SubmenuAnchor {
  top: number
}

/**
 * Generic positioned context menu rendered via a portal.
 * Closes on outside click or Escape key.
 * Spec §10
 */
export function ContextMenu({ items, position, onClose }: ContextMenuProps): JSX.Element {
  const menuRef = useRef<HTMLDivElement>(null)
  const [openSubmenuIndex, setOpenSubmenuIndex] = useState<number | null>(null)
  const [submenuAnchor, setSubmenuAnchor] = useState<SubmenuAnchor | null>(null)
  const [hoveredItemIndex, setHoveredItemIndex] = useState<number | null>(null)
  const [hoveredSubmenuItemIndex, setHoveredSubmenuItemIndex] = useState<number | null>(null)

  // Clamp to viewport on mount
  const menuWidth = 200
  const menuItemHeight = 30
  const menuPadding = 8
  // The submenu meets the parent edge-to-edge (a ~1px seam, not an obvious overlap
  // that covers the parent); that meeting edge takes a hairline — the only border on
  // an ordinary overlay. See specs/architecture/DESIGN.md.
  const submenuOverlap = 1
  const visibleItemCount = items.filter((item) => item.type !== 'separator').length
  const separatorCount = items.length - visibleItemCount
  const estimatedHeight =
    visibleItemCount * menuItemHeight + separatorCount * 9 + menuPadding * 2

  const left = clampMenuLeft(position.x, menuWidth)
  const top = clampMenuTop(position.y, estimatedHeight)
  const openSubmenuItem = openSubmenuIndex == null ? null : items[openSubmenuIndex]
  const submenuItems =
    openSubmenuItem && openSubmenuItem.type !== 'separator'
      ? openSubmenuItem.submenu ?? null
      : null
  const submenuEstimatedHeight = estimateMenuHeight(submenuItems ?? [], menuItemHeight, menuPadding)
  const submenuPreferredLeft = left + menuWidth - submenuOverlap
  const submenuFlippedLeft = left - menuWidth + submenuOverlap
  const submenuOpensLeft = submenuPreferredLeft + menuWidth + 8 > window.innerWidth
  const submenuLeft = clampMenuLeft(submenuOpensLeft ? submenuFlippedLeft : submenuPreferredLeft, menuWidth)
  const submenuTop = clampMenuTop(submenuAnchor?.top ?? top, submenuEstimatedHeight)
  const submenuLeftOffset = submenuLeft - left
  const submenuTopOffset = submenuTop - top

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

  function openSubmenu(index: number, element: HTMLElement): void {
    setOpenSubmenuIndex(index)
    setSubmenuAnchor(getSubmenuAnchor(element, index, top, items, menuPadding, menuItemHeight))
  }

  function closeSubmenu(): void {
    setOpenSubmenuIndex(null)
    setSubmenuAnchor(null)
    setHoveredSubmenuItemIndex(null)
  }

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
        border: 'none',
        borderRadius: '10px',
        boxShadow: 'var(--glass-shadow-soft)',
        backdropFilter: 'var(--glass-blur)',
        WebkitBackdropFilter: 'var(--glass-blur)',
        zIndex: 9999,
        padding: `${menuPadding}px 0`,
        overflow: 'visible'
      }}
    >
      {items.map((item, i) => {
        if (item.type === 'separator') {
          return (
            <div
              key={i}
              role="separator"
              style={{
                height: '1px',
                margin: '4px 0',
                backgroundColor: 'var(--glass-border)'
              }}
            />
          )
        }

        const itemActive = !item.disabled && (hoveredItemIndex === i || openSubmenuIndex === i)
        const button = (
          <button
            role="menuitem"
            aria-haspopup={item.submenu ? 'menu' : undefined}
            aria-expanded={item.submenu ? openSubmenuIndex === i : undefined}
            disabled={item.disabled}
            onClick={(event) => {
              if (!item.disabled) {
                if (item.submenu) {
                  if (openSubmenuIndex === i) {
                    closeSubmenu()
                  } else {
                    openSubmenu(i, event.currentTarget)
                  }
                  return
                }
                item.onClick()
                onClose()
              }
            }}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
              width: 'calc(100% - 12px)',
              margin: '0 6px',
              padding: '6px 8px',
              borderRadius: '6px',
              textAlign: 'left',
              background: itemActive ? 'var(--sidebar-control-hover)' : 'transparent',
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
              setHoveredItemIndex(i)
              if (item.disabled) return
              if (item.submenu) {
                openSubmenu(i, e.currentTarget)
              } else {
                closeSubmenu()
              }
            }}
            onMouseLeave={() => {
              setHoveredItemIndex((current) => current === i ? null : current)
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
            {item.submenu && (
              <ChevronRight
                size={14}
                aria-hidden="true"
                style={{ marginLeft: 'auto', flexShrink: 0, color: 'var(--text-dimmed)' }}
              />
            )}
          </button>
        )

        return item.title ? (
          <ActionTooltip key={i} label={item.title} placement="right" wrapperStyle={{ width: '100%' }}>
            {button}
          </ActionTooltip>
        ) : (
          <div key={i}>{button}</div>
        )
      })}
      {submenuItems && submenuItems.length > 0 && (
        <div
          role="menu"
          style={{
            position: 'absolute',
            top: submenuTopOffset,
            left: submenuLeftOffset,
            width: menuWidth,
            background: 'var(--glass-surface-strong)',
            borderTop: 'none',
            borderBottom: 'none',
            // Hairline on the overlapping edge only (faces the parent menu).
            borderLeft: submenuOpensLeft ? 'none' : '1px solid var(--glass-border)',
            borderRight: submenuOpensLeft ? '1px solid var(--glass-border)' : 'none',
            borderRadius: '10px',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            zIndex: 10000,
            padding: `${menuPadding}px 0`,
            overflow: 'hidden'
          }}
        >
          {submenuItems.map((item, i) => {
            if (item.type === 'separator') {
              return (
                <div
                  key={i}
                  role="separator"
                  style={{
                    height: '1px',
                    margin: '4px 0',
                    backgroundColor: 'var(--glass-border)'
                  }}
                />
              )
            }
            const submenuItemActive = !item.disabled && hoveredSubmenuItemIndex === i
            return (
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
                  width: 'calc(100% - 12px)',
                  margin: '0 6px',
                  padding: '6px 8px',
                  borderRadius: '6px',
                  textAlign: 'left',
                  background: submenuItemActive ? 'var(--sidebar-control-hover)' : 'transparent',
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
                onMouseEnter={() => {
                  setHoveredSubmenuItemIndex(i)
                }}
                onMouseLeave={() => {
                  setHoveredSubmenuItemIndex((current) => current === i ? null : current)
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
            )
          })}
        </div>
      )}
    </div>
  )

  return createPortal(menu, document.body) as JSX.Element
}

function getSubmenuAnchor(
  element: HTMLElement,
  index: number,
  menuTop: number,
  items: ContextMenuEntry[],
  menuPadding: number,
  menuItemHeight: number
): SubmenuAnchor {
  const rect = element.getBoundingClientRect()
  if (rect.width > 0 || rect.height > 0 || rect.left !== 0 || rect.top !== 0) {
    return {
      top: rect.top
    }
  }

  const offsetTop = menuPadding + items.slice(0, index).reduce((acc, item) => (
    acc + (item.type === 'separator' ? 9 : menuItemHeight)
  ), 0)
  return {
    top: menuTop + offsetTop
  }
}

function estimateMenuHeight(
  items: ContextMenuEntry[],
  menuItemHeight: number,
  menuPadding: number
): number {
  const visibleItemCount = items.filter((item) => item.type !== 'separator').length
  const separatorCount = items.length - visibleItemCount
  return visibleItemCount * menuItemHeight + separatorCount * 9 + menuPadding * 2
}

function clampMenuTop(top: number, estimatedHeight: number): number {
  return Math.max(8, Math.min(top, window.innerHeight - estimatedHeight - 8))
}

function clampMenuLeft(left: number, menuWidth: number): number {
  return Math.max(8, Math.min(left, window.innerWidth - menuWidth - 8))
}
