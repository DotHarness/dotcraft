import { useState, useEffect, useRef } from 'react'
import { ChevronRight } from 'lucide-react'
import { stripWorkspaceLockedIpcPrefix } from '../../../shared/workspaceSwitchErrors'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
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
  localWorkspacePath?: string
  localActionsDisabled?: boolean
}

/**
 * Compact workspace identity row shown below the LogoHeader.
 * Displays the workspace name with a subtle "···" overflow button that opens
 * the workspace menu (Open in Explorer, Switch Workspace, Recent Workspaces).
 * Spec §9.2.
 */
export function WorkspaceHeader({
  workspaceName,
  workspacePath,
  localWorkspacePath,
  localActionsDisabled = false
}: WorkspaceHeaderProps): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [open, setOpen] = useState(false)
  const [recents, setRecents] = useState<RecentWorkspace[]>([])
  const [showRecents, setShowRecents] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (!open) return
    window.api.workspace.getRecent().then((list) => {
      setRecents(list.filter((r) => r.path !== workspacePath))
    }).catch(() => {})

    function handleClick(e: MouseEvent): void {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false)
        setShowRecents(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [open, workspacePath])

  function openInExplorer(): void {
    if (localActionsDisabled) return
    setOpen(false)
    void window.api.shell.openPath(localWorkspacePath || workspacePath)
  }

  async function switchWorkspace(): Promise<void> {
    setOpen(false)
    try {
      await window.api.workspace.clearSelection()
    } catch (err) {
      window.alert(switchErrorMessage(err))
    }
  }

  async function switchToRecent(path: string): Promise<void> {
    setOpen(false)
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
        display: 'flex',
        alignItems: 'center',
        padding: '5px 8px 5px 14px',
        flexShrink: 0,
        gap: '4px',
        minHeight: '32px'
      }}
    >
      {/* Workspace name */}
      <span
        style={{
          flex: 1,
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)',
          color: 'var(--text-secondary)',
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap'
        }}
        title={workspacePath}
      >
        {workspaceName || 'DotCraft'}
      </span>

      {/* Overflow menu trigger */}
      <ActionTooltip label={t('workspaceHeader.optionsAria')} placement="bottom">
      <button
        onClick={(e) => { e.stopPropagation(); setOpen((v) => !v); if (open) setShowRecents(false) }}
        aria-label={t('workspaceHeader.optionsAria')}
        style={{
          flexShrink: 0,
          background: 'transparent',
          border: 'none',
          color: open ? 'var(--text-primary)' : 'var(--text-dimmed)',
          cursor: 'pointer',
          padding: '2px 4px',
          borderRadius: '4px',
          fontSize: 'var(--type-ui-size)',
          lineHeight: 'var(--type-ui-line-height)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center'
        }}
        onMouseEnter={(e) => {
          ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-primary)'
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--sidebar-control-hover)'
        }}
        onMouseLeave={(e) => {
          if (!open) {
            ;(e.currentTarget as HTMLButtonElement).style.color = 'var(--text-dimmed)'
          }
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'transparent'
        }}
      >
        ···
      </button>
      </ActionTooltip>

      {/* Dropdown */}
      {open && (
        <div
          style={{
            position: 'absolute',
            top: '100%',
            left: '8px',
            right: '8px',
            backgroundColor: 'var(--bg-secondary)',
            border: '1px solid var(--border-default)',
            borderRadius: '6px',
            boxShadow: 'var(--shadow-level-2)',
            zIndex: 100,
            overflow: 'visible'
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {/* Workspace path shown in menu header */}
          <div
            style={{
              padding: '6px 14px',
              fontSize: 'var(--type-secondary-size)',
              lineHeight: 'var(--type-secondary-line-height)',
              color: 'var(--text-dimmed)',
              borderBottom: '1px solid var(--border-default)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap'
            }}
            title={workspacePath}
          >
            {workspacePath}
          </div>
          <DropdownItem
            label={t('workspaceHeader.openInExplorer')}
            onClick={openInExplorer}
            disabled={localActionsDisabled}
          />
          <DropdownItem label={t('workspaceHeader.switchWorkspace')} onClick={() => { void switchWorkspace() }} />
          {/* Recent workspaces submenu */}
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
                  backgroundColor: 'var(--bg-secondary)',
                  border: '1px solid var(--border-default)',
                  borderRadius: '6px',
                  boxShadow: 'var(--shadow-level-2)',
                  zIndex: 101,
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
                    backgroundColor: 'var(--border-default)',
                    margin: '4px 0'
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
