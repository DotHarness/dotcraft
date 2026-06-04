import { useEffect, useMemo, useRef, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ChevronRight, Copy, ExternalLink, FolderOpen } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import {
  EDITOR_ICON_SIZE,
  listEditorsCached,
  placeExplorerFirst,
  renderEditorIcon,
  type EditorId,
  type EditorInfo
} from '../../utils/editorTargets'
import type { ContextMenuPosition } from '../ui/ContextMenu'

interface ReferencePathContextMenuProps {
  position: ContextMenuPosition
  targetPath: string
  onClose: () => void
}

const menuWidth = 248
const submenuWidth = 224
const menuPadding = 6
const menuGap = 6

export function ReferencePathContextMenu({
  position,
  targetPath,
  onClose
}: ReferencePathContextMenuProps): JSX.Element {
  const t = useT()
  const menuRef = useRef<HTMLDivElement>(null)
  const submenuRef = useRef<HTMLDivElement>(null)
  const [editors, setEditors] = useState<EditorInfo[]>([])
  const [lastOpenEditorId, setLastOpenEditorId] = useState<EditorId | undefined>(undefined)
  const [submenuOpen, setSubmenuOpen] = useState(false)

  const orderedEditors = useMemo(
    () => placeExplorerFirst(editors),
    [editors]
  )
  const resolvedLastOpenId = useMemo<EditorId>(() => {
    if (lastOpenEditorId && orderedEditors.some((entry) => entry.id === lastOpenEditorId)) {
      return lastOpenEditorId
    }
    return 'explorer'
  }, [lastOpenEditorId, orderedEditors])
  const primaryEditor = useMemo(() => {
    return orderedEditors.find((entry) => entry.id === resolvedLastOpenId)
      ?? orderedEditors[0]
      ?? { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
  }, [orderedEditors, resolvedLastOpenId])
  const primaryAppLabel = t(primaryEditor.labelKey)

  const estimatedHeight = 5 * 32 + menuPadding * 2 + 7
  const left = clamp(position.x, 8, window.innerWidth - menuWidth - 8)
  const top = clamp(position.y, 8, window.innerHeight - estimatedHeight - 8)
  const submenuLeft = left + menuWidth + menuGap + submenuWidth <= window.innerWidth - 8
    ? left + menuWidth + menuGap
    : Math.max(8, left - submenuWidth - menuGap)
  const submenuTop = clamp(top + 32, 8, window.innerHeight - (orderedEditors.length + 1) * 32 - menuPadding * 2 - 8)

  useEffect(() => {
    let cancelled = false
    window.api.settings.get()
      .then((settings) => {
        if (!cancelled) setLastOpenEditorId(settings.lastOpenEditorId)
      })
      .catch(() => {})
    void listEditorsCached()
      .then((entries) => {
        if (!cancelled) setEditors(entries)
      })
      .catch(() => {})
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    function handleMouseDown(event: MouseEvent): void {
      const target = event.target as Node
      if (menuRef.current?.contains(target) || submenuRef.current?.contains(target)) return
      onClose()
    }
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', handleMouseDown)
    document.addEventListener('keydown', handleKeyDown)
    return () => {
      document.removeEventListener('mousedown', handleMouseDown)
      document.removeEventListener('keydown', handleKeyDown)
    }
  }, [onClose])

  async function launchWithEditor(editor: EditorInfo): Promise<void> {
    try {
      if (editor.id === 'explorer') {
        await window.api.shell.revealLocalPath(targetPath)
      } else {
        await window.api.shell.launchLocalPathInEditor(editor.id, targetPath)
      }
      await window.api.settings.set({ lastOpenEditorId: editor.id })
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  async function openDefaultApp(): Promise<void> {
    try {
      await window.api.shell.openLocalPath(targetPath)
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  async function revealInExplorer(): Promise<void> {
    try {
      await window.api.shell.revealLocalPath(targetPath)
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  async function copyPath(): Promise<void> {
    try {
      await navigator.clipboard.writeText(targetPath)
      addToast(t('toast.copied'), 'success')
    } catch {
      addToast(t('conversation.reference.openFailed'), 'warning')
    }
  }

  function handlePrimary(): void {
    void launchWithEditor(primaryEditor)
  }

  const menu = (
    <>
      <div
        ref={menuRef}
        role="menu"
        style={{ ...menuStyle, left, top, width: menuWidth }}
        onContextMenu={(event) => event.preventDefault()}
      >
        <MenuButton
          label={t('threadHeader.openIn', { app: primaryAppLabel })}
          icon={renderEditorIcon(primaryEditor, EDITOR_ICON_SIZE)}
          onClick={handlePrimary}
          onClose={onClose}
        />
        <MenuButton
          label={t('conversation.reference.openWith')}
          icon={<ExternalLink size={16} />}
          trailing={<ChevronRight size={15} />}
          onMouseEnter={() => setSubmenuOpen(true)}
          onFocus={() => setSubmenuOpen(true)}
          onClick={() => setSubmenuOpen((current) => !current)}
        />
        <div style={dividerStyle} />
        <MenuButton
          label={t('conversation.reference.copyPath')}
          icon={<Copy size={16} />}
          onClick={() => { void copyPath() }}
          onClose={onClose}
        />
        <MenuButton
          label={t('conversation.reference.openInExplorer')}
          icon={<FolderOpen size={16} />}
          onClick={() => { void revealInExplorer() }}
          onClose={onClose}
        />
      </div>

      {submenuOpen && (
        <div
          ref={submenuRef}
          role="menu"
          style={{ ...menuStyle, left: submenuLeft, top: submenuTop, width: submenuWidth }}
          onMouseEnter={() => setSubmenuOpen(true)}
          onContextMenu={(event) => event.preventDefault()}
        >
          {orderedEditors.map((editor) => (
            <MenuButton
              key={editor.id}
              label={t(editor.labelKey)}
              icon={renderEditorIcon(editor, EDITOR_ICON_SIZE)}
              onClick={() => { void launchWithEditor(editor) }}
              onClose={onClose}
            />
          ))}
          {orderedEditors.length > 0 && <div style={dividerStyle} />}
          <MenuButton
            label={t('conversation.reference.defaultApp')}
            icon={<ExternalLink size={16} />}
            onClick={() => { void openDefaultApp() }}
            onClose={onClose}
          />
        </div>
      )}
    </>
  )

  return createPortal(menu, document.body) as JSX.Element
}

function MenuButton({
  label,
  icon,
  trailing,
  onClick,
  onClose,
  onMouseEnter,
  onFocus
}: {
  label: string
  icon?: ReactNode
  trailing?: ReactNode
  onClick: () => void
  onClose?: () => void
  onMouseEnter?: () => void
  onFocus?: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      type="button"
      role="menuitem"
      onMouseEnter={() => {
        setHovered(true)
        onMouseEnter?.()
      }}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => {
        setHovered(true)
        onFocus?.()
      }}
      onBlur={() => setHovered(false)}
      onClick={() => {
        onClick()
        onClose?.()
      }}
      style={{
        ...menuItemStyle,
        background: hovered ? 'var(--bg-tertiary)' : 'transparent'
      }}
    >
      <span style={menuItemIconStyle}>{icon}</span>
      <span style={menuItemLabelStyle}>{label}</span>
      {trailing && <span style={menuItemTrailingStyle}>{trailing}</span>}
    </button>
  )
}

function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(value, max))
}

const menuStyle: CSSProperties = {
  position: 'fixed',
  border: 'none',
  borderRadius: '10px',
  background: 'var(--glass-surface-strong)',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  padding: `${menuPadding}px`,
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
  width: EDITOR_ICON_SIZE,
  height: EDITOR_ICON_SIZE,
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

const menuItemTrailingStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0,
  color: 'var(--text-secondary)'
}

const dividerStyle: CSSProperties = {
  height: 1,
  margin: '6px -6px',
  background: 'var(--glass-border)'
}
