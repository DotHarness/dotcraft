import { useState, type CSSProperties, type KeyboardEvent } from 'react'
import {
  AlertCircle,
  Archive,
  CircleDashed,
  ExternalLink,
  Folder,
  FolderOpen,
  LogOut,
  Pin,
  Server,
  Settings,
  SquarePen,
  Trash2
} from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { ThreadSummary } from '../../types/thread'
import { Skeleton } from '../ui/Skeleton'
import { Spinner } from '../ui/Spinner'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { MoreActionsButton } from '../ui/MoreActionsButton'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../ui/ContextMenu'
import { archiveProjectThreads } from '../../utils/archiveProjectThreads'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { ProjectGlyph } from './ProjectGlyph'
import { useAddProjectFlow } from '../projects/AddProject'
import { SIDEBAR_ROW_MIN_HEIGHT } from './sidebarNavRowStyles'
import { SidebarEntryDetailsCard } from './SidebarEntryDetailsCard'
import { SidebarEntryDetailsActionRow } from './SidebarEntryDetailsActionRow'
import { canOpenSshProject, deviceProjectRef, sshProjectRef, useSshProjectMachine } from './sshRemoteProject'
import { newProjectChat, openProjectConnection } from './projectConnectionActions'
import { usePairedComputersStore } from '../../stores/pairedComputersStore'
import { useSshMachinesStore } from '../../stores/sshMachinesStore'
import { machineStatusView } from '../settings/panels/ssh/sshMachinePresentation'
import { isRemoteProject, isThreadRunning, isThreadWaiting, projectIdentity } from './projectThreads'
import { useMoveToSectionMenuEntry } from './sections/moveToSectionMenu'
import { useSidebarSectionDrag } from './sections/sidebarSectionDrag'

export function getProjectActivity(threads: ThreadSummary[]): ProjectActivity {
  if (threads.some(isThreadRunning)) return 'running'
  if (threads.some(isThreadWaiting)) return 'waiting'
  return null
}

export type ProjectActivity = 'running' | 'waiting' | null

export function ProjectHeader({
  project,
  active,
  collapsed,
  activity,
  cold,
  detailThreads,
  archivableThreads,
  live,
  onToggle
}: {
  project: WorkspaceProjectSummary
  active: boolean
  collapsed: boolean
  activity: ProjectActivity
  cold: boolean
  detailThreads: ThreadSummary[]
  archivableThreads: ThreadSummary[]
  live: boolean
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const menuOpen = menuPosition != null
  const addProject = useAddProjectFlow()
  const label = project.name || project.path
  const detailLabel = project.remote?.displayPath || project.remote?.endpoint || project.identityWorkspacePath || project.path
  const sshRef = sshProjectRef(project)
  const deviceRef = deviceProjectRef(project)
  const savedRemote = sshRef != null || deviceRef != null
  const sshMachine = useSshProjectMachine(sshRef)
  const sshUnreachable = cold && sshMachine?.status.kind === 'failed'
  const errorLabel = (sshUnreachable ? sshMachine?.status.message : project.errorMessage) || t('projectsRail.error')
  const showErrorIndicator = project.state === 'error' || sshUnreachable
  const actionColumnWidth = showErrorIndicator ? '86px' : '60px'
  const waitingCount = detailThreads.filter(isThreadWaiting).length
  const runningCount = detailThreads.filter((thread) => !isThreadWaiting(thread) && isThreadRunning(thread)).length
  const threadCount = detailThreads.length
  const detailsLoaded = project.loaded || project.state === 'foreground' || project.state === 'secondary'
  const detailFolders = isRemoteProject(project) ? [] : projectFolderPaths(project)

  async function toggleProjectPinned(): Promise<void> {
    const projectId = projectIdentity(project)
    if (!projectId) return
    const settings = await window.api.settings.get()
    const current = Array.isArray(settings.pinnedProjectIds)
      ? settings.pinnedProjectIds.filter((value): value is string => typeof value === 'string')
      : []
    const normalized = current.filter((value) => normalizeWorkspaceProjectKey(value) !== projectId)
    const next = project.pinned ? normalized : [...normalized, projectId]
    await window.api.settings.set({ pinnedProjectIds: next })
  }

  const projectDetailsContent = (
    <>
      <div className="sidebar-entry-details-header">
        <span className="sidebar-entry-details-title" title={label}>{label}</span>
        <IconButton
          icon={<Pin size={14} fill={project.pinned ? 'currentColor' : 'none'} aria-hidden />}
          label={project.pinned ? t('projectsRail.unpinProject') : t('projectsRail.pinProject')}
          size={24}
          radius={8}
          className="dc-thread-list-icon-button"
          aria-pressed={project.pinned === true}
          style={{ borderRadius: 'var(--sidebar-icon-control-radius)' }}
          onClick={() => { void toggleProjectPinned() }}
        />
      </div>
      {project.state === 'connecting' ? (
        <div className="sidebar-entry-details-row" aria-busy="true" aria-label={t('projectsRail.loadingDetails')}>
          <CircleDashed size={14} strokeWidth={1.8} aria-hidden />
          <Skeleton width={124} height={10} />
        </div>
      ) : detailsLoaded ? (
        <div className="sidebar-entry-details-row">
          <CircleDashed size={14} strokeWidth={1.8} aria-hidden />
          <span>
            {t(threadCount === 1 ? 'projectsRail.threadCountOne' : 'projectsRail.threadCountMany', { count: threadCount })}
            {waitingCount > 0 ? ` · ${t('projectsRail.waitingCount', { count: waitingCount })}` : ''}
            {runningCount > 0 ? ` · ${t('projectsRail.runningCount', { count: runningCount })}` : ''}
          </span>
        </div>
      ) : (
        <div className="sidebar-entry-details-row">
          <CircleDashed size={14} strokeWidth={1.8} aria-hidden />
          <span>{t('projectsRail.detailsNotLoaded')}</span>
        </div>
      )}
      <div className="sidebar-entry-details-divider" />
      {isRemoteProject(project) ? (
        <>
          <div className="sidebar-entry-details-row">
            <Folder size={14} strokeWidth={1.8} aria-hidden />
            <span title={detailLabel}>{detailLabel}</span>
          </div>
          {sshMachine && (
            <div className="sidebar-entry-details-row">
              <Server size={14} strokeWidth={1.8} aria-hidden />
              <span>{`${sshMachine.name} · ${machineStatusView(t, sshMachine).label}`}</span>
            </div>
          )}
        </>
      ) : (
        <>
          {detailFolders.map((folder) => (
            <SidebarEntryDetailsActionRow
              key={folder}
              icon={<Folder size={14} strokeWidth={1.8} aria-hidden />}
              label={folder}
              title={folder}
              ariaLabel={`${t('workspaceHeader.openInExplorer')}: ${folder}`}
              affordance
              onClick={() => { void window.api.shell.openPath(folder) }}
            />
          ))}
          <div className="sidebar-entry-details-divider" />
          <SidebarEntryDetailsActionRow
            icon={<Settings size={14} strokeWidth={1.8} aria-hidden />}
            label={t('projectsRail.editProject')}
            ariaLabel={t('projectsRail.editProject')}
            onClick={() => addProject.beginEdit(project, active)}
          />
        </>
      )}
    </>
  )

  async function removeProject(): Promise<void> {
    if (active) return
    if (isRemoteProject(project) && !savedRemote) return
    const confirmed = await confirm({
      title: t('projectsRail.removeProjectTitle', { project: label }),
      message: savedRemote
        ? t('projectsRail.removeRemoteProjectMessage', { machine: sshMachine?.name ?? project.remote?.serverName ?? '' })
        : t('projectsRail.removeProjectMessage'),
      confirmLabel: t('projectsRail.removeProjectConfirm'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    if (deviceRef) {
      await usePairedComputersStore.getState().removeProject(deviceRef.computerId, deviceRef.projectId)
      return
    }
    if (sshRef) {
      await useSshMachinesStore.getState().removeProject(sshRef.machineId, sshRef.projectId)
      return
    }
    await window.api.workspace.removeProject(project.path)
  }

  const remote = isRemoteProject(project)
  const projectKey = projectIdentity(project)
  const sectionItem = projectKey ? { kind: 'project' as const, projectKey } : null
  const beforeSectionMove = project.pinned ? toggleProjectPinned : undefined
  const moveToSection = useMoveToSectionMenuEntry(sectionItem, beforeSectionMove)
  const sectionDrag = useSidebarSectionDrag(sectionItem, beforeSectionMove)
  const menuItems: ContextMenuEntry[] = [
    ...(!active && (!remote || savedRemote)
      ? [{
          label: t('projectsRail.openProject'),
          icon: <ExternalLink size={14} aria-hidden />,
          disabled: !canOpenSshProject(sshMachine),
          onClick: () => { void openProjectConnection(project, active) }
        }]
      : []),
    {
      label: project.pinned ? t('projectsRail.unpinProject') : t('projectsRail.pinProject'),
      icon: <Pin size={14} fill={project.pinned ? 'currentColor' : 'none'} aria-hidden />,
      onClick: () => { void toggleProjectPinned() }
    },
    ...(moveToSection ? [moveToSection] : []),
    ...(!remote
      ? [
          {
            label: t('projectsRail.editProject'),
            icon: <Settings size={14} aria-hidden />,
            onClick: () => addProject.beginEdit(project, active)
          },
          { type: 'separator' as const },
          {
            label: t('workspaceHeader.openInExplorer'),
            icon: <FolderOpen size={14} aria-hidden />,
            onClick: () => { void window.api.shell.openPath(project.path) }
          }
        ]
      : []),
    { type: 'separator' },
    {
      label: t('projectsRail.archiveChats'),
      icon: <Archive size={14} aria-hidden />,
      disabled: archivableThreads.length === 0 || !project.loaded || (project.state !== 'foreground' && project.state !== 'secondary'),
      onClick: () => {
        void archiveProjectThreads({
          projectLabel: label, workspacePath: project.path, connectionKey: projectIdentity(project),
          threads: archivableThreads, live, t
        })
      }
    },
    { type: 'separator' },
    remote && !(savedRemote && cold)
      ? {
          label: t('projectsRail.disconnectRemote'),
          icon: <LogOut size={14} aria-hidden />,
          danger: true,
          onClick: () => { void window.api.workspace.disconnectRemote(projectIdentity(project)) }
        }
      : {
          label: t('projectsRail.removeProject'),
          icon: <Trash2 size={14} aria-hidden />,
          disabled: active,
          onClick: () => { void removeProject() }
        }
  ]

  function handlePrimaryAction(): void {
    if (cold) return
    onToggle()
  }

  function handleDoubleClick(): void {
    if (!cold) return
    void openProjectConnection(project, active)
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    if (cold) void openProjectConnection(project, active)
    else onToggle()
  }

  return (
    <>
    <SidebarEntryDetailsCard
      label={label}
      width={320}
      interactive
      disabled={menuOpen || sectionDrag.dragging}
      content={projectDetailsContent}
      wrapperStyle={{ width: '100%' }}
    >
    <div
      className="dotcraft-sidebar-row-radius dc-project-row dc-sidebar-section-source"
      {...sectionDrag.sourceProps}
      data-section-dragging={sectionDrag.dragging || undefined}
      data-menu-open={menuOpen || undefined}
      role="button"
      tabIndex={0}
      aria-expanded={cold ? undefined : !collapsed}
      aria-current={active ? 'true' : undefined}
      aria-label={label}
      onClick={handlePrimaryAction}
      onDoubleClick={handleDoubleClick}
      onKeyDown={handleKeyDown}
      onContextMenu={(event) => {
        event.preventDefault()
        setMenuPosition({ x: event.clientX, y: event.clientY })
      }}
      style={{
        position: 'relative',
        display: 'grid',
        gridTemplateColumns: `var(--sidebar-leading-slot-width) minmax(0, 1fr) ${actionColumnWidth}`,
        alignItems: 'center',
        gap: 'var(--sidebar-leading-gap)',
        minHeight: SIDEBAR_ROW_MIN_HEIGHT,
        margin: '2px 4px',
        padding: '2px 6px 2px 12px',
        borderRadius: 'var(--sidebar-row-radius)',
        cursor: 'pointer',
        userSelect: 'none'
      }}
    >
      <ProjectGlyph project={project} collapsed={collapsed} cold={cold} />
      <div style={{ minWidth: 0, display: 'flex', alignItems: 'center', gap: '4px' }}>
        <span
            style={{
              minWidth: 0,
              color: cold ? 'var(--text-secondary)' : 'var(--text-primary)',
              fontSize: 'var(--type-ui-size)',
              lineHeight: 'var(--type-ui-line-height)',
              fontWeight: 'var(--type-ui-emphasis-weight)',
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
              display: 'block'
            }}
          >
            {label}
          </span>
        {!cold && (
          <span className="dc-project-row__chevron" aria-hidden>
            <DisclosureChevron expanded={!collapsed} />
          </span>
        )}
      </div>
      <div
        style={{
          width: actionColumnWidth,
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'flex-end',
          gap: '2px'
        }}
        onClick={(event) => event.stopPropagation()}
      >
        <span className="dc-project-row__actions">
            <IconButton
              icon={<SquarePen size={14} aria-hidden />}
              label={t('projectsRail.newChat')}
              tooltipLabel={t('projectsRail.newChat')}
              size={24}
              radius={6}
              className="dc-thread-list-icon-button"
              onClick={() => { void newProjectChat(project, active) }}
            />
            <MoreActionsButton
              label={t('projectsRail.moreActions')}
              size={24}
              radius={6}
              iconSize={15}
              tooltipPlacement="top"
              className="dc-thread-list-icon-button"
              open={menuOpen}
              onMouseDown={(event) => { if (menuOpen) event.stopPropagation() }}
              onClick={(event) => {
                if (menuOpen) {
                  setMenuPosition(null)
                  return
                }
                const rect = event.currentTarget.getBoundingClientRect()
                setMenuPosition({ x: rect.left, y: rect.bottom + 4 })
              }}
            />
        </span>
        {showErrorIndicator ? (
          <ProjectErrorIndicator label={errorLabel} />
        ) : collapsed && activity === 'running' ? (
          <span className="dc-project-row__status" style={projectStatusIndicatorSlotStyle}>
            <span className="dc-status-indicator">
              <Spinner label={t('threadEntry.turnRunning')} />
            </span>
          </span>
        ) : collapsed && activity === 'waiting' ? (
          <span className="dc-project-row__status" style={projectStatusIndicatorSlotStyle}>
            <span className="dc-status-indicator" role="img" aria-label={t('projectsRail.awaitingResponse')}>
              <span className="dc-status-indicator__dot" data-tone="warning" />
            </span>
          </span>
        ) : null}
      </div>
    </div>
    </SidebarEntryDetailsCard>
    {menuPosition && (
      <ContextMenu position={menuPosition} items={menuItems} onClose={() => setMenuPosition(null)} />
    )}
    {addProject.dialog}
    </>
  )
}

function ProjectErrorIndicator({ label }: { label: string }): JSX.Element {
  return (
    <span style={projectStatusIndicatorSlotStyle}>
      <ActionTooltip label={label}>
        <span aria-label={label} style={projectStatusIndicatorSlotStyle}>
          <AlertCircle size={14} aria-hidden style={{ color: 'var(--error)' }} />
        </span>
      </ActionTooltip>
    </span>
  )
}

const projectStatusIndicatorSlotStyle: CSSProperties = {
  width: '24px',
  height: '24px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

function projectFolderPaths(project: WorkspaceProjectSummary): string[] {
  const folders = [project.primaryFolder ?? project.path, ...(project.secondaryFolders ?? [])]
  return folders.filter((folder, index) => {
    const key = normalizeWorkspaceProjectKey(folder)
    return key.length > 0 &&
      folders.findIndex((candidate) => normalizeWorkspaceProjectKey(candidate) === key) === index
  })
}
