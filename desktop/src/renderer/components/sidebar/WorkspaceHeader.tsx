import { useState, useEffect, useRef } from 'react'
import { createPortal } from 'react-dom'
import { ChevronRight, MoreHorizontal } from 'lucide-react'
import { stripWorkspaceLockedIpcPrefix } from '../../../shared/workspaceSwitchErrors'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { useConfirmDialog } from '../ui/ConfirmDialog'

/** Extracts a clean user-facing message from a workspace switch error. */
function switchErrorMessage(err: unknown): string {
  const raw = err instanceof Error ? err.message : String(err)
  // Strip the Electron IPC prefix "Error invoking remote method '...': Error: ..."
  const match = raw.match(/Error invoking remote method '[^']+': Error: (.+)/)
  const inner = match ? match[1] : raw
  return stripWorkspaceLockedIpcPrefix(inner)
}

interface RecentWorkspace {
  path: string
  name: string
  lastOpenedAt: string
}

interface WorkspaceHeaderProps {
  workspaceName: string
  workspacePath: string
}

export function WorkspaceHeader({
  workspaceName,
  workspacePath
}: WorkspaceHeaderProps): JSX.Element {
  return (
    <div
      style={{
        display: 'flex',
        alignItems: 'center',
        padding: '5px 8px 5px 14px',
        flexShrink: 0,
        minHeight: '32px'
      }}
    >
      <ActionTooltip label={workspacePath} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flex: 1 }}>
        <span
          style={{
            flex: 1,
            fontSize: 'var(--type-secondary-size)',
            lineHeight: 'var(--type-secondary-line-height)',
            fontWeight: 'var(--type-ui-emphasis-weight)',
            color: 'var(--text-secondary)',
            overflow: 'hidden',
            textOverflow: 'ellipsis',
            whiteSpace: 'nowrap',
            display: 'block'
          }}
        >
          {workspaceName || 'DotCraft'}
        </span>
      </ActionTooltip>
    </div>
  )
}

interface WorkspaceOptionsMenuProps {
  workspacePath: string
  localWorkspacePath?: string
  localActionsDisabled?: boolean
  onOpenChange?: (open: boolean) => void
}

export function WorkspaceOptionsMenu({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled = false,
  onOpenChange
}: WorkspaceOptionsMenuProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [open, setOpen] = useState(false)
  const [recents, setRecents] = useState<RecentWorkspace[]>([])
  const [showRecents, setShowRecents] = useState(false)
  const ref = useRef<HTMLDivElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [menuPosition, setMenuPosition] = useState<{ top: number; left: number; width: number } | null>(null)

  function updateOpen(next: boolean): void {
    setOpen(next)
    onOpenChange?.(next)
  }

  function updateMenuPosition(): void {
    const rect = ref.current?.getBoundingClientRect()
    if (!rect) return
    const viewportWidth = window.innerWidth || 320
    const menuWidth = 220
    // Left-aligned to the trigger so the menu opens downward-right from the button.
    const left = Math.max(8, Math.min(rect.left, viewportWidth - menuWidth - 8))
    setMenuPosition({
      top: rect.bottom + 4,
      left,
      width: menuWidth
    })
  }

  useEffect(() => {
    if (!open) return
    window.api.workspace.getRecent().then((list) => {
      setRecents(list.filter((r) => r.path !== workspacePath))
    }).catch(() => {})
    updateMenuPosition()

    function handleClick(e: MouseEvent): void {
      const target = e.target as Node
      if (ref.current?.contains(target) || menuRef.current?.contains(target)) return
      updateOpen(false)
      setShowRecents(false)
    }
    function handlePositionChange(): void {
      updateMenuPosition()
    }
    document.addEventListener('mousedown', handleClick)
    window.addEventListener('resize', handlePositionChange)
    window.addEventListener('scroll', handlePositionChange, true)
    return () => {
      document.removeEventListener('mousedown', handleClick)
      window.removeEventListener('resize', handlePositionChange)
      window.removeEventListener('scroll', handlePositionChange, true)
    }
  }, [open, workspacePath])

  function openInExplorer(): void {
    if (localActionsDisabled) return
    updateOpen(false)
    void window.api.shell.openPath(localWorkspacePath || workspacePath)
  }

  async function switchWorkspace(): Promise<void> {
    updateOpen(false)
    try {
      await window.api.workspace.clearSelection()
    } catch (err) {
      window.alert(switchErrorMessage(err))
    }
  }

  async function switchToRecent(path: string): Promise<void> {
    updateOpen(false)
    setShowRecents(false)
    try {
      await window.api.workspace.switch(path)
    } catch (err) {
      window.alert(switchErrorMessage(err))
    }
  }

  async function clearRecentWorkspaces(): Promise<void> {
    const confirmed = await confirm({
      title: t('workspaceHeader.clearRecentConfirmTitle'),
      message: t('workspaceHeader.clearRecentConfirmMessage'),
      confirmLabel: t('workspaceHeader.clearRecentConfirmAction'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    try {
      await window.api.workspace.clearRecent()
      setRecents([])
      setShowRecents(false)
    } catch (err) {
      window.alert(err instanceof Error ? err.message : String(err))
    }
  }

  return (
    <div
      ref={ref}
      style={{
        position: 'relative',
        display: 'inline-flex',
        alignItems: 'center'
      }}
    >
      <IconButton
        icon={<MoreHorizontal size={16} aria-hidden />}
        label={t('workspaceHeader.optionsAria')}
        tooltipLabel={t('workspaceHeader.optionsAria')}
        tooltipPlacement="bottom"
        size={24}
        radius={4}
        className="dc-thread-list-icon-button"
        aria-expanded={open}
        onClick={(e) => {
          e.stopPropagation()
          if (!open) updateMenuPosition()
          updateOpen(!open)
          if (open) setShowRecents(false)
        }}
      />

      {open && menuPosition && typeof document !== 'undefined' && createPortal(
        <div
          ref={menuRef}
          style={{
            position: 'fixed',
            top: menuPosition.top,
            left: menuPosition.left,
            width: menuPosition.width,
            backgroundColor: 'var(--glass-surface-strong)',
            border: 'none',
            borderRadius: '10px',
            boxShadow: 'var(--glass-shadow-soft)',
            backdropFilter: 'var(--glass-blur)',
            WebkitBackdropFilter: 'var(--glass-blur)',
            zIndex: 1000,
            padding: '6px',
            overflow: 'visible'
          }}
          onClick={(e) => e.stopPropagation()}
        >
          <ActionTooltip label={workspacePath} wrapperStyle={{ display: 'block', minWidth: 0, overflow: 'hidden', flexShrink: 1 }}>
          <div
            style={{
              padding: '7px 10px',
              fontSize: 'var(--type-secondary-size)',
              lineHeight: 'var(--type-secondary-line-height)',
              color: 'var(--text-dimmed)',
              background: 'var(--bg-tertiary)',
              borderRadius: '8px',
              marginBottom: '6px',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              display: 'block'
            }}
          >
            {workspacePath}
          </div>
          </ActionTooltip>
          <DropdownItem
            label={t('workspaceHeader.openInExplorer')}
            onClick={openInExplorer}
            disabled={localActionsDisabled}
          />
          <DropdownItem label={t('workspaceHeader.switchWorkspace')} onClick={() => { void switchWorkspace() }} />
          <div
            style={{ position: 'relative' }}
            onMouseEnter={() => setShowRecents(true)}
            onMouseLeave={() => setShowRecents(false)}
          >
            <DropdownItem
              label={t('workspaceHeader.recentWorkspaces')}
              disabled={recents.length === 0}
              hasSubmenu={recents.length > 0}
            />
            {showRecents && recents.length > 0 && (
              <div
                style={{
                  position: 'absolute',
                  top: 0,
                  left: '100%',
                  backgroundColor: 'var(--glass-surface-strong)',
                  border: 'none',
                  borderRadius: '10px',
                  boxShadow: 'var(--glass-shadow-soft)',
                  backdropFilter: 'var(--glass-blur)',
                  WebkitBackdropFilter: 'var(--glass-blur)',
                  zIndex: 1001,
                  padding: '6px',
                  minWidth: '220px',
                  maxWidth: '320px',
                  maxHeight: '300px',
                  overflowY: 'auto'
                }}
              >
                {recents.map((r) => (
                  <ActionTooltip key={r.path} label={r.path} wrapperStyle={{ display: 'block', width: '100%' }}>
                    <button
                      onClick={() => { void switchToRecent(r.path) }}
                      style={{
                        display: 'flex',
                        flexDirection: 'column',
                        alignItems: 'flex-start',
                        width: '100%',
                        padding: '7px 14px',
                        border: 'none',
                        background: 'transparent',
                        color: 'var(--text-primary)',
                        cursor: 'pointer',
                        textAlign: 'left',
                        gap: '1px'
                      }}
                      onMouseEnter={(e) => {
                        (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--sidebar-control-hover)'
                      }}
                      onMouseLeave={(e) => {
                        (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
                      }}
                    >
                      <span style={{
                        fontSize: 'var(--type-ui-size)',
                        lineHeight: 'var(--type-ui-line-height)',
                        fontWeight: 'var(--type-ui-emphasis-weight)',
                        whiteSpace: 'nowrap'
                      }}>{r.name}</span>
                      <span
                        style={{
                          fontSize: 'var(--type-secondary-size)',
                          lineHeight: 'var(--type-secondary-line-height)',
                          color: 'var(--text-dimmed)',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                          maxWidth: '280px'
                        }}
                      >
                        {r.path}
                      </span>
                    </button>
                  </ActionTooltip>
                ))}
                <div
                  style={{
                    height: '1px',
                    backgroundColor: 'color-mix(in srgb, var(--text-primary) 9%, transparent)',
                    margin: '6px 8px'
                  }}
                />
                <button
                  onClick={() => { void clearRecentWorkspaces() }}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    width: '100%',
                    padding: '7px 14px',
                    border: 'none',
                    background: 'transparent',
                    color: 'var(--text-primary)',
                    cursor: 'pointer',
                    textAlign: 'left'
                  }}
                  onMouseEnter={(e) => {
                    (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--sidebar-control-hover)'
                  }}
                  onMouseLeave={(e) => {
                    (e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
                  }}
                >
                  {t('workspaceHeader.clearRecentWorkspaces')}
                </button>
              </div>
            )}
          </div>
        </div>
        , document.body
      )}
    </div>
  )
}

interface DropdownItemProps {
  label: string
  onClick?: () => void
  disabled?: boolean
  hasSubmenu?: boolean
}

function DropdownItem({ label, onClick, disabled = false, hasSubmenu = false }: DropdownItemProps): JSX.Element {
  return (
    <div
      onClick={disabled ? undefined : onClick}
      style={{
        padding: '7px 14px',
        fontSize: 'var(--type-ui-size)',
        lineHeight: 'var(--type-ui-line-height)',
        color: disabled ? 'var(--text-dimmed)' : 'var(--text-primary)',
        cursor: disabled ? 'default' : 'pointer',
        transition: 'background-color 100ms ease',
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center'
      }}
      onMouseEnter={(e) => {
        if (!disabled) {
          ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'var(--sidebar-control-hover)'
        }
      }}
      onMouseLeave={(e) => {
        ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
      }}
    >
      <span>{label}</span>
      {hasSubmenu && (
        <ChevronRight
          size={14}
          strokeWidth={2}
          aria-hidden
          style={{ color: 'var(--text-dimmed)', flexShrink: 0, marginLeft: 8 }}
        />
      )}
    </div>
  )
}
