import {
  useCallback,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type ReactNode
} from 'react'
import { createPortal } from 'react-dom'

interface PillDropdownProps {
  label: ReactNode
  ariaLabel: string
  /** Panel content; receives a `close` callback to dismiss after a choice. */
  children: (close: () => void) => ReactNode
  icon?: ReactNode
  disabled?: boolean
  /** Subtle accent emphasis on the trigger (e.g. a non-default / bound selection). */
  accent?: boolean
  panelMinWidth?: number
  panelMaxHeight?: number
}

interface PanelPosition {
  left: number
  top: number
}

/** The panel is portalled to `document.body` so it never clips inside a scrollable dialog body. */
export function PillDropdown({
  label,
  ariaLabel,
  children,
  icon,
  disabled = false,
  accent = false,
  panelMinWidth = 200,
  panelMaxHeight = 360
}: PillDropdownProps): JSX.Element {
  const [open, setOpen] = useState(false)
  const [hover, setHover] = useState(false)
  const [pos, setPos] = useState<PanelPosition | null>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)

  const close = useCallback(() => setOpen(false), [])

  const reposition = useCallback(() => {
    const trigger = triggerRef.current
    const panel = panelRef.current
    if (!trigger) return
    const r = trigger.getBoundingClientRect()
    const gap = 6
    const margin = 8
    const panelH = panel?.offsetHeight ?? 0
    const panelW = panel?.offsetWidth ?? panelMinWidth
    const spaceBelow = window.innerHeight - r.bottom
    const openUp = panelH > 0 && spaceBelow < panelH + gap + margin && r.top > spaceBelow
    let top = openUp ? r.top - panelH - gap : r.bottom + gap
    top = Math.max(margin, Math.min(top, window.innerHeight - panelH - margin))
    let left = r.left
    left = Math.max(margin, Math.min(left, window.innerWidth - panelW - margin))
    setPos({ left, top })
  }, [panelMinWidth])

  useLayoutEffect(() => {
    if (!open) {
      setPos(null)
      return
    }
    reposition()
    const panel = panelRef.current
    const observer =
      panel && typeof ResizeObserver !== 'undefined' ? new ResizeObserver(() => reposition()) : null
    if (observer && panel) observer.observe(panel)

    const onScrollOrResize = (): void => reposition()
    const onPointerDown = (e: MouseEvent): void => {
      const target = e.target as Node
      if (triggerRef.current?.contains(target)) return
      if (panelRef.current?.contains(target)) return
      setOpen(false)
    }
    const onKeyDown = (e: KeyboardEvent): void => {
      if (e.key === 'Escape') {
        e.stopPropagation()
        setOpen(false)
      }
    }
    window.addEventListener('resize', onScrollOrResize)
    window.addEventListener('scroll', onScrollOrResize, true)
    document.addEventListener('mousedown', onPointerDown, true)
    document.addEventListener('keydown', onKeyDown, true)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', onScrollOrResize)
      window.removeEventListener('scroll', onScrollOrResize, true)
      document.removeEventListener('mousedown', onPointerDown, true)
      document.removeEventListener('keydown', onKeyDown, true)
    }
  }, [open, reposition])

  const triggerStyle: CSSProperties = {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    height: '28px',
    padding: '0 8px 0 10px',
    borderRadius: '8px',
    border: accent ? '1px solid var(--accent)' : '1px solid var(--border-default)',
    background: open
      ? 'var(--sidebar-control-active)'
      : hover
        ? 'var(--sidebar-control-hover)'
        : accent
          ? 'color-mix(in srgb, var(--accent) 10%, transparent)'
          : 'transparent',
    color: accent ? 'var(--accent)' : open ? 'var(--text-primary)' : 'var(--text-secondary)',
    fontSize: '12px',
    fontWeight: 500,
    cursor: disabled ? 'default' : 'pointer',
    maxWidth: '240px',
    minWidth: 0,
    whiteSpace: 'nowrap',
    transition: 'background-color 100ms ease'
  }

  return (
    <>
      <button
        ref={triggerRef}
        type="button"
        aria-label={ariaLabel}
        aria-haspopup="menu"
        aria-expanded={open}
        disabled={disabled}
        onMouseEnter={() => setHover(true)}
        onMouseLeave={() => setHover(false)}
        onClick={() => {
          if (disabled) return
          setOpen((v) => !v)
        }}
        style={triggerStyle}
      >
        {icon && (
          <span
            aria-hidden
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              flexShrink: 0,
              color: 'currentColor'
            }}
          >
            {icon}
          </span>
        )}
        <span
          style={{
            minWidth: 0,
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap'
          }}
        >
          {label}
        </span>
        <Chevron open={open} />
      </button>

      {open &&
        createPortal(
          <div
            ref={panelRef}
            role="menu"
            aria-label={ariaLabel}
            style={{
              position: 'fixed',
              top: pos?.top ?? 0,
              left: pos?.left ?? 0,
              visibility: pos ? 'visible' : 'hidden',
              zIndex: 1100,
              minWidth: panelMinWidth,
              maxWidth: '320px',
              maxHeight: panelMaxHeight,
              overflowY: 'auto',
              background: 'var(--glass-surface-strong)',
              border: 'none',
              borderRadius: '12px',
              boxShadow: 'var(--glass-shadow-soft)',
              backdropFilter: 'var(--glass-blur)',
              WebkitBackdropFilter: 'var(--glass-blur)',
              padding: '6px'
            }}
          >
            {children(close)}
          </div>,
          document.body
        )}
    </>
  )
}

export function MenuHeading({ children }: { children: ReactNode }): JSX.Element {
  return (
    <div
      style={{
        padding: '4px 9px',
        color: 'var(--text-dimmed)',
        fontSize: '11px',
        fontWeight: 700,
        textTransform: 'uppercase'
      }}
    >
      {children}
    </div>
  )
}

export function MenuOption({
  selected = false,
  onClick,
  children,
  description,
  icon
}: {
  selected?: boolean
  onClick(): void
  children: ReactNode
  description?: ReactNode
  icon?: ReactNode
}): JSX.Element {
  const [hover, setHover] = useState(false)
  return (
    <button
      type="button"
      role="menuitemradio"
      aria-checked={selected}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      onClick={onClick}
      style={{
        width: '100%',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: '10px',
        border: 'none',
        borderRadius: '8px',
        padding: '7px 9px',
        background: hover ? 'var(--sidebar-control-hover)' : 'transparent',
        color: selected ? 'var(--text-primary)' : 'var(--text-secondary)',
        cursor: 'pointer',
        textAlign: 'left',
        fontSize: '12px',
        lineHeight: 1.35,
        transition: 'background-color 80ms ease'
      }}
    >
      <span style={{ display: 'flex', alignItems: 'flex-start', gap: '8px', minWidth: 0 }}>
        {icon && (
          <span
            aria-hidden
            style={{
              display: 'inline-flex',
              alignItems: 'center',
              flexShrink: 0,
              marginTop: '1px',
              color: 'currentColor'
            }}
          >
            {icon}
          </span>
        )}
        <span style={{ minWidth: 0 }}>
          {children}
          {description && (
            <span
              style={{
                display: 'block',
                color: 'var(--text-dimmed)',
                fontSize: '11px',
                lineHeight: 1.3,
                marginTop: '1px'
              }}
            >
              {description}
            </span>
          )}
        </span>
      </span>
      {selected && (
        <span
          aria-hidden
          style={{
            width: '6px',
            height: '6px',
            borderRadius: '999px',
            background: 'var(--accent)',
            flexShrink: 0
          }}
        />
      )}
    </button>
  )
}

function Chevron({ open }: { open: boolean }): JSX.Element {
  return (
    <span
      aria-hidden
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: '12px',
        height: '12px',
        flexShrink: 0,
        color: 'var(--text-dimmed)',
        transform: open ? 'rotate(180deg)' : 'none',
        transition: 'transform 120ms ease'
      }}
    >
      <svg width="10" height="10" viewBox="0 0 12 12" fill="none">
        <path
          d="M3 4.5L6 7.5L9 4.5"
          stroke="currentColor"
          strokeWidth="1.7"
          strokeLinecap="round"
          strokeLinejoin="round"
        />
      </svg>
    </span>
  )
}
