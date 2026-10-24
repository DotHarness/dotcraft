import {
  useContext,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent as ReactKeyboardEvent
} from 'react'
import { createPortal } from 'react-dom'
import { MoreHorizontal } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerContext } from '../../contexts/LayerContext'
import { useSidebarDestinationsStore } from '../../stores/sidebarDestinationsStore'
import { IconButton } from '../ui/IconButton'
import { PinIcon } from './ThreadEntry'
import type { SidebarDestination } from './sidebarDestinations'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'

const MENU_WIDTH = 220
const MENU_PADDING = 8
const MENU_ITEM_HEIGHT = 30
const HOVER_OPEN_DELAY_MS = 120
const HOVER_CLOSE_DELAY_MS = 220

interface SidebarMoreMenuProps {
  destinations: SidebarDestination[]
  variant: 'row' | 'icon'
}

export function SidebarMoreMenu({ destinations, variant }: SidebarMoreMenuProps): JSX.Element | null {
  const t = useT()
  const setPinned = useSidebarDestinationsStore((s) => s.setPinned)
  const layerDepth = useContext(LayerContext)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const openTimerRef = useRef<number | null>(null)
  const closeTimerRef = useRef<number | null>(null)
  const [open, setOpen] = useState<'hover' | 'click' | null>(null)
  const [position, setPosition] = useState<{ top: number; left: number } | null>(null)
  const [hoveredId, setHoveredId] = useState<string | null>(null)

  const label = t('sidebar.more')
  const active = destinations.some((destination) => destination.active)

  function clearTimers(): void {
    if (openTimerRef.current != null) window.clearTimeout(openTimerRef.current)
    if (closeTimerRef.current != null) window.clearTimeout(closeTimerRef.current)
    openTimerRef.current = null
    closeTimerRef.current = null
  }

  function close(): void {
    clearTimers()
    setOpen(null)
    setHoveredId(null)
  }

  function handlePointerEnter(): void {
    if (closeTimerRef.current != null) window.clearTimeout(closeTimerRef.current)
    closeTimerRef.current = null
    if (open || openTimerRef.current != null) return
    openTimerRef.current = window.setTimeout(() => {
      openTimerRef.current = null
      setOpen('hover')
    }, HOVER_OPEN_DELAY_MS)
  }

  function handlePointerLeave(): void {
    if (openTimerRef.current != null) window.clearTimeout(openTimerRef.current)
    openTimerRef.current = null
    if (open !== 'hover') return
    closeTimerRef.current = window.setTimeout(close, HOVER_CLOSE_DELAY_MS)
  }

  function focusItem(index: 'first' | 'last' | 1 | -1): void {
    const items = Array.from(menuRef.current?.querySelectorAll<HTMLButtonElement>('[data-more-item]') ?? [])
    if (items.length === 0) return
    if (index === 'first') return items[0].focus()
    if (index === 'last') return items[items.length - 1].focus()
    const current = items.indexOf(document.activeElement as HTMLButtonElement)
    items[(current + index + items.length) % items.length].focus()
  }

  function handleTriggerKeyDown(event: ReactKeyboardEvent<HTMLButtonElement>): void {
    if (event.key !== 'ArrowDown' && event.key !== 'ArrowRight') return
    event.preventDefault()
    setOpen('click')
    window.requestAnimationFrame(() => focusItem('first'))
  }

  function handleMenuKeyDown(event: ReactKeyboardEvent<HTMLDivElement>): void {
    const moves: Record<string, 'first' | 'last' | 1 | -1> = {
      ArrowDown: 1,
      ArrowUp: -1,
      Home: 'first',
      End: 'last'
    }
    const move = moves[event.key]
    if (move !== undefined) {
      event.preventDefault()
      focusItem(move)
    } else if (event.key === 'Escape' || event.key === 'ArrowLeft') {
      event.preventDefault()
      close()
      triggerRef.current?.focus()
    }
  }

  useLayoutEffect(() => {
    if (!open) {
      setPosition(null)
      return
    }
    const trigger = triggerRef.current
    if (!trigger) return
    const rect = trigger.getBoundingClientRect()
    const height = menuRef.current?.offsetHeight ?? destinations.length * MENU_ITEM_HEIGHT + MENU_PADDING * 2
    const alignedTop = rect.top + rect.height / 2 - MENU_ITEM_HEIGHT / 2 - MENU_PADDING
    setPosition({
      left: rect.right + 4,
      top: Math.max(8, Math.min(alignedTop, window.innerHeight - height - 8))
    })
  }, [open, destinations.length])

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || triggerRef.current?.contains(target)) return
      close()
    }
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !event.defaultPrevented) close()
    }
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [open])

  useEffect(() => clearTimers, [])

  useEffect(() => {
    if (destinations.length === 0) close()
  }, [destinations.length])

  if (destinations.length === 0) return null

  const triggerProps = {
    ref: triggerRef,
    'aria-haspopup': 'menu' as const,
    'aria-expanded': open != null,
    onClick: () => {
      clearTimers()
      setOpen((current) => (current === 'click' ? null : 'click'))
    },
    onKeyDown: handleTriggerKeyDown,
    onPointerEnter: handlePointerEnter,
    onPointerLeave: handlePointerLeave
  }

  const trigger = variant === 'icon'
    ? (
      <IconButton
        {...triggerProps}
        data-testid="nav-more"
        icon={<MoreHorizontal size={16} strokeWidth={2} aria-hidden />}
        label={label}
        tooltipLabel={label}
        tooltipPlacement="right"
        size={32}
        radius={8}
        className="dc-sidebar-icon-button"
        active={active || open != null}
      />
    )
    : (
      <button
        {...triggerProps}
        type="button"
        data-testid="nav-more"
        className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
        data-active={active || open != null ? 'true' : undefined}
        style={{ ...SIDEBAR_NAV_ROW_OUTER, ...SIDEBAR_NAV_BORDER_INACTIVE }}
      >
        <span style={SIDEBAR_NAV_ICON_SLOT}>
          <MoreHorizontal size={16} strokeWidth={2} aria-hidden />
        </span>
        <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
      </button>
    )

  const menu = open && createPortal(
    <div
      ref={menuRef}
      role="menu"
      aria-label={label}
      onKeyDown={handleMenuKeyDown}
      onPointerEnter={handlePointerEnter}
      onPointerLeave={handlePointerLeave}
      style={{
        position: 'fixed',
        top: position?.top ?? 0,
        left: position?.left ?? 0,
        visibility: position ? 'visible' : 'hidden',
        width: MENU_WIDTH,
        background: 'var(--glass-surface-strong)',
        borderRadius: '10px',
        boxShadow: 'var(--glass-shadow-soft)',
        backdropFilter: 'var(--glass-blur)',
        WebkitBackdropFilter: 'var(--glass-blur)',
        zIndex: layerDepth > 0 ? 10100 : 9999,
        padding: `${MENU_PADDING}px 0`
      }}
    >
      {destinations.map((destination) => {
        const rowActive = hoveredId === destination.id
        return (
          <div
            key={destination.id}
            role="group"
            onPointerEnter={() => setHoveredId(destination.id)}
            onPointerLeave={() => setHoveredId((current) => (current === destination.id ? null : current))}
            onFocus={() => setHoveredId(destination.id)}
            style={{
              display: 'flex',
              alignItems: 'center',
              margin: '0 6px',
              paddingRight: '4px',
              borderRadius: '6px',
              background: rowActive || destination.active ? 'var(--sidebar-control-hover)' : 'transparent',
              transition: 'background-color 80ms ease'
            }}
          >
            <button
              type="button"
              role="menuitem"
              data-more-item=""
              data-nav-icon-host=""
              data-testid={destination.testId}
              aria-current={destination.active ? 'page' : undefined}
              onClick={() => {
                close()
                destination.select()
              }}
              onPointerEnter={destination.prefetch}
              onFocus={destination.prefetch}
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                flex: 1,
                minWidth: 0,
                height: MENU_ITEM_HEIGHT,
                padding: '0 8px',
                border: 'none',
                background: 'transparent',
                color: 'var(--text-primary)',
                fontSize: '13px',
                textAlign: 'left',
                cursor: 'pointer',
                outline: 'none'
              }}
            >
              <span style={SIDEBAR_NAV_ICON_SLOT}>{destination.icon}</span>
              <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {destination.label}
              </span>
            </button>
            <IconButton
              icon={<PinIcon filled={false} />}
              label={t('sidebar.pinToSidebar')}
              tooltipLabel={t('sidebar.pinToSidebar')}
              tooltipPlacement="right"
              size={22}
              radius={6}
              className="dc-sidebar-icon-button"
              onClick={() => setPinned(destination.id, true)}
              style={{
                flexShrink: 0,
                opacity: rowActive ? 1 : 0,
                transition: 'opacity 120ms ease'
              }}
            />
          </div>
        )
      })}
    </div>,
    document.body
  )

  return (
    <>
      {trigger}
      {menu}
    </>
  )
}
