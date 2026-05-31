/**
 * The viewer header "…" overflow menu.
 *
 * Actions: Copy path, Copy file contents, and (text viewers only) Toggle word
 * wrap. Rendered through a portal so the menu is never clipped by the viewer
 * body's `overflow: hidden`, mirroring `ReferencePathContextMenu`'s styling.
 */
import { useEffect, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { Copy, FileText, MoreHorizontal, WrapText } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { ActionTooltip } from '../ui/ActionTooltip'

interface ViewerActionsMenuProps {
  absolutePath: string
  /** Whether the active viewer is the text editor (controls word-wrap item). */
  isText: boolean
  wordWrap: boolean
  onToggleWordWrap: () => void
}

const MENU_WIDTH = 232

export function ViewerActionsMenu({
  absolutePath,
  isText,
  wordWrap,
  onToggleWordWrap
}: ViewerActionsMenuProps): JSX.Element {
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

  async function copyPath(): Promise<void> {
    try {
      await navigator.clipboard.writeText(absolutePath)
      addToast(t('toast.copied'), 'success')
    } catch {
      addToast(t('viewer.actionFailed'), 'warning')
    }
  }

  async function copyContents(): Promise<void> {
    try {
      const result = await window.api.workspace.viewer.readText({ absolutePath })
      await navigator.clipboard.writeText(result.text)
      addToast(t('toast.copied'), 'success')
    } catch {
      addToast(t('viewer.copyContentsFailed'), 'warning')
    }
  }

  const menu = open && anchor ? createPortal(
    <div
      ref={menuRef}
      role="menu"
      style={{ ...menuStyle, top: anchor.top, right: anchor.right, width: MENU_WIDTH }}
      onContextMenu={(event) => event.preventDefault()}
    >
      <MenuItem
        label={t('viewer.copyPath')}
        icon={<Copy size={15} />}
        onClick={() => { void copyPath(); setOpen(false) }}
      />
      <MenuItem
        label={t('viewer.copyContents')}
        icon={<FileText size={15} />}
        onClick={() => { void copyContents(); setOpen(false) }}
      />
      {isText && (
        <MenuItem
          label={wordWrap ? t('viewer.disableWordWrap') : t('viewer.enableWordWrap')}
          icon={<WrapText size={15} />}
          onClick={() => { onToggleWordWrap(); setOpen(false) }}
        />
      )}
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
      style={{ ...menuItemStyle, background: hovered ? 'var(--glass-surface-soft)' : 'transparent' }}
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
  border: '1px solid var(--glass-border)',
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
