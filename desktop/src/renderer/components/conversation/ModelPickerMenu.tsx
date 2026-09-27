import type { CSSProperties, JSX, MouseEvent as ReactMouseEvent } from 'react'
import { Check, ChevronRight } from 'lucide-react'

export type SecondaryMenu = 'provider' | 'model'
export const MODEL_MENU_WIDTH = 310
export const PROVIDER_MENU_WIDTH = 280

export function MainMenuRow({
  label,
  value,
  highlighted,
  submenu,
  onHover,
  onClick
}: {
  label: string
  value: string
  highlighted: boolean
  submenu: SecondaryMenu
  onHover: (event: ReactMouseEvent<HTMLButtonElement>) => void
  onClick: (event: ReactMouseEvent<HTMLButtonElement>) => void
}): JSX.Element {
  return (
    <button
      type="button"
      role="menuitem"
      aria-haspopup="listbox"
      data-main-action
      data-submenu={submenu}
      onMouseEnter={onHover}
      onMouseMove={onHover}
      onClick={onClick}
      style={mainMenuRowStyle(highlighted)}
    >
      <span style={mainLabelStyle}>{label}</span>
      <span style={mainValueStyle}>{value}</span>
      <span style={trailingSlotStyle} aria-hidden>
        <ChevronRight size={15} strokeWidth={1.7} />
      </span>
    </button>
  )
}

export function OptionRow({
  selected,
  highlighted,
  label,
  description,
  onHover,
  onSelect
}: {
  selected: boolean
  highlighted: boolean
  label: string
  description?: string
  onHover?: () => void
  onSelect?: () => void
}): JSX.Element {
  return (
    <button
      type="button"
      role="option"
      aria-selected={selected}
      data-submenu-option
      onMouseEnter={onHover}
      onFocus={onHover}
      onClick={onSelect}
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: '10px',
        width: '100%',
        minHeight: '35px',
        padding: '7px 9px',
        border: 'none',
        borderRadius: '7px',
        background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
        color: highlighted || selected ? 'var(--text-primary)' : 'var(--text-secondary)',
        cursor: 'pointer',
        textAlign: 'left'
      }}
    >
      <span style={{ display: 'flex', minWidth: 0, flex: 1, flexDirection: 'column', gap: '2px' }}>
        <span style={{ ...ellipsisStyle, fontSize: '12px' }}>{label}</span>
        {description && (
          <small style={{ color: 'var(--text-dimmed)', fontSize: '10px', lineHeight: 1.3 }}>
            {description}
          </small>
        )}
      </span>
      <Check
        aria-hidden
        size={15}
        strokeWidth={2}
        style={{ flexShrink: 0, color: 'var(--text-primary)', opacity: selected ? 1 : 0 }}
      />
    </button>
  )
}

export const ellipsisStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const mainLabelStyle: CSSProperties = {
  ...ellipsisStyle,
  fontSize: '13px'
}

const mainValueStyle: CSSProperties = {
  ...ellipsisStyle,
  color: 'var(--text-secondary)',
  textAlign: 'right',
  fontSize: '12px'
}

const trailingSlotStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  justifySelf: 'end',
  width: '32px',
  height: '100%'
}

function mainMenuRowStyle(highlighted: boolean): CSSProperties {
  return {
    display: 'grid',
    gridTemplateColumns: 'minmax(82px, 1fr) minmax(0, 110px) 32px',
    alignItems: 'center',
    gap: 0,
    width: '100%',
    minHeight: '40px',
    padding: '0 4px 0 9px',
    border: 'none',
    borderRadius: '8px',
    background: highlighted ? 'var(--bg-tertiary)' : 'transparent',
    color: 'var(--text-primary)',
    cursor: 'pointer',
    textAlign: 'left'
  }
}

export function submenuStyle(kind: SecondaryMenu, top: number, opensLeft: boolean, maxHeight: number): CSSProperties {
  const width = kind === 'provider' ? PROVIDER_MENU_WIDTH : MODEL_MENU_WIDTH
  return {
    position: 'absolute',
    top,
    left: opensLeft ? `calc(-${width}px + 1px)` : 'calc(100% - 1px)',
    zIndex: 72,
    width,
    boxSizing: 'border-box',
    maxHeight: `${maxHeight}px`,
    padding: '6px',
    overflowX: 'hidden',
    overflowY: 'auto',
    borderTop: 'none',
    borderRight: opensLeft ? '1px solid var(--glass-border)' : 'none',
    borderBottom: 'none',
    borderLeft: opensLeft ? 'none' : '1px solid var(--glass-border)',
    borderRadius: '10px',
    background: 'var(--glass-surface-strong)',
    boxShadow: 'var(--glass-shadow-soft)',
    backdropFilter: 'var(--glass-blur)',
    WebkitBackdropFilter: 'var(--glass-blur)'
  }
}
