/**
 * The Changes header "…" overflow menu.
 *
 * Mirrors `ViewerActionsMenu` styling. Holds the view preferences that don't
 * warrant a dedicated toolbar button: word-wrap toggle and expand / collapse all
 * file diffs. Rendered through a portal so the menu is never clipped by the
 * panel body's `overflow: hidden`.
 */
import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronsDownUp, ChevronsUpDown, MoreHorizontal, WrapText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ChangesActionsMenuProps {
  wordWrap: boolean
  onToggleWordWrap: () => void
  onExpandAll: () => void
  onCollapseAll: () => void
}

const MENU_WIDTH = 220

export function ChangesActionsMenu({
  wordWrap,
  onToggleWordWrap,
  onExpandAll,
  onCollapseAll
}: ChangesActionsMenuProps): JSX.Element {
  const t = useT()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [anchor, setAnchor] = useState<{ top: number; right: number } | null>(null)

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || buttonRef.current?.contains(target)) return
      setOpen(false)
    }
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') setOpen(false)
    }
    document.addEventListener('mousedown', handlePointerDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [open])

  function toggleOpen(): void {
    const rect = buttonRef.current?.getBoundingClientRect()
    if (rect) {
      setAnchor({ top: rect.bottom + 4, right: window.innerWidth - rect.right })
    }
    setOpen((current) => !current)
  }

  const menu = open && anchor ? createPortal(
    <div
      ref={menuRef}
      role="menu"
      style={{ ...menuStyle, top: anchor.top, right: anchor.right, width: MENU_WIDTH }}
      onContextMenu={(event) => event.preventDefault()}
    >
      <MenuItem
        label={wordWrap ? t('viewer.disableWordWrap') : t('viewer.enableWordWrap')}
        icon={<WrapText size={15} />}
        onClick={() => { onToggleWordWrap(); setOpen(false) }}
      />
      <MenuItem
        label={t('changes.expandAll')}
        icon={<ChevronsUpDown size={15} />}
        onClick={() => { onExpandAll(); setOpen(false) }}
      />
      <MenuItem
        label={t('changes.collapseAll')}
        icon={<ChevronsDownUp size={15} />}
        onClick={() => { onCollapseAll(); setOpen(false) }}
      />
    </div>,
    document.body
  ) : null

  return (
    <>
      <ActionTooltip label={t('viewer.moreActions')} placement="bottom">
        <button
          ref={buttonRef}
          type="button"
          aria-label={t('viewer.moreActions')}
          aria-haspopup="menu"
          aria-expanded={open}
          onClick={toggleOpen}
          style={{ ...iconButtonStyle, background: open ? 'var(--bg-tertiary)' : 'transparent' }}
          onMouseEnter={(e) => { if (!open) (e.currentTarget as HTMLButtonElement).style.background = 'var(--bg-tertiary)' }}
          onMouseLeave={(e) => { if (!open) (e.currentTarget as HTMLButtonElement).style.background = 'transparent' }}
        >
          <MoreHorizontal size={16} aria-hidden style={{ display: 'block' }} />
        </button>
      </ActionTooltip>
      {menu}
    </>
  )
}

function MenuItem({
  label,
  icon,
  onClick
}: {
  label: string
  icon: ReactNode
  onClick: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      type="button"
      role="menuitem"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onClick={onClick}
      style={{ ...menuItemStyle, background: hovered ? 'var(--bg-tertiary)' : 'transparent' }}
    >
      <span style={menuItemIconStyle}>{icon}</span>
      <span style={menuItemLabelStyle}>{label}</span>
    </button>
  )
}

const iconButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '28px',
  height: '28px',
  padding: 0,
  border: 'none',
  borderRadius: '6px',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  flexShrink: 0,
  transition: 'background-color 100ms ease'
}

const menuStyle: CSSProperties = {
  position: 'fixed',
  border: 'none',
  borderRadius: '10px',
  background: 'var(--glass-surface-strong)',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  padding: '6px',
  zIndex: 9999
}

const menuItemStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '10px',
  width: '100%',
  minHeight: 32,
  padding: '7px 10px',
  border: 'none',
  borderRadius: '7px',
  color: 'var(--text-primary)',
  background: 'transparent',
  cursor: 'pointer',
  fontSize: '13px',
  lineHeight: 1.25,
  textAlign: 'left'
}

const menuItemIconStyle: CSSProperties = {
  width: 16,
  height: 16,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
  color: 'var(--text-secondary)'
}

const menuItemLabelStyle: CSSProperties = {
  minWidth: 0,
  flex: 1,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}
