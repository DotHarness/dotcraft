import { useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { ChevronsUpDown, MessageCircle } from 'lucide-react'

import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { menuStyle } from '../conversation/composerFooterPrimitives'
import {
  projectIcon,
  projectIdentity,
  projectLabel,
  useWorkspaceProjectChoices,
  WorkspaceProjectMenuItems
} from '../conversation/workspaceProjectMenu'
import { useAddProjectFlow } from '../projects/AddProject'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'
import { IconButton } from '../ui/IconButton'
import { Spinner } from '../ui/Spinner'

const MENU_GAP = 6
const MENU_HEIGHT_ESTIMATE = 300

export function SettingsWorkspacePicker({ collapsed = false }: { collapsed?: boolean }): JSX.Element | null {
  const t = useT()
  const workspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)
  const choices = useWorkspaceProjectChoices(workspacePath)
  const addProject = useAddProjectFlow()
  const anchorRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [switchingTo, setSwitchingTo] = useState<string | null>(null)

  if (!workspacePath.trim()) return null

  const selectProject = async (project: WorkspaceProjectSummary): Promise<void> => {
    setOpen(false)
    if (projectIdentity(project) === choices.selectedProjectId) return
    const label = project === choices.chat ? t('chatsRail.title') : projectLabel(project)
    setSwitchingTo(label)
    try {
      await window.api.workspace.switch(project.path)
    } catch {
      addToast(t('settings.workspace.switchFailed', { name: label }), 'error')
    } finally {
      setSwitchingTo(null)
    }
  }

  const name = switchingTo
    ?? (choices.foregroundIsChat
      ? t('chatsRail.title')
      : choices.selectedProject ? projectLabel(choices.selectedProject) : '')
  const icon = switchingTo
    ? <Spinner size={14} />
    : choices.foregroundIsChat || !choices.selectedProject
      ? <MessageCircle size={14} strokeWidth={1.8} aria-hidden />
      : projectIcon(choices.selectedProject)
  const switchLabel = `${t('settings.workspace.switch')}: ${name}`

  return (
    <div ref={anchorRef}>
      {collapsed ? (
        <IconButton
          size={32}
          radius={8}
          className="dc-sidebar-icon-button"
          label={switchLabel}
          tooltipLabel={name}
          tooltipPlacement="right"
          active={open}
          disabled={switchingTo !== null}
          onClick={() => setOpen((current) => !current)}
          icon={icon}
        />
      ) : (
        <button
          type="button"
          className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
          aria-label={switchLabel}
          aria-haspopup="menu"
          aria-expanded={open}
          aria-busy={switchingTo !== null}
          disabled={switchingTo !== null}
          title={name}
          onClick={() => setOpen((current) => !current)}
          style={rowStyle}
        >
          <span style={SIDEBAR_NAV_ICON_SLOT}>{icon}</span>
          <span style={nameStyle}>{name}</span>
          <ChevronsUpDown size={14} strokeWidth={1.8} aria-hidden style={chevronStyle} />
        </button>
      )}
      {open && (
        <WorkspaceMenu anchorRef={anchorRef} side={collapsed ? 'right' : 'below'} onClose={() => setOpen(false)}>
          <WorkspaceProjectMenuItems
            choices={choices}
            addBusy={addProject.busy}
            onSelect={(project) => { void selectProject(project) }}
            onAddProject={() => {
              setOpen(false)
              addProject.beginCreate()
            }}
          />
        </WorkspaceMenu>
      )}
      {addProject.dialog}
    </div>
  )
}

function WorkspaceMenu({
  anchorRef,
  side,
  onClose,
  children
}: {
  anchorRef: { current: HTMLElement | null }
  side: 'below' | 'right'
  onClose: () => void
  children: JSX.Element
}): JSX.Element | null {
  const menuRef = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState<CSSProperties | null>(null)

  useLayoutEffect(() => {
    const anchor = anchorRef.current?.getBoundingClientRect()
    if (!anchor) return
    if (side === 'right') {
      setPosition({ left: anchor.right + MENU_GAP, top: anchor.top, bottom: 'auto' })
    } else if (window.innerHeight - anchor.bottom < MENU_HEIGHT_ESTIMATE) {
      setPosition({ left: anchor.left + 4, bottom: window.innerHeight - anchor.top + MENU_GAP })
    } else {
      setPosition({ left: anchor.left + 4, top: anchor.bottom + MENU_GAP, bottom: 'auto' })
    }
  }, [anchorRef, side])

  useEffect(() => {
    const onPointerDown = (event: MouseEvent): void => {
      const target = event.target as Node
      if (!menuRef.current?.contains(target) && !anchorRef.current?.contains(target)) onClose()
    }
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [anchorRef, onClose])

  if (!position) return null
  return createPortal(
    <div ref={menuRef} style={{ ...menuStyle, position: 'fixed', ...position }}>
      {children}
    </div>,
    document.body
  )
}

const rowStyle: CSSProperties = {
  ...SIDEBAR_NAV_ROW_OUTER,
  ...SIDEBAR_NAV_BORDER_INACTIVE,
  color: 'var(--text-primary)'
}

const nameStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const chevronStyle: CSSProperties = {
  flexShrink: 0,
  color: 'var(--text-dimmed)'
}
