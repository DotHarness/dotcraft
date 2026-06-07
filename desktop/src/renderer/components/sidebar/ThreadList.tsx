import { useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import { useShallow } from 'zustand/react/shallow'
import {
  AlertCircle,
  Cloud,
  Copy,
  CircleDashed,
  ExternalLink,
  Folder,
  FolderOpen,
  FolderPlus,
  LogOut,
  MoreHorizontal,
  Server,
  SquarePen,
  Trash2
} from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { useDragDropStore } from '../../stores/dragDropStore'
import { useThreadStore, selectFilteredThreads } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadSummary } from '../../types/thread'
import { getSubAgentParentThreadId, isSubAgentThread } from '../../utils/subAgentThreads'
import { isInternalThread } from '../../utils/internalThreads'
import { Skeleton } from '../ui/Skeleton'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ActionTooltip } from '../ui/ActionTooltip'
import { useLocale } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { addToast } from '../../stores/toastStore'
import { PinIcon, ThreadEntry } from './ThreadEntry'
import { WorkspaceOptionsMenu } from './WorkspaceHeader'
import {
  isRemoteProjectKey,
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../../../shared/workspaceProjectKey'

/**
 * Scrollable container for the grouped thread list.
 * Handles empty states for "no threads" and "no search results".
 * Spec §9.5
 */
interface ThreadListProps {
  workspacePath?: string
  localWorkspacePath?: string
  localActionsDisabled?: boolean
}

export function ThreadList({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled = false
}: ThreadListProps = {}): JSX.Element {
  const t = useT()
  const { threadList, threadListProjectKey, searchQuery, loading, pinnedThreadIds } = useThreadStore()
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const foregroundWorkspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const [collapsedProjects, setCollapsedProjects] = useState<Set<string>>(() => new Set())
  // useShallow prevents infinite re-renders: selectFilteredThreads returns a new
  // array on every call (via .filter), so without shallow equality Zustand's
  // useSyncExternalStore sees a changed snapshot every render and loops.
  const filteredThreads = useThreadStore(useShallow(selectFilteredThreads))
  const dragActive = useDragDropStore((s) => s.active)
  const dragHintTitle =
    dragActive?.kind === 'automation-task' ? dragActive.title : null

  const showProjects = projects.length > 0

  if (loading && !showProjects) {
    return (
      <div
        role="status"
        aria-busy="true"
        aria-label={t('threadList.loading')}
        style={{ padding: '8px 10px', display: 'flex', flexDirection: 'column', gap: '2px' }}
      >
        {[72, 58, 80, 50, 66, 60, 74].map((width, row) => (
          <div
            key={row}
            style={{ height: '28px', display: 'flex', alignItems: 'center', padding: '0 6px' }}
          >
            <Skeleton width={`${width}%`} height={12} />
          </div>
        ))}
      </div>
    )
  }

  if (!showProjects && threadList.length === 0) {
    return (
      <div style={emptyStyle}>
        <span style={{
          color: 'var(--text-dimmed)',
          fontSize: 'var(--type-ui-size)',
          lineHeight: 'var(--type-ui-line-height)',
          textAlign: 'center'
        }}>
          {t('threadList.empty')}
          <br />
          {t('threadList.emptyHint', { label: t('sidebar.newThreadLabel') })}
        </span>
      </div>
    )
  }

  if (!showProjects && filteredThreads.length === 0 && searchQuery) {
    return (
      <div style={emptyStyle}>
        <span style={{
          color: 'var(--text-dimmed)',
          fontSize: 'var(--type-ui-size)',
          lineHeight: 'var(--type-ui-line-height)'
        }}>
          {t('threadList.noSearchResults')}
        </span>
      </div>
    )
  }

  const orderedThreads = orderSubAgentsAfterParents(sortThreadsByRecentActivity(filteredThreads))
  const { pinnedThreads, unpinnedThreads } = partitionPinnedThreads(
    orderedThreads,
    pinnedThreadIds
  )

  if (showProjects) {
    const foregroundRenderKey = foregroundProjectId || foregroundWorkspacePath
    const foregroundThreadListMatches = isForegroundThreadListForProject(
      threadListProjectKey,
      foregroundRenderKey
    )
    const foregroundStoreThreads = foregroundThreadListMatches ? orderedThreads : []
    const foregroundStorePinnedThreadIds = foregroundThreadListMatches ? pinnedThreadIds : []
    const foregroundProject = projects.find((project) =>
      isProjectForeground(project, foregroundProjectId, foregroundWorkspacePath)
    )
    const projectsForRender = foregroundProject
      ? projects
      : [
          {
            projectId: foregroundProjectId || foregroundWorkspacePath,
            kind: 'local',
            path: foregroundWorkspacePath,
            identityWorkspacePath: foregroundWorkspacePath,
            name: foregroundWorkspacePath,
            state: 'foreground',
            running: true,
            loaded: true,
            threadCount: foregroundStoreThreads.length,
            threads: foregroundStoreThreads,
            pinnedThreadIds: foregroundStorePinnedThreadIds
          } satisfies WorkspaceProjectSummary,
          ...projects
        ].filter((project) => project.path.trim().length > 0)
    const pinnedProjectRows = collectPinnedProjectRows(
      projectsForRender,
      foregroundProjectId,
      foregroundWorkspacePath,
      threadListProjectKey,
      orderedThreads,
      pinnedThreadIds,
      searchQuery
    )
    return (
      <div
        style={{
          flex: 1,
          overflowY: 'auto',
          overflowX: 'hidden',
          paddingBottom: '8px',
          scrollbarWidth: 'thin',
          scrollbarColor: 'var(--border-default) transparent',
          position: 'relative'
        }}
      >
        {dragHintTitle !== null && <DragHint title={dragHintTitle} />}
        {pinnedProjectRows.length > 0 && (
          <PinnedProjectSection rows={pinnedProjectRows} />
        )}
        <ProjectsSectionHeader
          workspacePath={workspacePath || foregroundWorkspacePath}
          localWorkspacePath={localWorkspacePath}
          localActionsDisabled={localActionsDisabled}
        />
        {projectsForRender.map((project) => {
          const projectKey = projectIdentity(project)
          const isForeground = isProjectForeground(project, foregroundProjectId, foregroundWorkspacePath)
          const cold = isColdProject(project)
          const collapsed = cold || collapsedProjects.has(projectKey)
          const cachedProjectThreads = orderSubAgentsAfterParents(filterProjectThreads(project, searchQuery))
          const foregroundListMatchesProject =
            isForeground && isForegroundThreadListForProject(threadListProjectKey, projectKey)
          const rawProjectThreads = foregroundListMatchesProject ? orderedThreads : cachedProjectThreads
          const projectPinnedIds = foregroundListMatchesProject ? pinnedThreadIds : (project.pinnedThreadIds ?? [])
          const projectThreads = excludePinnedThreadTrees(rawProjectThreads, projectPinnedIds)
          const activity = getProjectActivity(rawProjectThreads)
          return (
            <div key={projectKey} style={{ marginBottom: '6px' }}>
              <ProjectHeader
                project={project}
                projectKey={projectKey}
                active={isForeground}
                collapsed={collapsed}
                activity={activity}
                cold={cold}
                onToggle={() => {
                  if (cold) return
                  setCollapsedProjects((current) => {
                    const next = new Set(current)
                    if (next.has(projectKey)) next.delete(projectKey)
                    else next.add(projectKey)
                    return next
                  })
                }}
              />
              {!collapsed && (
                <>
                  {projectThreads.length === 0 && project.loaded && searchQuery && (
                    <ProjectHint label={t('threadList.noSearchResults')} />
                  )}
                  {projectThreads.map((thread) => (
                    isForeground ? (
                      <ThreadEntryWrapper key={thread.id} thread={thread} />
                    ) : (
                      <ReadonlyThreadRow key={thread.id} thread={thread} project={project} />
                    )
                  ))}
                </>
              )}
            </div>
          )
        })}
      </div>
    )
  }

  return (
    <div
      style={{
        flex: 1,
        overflowY: 'auto',
        overflowX: 'hidden',
        paddingBottom: '8px',
        scrollbarWidth: 'thin',
        scrollbarColor: 'var(--border-default) transparent',
        position: 'relative'
      }}
    >
      {dragHintTitle !== null && (
        <DragHint title={dragHintTitle} />
      )}
      {pinnedThreads.length > 0 && (
        <FlatSectionTitle label={t('threadGroup.pinned')} />
      )}
      {pinnedThreads.map((thread) => (
        <ThreadEntryWrapper key={thread.id} thread={thread} />
      ))}
      {unpinnedThreads.map((thread) => (
        <ThreadEntryWrapper key={thread.id} thread={thread} />
      ))}
    </div>
  )
}

function ThreadEntryWrapper({ thread }: { thread: ThreadSummary }): JSX.Element {
  return <ThreadEntry thread={thread} />
}

function DragHint({ title }: { title: string }): JSX.Element {
  const t = useT()
  return (
    <div
      aria-hidden="true"
      style={{
        position: 'sticky',
        top: 0,
        zIndex: 2,
        margin: '4px 10px 6px',
        padding: '6px 10px',
        borderRadius: '999px',
        fontSize: 'var(--type-secondary-size)',
        lineHeight: 'var(--type-secondary-line-height)',
        fontWeight: 'var(--type-ui-emphasis-weight)',
        color: 'var(--accent)',
        backgroundColor: 'color-mix(in srgb, var(--accent) 12%, var(--bg-secondary))',
        border: '1px solid color-mix(in srgb, var(--accent) 30%, transparent)',
        boxShadow: '0 2px 8px color-mix(in srgb, var(--accent) 12%, transparent)',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        pointerEvents: 'none',
        animation: 'fadeSlideDown 160ms ease'
      }}
    >
      {t('auto.dnd.hintBar', { title })}
    </div>
  )
}

function sameWorkspacePath(left: string, right: string): boolean {
  return sameWorkspaceProjectKey(left, right)
}

function projectIdentity(project: WorkspaceProjectSummary): string {
  return project.projectId?.trim() || normalizeWorkspacePath(project.path)
}

function sameProjectIdentity(left: string, right: string): boolean {
  return sameWorkspaceProjectKey(left, right)
}

function isForegroundThreadListForProject(
  threadListProjectKey: string | null,
  projectKey: string
): boolean {
  return sameWorkspaceProjectKey(threadListProjectKey, projectKey)
}

function isRemoteProject(project: WorkspaceProjectSummary): boolean {
  return project.kind === 'remote'
}

function isColdProject(project: WorkspaceProjectSummary): boolean {
  return project.state === 'cold'
}

function isProjectForeground(
  project: WorkspaceProjectSummary,
  foregroundProjectId: string,
  foregroundWorkspacePath: string
): boolean {
  const projectId = projectIdentity(project)
  const foregroundId = foregroundProjectId.trim()
  if (!foregroundId) {
    return sameWorkspacePath(project.path, foregroundWorkspacePath)
  }
  if (sameProjectIdentity(projectId, foregroundId)) {
    return true
  }
  if (isRemoteProject(project) || isRemoteProjectKey(foregroundId)) {
    return false
  }
  return sameWorkspacePath(project.path, foregroundWorkspacePath)
}

function normalizeWorkspacePath(path: string): string {
  return normalizeWorkspaceProjectKey(path)
}

function isThreadSummary(value: unknown): value is ThreadSummary {
  return Boolean(value && typeof value === 'object' && typeof (value as { id?: unknown }).id === 'string')
}

function filterProjectThreads(project: WorkspaceProjectSummary, searchQuery: string): ThreadSummary[] {
  const query = searchQuery.trim().toLowerCase()
  return sortThreadsByRecentActivity(project.threads
    .filter(isThreadSummary)
    .filter((thread) => !isInternalThread(thread))
    .filter((thread) => {
      if (!query) return true
      return (thread.displayName ?? '').toLowerCase().includes(query)
    }))
}

function sortThreadsByRecentActivity(threads: ThreadSummary[]): ThreadSummary[] {
  return [...threads].sort((left, right) => {
    const leftTime = Date.parse(left.lastActiveAt)
    const rightTime = Date.parse(right.lastActiveAt)
    return (Number.isFinite(rightTime) ? rightTime : 0) - (Number.isFinite(leftTime) ? leftTime : 0)
  })
}

function collectPinnedProjectRows(
  projects: WorkspaceProjectSummary[],
  foregroundProjectId: string,
  foregroundWorkspacePath: string,
  foregroundThreadListProjectKey: string | null,
  foregroundThreads: ThreadSummary[],
  foregroundPinnedThreadIds: string[],
  searchQuery: string
): PinnedProjectRow[] {
  const rows: PinnedProjectRow[] = []
  for (const project of projects) {
    const foreground = isProjectForeground(project, foregroundProjectId, foregroundWorkspacePath)
    const foregroundListMatchesProject =
      foreground && isForegroundThreadListForProject(foregroundThreadListProjectKey, projectIdentity(project))
    const threads = foregroundListMatchesProject
      ? foregroundThreads
      : orderSubAgentsAfterParents(filterProjectThreads(project, searchQuery))
    const pinnedIds = foregroundListMatchesProject ? foregroundPinnedThreadIds : (project.pinnedThreadIds ?? [])
    const { pinnedThreads } = partitionPinnedThreads(threads, pinnedIds)
    for (const thread of pinnedThreads) {
      rows.push({ project, thread, interactiveForeground: foregroundListMatchesProject })
    }
  }
  return rows
}

function excludePinnedThreadTrees(threads: ThreadSummary[], pinnedThreadIds: string[]): ThreadSummary[] {
  return partitionPinnedThreads(threads, pinnedThreadIds).unpinnedThreads
}

function getProjectActivity(threads: ThreadSummary[]): ProjectActivity {
  if (threads.some(isThreadRunning)) return 'running'
  if (threads.some(isThreadWaiting)) return 'waiting'
  return null
}

function isThreadRunning(thread: ThreadSummary): boolean {
  return thread.runtime?.running === true || thread.runtime?.busy === true
}

function isThreadWaiting(thread: ThreadSummary): boolean {
  return thread.runtime?.waitingOnApproval === true
    || thread.runtime?.waitingOnInput === true
    || thread.runtime?.waitingOnPlanConfirmation === true
}

type ProjectActivity = 'running' | 'waiting' | null

interface PinnedProjectRow {
  project: WorkspaceProjectSummary
  thread: ThreadSummary
  interactiveForeground: boolean
}

function ProjectsSectionHeader({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled
}: {
  workspacePath: string
  localWorkspacePath?: string
  localActionsDisabled: boolean
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const [workspaceMenuOpen, setWorkspaceMenuOpen] = useState(false)
  const showActions = hovered || focused || workspaceMenuOpen

  async function addProject(): Promise<void> {
    const path = await window.api.workspace.pickFolder()
    if (!path) return
    await window.api.workspace.switch(path)
  }

  return (
    <div
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setFocused(false)
        }
      }}
      style={{
        display: 'grid',
        gridTemplateColumns: 'minmax(0, 1fr) 56px',
        alignItems: 'center',
        gap: '4px',
        minHeight: '28px',
        padding: '8px 8px 2px',
        position: 'relative'
      }}
    >
      <span
        style={{
          color: 'var(--text-dimmed)',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)'
        }}
      >
        {t('projectsRail.title')}
      </span>
      <div
        style={{
          width: '56px',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'flex-end',
          gap: '4px',
          opacity: showActions ? 1 : 0,
          pointerEvents: showActions ? 'auto' : 'none',
          transition: 'opacity 120ms ease'
        }}
      >
        {workspacePath.trim().length > 0 && (
          <WorkspaceOptionsMenu
            workspacePath={workspacePath}
            localWorkspacePath={localWorkspacePath}
            localActionsDisabled={localActionsDisabled}
            buttonStyle={projectIconButtonStyle}
            onOpenChange={setWorkspaceMenuOpen}
          />
        )}
        <ActionTooltip label={t('projectsRail.addProject')}>
          <button
            type="button"
            aria-label={t('projectsRail.addProject')}
            onClick={() => { void addProject() }}
            style={projectIconButtonStyle}
          >
            <FolderPlus size={15} aria-hidden />
          </button>
        </ActionTooltip>
      </div>
    </div>
  )
}

function PinnedProjectSection({ rows }: { rows: PinnedProjectRow[] }): JSX.Element {
  const t = useT()
  return (
    <div style={{ marginBottom: '8px' }}>
      <FlatSectionTitle label={t('threadGroup.pinned')} />
      {rows.map(({ project, thread, interactiveForeground }) => (
        interactiveForeground ? (
          <ThreadEntry key={`${projectIdentity(project)}:${thread.id}`} thread={thread} />
        ) : (
          <ReadonlyThreadRow key={`${projectIdentity(project)}:${thread.id}`} thread={thread} project={project} pinned />
        )
      ))}
    </div>
  )
}

function FlatSectionTitle({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        padding: '8px 8px 4px',
        color: 'var(--text-dimmed)',
        fontSize: 'var(--type-secondary-size)',
        lineHeight: 'var(--type-secondary-line-height)'
      }}
    >
      {label}
    </div>
  )
}

function ProjectHeader({
  project,
  projectKey,
  active,
  collapsed,
  activity,
  cold,
  onToggle
}: {
  project: WorkspaceProjectSummary
  projectKey: string
  active: boolean
  collapsed: boolean
  activity: ProjectActivity
  cold: boolean
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const rowRef = useRef<HTMLDivElement>(null)
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const label = project.name || project.path
  const showActions = hovered || menuOpen
  const ProjectIcon = isRemoteProject(project)
    ? (project.remote?.source === 'servers' ? Server : Cloud)
    : (collapsed ? Folder : FolderOpen)
  const detailLabel = project.remote?.displayPath || project.remote?.endpoint || project.identityWorkspacePath || project.path
  const iconLabel = cold
    ? t('projectsRail.notRunning')
    : project.state === 'connecting'
      ? t('projectsRail.connecting')
      : detailLabel

  async function openProject(): Promise<void> {
    if (active) return
    if (isRemoteProject(project)) return
    await window.api.workspace.switch(project.path)
  }

  async function newChat(): Promise<void> {
    if (!active && !isRemoteProject(project)) {
      await window.api.workspace.switch(project.path)
    }
    useUIStore.getState().goToNewChat({ workspacePath: projectKey, clearDraft: true })
    setActiveMainView('conversation')
  }

  async function copyPath(): Promise<void> {
    await navigator.clipboard.writeText(detailLabel)
    addToast(t('projectsRail.pathCopied'), 'success')
  }

  async function removeProject(): Promise<void> {
    if (active) return
    if (isRemoteProject(project)) return
    await window.api.workspace.removeRecent(project.path)
  }

  async function disconnectRemote(): Promise<void> {
    if (!isRemoteProject(project)) return
    await window.api.workspace.disconnectRemote()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    onToggle()
  }

  return (
    <div
      ref={rowRef}
      role="button"
      tabIndex={0}
      aria-expanded={!collapsed}
      aria-label={label}
      onClick={onToggle}
      onKeyDown={handleKeyDown}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: 'relative',
        display: 'grid',
        gridTemplateColumns: '20px minmax(0, 1fr) 60px',
        alignItems: 'center',
        gap: '6px',
        minHeight: '30px',
        margin: '2px 8px',
        padding: '2px 6px',
        borderRadius: 'var(--sidebar-control-radius)',
        backgroundColor: active ? 'var(--sidebar-control-active)' : hovered ? 'var(--sidebar-control-hover)' : 'transparent',
        cursor: 'pointer',
        userSelect: 'none'
      }}
    >
      <ActionTooltip label={iconLabel} placement="right">
        <span style={projectIconSlotStyle}>
          <ProjectIcon
            size={15}
            strokeWidth={1.7}
            aria-hidden
            style={{ color: cold ? 'var(--text-tertiary)' : 'var(--text-dimmed)' }}
          />
          {cold && (
            <CircleDashed
              size={8}
              strokeWidth={2.2}
              aria-hidden
              style={projectColdBadgeStyle}
            />
          )}
        </span>
      </ActionTooltip>
      <ActionTooltip label={detailLabel} wrapperStyle={{ minWidth: 0 }}>
        <span
          style={{
            minWidth: 0,
            color: active ? 'var(--text-primary)' : 'var(--text-secondary)',
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
      </ActionTooltip>
      <div
        style={{
          width: '60px',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'flex-end',
          gap: '2px'
        }}
        onClick={(event) => event.stopPropagation()}
      >
        {showActions ? (
          <>
            <ActionTooltip label={t('projectsRail.newChat')}>
              <button
                type="button"
                aria-label={t('projectsRail.newChat')}
                onClick={() => { void newChat() }}
                style={projectIconButtonStyle}
              >
                <SquarePen size={14} aria-hidden />
              </button>
            </ActionTooltip>
            <ActionTooltip label={t('projectsRail.moreActions')}>
              <button
                type="button"
                aria-label={t('projectsRail.moreActions')}
                onClick={() => setMenuOpen((open) => !open)}
                style={projectIconButtonStyle}
              >
                <MoreHorizontal size={15} aria-hidden />
              </button>
            </ActionTooltip>
          </>
        ) : project.state === 'error' ? (
          <ActionTooltip label={project.errorMessage || t('projectsRail.error')}>
            <AlertCircle size={14} aria-hidden style={{ color: 'var(--error)' }} />
          </ActionTooltip>
        ) : project.state === 'connecting' ? (
          <RunningSpinner label={t('projectsRail.connecting')} />
        ) : collapsed && activity === 'running' ? (
          <RunningSpinner label={t('threadEntry.turnRunning')} />
        ) : collapsed && activity === 'waiting' ? (
          <span style={projectWaitingDotStyle} aria-label={t('projectsRail.awaitingResponse')} />
        ) : null}
      </div>
      {menuOpen && (
        <div
          role="menu"
          aria-label={t('projectsRail.moreActions')}
          style={projectMenuStyle}
          onClick={(event) => event.stopPropagation()}
        >
          {!isRemoteProject(project) && (
            <ProjectMenuItem icon={<ExternalLink size={14} aria-hidden />} label={t('projectsRail.openProject')} onClick={() => { setMenuOpen(false); void openProject() }} />
          )}
          {!isRemoteProject(project) && (
            <ProjectMenuItem icon={<FolderOpen size={14} aria-hidden />} label={t('workspaceHeader.openInExplorer')} onClick={() => { setMenuOpen(false); void window.api.shell.openPath(project.path) }} />
          )}
          <ProjectMenuItem icon={<Copy size={14} aria-hidden />} label={t('projectsRail.copyPath')} onClick={() => { setMenuOpen(false); void copyPath() }} />
          {isRemoteProject(project) ? (
            <ProjectMenuItem
              icon={<LogOut size={14} aria-hidden />}
              label={t('projectsRail.disconnectRemote')}
              danger
              onClick={() => { setMenuOpen(false); void disconnectRemote() }}
            />
          ) : (
            <ProjectMenuItem
              icon={<Trash2 size={14} aria-hidden />}
              label={t('projectsRail.removeProject')}
              disabled={active}
              danger
              onClick={() => { setMenuOpen(false); void removeProject() }}
            />
          )}
        </div>
      )}
    </div>
  )
}

function ProjectMenuItem({
  icon,
  label,
  onClick,
  disabled = false,
  danger = false
}: {
  icon: ReactNode
  label: string
  onClick: () => void
  disabled?: boolean
  danger?: boolean
}): JSX.Element {
  return (
    <button
      type="button"
      role="menuitem"
      disabled={disabled}
      onClick={onClick}
      style={{
        width: '100%',
        border: 'none',
        borderRadius: '4px',
        background: 'transparent',
        color: disabled
          ? 'var(--text-tertiary)'
          : danger
            ? 'var(--error)'
            : 'var(--text-primary)',
        display: 'grid',
        gridTemplateColumns: '18px minmax(0, 1fr)',
        alignItems: 'center',
        gap: '8px',
        padding: '7px 14px',
        fontSize: 'var(--type-ui-size)',
        lineHeight: 'var(--type-ui-line-height)',
        cursor: disabled ? 'default' : 'pointer',
        textAlign: 'left'
      }}
      onMouseEnter={(event) => {
        if (!disabled) event.currentTarget.style.backgroundColor = 'var(--sidebar-control-hover)'
      }}
      onMouseLeave={(event) => {
        event.currentTarget.style.backgroundColor = 'transparent'
      }}
    >
      {icon}
      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
        {label}
      </span>
    </button>
  )
}

function ProjectHint({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        padding: '4px 16px 8px 32px',
        color: 'var(--text-dimmed)',
        fontSize: 'var(--type-secondary-size)',
        lineHeight: 'var(--type-secondary-line-height)'
      }}
    >
      {label}
    </div>
  )
}

function ReadonlyThreadRow({
  thread,
  project,
  pinned = false
}: {
  thread: ThreadSummary
  project: WorkspaceProjectSummary
  pinned?: boolean
}): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const running = isThreadRunning(thread)
  const waiting = isThreadWaiting(thread)
  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)

  async function openThread(): Promise<void> {
    if (!isRemoteProject(project)) {
      await window.api.workspace.switch(project.path)
    }
    setActiveMainView('conversation')
    useThreadStore.getState().setActiveThreadId(thread.id)
  }

  return (
    <ActionTooltip label={displayName} wrapperStyle={{ display: 'block', width: '100%' }}>
      <button
        type="button"
        onClick={() => void openThread()}
        data-testid={`project-thread-entry-${projectIdentity(project)}-${thread.id}`}
        style={{
          display: 'grid',
          gridTemplateColumns: 'minmax(0, 1fr) minmax(24px, max-content)',
          alignItems: 'center',
          columnGap: '7px',
          width: 'calc(100% - 32px)',
          margin: '2px 10px 2px 22px',
          padding: '6px 12px',
          border: 'none',
          borderRadius: 'var(--sidebar-control-radius)',
          backgroundColor: 'transparent',
          color: 'var(--text-primary)',
          cursor: 'pointer',
          textAlign: 'left'
        }}
        onMouseEnter={(e) => {
          e.currentTarget.style.backgroundColor = 'var(--sidebar-control-hover)'
        }}
        onMouseLeave={(e) => {
          e.currentTarget.style.backgroundColor = 'transparent'
        }}
      >
        <span
          style={{
            minWidth: 0,
            display: 'flex',
            alignItems: 'center',
            gap: '6px',
            fontSize: 'var(--type-ui-size)',
            lineHeight: 'var(--type-ui-line-height)'
          }}
        >
          {pinned && (
            <ActionTooltip label={t('threadGroup.pinned')} placement="right">
              <span
                aria-label={t('threadGroup.pinned')}
                data-testid={`project-thread-pinned-${projectIdentity(project)}-${thread.id}`}
                style={{
                  width: '18px',
                  minWidth: '18px',
                  height: '20px',
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  color: 'var(--text-secondary)'
                }}
              >
                <PinIcon filled />
              </span>
            </ActionTooltip>
          )}
          <span
            style={{
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap'
            }}
          >
            {displayName}
          </span>
        </span>
        <span
          style={{
            color: 'var(--text-dimmed)',
            fontSize: 'var(--type-secondary-size)',
            lineHeight: 'var(--type-secondary-line-height)',
            whiteSpace: 'nowrap'
          }}
        >
          {running ? (
            <RunningSpinner label={t('threadEntry.turnRunning')} />
          ) : waiting ? (
            <span style={readonlyWaitingBadgeStyle}>
              {t('projectsRail.awaitingResponse')}
            </span>
          ) : (
            relativeTime
          )}
        </span>
      </button>
    </ActionTooltip>
  )
}

function orderSubAgentsAfterParents(threads: ThreadSummary[]): ThreadSummary[] {
  const childrenByParent = new Map<string, ThreadSummary[]>()
  const topLevel: ThreadSummary[] = []
  const emitted = new Set<string>()

  for (const thread of threads) {
    const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
    if (parentId) {
      const children = childrenByParent.get(parentId) ?? []
      children.push(thread)
      childrenByParent.set(parentId, children)
    } else {
      topLevel.push(thread)
    }
  }

  const result: ThreadSummary[] = []
  for (const thread of topLevel) {
    result.push(thread)
    emitted.add(thread.id)
    const children = childrenByParent.get(thread.id) ?? []
    for (const child of children) {
      result.push(child)
      emitted.add(child.id)
    }
  }

  for (const thread of threads) {
    if (!emitted.has(thread.id)) {
      result.push(thread)
      emitted.add(thread.id)
    }
  }

  return result
}

function partitionPinnedThreads(
  threads: ThreadSummary[],
  pinnedThreadIds: string[]
): { pinnedThreads: ThreadSummary[]; unpinnedThreads: ThreadSummary[] } {
  if (pinnedThreadIds.length === 0 || threads.length === 0) {
    return { pinnedThreads: [], unpinnedThreads: threads }
  }

  const byId = new Map(threads.map((thread) => [thread.id, thread]))
  const childrenByParent = new Map<string, ThreadSummary[]>()
  for (const thread of threads) {
    const parentId = isSubAgentThread(thread) ? getSubAgentParentThreadId(thread) : null
    if (!parentId) continue
    const children = childrenByParent.get(parentId) ?? []
    children.push(thread)
    childrenByParent.set(parentId, children)
  }

  const included = new Set<string>()
  const pinnedThreads: ThreadSummary[] = []

  function appendThreadTree(threadId: string): void {
    if (included.has(threadId)) return
    const thread = byId.get(threadId)
    if (!thread) return
    included.add(threadId)
    pinnedThreads.push(thread)
    for (const child of childrenByParent.get(threadId) ?? []) {
      appendThreadTree(child.id)
    }
  }

  for (const threadId of pinnedThreadIds) {
    const thread = byId.get(threadId)
    if (!thread || isSubAgentThread(thread)) continue
    appendThreadTree(threadId)
  }

  return {
    pinnedThreads,
    unpinnedThreads: threads.filter((thread) => !included.has(thread.id))
  }
}

const projectIconButtonStyle: CSSProperties = {
  width: '24px',
  height: '24px',
  padding: 0,
  border: 'none',
  borderRadius: 'var(--sidebar-icon-control-radius)',
  backgroundColor: 'transparent',
  color: 'var(--text-dimmed)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer'
}

const projectIconSlotStyle: CSSProperties = {
  position: 'relative',
  width: '20px',
  height: '20px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center'
}

const projectColdBadgeStyle: CSSProperties = {
  position: 'absolute',
  right: '1px',
  bottom: '1px',
  color: 'var(--text-tertiary)',
  backgroundColor: 'var(--bg-secondary)',
  borderRadius: '999px'
}

const projectMenuStyle: CSSProperties = {
  position: 'absolute',
  right: '6px',
  top: 'calc(100% + 4px)',
  zIndex: 30,
  minWidth: '220px',
  maxWidth: '320px',
  padding: '6px',
  borderRadius: '10px',
  backgroundColor: 'var(--glass-surface-strong)',
  border: 'none',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  color: 'var(--text-primary)'
}

const projectWaitingDotStyle: CSSProperties = {
  width: '8px',
  height: '8px',
  borderRadius: '999px',
  backgroundColor: 'var(--warning)',
  display: 'inline-block'
}

const readonlyWaitingBadgeStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  maxWidth: '140px',
  minWidth: 0,
  height: '18px',
  padding: '2px 8px',
  borderRadius: '999px',
  backgroundColor: 'color-mix(in srgb, var(--success) 20%, transparent)',
  color: 'var(--success)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  whiteSpace: 'nowrap',
  overflow: 'hidden',
  textOverflow: 'ellipsis'
}

const emptyStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px 16px'
}
