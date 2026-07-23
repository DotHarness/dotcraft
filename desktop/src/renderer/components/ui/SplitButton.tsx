import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { ChevronDownIcon } from './AppIcons'
import { ActionTooltip } from './ActionTooltip'
import { Button, type ButtonVariant } from './Button'

export interface SplitButtonItem {
  key: string
  label: string
  icon?: ReactNode
  disabled?: boolean
  /** Marks the current choice when the menu picks a default rather than running a command. */
  selected?: boolean
  onClick: () => void
}

interface SplitButtonProps {
  /** Label of the principal segment, which runs the default action directly. Omit for icon-only. */
  label?: string
  /** Accessible name for the principal segment; required when there is no visible label. */
  ariaLabel?: string
  /** Leading glyph for the principal segment. */
  icon?: ReactNode
  onClick: () => void
  items: SplitButtonItem[]
  /** Accessible name for the menu segment. */
  menuLabel: string
  /** Hover hint for the principal segment; the menu segment falls back to `menuLabel`. */
  tooltip?: string
  /** Shown in place of the tooltip while the control is unavailable. */
  disabledReason?: string
  tooltipPlacement?: 'top' | 'bottom' | 'left' | 'right'
  /** Intent shared by both segments. `primary` is the neutral inversion. */
  variant?: ButtonVariant
  disabled?: boolean
}

/**
 * Compound trigger that pairs a principal action with a menu of related commands.
 *
 * Both segments carry the same fill and sit flush: the touching edges are stripped and
 * the wrapper clips the outer corners, so no divider is painted. Hovering lightens only
 * the hovered segment, and that is what makes the seam visible. Everything sits in the
 * catalog toolbar band. `primary` leads a surface; `secondary` suits triggers that sit
 * among other chrome. See the Compound Triggers section in specs/architecture/DESIGN.md.
 */
export function SplitButton({
  label,
  ariaLabel,
  icon,
  onClick,
  items,
  menuLabel,
  tooltip,
  disabledReason,
  tooltipPlacement = 'bottom',
  variant = 'primary',
  disabled = false
}: SplitButtonProps): JSX.Element {
  const wrapRef = useRef<HTMLDivElement>(null)
  const menuButtonRef = useRef<HTMLButtonElement>(null)
  const [open, setOpen] = useState(false)
  const [highlight, setHighlight] = useState(0)

  // A menu that marks a current choice opens on it rather than at the top.
  useEffect(() => {
    if (!open) return
    const selected = items.findIndex((item) => item.selected)
    setHighlight(selected >= 0 ? selected : 0)
  }, [items, open])

  function runItem(item: SplitButtonItem): void {
    if (item.disabled) return
    setOpen(false)
    item.onClick()
    window.setTimeout(() => menuButtonRef.current?.focus(), 0)
  }

  useEffect(() => {
    if (!open) return

    const handlePointerDown = (event: MouseEvent): void => {
      if (event.button !== 0) return
      if (!wrapRef.current?.contains(event.target as Node)) setOpen(false)
    }
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        setOpen(false)
        window.setTimeout(() => menuButtonRef.current?.focus(), 0)
        return
      }
      if (items.length === 0) return
      if (event.key === 'ArrowDown') {
        event.preventDefault()
        setHighlight((current) => Math.min(items.length - 1, current + 1))
        return
      }
      if (event.key === 'ArrowUp') {
        event.preventDefault()
        setHighlight((current) => Math.max(0, current - 1))
        return
      }
      if (event.key === 'Enter') {
        const item = items[highlight]
        if (!item || item.disabled) return
        event.preventDefault()
        runItem(item)
      }
    }

    window.addEventListener('mousedown', handlePointerDown, true)
    window.addEventListener('keydown', handleKeyDown, true)
    return () => {
      window.removeEventListener('mousedown', handlePointerDown, true)
      window.removeEventListener('keydown', handleKeyDown, true)
    }
  }, [highlight, items, open])

  // A non-left press must neither steal focus nor open a native menu on either segment.
  const guardPress = (event: { button: number; preventDefault: () => void }): void => {
    if (event.button !== 0) event.preventDefault()
  }

  const primary = (
    <Button
      variant={variant}
      size="toolbar"
      iconLeft={label != null ? icon : undefined}
      aria-label={ariaLabel}
      disabled={disabled}
      onMouseDown={guardPress}
      onContextMenu={(event) => event.preventDefault()}
      onClick={onClick}
      style={label != null ? primarySegmentStyle : iconPrimarySegmentStyle}
    >
      {label ?? icon}
    </Button>
  )

  const menu = (
    <Button
      ref={menuButtonRef}
      variant={variant}
      size="toolbar"
      aria-label={menuLabel}
      aria-haspopup="menu"
      aria-expanded={open}
      disabled={disabled || items.length === 0}
      onMouseDown={guardPress}
      onContextMenu={(event) => event.preventDefault()}
      onClick={() => setOpen((current) => !current)}
      style={menuSegmentStyle}
    >
      <span style={chevronStyle}>
        <ChevronDownIcon size={12} />
      </span>
    </Button>
  )

  return (
    <div ref={wrapRef} style={wrapStyle}>
      <div style={groupStyle}>
        {tooltip != null
          ? (
            <ActionTooltip label={tooltip} disabledReason={disabledReason} placement={tooltipPlacement}>
              {primary}
            </ActionTooltip>
          )
          : primary}
        {tooltip != null
          ? (
            <ActionTooltip label={menuLabel} disabledReason={disabledReason} placement={tooltipPlacement}>
              {menu}
            </ActionTooltip>
          )
          : menu}
      </div>

      {open && items.length > 0 && (
        <div role="menu" aria-label={menuLabel} style={menuStyle}>
          {items.map((item, index) => (
            <button
              key={item.key}
              type="button"
              role="menuitem"
              aria-label={item.label}
              disabled={item.disabled}
              onMouseEnter={() => setHighlight(index)}
              onContextMenu={(event) => event.preventDefault()}
              onClick={() => runItem(item)}
              style={menuItemStyle(highlight === index, item.disabled === true)}
            >
              <span style={menuItemBodyStyle}>
                {item.icon != null && <span style={menuIconStyle}>{item.icon}</span>}
                <span style={menuLabelStyle}>{item.label}</span>
              </span>
              {item.selected === true && <span aria-hidden style={selectedDotStyle} />}
            </button>
          ))}
        </div>
      )}
    </div>
  )
}

const wrapStyle: CSSProperties = { position: 'relative', display: 'inline-flex', flexShrink: 0 }

// `overflow: hidden` is what rounds the group: each segment keeps square inner corners
// and the wrapper clips the outer ones, so the two fills meet without a seam.
const groupStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'stretch',
  overflow: 'hidden',
  borderRadius: 'var(--toolbar-control-radius)'
}

const primarySegmentStyle: CSSProperties = {
  borderTopRightRadius: 0,
  borderBottomRightRadius: 0,
  borderRightWidth: 0,
  paddingRight: '4px'
}

// An icon-only segment tightens the leading edge so the glyph is not left adrift.
const iconPrimarySegmentStyle: CSSProperties = {
  ...primarySegmentStyle,
  paddingLeft: '8px'
}

const menuSegmentStyle: CSSProperties = {
  gap: 0,
  borderTopLeftRadius: 0,
  borderBottomLeftRadius: 0,
  borderLeftWidth: 0,
  padding: '0 6px 0 2px'
}

// The chevron reads as a secondary affordance next to the label, not a second action.
const chevronStyle: CSSProperties = { display: 'inline-flex', opacity: 0.5 }

const menuStyle: CSSProperties = {
  position: 'absolute',
  top: 'calc(100% + 6px)',
  right: 0,
  minWidth: '200px',
  maxWidth: '280px',
  border: 'none',
  borderRadius: '10px',
  background: 'var(--glass-surface-strong)',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  padding: '6px',
  zIndex: 80
}

function menuItemStyle(highlighted: boolean, disabled: boolean): CSSProperties {
  return {
    width: '100%',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: '8px',
    border: 'none',
    borderRadius: '8px',
    padding: '8px 10px',
    background: highlighted && !disabled ? 'var(--bg-tertiary)' : 'transparent',
    color: disabled ? 'var(--text-tertiary)' : 'var(--text-primary)',
    cursor: disabled ? 'default' : 'pointer',
    textAlign: 'left',
    fontSize: '12.5px'
  }
}

const menuItemBodyStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '8px',
  minWidth: 0
}

const menuIconStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '16px',
  height: '16px',
  flexShrink: 0,
  color: 'currentColor',
  opacity: 0.75
}

const menuLabelStyle: CSSProperties = {
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const selectedDotStyle: CSSProperties = {
  width: '7px',
  height: '7px',
  borderRadius: '999px',
  background: 'var(--accent)',
  flexShrink: 0
}
