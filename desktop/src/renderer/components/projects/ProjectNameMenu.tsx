import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type JSX, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useT } from '../../contexts/LocaleContext'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { menuStyle } from '../conversation/composerFooterPrimitives'
import {
  WorkspaceProjectMenuItems,
  projectLabel,
  useWorkspaceProjectChoices,
  workspaceSlug
} from '../conversation/workspaceProjectMenu'
import { useAddProjectFlow } from './AddProject'

const NAME_PLACEHOLDER = /\{\{\s*(?:name|project)\s*\}\}/

export function ProjectNameMenu({
  workspacePath,
  name,
  placement,
  onSelect
}: {
  workspacePath: string
  name: string
  placement: 'above' | 'below'
  onSelect: (project: WorkspaceProjectSummary) => void
}): JSX.Element {
  const t = useT()
  const choices = useWorkspaceProjectChoices(workspacePath)
  const addProject = useAddProjectFlow()
  const [open, setOpen] = useState(false)
  const [position, setPosition] = useState<CSSProperties>({ visibility: 'hidden' })
  const anchorRef = useRef<HTMLSpanElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const close = useCallback(() => setOpen(false), [])

  useEffect(() => {
    if (!open) return
    function closeOnOutsideClick(event: MouseEvent): void {
      const target = event.target as Node
      if (!anchorRef.current?.contains(target) && !menuRef.current?.contains(target)) close()
    }
    function closeOnEscape(event: KeyboardEvent): void {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('mousedown', closeOnOutsideClick)
    document.addEventListener('keydown', closeOnEscape)
    window.addEventListener('resize', close)
    document.addEventListener('scroll', close, true)
    return () => {
      document.removeEventListener('mousedown', closeOnOutsideClick)
      document.removeEventListener('keydown', closeOnEscape)
      window.removeEventListener('resize', close)
      document.removeEventListener('scroll', close, true)
    }
  }, [close, open])

  // The menu is portalled so a clipping heading or scroll area cannot cut it off.
  useLayoutEffect(() => {
    if (!open) {
      setPosition({ visibility: 'hidden' })
      return
    }
    const anchor = anchorRef.current?.getBoundingClientRect()
    const menu = menuRef.current
    if (!anchor || !menu) return
    const gap = 6
    const height = menu.offsetHeight
    const roomAbove = anchor.top - gap >= height + 10
    const roomBelow = window.innerHeight - anchor.bottom - gap >= height + 10
    const below = placement === 'below' ? roomBelow || !roomAbove : !roomAbove
    const left = placement === 'above'
      ? anchor.left + anchor.width / 2 - menu.offsetWidth / 2
      : anchor.left
    setPosition({
      top: below ? anchor.bottom + gap : anchor.top - gap - height,
      left: Math.max(8, Math.min(left, window.innerWidth - menu.offsetWidth - 8))
    })
  }, [open, placement, choices.projects.length])

  return (
    <span className="project-name-menu" ref={anchorRef}>
      <button
        type="button"
        className="project-name-menu-trigger"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-label={t('welcome.changeProjectAria', { project: name })}
        onClick={() => setOpen((current) => !current)}
      >
        {name}
      </button>
      {open && createPortal(
        <div
          ref={menuRef}
          className="project-name-menu-popover"
          style={{ ...menuStyle, position: 'fixed', bottom: 'auto', ...position }}
        >
          <WorkspaceProjectMenuItems
            choices={choices}
            addBusy={addProject.busy}
            onSelect={(project) => {
              close()
              onSelect(project)
            }}
            onAddProject={() => {
              close()
              addProject.beginCreate()
            }}
          />
        </div>,
        document.body
      )}
      {addProject.dialog}
    </span>
  )
}

export function titleWithName(template: string, name: ReactNode): ReactNode {
  const match = NAME_PLACEHOLDER.exec(template)
  if (!match) return template
  return (
    <>
      {template.slice(0, match.index)}
      {name}
      {template.slice(match.index + match[0].length)}
    </>
  )
}

export function ForegroundWorkspaceName(): JSX.Element {
  const t = useT()
  const workspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)
  const { selectedProject, foregroundIsChat } = useWorkspaceProjectChoices(workspacePath)
  const name = foregroundIsChat
    ? t('chatsRail.title')
    : selectedProject ? projectLabel(selectedProject) : workspaceSlug(workspacePath)
  return (
    <ProjectNameMenu
      workspacePath={workspacePath}
      name={name}
      placement="below"
      onSelect={(project) => {
        if (project.kind === 'remote' || project.path === workspacePath) return
        void window.api.workspace.switch(project.path)
      }}
    />
  )
}
