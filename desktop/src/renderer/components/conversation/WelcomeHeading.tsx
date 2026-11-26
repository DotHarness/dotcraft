import type { JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { ProjectNameMenu, titleWithName } from '../projects/ProjectNameMenu'
import { projectLabel, useWorkspaceProjectChoices } from './workspaceProjectMenu'

export function WelcomeHeading({
  workspacePath,
  onSelectWorkspace
}: {
  workspacePath: string
  onSelectWorkspace: (workspacePath: string) => Promise<void> | void
}): JSX.Element {
  const t = useT()
  const choices = useWorkspaceProjectChoices(workspacePath)
  const name = choices.foregroundIsChat || !choices.selectedProject
    ? ''
    : projectLabel(choices.selectedProject)

  if (!name) {
    return <h1 className="welcome-heading">{t('welcome.heroTitle')}</h1>
  }

  return (
    <h1 className="welcome-heading">
      {titleWithName(t('welcome.heroTitleInProject'), (
        <ProjectNameMenu
          workspacePath={workspacePath}
          name={name}
          placement="above"
          onSelect={(project) => {
            if (project.kind === 'remote') return
            void onSelectWorkspace(project.path)
          }}
        />
      ))}
    </h1>
  )
}
