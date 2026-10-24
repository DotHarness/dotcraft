import { Folder, Plus, TriangleAlert } from 'lucide-react'
import { useState, type JSX } from 'react'
import type { ProjectEnvironmentEntry } from '../../../../../shared/projectActions'
import type { WorkspaceProjectSummary } from '../../../../../shared/workspaceProjects'
import { useT } from '../../../../contexts/LocaleContext'
import { useAddProjectFlow } from '../../../projects/AddProject'
import { Button } from '../../../ui/Button'
import { DisclosureChevron } from '../../../ui/DisclosureChevron'
import { IconButton } from '../../../ui/IconButton'
import { SettingsGroup } from '../../SettingsGroup'
import type { ProjectEnvironmentListState } from './useProjectEnvironmentLists'
import styles from './environments.module.css'

function EnvironmentRow({ environment, onOpen }: {
  environment: ProjectEnvironmentEntry
  onOpen: () => void
}): JSX.Element {
  const t = useT()
  const meta = environment.inherited
    ? t('settings.environments.inheritedFrom', { folder: environment.folder })
    : environment.fileName !== environment.name ? environment.fileName : null
  return (
    <div className={`${styles.row} ${styles.nested}`}>
      <button type="button" className={styles.rowButton} onClick={onOpen}>
        <span className={styles.rowText}>
          <span className={styles.rowTitle}>{environment.name}</span>
          {meta && <span className={styles.rowMeta}>{meta}</span>}
        </span>
        {environment.error !== undefined && (
          <span className={styles.attention}>
            <TriangleAlert size={13} aria-hidden />
            {t('settings.environments.needsAttention')}
          </span>
        )}
      </button>
    </div>
  )
}

function ProjectRows({ project, list, onOpenProject, onOpenEnvironment, onAddEnvironment }: {
  project: WorkspaceProjectSummary
  list: ProjectEnvironmentListState | undefined
  onOpenProject: () => void
  onOpenEnvironment: (environment: ProjectEnvironmentEntry) => void
  onAddEnvironment: () => void
}): JSX.Element {
  const t = useT()
  const [inheritedOpen, setInheritedOpen] = useState(false)
  const environments = typeof list === 'object' ? list.environments : []
  const own = environments.filter((environment) => !environment.inherited)
  const inherited = environments.filter((environment) => environment.inherited)
  const addLabel = t('settings.environments.addEnvironment', { project: project.name })
  return (
    <>
      <div className={styles.row}>
        <button type="button" className={styles.rowButton} onClick={onOpenProject}>
          <span className={styles.rowGlyph}><Folder size={16} aria-hidden /></span>
          <span className={styles.rowText}>
            <span className={styles.rowTitle}>{project.name}</span>
            {list === 'failed' && <span className={styles.rowMeta}>{t('settings.environments.loadFailed')}</span>}
          </span>
        </button>
        <IconButton
          icon={<Plus size={15} aria-hidden />}
          label={addLabel}
          tooltipLabel={addLabel}
          disabled={typeof list !== 'object'}
          onClick={onAddEnvironment}
        />
      </div>
      {own.map((environment) => (
        <EnvironmentRow key={environment.id} environment={environment} onOpen={() => onOpenEnvironment(environment)} />
      ))}
      {inherited.length > 0 && (
        <div className={`${styles.row} ${styles.nested}`}>
          <button
            type="button"
            className={styles.rowButton}
            aria-expanded={inheritedOpen}
            onClick={() => setInheritedOpen((open) => !open)}
          >
            <span className={styles.rowText}>
              <span className={styles.rowMeta}>{t('settings.environments.inherited', { count: inherited.length })}</span>
            </span>
            <span className={styles.rowGlyph}><DisclosureChevron expanded={inheritedOpen} /></span>
          </button>
        </div>
      )}
      {inheritedOpen && inherited.map((environment) => (
        <EnvironmentRow key={environment.id} environment={environment} onOpen={() => onOpenEnvironment(environment)} />
      ))}
    </>
  )
}

export function EnvironmentProjectList({ projects, lists, onOpenProject, onOpenEnvironment, onAddEnvironment }: {
  projects: readonly WorkspaceProjectSummary[]
  lists: Readonly<Record<string, ProjectEnvironmentListState>>
  onOpenProject: (project: WorkspaceProjectSummary) => void
  onOpenEnvironment: (project: WorkspaceProjectSummary, environment: ProjectEnvironmentEntry) => void
  onAddEnvironment: (project: WorkspaceProjectSummary) => void
}): JSX.Element {
  const t = useT()
  const addProject = useAddProjectFlow()
  return (
    <>
      <SettingsGroup
        title={t('settings.environments.projects')}
        headerAction={(
          <Button
            size="sm"
            variant="secondary"
            iconLeft={<Plus size={15} aria-hidden />}
            disabled={addProject.busy}
            onClick={() => addProject.beginCreate()}
          >
            {t('settings.environments.addProject')}
          </Button>
        )}
      >
        {projects.length === 0 ? (
          <div className={styles.empty}>{t('settings.environments.noProjects')}</div>
        ) : projects.map((project) => (
          <ProjectRows
            key={project.path}
            project={project}
            list={lists[project.path]}
            onOpenProject={() => onOpenProject(project)}
            onOpenEnvironment={(environment) => onOpenEnvironment(project, environment)}
            onAddEnvironment={() => onAddEnvironment(project)}
          />
        ))}
      </SettingsGroup>
      {addProject.dialog}
    </>
  )
}
