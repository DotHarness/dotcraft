import { useCallback, useEffect, useMemo, useRef, useState, type JSX } from 'react'
import type { ProjectEnvironmentEntry, ProjectEnvironmentList } from '../../../../../shared/projectActions'
import { useT } from '../../../../contexts/LocaleContext'
import { returnToAppNavigationLocation, type AppNavigationLocation } from '../../../../stores/appNavigationStore'
import { showToast } from '../../../../stores/toastStore'
import { useUIStore } from '../../../../stores/uiStore'
import { localProjectsByLastOpened, useWorkspaceProjectsStore } from '../../../../stores/workspaceProjectsStore'
import { useConfirmDialog } from '../../../ui/ConfirmDialog'
import { Button } from '../../../ui/Button'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import { SettingsPageHeader } from '../../SettingsPageHeader'
import { SettingsPanelSkeleton } from '../../SettingsSkeleton'
import { SegmentedControl } from '../../ui/SegmentedControl'
import { EnvironmentEditor } from './EnvironmentEditor'
import { EnvironmentProjectList } from './EnvironmentProjectList'
import { projectFolderName } from './environmentPresentation'
import { EnvironmentView } from './EnvironmentView'
import { useProjectEnvironmentLists } from './useProjectEnvironmentLists'
import { WorktreeGeneralTab } from './WorktreeGeneralTab'
import styles from './environments.module.css'

type Segment = 'general' | 'environments'

interface EnvironmentRef {
  id?: string
  path?: string
}

type EnvironmentPage =
  | { kind: 'list' }
  | { kind: 'view'; projectPath: string; environment: EnvironmentRef | null }
  | { kind: 'edit'; projectPath: string; path: string | null; create: boolean; first: boolean; environment: EnvironmentRef | null }

interface ReturnTarget {
  location: AppNavigationLocation
  page: 'view' | 'edit'
}

function resolveEnvironment(list: ProjectEnvironmentList, ref: EnvironmentRef | null): ProjectEnvironmentEntry | null {
  const environments = list.environments
  const match = ref && environments.find((environment) => (ref.path && environment.path === ref.path) || (ref.id && environment.id === ref.id))
  return match || environments.find((environment) => environment.isDefault) || environments[0] || null
}

function ownCount(list: ProjectEnvironmentList | undefined): number {
  return list ? list.environments.filter((environment) => !environment.inherited).length : 0
}

export function WorktreesPanel({ workspacePath }: { workspacePath?: string }): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [segment, setSegment] = useState<Segment>('general')
  const [page, setPage] = useState<EnvironmentPage>({ kind: 'list' })
  const [returnTarget, setReturnTarget] = useState<ReturnTarget | null>(null)
  const dirty = useRef(false)
  const target = useUIStore((state) => state.environmentSettingsTarget)
  const allProjects = useWorkspaceProjectsStore((state) => state.projects)
  const projects = useMemo(() => localProjectsByLastOpened(allProjects), [allProjects])
  const pagePath = page.kind === 'list' ? null : page.projectPath
  const paths = useMemo(() => {
    const result = projects.map((project) => project.path)
    if (pagePath && !result.includes(pagePath)) result.push(pagePath)
    return result
  }, [pagePath, projects])
  const { lists, revision, refresh } = useProjectEnvironmentLists(paths)
  const pageList = pagePath ? lists[pagePath] : undefined
  const loadedList = typeof pageList === 'object' ? pageList : undefined

  useEffect(() => {
    if (!target) return
    setSegment('environments')
    setReturnTarget(target.returnTo ? { location: target.returnTo, page: target.create ? 'edit' : 'view' } : null)
    const environment = target.environmentId ? { id: target.environmentId } : null
    setPage(target.create
      ? { kind: 'edit', projectPath: target.projectPath, path: null, create: true, first: true, environment: null }
      : { kind: 'view', projectPath: target.projectPath, environment })
    useUIStore.getState().clearEnvironmentSettingsTarget()
  }, [target])

  useEffect(() => {
    if (page.kind !== 'edit' || page.path !== null || !loadedList) return
    setPage({ ...page, path: loadedList.newEnvironmentPath, first: ownCount(loadedList) === 0 })
  }, [loadedList, page])

  const onDirtyChange = useCallback((value: boolean) => {
    dirty.current = value
  }, [])

  const confirmLeave = useCallback(async (): Promise<boolean> => {
    if (!dirty.current) return true
    const ok = await confirm({
      title: t('settings.environments.discardConfirm.title'),
      message: t('settings.environments.discardConfirm.message'),
      confirmLabel: t('settings.environments.discardConfirm.confirm'),
      danger: true
    })
    if (ok) dirty.current = false
    return ok
  }, [confirm, t])

  async function navigate(next: EnvironmentPage): Promise<void> {
    if (!(await confirmLeave())) return
    setPage(next)
  }

  async function returnToCaller(skipConfirm = false): Promise<void> {
    if (!returnTarget) return
    if (!skipConfirm && !(await confirmLeave())) return
    const location = returnTarget.location
    setReturnTarget(null)
    setPage({ kind: 'list' })
    returnToAppNavigationLocation(location)
  }

  function startEdit(projectPath: string, environment: ProjectEnvironmentEntry): void {
    void navigate({ kind: 'edit', projectPath, path: environment.path, create: false, first: false, environment: { path: environment.path } })
  }

  function startCreate(projectPath: string, list: ProjectEnvironmentList | undefined): void {
    void navigate({
      kind: 'edit',
      projectPath,
      path: list?.newEnvironmentPath ?? null,
      create: true,
      first: ownCount(list) === 0,
      environment: null
    })
  }

  async function handleSaved(projectPath: string, path: string, create: boolean, first: boolean): Promise<void> {
    dirty.current = false
    const list = await refresh(projectPath)
    const saved = list?.environments.find((environment) => environment.path === path)
    if (first && saved) {
      await window.api.projectActions.select({ workspacePath: projectPath, environmentId: saved.id }).catch(() => undefined)
    }
    showToast({ message: t('settings.environments.saved'), type: 'success' })
    if (create && returnTarget?.page === 'edit') {
      await returnToCaller(true)
      return
    }
    setPage({ kind: 'view', projectPath, environment: { path } })
  }

  if (page.kind === 'list') {
    return (
      <div className={styles.page}>
        <SettingsPageHeader title={t('settings.tab.worktrees')} description={t('settings.worktrees.description')} />
        <div>
          <SegmentedControl<Segment>
            ariaLabel={t('settings.tab.worktrees')}
            value={segment}
            options={[
              { value: 'general', label: t('settings.worktrees.tab.general') },
              { value: 'environments', label: t('settings.worktrees.tab.environments') }
            ]}
            onChange={setSegment}
          />
        </div>
        {segment === 'general' ? <WorktreeGeneralTab workspacePath={workspacePath} /> : (
          <EnvironmentProjectList
            projects={projects}
            lists={lists}
            onOpenProject={(project) => setPage({ kind: 'view', projectPath: project.path, environment: null })}
            onOpenEnvironment={(project, environment) =>
              setPage({ kind: 'view', projectPath: project.path, environment: { id: environment.id, path: environment.path } })}
            onAddEnvironment={(project) => {
              const list = lists[project.path]
              startCreate(project.path, typeof list === 'object' ? list : undefined)
            }}
          />
        )}
      </div>
    )
  }

  const projectPath = page.projectPath
  const projectName = projects.find((project) => project.path === projectPath)?.name ?? projectFolderName(projectPath)
  const environment = loadedList ? resolveEnvironment(loadedList, page.environment) : null
  const viewPage: EnvironmentPage = { kind: 'view', projectPath, environment: page.environment }
  const callerPage = returnTarget?.page === page.kind

  let breadcrumb: JSX.Element
  if (page.kind === 'view') {
    breadcrumb = callerPage ? (
      <SettingsBreadcrumb returnsToCaller parentLabel="" currentLabel={projectName} onBack={() => void returnToCaller()} />
    ) : (
      <SettingsBreadcrumb parentLabel={t('settings.worktrees.tab.environments')} currentLabel={projectName} onBack={() => setPage({ kind: 'list' })} />
    )
  } else {
    const currentLabel = page.create ? t('settings.environments.new') : t('settings.environments.edit')
    breadcrumb = callerPage ? (
      <SettingsBreadcrumb returnsToCaller parentLabel="" currentLabel={currentLabel} onBack={() => void returnToCaller()} />
    ) : returnTarget ? (
      <SettingsBreadcrumb returnsToCaller parentLabel="" currentLabel={currentLabel} onBack={() => void navigate(viewPage)} />
    ) : (
      <SettingsBreadcrumb
        parentLabel={t('settings.worktrees.tab.environments')}
        trail={[{ label: projectName, onClick: () => void navigate(viewPage) }]}
        currentLabel={currentLabel}
        onBack={() => void navigate({ kind: 'list' })}
      />
    )
  }

  let content: JSX.Element
  if (pageList === 'failed') {
    content = <div className={styles.emptyState}>{t('settings.environments.loadFailed')}</div>
  } else if (page.kind === 'edit') {
    content = page.path === null ? <SettingsPanelSkeleton /> : (
      <EnvironmentEditor
        key={page.path}
        projectPath={projectPath}
        path={page.path}
        fallbackName={page.create ? projectFolderName(projectPath) : environment?.name ?? projectFolderName(projectPath)}
        onDirtyChange={onDirtyChange}
        onSaved={(path) => void handleSaved(projectPath, path, page.create, page.first)}
      />
    )
  } else if (!loadedList) {
    content = <SettingsPanelSkeleton />
  } else if (!environment) {
    content = (
      <div className={styles.emptyState}>
        <span>{t('settings.environments.empty')}</span>
        <Button variant="primary" onClick={() => startCreate(projectPath, loadedList)}>
          {t('settings.environments.create')}
        </Button>
      </div>
    )
  } else {
    content = (
      <EnvironmentView
        projectPath={projectPath}
        environment={environment}
        revision={revision}
        onEdit={() => startEdit(projectPath, environment)}
      />
    )
  }

  return (
    <div className={styles.page}>
      {breadcrumb}
      {content}
    </div>
  )
}
