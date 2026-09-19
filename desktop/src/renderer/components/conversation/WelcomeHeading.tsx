import { useCallback, useEffect, useLayoutEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useAddProjectFlow } from '../projects/AddProject'
import { menuStyle } from './composerFooterPrimitives'
import {
  WorkspaceProjectMenuItems,
  projectLabel,
  useWorkspaceProjectChoices
} from './workspaceProjectMenu'

const PROJECT_PLACEHOLDER = '{{project}}'

export function WelcomeHeading({
  workspacePath,
  onSelectWorkspace
}: {
  workspacePath: string
  onSelectWorkspace: (workspacePath: string) => Promise<void> | void
}): JSX.Element {
  const t = useT()
  const choices = useWorkspaceProjectChoices(workspacePath)
  const addProject = useAddProjectFlow()
  const [open, setOpen] = useState(false)
  const [flipped, setFlipped] = useState(false)
  const anchorRef = useRef<HTMLSpanElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)

  const close = useCallback(() => setOpen(false), [])

  useEffect(() => {
    if (!open) return
    function closeOnOutsideClick(event: MouseEvent): void {
      if (!anchorRef.current?.contains(event.target as Node)) close()
    }
    function closeOnEscape(event: KeyboardEvent): void {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('mousedown', closeOnOutsideClick)
    document.addEventListener('keydown', closeOnEscape)
    return () => {
      document.removeEventListener('mousedown', closeOnOutsideClick)
      document.removeEventListener('keydown', closeOnEscape)
    }
  }, [close, open])

  // Opening upward keeps the composer visible; a short window has no room for that.
  useLayoutEffect(() => {
    if (!open) return
    const anchor = anchorRef.current
    const menu = menuRef.current
    if (!anchor || !menu) return
    setFlipped(anchor.getBoundingClientRect().top < menu.offsetHeight + 16)
  }, [open, choices.projects.length])

  const name = choices.foregroundIsChat || !choices.selectedProject
    ? ''
    : projectLabel(choices.selectedProject)
  const template = name ? t('welcome.heroTitleInProject') : ''
  const split = template.indexOf(PROJECT_PLACEHOLDER)

  if (split < 0) {
    return <h1 className="welcome-heading">{t('welcome.heroTitle')}</h1>
  }

  const placement: CSSProperties = {
    left: '50%',
    transform: 'translateX(-50%)',
    ...(flipped ? { top: 'calc(100% + 6px)', bottom: 'auto' } : {})
  }

  return (
    <>
      <h1 className="welcome-heading">
        {template.slice(0, split)}
        <span className="welcome-heading-anchor" ref={anchorRef}>
          <button
            type="button"
            className="welcome-heading-project"
            aria-haspopup="menu"
            aria-expanded={open}
            aria-label={t('welcome.changeProjectAria', { project: name })}
            onClick={() => setOpen((current) => !current)}
          >
            {name}
          </button>
          {open && (
            <div ref={menuRef} className="welcome-heading-menu" style={{ ...menuStyle, ...placement }}>
              <WorkspaceProjectMenuItems
                choices={choices}
                addBusy={addProject.busy}
                onSelect={(project) => {
                  close()
                  if (project.kind === 'remote') return
                  void onSelectWorkspace(project.path)
                }}
                onAddProject={() => {
                  close()
                  addProject.beginCreate()
                }}
              />
            </div>
          )}
        </span>
        {template.slice(split + PROJECT_PLACEHOLDER.length)}
      </h1>
      {addProject.dialog}
    </>
  )
}
