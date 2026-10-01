import { useContext, useEffect, useRef, useState, type MouseEvent as ReactMouseEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Check, ChevronRight } from 'lucide-react'
import { useMenuAim } from '../../hooks/useMenuAim'
import { ActionTooltip } from './ActionTooltip'
import { LayerContext } from '../../contexts/LayerContext'

export interface ContextMenuItem {
  type?: 'item'
  label: string
  onClick: () => void
  icon?: ReactNode
  /** Content aligned to the trailing edge, such as a selected-state check. */
  trailing?: ReactNode
  selection?: 'radio' | 'checkbox'
  checked?: boolean
  /** Native tooltip describing what the item does (shown on hover). */
  title?: string
  danger?: boolean
  disabled?: boolean
  submenu?: ContextMenuEntry[]
}

export interface ContextMenuSeparator {
  type: 'separator'
}

export interface ContextMenuLabel {
  type: 'label'
  label: string
}

export type ContextMenuEntry = ContextMenuItem | ContextMenuSeparator | ContextMenuLabel

const MENU_SEPARATOR_HEIGHT = 9
const MENU_LABEL_HEIGHT = 26

function menuItemRole(item: ContextMenuItem): 'menuitem' | 'menuitemradio' | 'menuitemcheckbox' {
  if (item.selection === 'radio') return 'menuitemradio'
  if (item.selection === 'checkbox') return 'menuitemcheckbox'
  return 'menuitem'
}

function menuItemTrailing(item: ContextMenuItem): ReactNode {
  if (item.trailing) return item.trailing
  return item.selection && item.checked ? <Check size={14} aria-hidden /> : null
}

function MenuLabel({ label }: { label: string }): JSX.Element {
  return (
    <div
      role="presentation"
      title={label}
      style={{
        padding: '6px 14px 4px',
        fontSize: 'var(--type-secondary-size)',
        lineHeight: 'var(--type-secondary-line-height)',
        color: 'var(--text-dimmed)',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        userSelect: 'none'
      }}
    >
      {label}
    </div>
  )
}

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

export function ContextMenu({ items, position, onClose }: ContextMenuProps): JSX.Element {
  const menuRef = useRef<HTMLDivElement>(null)
  const submenuRef = useRef<HTMLDivElement>(null)
  const menuZIndex = useContext(LayerContext) > 0 ? 10100 : 9999
  const [openSubmenuIndex, setOpenSubmenuIndex] = useState<number | null>(null)
  const [submenuAnchor, setSubmenuAnchor] = useState<SubmenuAnchor | null>(null)
  const [hoveredItemIndex, setHoveredItemIndex] = useState<number | null>(null)
  const [hoveredSubmenuItemIndex, setHoveredSubmenuItemIndex] = useState<number | null>(null)

  const menuWidth = 200
  const menuItemHeight = 30
  const menuPadding = 8
  // The submenu meets the parent edge-to-edge (a ~1px seam, not an obvious overlap
  // that covers the parent); that meeting edge takes a hairline — the only border on
  // an ordinary overlay.
  const submenuOverlap = 1
  const estimatedHeight = estimateMenuHeight(items, menuItemHeight, menuPadding)

  const left = clampMenuLeft(position.x, menuWidth)
  const top = clampMenuTop(position.y, estimatedHeight)
  const openSubmenuItem = openSubmenuIndex == null ? null : items[openSubmenuIndex]
  const submenuItems =
    openSubmenuItem && isMenuItem(openSubmenuItem)
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
  const {
    track: trackMenuAim,
    guard: guardMenuAim,
    cancel: cancelMenuAim
  } = useMenuAim({
    submenuRef,
    side: submenuOpensLeft ? 'left' : 'right'
  })

  useEffect(() => {
    function handleMouseDown(e: MouseEvent): void {
      if (menuRef.current && !menuRef.current.contains(e.target as Node)) {
        cancelMenuAim()
        onClose()
      }
    }
    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') {
        cancelMenuAim()
        onClose()
      }
    }
    document.addEventListener('mousedown', handleMouseDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleMouseDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [cancelMenuAim, onClose])

  function openSubmenu(index: number, element: HTMLElement): void {
    setOpenSubmenuIndex(index)
    setSubmenuAnchor(getSubmenuAnchor(element, index, top, items, menuPadding, menuItemHeight))
  }

  function closeSubmenu(): void {
    setOpenSubmenuIndex(null)
    setSubmenuAnchor(null)
    setHoveredSubmenuItemIndex(null)
  }

  function handleParentItemPointer(
    item: ContextMenuItem,
    index: number,
    event: ReactMouseEvent<HTMLButtonElement>
  ): void {
    if (item.disabled) {
      setHoveredItemIndex(index)
      return
    }

    if (item.submenu) {
      if (openSubmenuIndex === index) {
        setHoveredItemIndex(index)
        trackMenuAim(event)
        return
      }

      const row = event.currentTarget
      if (openSubmenuIndex !== null) {
        guardMenuAim(event, () => {
          setHoveredItemIndex(index)
          openSubmenu(index, row)
        })
        return
      }

      cancelMenuAim()
      setHoveredItemIndex(index)
      openSubmenu(index, row)
      trackMenuAim(event)
      return
    }

    if (openSubmenuIndex !== null) {
      guardMenuAim(event, () => {
        setHoveredItemIndex(index)
        closeSubmenu()
      })
      return
    }

    cancelMenuAim()
    setHoveredItemIndex(index)
    closeSubmenu()
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
        zIndex: menuZIndex,
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
        if (item.type === 'label') return <MenuLabel key={i} label={item.label} />

        const itemActive = !item.disabled && (hoveredItemIndex === i || openSubmenuIndex === i)
        const trailing = menuItemTrailing(item)
        const button = (
          <button
            role={menuItemRole(item)}
            aria-checked={item.selection ? item.checked === true : undefined}
            aria-haspopup={item.submenu ? 'menu' : undefined}
            aria-expanded={item.submenu ? openSubmenuIndex === i : undefined}
            disabled={item.disabled}
            onClick={(event) => {
              cancelMenuAim()
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
            onMouseEnter={(event) => handleParentItemPointer(item, i, event)}
            onMouseMove={(event) => handleParentItemPointer(item, i, event)}
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
            {trailing ? <MenuItemTrailing>{trailing}</MenuItemTrailing> : null}
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
          ref={submenuRef}
          role="menu"
          onMouseEnter={cancelMenuAim}
          onMouseMove={cancelMenuAim}
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
            zIndex: menuZIndex + 1,
            padding: `${menuPadding}px 0`,
            maxHeight: 'calc(100vh - 16px)',
            overflowX: 'hidden',
            overflowY: 'auto'
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
            if (item.type === 'label') return <MenuLabel key={i} label={item.label} />
            const submenuItemActive = !item.disabled && hoveredSubmenuItemIndex === i
            const trailing = menuItemTrailing(item)
            const submenuButton = (
              <button
                key={i}
                role={menuItemRole(item)}
                aria-checked={item.selection ? item.checked === true : undefined}
                disabled={item.disabled}
                onClick={() => {
                  cancelMenuAim()
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
                {trailing ? <MenuItemTrailing>{trailing}</MenuItemTrailing> : null}
              </button>
            )
            return item.title ? (
              <ActionTooltip key={i} label={item.title} placement="right" wrapperStyle={{ width: '100%' }}>
                {submenuButton}
              </ActionTooltip>
            ) : submenuButton
          })}
        </div>
      )}
    </div>
  )

  return createPortal(menu, document.body) as JSX.Element
}

function MenuItemTrailing({ children }: { children: ReactNode }): JSX.Element {
  return (
    <span aria-hidden="true" style={{ display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 16, height: 16, marginLeft: 'auto', flexShrink: 0 }}>
      {children}
    </span>
  )
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
    acc + entryHeight(item, menuItemHeight)
  ), 0)
  return {
    top: menuTop + offsetTop
  }
}

function isMenuItem(entry: ContextMenuEntry): entry is ContextMenuItem {
  return entry.type !== 'separator' && entry.type !== 'label'
}

function entryHeight(entry: ContextMenuEntry, menuItemHeight: number): number {
  if (entry.type === 'separator') return MENU_SEPARATOR_HEIGHT
  if (entry.type === 'label') return MENU_LABEL_HEIGHT
  return menuItemHeight
}

function estimateMenuHeight(
  items: ContextMenuEntry[],
  menuItemHeight: number,
  menuPadding: number
): number {
  return items.reduce((acc, item) => acc + entryHeight(item, menuItemHeight), 0) + menuPadding * 2
}

function clampMenuTop(top: number, estimatedHeight: number): number {
  return Math.max(8, Math.min(top, window.innerHeight - estimatedHeight - 8))
}

function clampMenuLeft(left: number, menuWidth: number): number {
  return Math.max(8, Math.min(left, window.innerWidth - menuWidth - 8))
}
