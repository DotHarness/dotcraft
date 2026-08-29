import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useShallow } from 'zustand/react/shallow'
import {
  AlertCircle,
  Archive,
  ArrowUpRight,
  ChevronRight,
  Cloud,
  Copy,
  CircleDashed,
  ExternalLink,
  Folder,
  FolderOpen,
  FolderPlus,
  LogOut,
  MoreHorizontal,
  Pin,
  RotateCw,
  Server,
  Settings,
  Square,
  SquarePen,
  Trash2
} from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { useDragDropStore } from '../../stores/dragDropStore'
import { useThreadStore, selectFilteredThreads } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadSummary } from '../../types/thread'
import { getSubAgentDepth, getSubAgentParentThreadId, isSubAgentThread } from '../../utils/subAgentThreads'
import { ThreadRowLayout } from './ThreadRowLayout'
import { isInternalThread } from '../../utils/internalThreads'
import { Skeleton } from '../ui/Skeleton'
import { RunningSpinner } from '../ui/RunningSpinner'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import { useLocale } from '../../contexts/LocaleContext'
import { formatRelativeTime } from '../../utils/relativeTime'
import type { WorkspaceProjectSummary, WorkspaceProjectState } from '../../../shared/workspaceProjects'
import { addToast } from '../../stores/toastStore'
import { PinIcon, ThreadEntry } from './ThreadEntry'
import { WorkspaceOptionsMenu } from './WorkspaceHeader'
import { useAddProjectFlow } from '../projects/AddProject'
import { SIDEBAR_RAIL_CONTENT_INSET, SIDEBAR_ROW_MIN_HEIGHT } from './sidebarNavRowStyles'
import {
  isRemoteProjectKey,
  normalizeWorkspaceProjectKey,
  sameWorkspaceProjectKey
} from '../../../shared/workspaceProjectKey'
import { SidebarEntryDetailsCard } from './SidebarEntryDetailsCard'
import { threadOriginBadge, useThreadEntryDetails } from './ThreadEntryDetails'
import { buildWorkspaceOpenDeepLink } from '../../../shared/desktopDeepLink'

interface ThreadListProps {
  workspacePath?: string
  localWorkspacePath?: string
  localActionsDisabled?: boolean
  foregroundOpening?: boolean
  openingWorkspacePath?: string
}

export function ThreadList({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled = false,
  foregroundOpening = false,
  openingWorkspacePath
}: ThreadListProps = {}): JSX.Element {
  const t = useT()
  const { threadList, threadListProjectKey, searchQuery, loading, pinnedThreadIds } = useThreadStore()
  const projects = useWorkspaceProjectsStore((s) => s.projects)
  const chat = useWorkspaceProjectsStore((s) => s.chat)
  const foregroundWorkspacePath = useWorkspaceProjectsStore((s) => s.foregroundWorkspacePath)
  const foregroundProjectId = useWorkspaceProjectsStore((s) => s.foregroundProjectId)
  const projectsSectionCollapsed = useUIStore((s) => s.projectsSectionCollapsed)
  const pinnedSectionCollapsed = useUIStore((s) => s.pinnedSectionCollapsed)
  const chatsSectionCollapsed = useUIStore((s) => s.chatsSectionCollapsed)
  const setProjectsSectionCollapsed = useUIStore((s) => s.setProjectsSectionCollapsed)
  const setPinnedSectionCollapsed = useUIStore((s) => s.setPinnedSectionCollapsed)
  const setChatsSectionCollapsed = useUIStore((s) => s.setChatsSectionCollapsed)
  const [collapsedProjects, setCollapsedProjects] = useState<Set<string>>(() => new Set())
  // useShallow prevents infinite re-renders: selectFilteredThreads returns a new
  // array on every call (via .filter), so without shallow equality Zustand's
  // useSyncExternalStore sees a changed snapshot every render and loops.
  const filteredThreads = useThreadStore(useShallow(selectFilteredThreads))
  const dragActive = useDragDropStore((s) => s.active)
  const dragHintTitle =
    dragActive?.kind === 'automation-task' ? dragActive.title : null

  const showChats = chat != null
  const hasProjectRows = projects.length > 0
  const chatIsCurrentWorkspace =
    chat != null &&
    sameWorkspacePath(chat.path, workspacePath || foregroundWorkspacePath || foregroundProjectId)
  const showProjects = hasProjectRows || showChats
  const showGroupedLayout = showProjects || showChats

  if (loading && !showGroupedLayout) {
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

  if (!showGroupedLayout && threadList.length === 0) {
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

  if (!showGroupedLayout && filteredThreads.length === 0 && searchQuery) {
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

  if (showGroupedLayout) {
    const openingProjectKey = foregroundOpening
      ? normalizeWorkspacePath(openingWorkspacePath || workspacePath || '')
      : ''
    const effectiveForegroundProjectId = openingProjectKey || foregroundProjectId
    const effectiveForegroundWorkspacePath = openingProjectKey || foregroundWorkspacePath
    const foregroundRenderKey = effectiveForegroundProjectId || effectiveForegroundWorkspacePath
    const foregroundThreadListMatches = isForegroundThreadListForProject(
      threadListProjectKey,
      foregroundRenderKey
    )
    const foregroundStoreThreads = foregroundThreadListMatches ? orderedThreads : []
    const foregroundStorePinnedThreadIds = foregroundThreadListMatches ? pinnedThreadIds : []
    const foregroundProject = projects.find((project) =>
      isProjectForeground(project, effectiveForegroundProjectId, effectiveForegroundWorkspacePath)
    )
    // When the default Chat workspace is foreground, the `Recents` group represents it,
    // so the foreground must not be synthesized as a Project row below.
    const chatIsForeground =
      chat != null &&
      isProjectForeground(chat, effectiveForegroundProjectId, effectiveForegroundWorkspacePath)
    const chatForegroundListMatches =
      chatIsForeground && isForegroundThreadListForProject(threadListProjectKey, projectIdentity(chat!))
    // Keep the project order stable (store order); the active project is marked
    // with a badge on its folder icon rather than being hoisted to the top.
    const projectsForRender = foregroundProject || chatIsForeground
      ? projects
      : [
          {
            projectId: effectiveForegroundProjectId || effectiveForegroundWorkspacePath,
            kind: 'local',
            path: effectiveForegroundWorkspacePath,
            identityWorkspacePath: effectiveForegroundWorkspacePath,
            name: effectiveForegroundWorkspacePath,
            state: 'foreground',
            running: true,
            loaded: true,
            threadCount: foregroundStoreThreads.length,
            threads: foregroundStoreThreads,
            pinnedThreadIds: foregroundStorePinnedThreadIds,
            pinned: false
          } satisfies WorkspaceProjectSummary,
          ...projects
        ].filter((project) => project.path.trim().length > 0)
    const pinnedThreadRows = collectPinnedProjectRows(
      projectsForRender,
      effectiveForegroundProjectId,
      effectiveForegroundWorkspacePath,
      threadListProjectKey,
      orderedThreads,
      pinnedThreadIds,
      searchQuery
    )
    const pinnedProjects = projectsForRender.filter((project) => project.pinned === true)
    const ordinaryProjects = projectsForRender.filter((project) => project.pinned !== true)

    const renderProjectBlock = (project: WorkspaceProjectSummary): JSX.Element => {
      const projectKey = projectIdentity(project)
      const isForeground = isProjectForeground(project, effectiveForegroundProjectId, effectiveForegroundWorkspacePath)
      const cachedProjectThreads = orderSubAgentsAfterParents(filterProjectThreads(project, searchQuery))
      const foregroundListMatchesProject =
        isForeground && isForegroundThreadListForProject(threadListProjectKey, projectKey)
      const openingProject = isForeground && (
        foregroundOpening ||
        project.state === 'connecting' ||
        (loading && !foregroundListMatchesProject)
      )
      const cold = isColdProject(project) && !openingProject
      const collapsed = cold || (!openingProject && collapsedProjects.has(projectKey))
      const rawProjectThreads = foregroundListMatchesProject ? orderedThreads : cachedProjectThreads
      const detailThreads = foregroundListMatchesProject
        ? orderSubAgentsAfterParents(visibleProjectThreads(threadList))
        : orderSubAgentsAfterParents(filterProjectThreads(project, ''))
      const projectPinnedIds = foregroundListMatchesProject ? pinnedThreadIds : (project.pinnedThreadIds ?? [])
      const projectThreads = excludePinnedThreadTrees(rawProjectThreads, projectPinnedIds)
      const activity = getProjectActivity(detailThreads)
      const showProjectThreadSkeleton =
        openingProject &&
        (foregroundOpening || project.state === 'connecting' || projectThreads.length === 0)
      return (
        <div key={projectKey} style={{ marginBottom: '6px' }}>
          <ProjectHeader
            project={project}
            projectKey={projectKey}
            active={isForeground}
            collapsed={collapsed}
            activity={activity}
            cold={cold}
            detailThreads={detailThreads}
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
          <CollapsibleThreads collapsed={collapsed}>
            {showProjectThreadSkeleton ? (
              <ProjectThreadSkeletonList />
            ) : (
              <>
                {project.loaded && rawProjectThreads.length === 0 && (
                  <ProjectHint
                    label={searchQuery
                      ? t('threadList.noSearchResults')
                      : t('projectsRail.noChats')}
                  />
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
          </CollapsibleThreads>
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
          position: 'relative'
        }}
      >
        {dragHintTitle !== null && <DragHint title={dragHintTitle} />}
        {(pinnedThreadRows.length > 0 || pinnedProjects.length > 0) && (
          <PinnedProjectSection
            rows={pinnedThreadRows}
            projects={pinnedProjects}
            renderProject={renderProjectBlock}
            collapsed={pinnedSectionCollapsed}
            onToggle={() => setPinnedSectionCollapsed(!pinnedSectionCollapsed)}
          />
        )}
        {showProjects && (
          <ProjectsSectionHeader
            workspacePath={hasProjectRows || !chatIsCurrentWorkspace ? (workspacePath || foregroundWorkspacePath) : ''}
            localWorkspacePath={localWorkspacePath}
            localActionsDisabled={localActionsDisabled}
            collapsed={projectsSectionCollapsed}
            onToggle={() => setProjectsSectionCollapsed(!projectsSectionCollapsed)}
          />
        )}
        {showProjects && (
          <CollapsibleThreads collapsed={projectsSectionCollapsed} marginTop={0}>
            {projectsForRender.length === 0 && (
              <ProjectHint label={t('projectsRail.noProjects')} alignment="section" />
            )}
            {ordinaryProjects.map(renderProjectBlock)}
          </CollapsibleThreads>
        )}
        {showChats && chat && (
          <ChatsSection
            chat={chat}
            interactive={chatForegroundListMatches}
            foreground={chatIsForeground}
            foregroundThreads={orderedThreads}
            foregroundPinnedThreadIds={pinnedThreadIds}
            searchQuery={searchQuery}
            opening={chatIsForeground && (foregroundOpening || chat.state === 'connecting')}
            collapsed={chatsSectionCollapsed}
            onToggle={() => setChatsSectionCollapsed(!chatsSectionCollapsed)}
          />
        )}
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
        position: 'relative'
      }}
    >
      {dragHintTitle !== null && (
        <DragHint title={dragHintTitle} />
      )}
      {pinnedThreads.length > 0 && (
        <>
          <PinnedSectionHeader
            collapsed={pinnedSectionCollapsed}
            onToggle={() => setPinnedSectionCollapsed(!pinnedSectionCollapsed)}
          />
          <CollapsibleThreads collapsed={pinnedSectionCollapsed} marginTop={0}>
            {pinnedThreads.map((thread) => (
              <ThreadEntryWrapper key={thread.id} thread={thread} />
            ))}
          </CollapsibleThreads>
        </>
      )}
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

export function projectIdentity(project: WorkspaceProjectSummary): string {
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

export function isRemoteProject(project: WorkspaceProjectSummary): boolean {
  return project.kind === 'remote'
}

export function isColdProject(project: WorkspaceProjectSummary): boolean {
  return project.state === 'cold'
}

export function isProjectForeground(
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
  return sortThreadsByRecentActivity(visibleProjectThreads(project.threads)
    .filter((thread) => {
      if (!query) return true
      return (thread.displayName ?? '').toLowerCase().includes(query)
    }))
}

function visibleProjectThreads(threads: unknown[]): ThreadSummary[] {
  return threads
    .filter(isThreadSummary)
    .filter((thread) => !isInternalThread(thread))
    .filter((thread) => thread.status?.toLowerCase() !== 'archived')
    // Subagent threads are surfaced via the dock / Subagents tab, not the sidebar.
    .filter((thread) => !isSubAgentThread(thread))
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

/**
 * Pin is a Desktop-local setting keyed by workspace path, so the whole
 * `pinnedThreadIdsByWorkspace[key]` list is persisted directly. The main process
 * re-pushes the workspace projects payload afterwards, which moves the row.
 */
function toggleWorkspacePin(
  workspacePath: string,
  threadId: string,
  currentPinnedIds: string[]
): void {
  const workspaceKey = normalizeWorkspaceProjectKey(workspacePath)
  const id = threadId.trim()
  if (!workspaceKey || !id) return
  const next = currentPinnedIds.includes(id)
    ? currentPinnedIds.filter((existing) => existing !== id)
    : [id, ...currentPinnedIds]
  void window.api?.settings
    ?.set({ pinnedThreadIdsByWorkspace: { [workspaceKey]: next } })
    .catch((err: unknown) =>
      console.error('settings:set pinnedThreadIdsByWorkspace failed:', err)
    )
}

/**
 * Prefers the most-recently-used *other* running workspace, else the default
 * Chats workspace, so the main view never lingers on a dead connection.
 */
function pickNextWorkspaceAfterStop(
  stoppedPath: string
): { path: string; name: string } | null {
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const stoppedKey = normalizeWorkspaceProjectKey(stoppedPath)
  const runningOthers = projects
    .filter((candidate) => candidate.kind !== 'remote')
    .filter((candidate) => normalizeWorkspaceProjectKey(candidate.path) !== stoppedKey)
    .filter((candidate) => candidate.running && candidate.state !== 'error')
    .sort((left, right) =>
      (right.lastOpenedAt ?? '').localeCompare(left.lastOpenedAt ?? '')
    )
  const mru = runningOthers[0]
  if (mru) return { path: mru.path, name: mru.name || mru.path }
  if (chat && normalizeWorkspaceProjectKey(chat.path) !== stoppedKey) {
    return { path: chat.path, name: chat.name || chat.path }
  }
  return null
}

async function archiveWorkspaceThread(workspacePath: string, threadId: string): Promise<void> {
  try {
    await window.api.workspace.archiveThread(workspacePath, threadId)
  } catch (err) {
    // Best-effort: warm secondary connections normally succeed; a rare failure
    // just leaves the row in place instead of surfacing a modal.
    console.error('Failed to archive project thread:', err)
  }
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

/** Callers gate `visible` on their own hover/focus state, so the chevron only appears while their header or row is hovered. */
function CollapseChevron({
  collapsed,
  visible,
  size = 14
}: {
  collapsed: boolean
  visible: boolean
  size?: number
}): JSX.Element {
  return (
    <span
      aria-hidden
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        flexShrink: 0,
        color: 'var(--text-dimmed)',
        opacity: visible ? 1 : 0,
        transform: collapsed ? 'rotate(0deg)' : 'rotate(90deg)',
        transition: 'opacity 120ms ease, transform 160ms cubic-bezier(0.4, 0, 0.2, 1)'
      }}
    >
      <ChevronRight size={size} strokeWidth={2} aria-hidden />
    </span>
  )
}

function ProjectsSectionHeader({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled,
  collapsed,
  onToggle
}: {
  workspacePath: string
  localWorkspacePath?: string
  localActionsDisabled: boolean
  collapsed: boolean
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const [workspaceMenuOpen, setWorkspaceMenuOpen] = useState(false)
  const addProject = useAddProjectFlow()
  const showActions = hovered || focused || workspaceMenuOpen

  return (
    <>
    <div
      role="button"
      tabIndex={0}
      aria-expanded={!collapsed}
      aria-label={t('projectsRail.toggleSection', { section: t('projectsRail.title') })}
      onClick={onToggle}
      onKeyDown={(event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        onToggle()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setFocused(false)
        }
      }}
      style={{
        ...sidebarSectionHeaderStyle,
        position: 'relative',
      }}
    >
      <span
        style={{
          color: 'var(--text-secondary)',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)'
        }}
      >
        {t('projectsRail.title')}
      </span>
      <CollapseChevron collapsed={collapsed} visible={showActions} />
      <div
        onClick={(event) => event.stopPropagation()}
        style={{
          width: '56px',
          marginLeft: 'auto',
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
            onOpenChange={setWorkspaceMenuOpen}
          />
        )}
        <IconButton
          icon={<FolderPlus size={15} aria-hidden />}
          label={t('projectsRail.addProject')}
          tooltipLabel={t('projectsRail.addProject')}
          size={24}
          radius={6}
          className="dc-thread-list-icon-button"
          disabled={addProject.busy}
          onClick={() => addProject.beginCreate()}
        />
      </div>
    </div>
    {addProject.dialog}
    </>
  )
}

/**
 * The `Recents` group deliberately has no folder icon, project path, or project
 * actions — only a `New chat` affordance and the usual thread rows.
 */
function ChatsSection({
  chat,
  interactive,
  foreground,
  foregroundThreads,
  foregroundPinnedThreadIds,
  searchQuery,
  opening,
  collapsed,
  onToggle
}: {
  chat: WorkspaceProjectSummary
  interactive: boolean
  foreground: boolean
  foregroundThreads: ThreadSummary[]
  foregroundPinnedThreadIds: string[]
  searchQuery: string
  opening: boolean
  collapsed: boolean
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const chatKey = projectIdentity(chat)
  const rawThreads = interactive
    ? foregroundThreads
    : orderSubAgentsAfterParents(filterProjectThreads(chat, searchQuery))
  const pinnedIds = interactive ? foregroundPinnedThreadIds : (chat.pinnedThreadIds ?? [])
  const { pinnedThreads, unpinnedThreads } = partitionPinnedThreads(rawThreads, pinnedIds)
  const threads = [...pinnedThreads, ...unpinnedThreads]
  const showSkeleton = opening && threads.length === 0

  async function newChat(): Promise<void> {
    // Creating a chat targets the Chat workspace, so promote it to foreground first
    // (mirrors a project's New chat). The switch never adds it to recent Projects.
    if (!foreground) {
      await window.api.workspace.switch(chat.path)
    }
    useUIStore.getState().goToNewChat({ workspacePath: chatKey })
    setActiveMainView('conversation')
  }

  return (
    <div style={{ marginBottom: '6px' }}>
      <ChatsSectionHeader
        collapsed={collapsed}
        onToggle={onToggle}
        onNewChat={() => { void newChat() }}
      />
      <CollapsibleThreads collapsed={collapsed} marginTop={0}>
        {showSkeleton ? (
          <ProjectThreadSkeletonList />
        ) : threads.length === 0 ? (
          <ProjectHint
            label={searchQuery ? t('threadList.noSearchResults') : t('projectsRail.noChats')}
            alignment="section"
          />
        ) : (
          threads.map((thread) => (
            interactive ? (
              <ThreadEntry key={thread.id} thread={thread} />
            ) : (
              <ReadonlyThreadRow
                key={thread.id}
                thread={thread}
                project={chat}
                pinned={pinnedIds.includes(thread.id)}
              />
            )
          ))
        )}
      </CollapsibleThreads>
    </div>
  )
}

function ChatsSectionHeader({
  collapsed,
  onToggle,
  onNewChat
}: {
  collapsed: boolean
  onToggle: () => void
  onNewChat: () => void
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const showActions = hovered || focused
  return (
    <div
      role="button"
      tabIndex={0}
      aria-expanded={!collapsed}
      aria-label={t('projectsRail.toggleSection', { section: t('recentsRail.title') })}
      onClick={onToggle}
      onKeyDown={(event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        onToggle()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) {
          setFocused(false)
        }
      }}
      style={sidebarSectionHeaderStyle}
    >
      <span
        style={{
          color: 'var(--text-secondary)',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)'
        }}
      >
        {t('recentsRail.title')}
      </span>
      <CollapseChevron collapsed={collapsed} visible={showActions} />
      <div
        onClick={(event) => event.stopPropagation()}
        style={{
          marginLeft: 'auto',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'flex-end',
          opacity: showActions ? 1 : 0,
          pointerEvents: showActions ? 'auto' : 'none',
          transition: 'opacity 120ms ease'
        }}
      >
        <IconButton
          icon={<SquarePen size={15} aria-hidden />}
          label={t('sidebar.newThreadLabel')}
          tooltipLabel={t('sidebar.newThreadLabel')}
          size={24}
          radius={6}
          className="dc-thread-list-icon-button"
          onClick={onNewChat}
        />
      </div>
    </div>
  )
}

function PinnedProjectSection({
  rows,
  projects,
  renderProject,
  collapsed,
  onToggle
}: {
  rows: PinnedProjectRow[]
  projects: WorkspaceProjectSummary[]
  renderProject: (project: WorkspaceProjectSummary) => JSX.Element
  collapsed: boolean
  onToggle: () => void
}): JSX.Element {
  return (
    <div style={{ marginBottom: '8px' }}>
      <PinnedSectionHeader collapsed={collapsed} onToggle={onToggle} />
      <CollapsibleThreads collapsed={collapsed} marginTop={0}>
        {rows.map(({ project, thread, interactiveForeground }) => (
          interactiveForeground ? (
            <ThreadEntry key={`${projectIdentity(project)}:${thread.id}`} thread={thread} />
          ) : (
            <ReadonlyThreadRow
              key={`${projectIdentity(project)}:${thread.id}`}
              thread={thread}
              project={project}
              pinned
              variant="pinned"
            />
          )
        ))}
        {projects.map(renderProject)}
      </CollapsibleThreads>
    </div>
  )
}

function PinnedSectionHeader({
  collapsed,
  onToggle
}: {
  collapsed: boolean
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  return (
    <div
      role="button"
      tabIndex={0}
      aria-expanded={!collapsed}
      aria-label={t('projectsRail.toggleSection', { section: t('threadGroup.pinned') })}
      onClick={onToggle}
      onKeyDown={(event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        onToggle()
      }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '4px',
        minHeight: '28px',
        padding: '8px 8px 2px',
        cursor: 'pointer',
        userSelect: 'none'
      }}
    >
      <span
        style={{
          color: 'var(--text-secondary)',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)'
        }}
      >
        {t('threadGroup.pinned')}
      </span>
      <CollapseChevron collapsed={collapsed} visible={hovered || focused} />
    </div>
  )
}

/**
 * Shared by the expanded Projects rail (ProjectHeader) and the collapsed sidebar
 * so both render a project's identity and status identically.
 */
export function ProjectGlyph({
  project,
  collapsed,
  cold,
  active
}: {
  project: WorkspaceProjectSummary
  collapsed: boolean
  cold: boolean
  /** Foreground (currently open) workspace — gets an accent ring on its dot. */
  active: boolean
}): JSX.Element {
  const ProjectIcon = isRemoteProject(project)
    ? (project.remote?.source === 'servers' ? Server : Cloud)
    : (collapsed ? Folder : FolderOpen)
  return (
    <span style={projectIconSlotStyle}>
      <ProjectIcon
        size={15}
        strokeWidth={1.7}
        aria-hidden
        style={{ color: cold ? 'var(--text-tertiary)' : 'var(--text-dimmed)' }}
      />
      {cold ? (
        <CircleDashed
          size={9}
          strokeWidth={2.35}
          aria-hidden
          style={projectColdBadgeStyle}
        />
      ) : (
        <span
          aria-hidden
          style={{
            ...projectStatusBadgeStyle,
            backgroundColor: projectStatusDotColor(project.state),
            boxShadow: active
              ? '0 0 0 1.5px var(--bg-primary), 0 0 0 3px color-mix(in srgb, var(--accent) 85%, transparent)'
              : projectStatusBadgeStyle.boxShadow
          }}
        />
      )}
    </span>
  )
}

function ProjectHeader({
  project,
  projectKey,
  active,
  collapsed,
  activity,
  cold,
  detailThreads,
  onToggle
}: {
  project: WorkspaceProjectSummary
  projectKey: string
  /** This is the foreground (currently open) workspace. */
  active: boolean
  collapsed: boolean
  activity: ProjectActivity
  cold: boolean
  detailThreads: ThreadSummary[]
  onToggle: () => void
}): JSX.Element {
  const t = useT()
  const confirm = useConfirmDialog()
  const [hovered, setHovered] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)
  const rowRef = useRef<HTMLDivElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [menuPosition, setMenuPosition] = useState<{ top: number; left: number; width: number } | null>(null)
  const addProject = useAddProjectFlow()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const label = project.name || project.path
  const showActions = hovered || menuOpen
  const detailLabel = project.remote?.displayPath || project.remote?.endpoint || project.identityWorkspacePath || project.path
  const errorLabel = project.errorMessage || t('projectsRail.error')
  const showErrorIndicator = project.state === 'error'
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
        <div className="sidebar-entry-details-row">
          <Folder size={14} strokeWidth={1.8} aria-hidden />
          <span title={detailLabel}>{detailLabel}</span>
        </div>
      ) : (
        <>
          {detailFolders.map((folder) => (
            <ProjectDetailsActionRow
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
          <ProjectDetailsActionRow
            icon={<Settings size={14} strokeWidth={1.8} aria-hidden />}
            label={t('projectsRail.editProject')}
            ariaLabel={t('projectsRail.editProject')}
            onClick={() => addProject.beginEdit(project, active)}
          />
        </>
      )}
    </>
  )

  function updateProjectMenuPosition(): void {
    const rect = rowRef.current?.getBoundingClientRect()
    if (!rect) return
    const viewportWidth = window.innerWidth || 320
    const viewportHeight = window.innerHeight || 480
    const menuWidth = 220
    // Local projects gain an "Edit project" row; remote projects do not.
    const estimatedMenuHeight = isRemoteProject(project) ? 144 : project.running ? 298 : 210
    const left = Math.max(8, Math.min(rect.left, viewportWidth - menuWidth - 8))
    const belowTop = rect.bottom + 4
    const top = belowTop + estimatedMenuHeight > viewportHeight - 8
      ? Math.max(8, rect.top - estimatedMenuHeight - 4)
      : belowTop
    setMenuPosition({ top, left, width: menuWidth })
  }

  useEffect(() => {
    if (!menuOpen) return
    updateProjectMenuPosition()

    function handleClick(event: MouseEvent): void {
      const target = event.target as Node
      if (rowRef.current?.contains(target) || menuRef.current?.contains(target)) return
      setMenuOpen(false)
    }

    function handlePositionChange(): void {
      updateProjectMenuPosition()
    }

    document.addEventListener('mousedown', handleClick)
    window.addEventListener('resize', handlePositionChange)
    window.addEventListener('scroll', handlePositionChange, true)
    return () => {
      document.removeEventListener('mousedown', handleClick)
      window.removeEventListener('resize', handlePositionChange)
      window.removeEventListener('scroll', handlePositionChange, true)
    }
  }, [menuOpen, project])

  async function openProject(): Promise<void> {
    if (active) return
    if (isRemoteProject(project)) return
    await window.api.workspace.switch(project.path)
  }

  async function newChat(): Promise<void> {
    if (!active && !isRemoteProject(project)) {
      await window.api.workspace.switch(project.path)
    }
    useUIStore.getState().goToNewChat({ workspacePath: projectKey })
    setActiveMainView('conversation')
  }

  async function copyPath(): Promise<void> {
    await navigator.clipboard.writeText(detailLabel)
    addToast(t('projectsRail.pathCopied'), 'success')
  }

  async function removeProject(): Promise<void> {
    if (active) return
    if (isRemoteProject(project)) return
    const confirmed = await confirm({
      title: t('projectsRail.removeProjectTitle', { project: label }),
      message: t('projectsRail.removeProjectMessage'),
      confirmLabel: t('projectsRail.removeProjectConfirm'),
      cancelLabel: t('common.cancel'),
      danger: true
    })
    if (!confirmed) return
    await window.api.workspace.removeRecent(project.path)
  }

  async function disconnectRemote(): Promise<void> {
    if (!isRemoteProject(project)) return
    await window.api.workspace.disconnectRemote()
  }

  async function restartWorkspace(): Promise<void> {
    if (isRemoteProject(project)) return
    await window.api.workspace.restart(project.path)
  }

  async function stopWorkspace(): Promise<void> {
    if (isRemoteProject(project)) return
    // Stopping the foreground workspace would leave the main view on a dead
    // connection, so resolve the next target before the stop request.
    const nextTarget = active ? pickNextWorkspaceAfterStop(project.path) : null
    await window.api.workspace.stop(project.path)
    if (nextTarget) {
      try {
        await window.api.workspace.switch(nextTarget.path)
        addToast(t('projectsRail.stoppedSwitched', { project: nextTarget.name }), 'info')
      } catch (err) {
        console.error('Failed to switch workspace after stop:', err)
      }
    }
  }

  function handlePrimaryAction(): void {
    if (cold) return
    onToggle()
  }

  function handleDoubleClick(): void {
    if (!cold) return
    void openProject()
  }

  function handleKeyDown(event: KeyboardEvent<HTMLDivElement>): void {
    if (event.key !== 'Enter' && event.key !== ' ') return
    event.preventDefault()
    if (cold) void openProject()
    else onToggle()
  }

  return (
    <>
    <SidebarEntryDetailsCard
      label={label}
      width={320}
      interactive
      disabled={menuOpen}
      content={projectDetailsContent}
      wrapperStyle={{ width: '100%' }}
    >
    <div
      ref={rowRef}
      className="dotcraft-sidebar-row-radius"
      role="button"
      tabIndex={0}
      aria-expanded={cold ? undefined : !collapsed}
      aria-current={active ? 'true' : undefined}
      aria-label={label}
      onClick={handlePrimaryAction}
      onDoubleClick={handleDoubleClick}
      onKeyDown={handleKeyDown}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onContextMenu={(event) => {
        event.preventDefault()
        updateProjectMenuPosition()
        setMenuOpen(true)
      }}
      style={{
        position: 'relative',
        display: 'grid',
        gridTemplateColumns: `18px minmax(0, 1fr) ${actionColumnWidth}`,
        alignItems: 'center',
        gap: '8px',
        minHeight: SIDEBAR_ROW_MIN_HEIGHT,
        // 4px side inset matches the sidebar nav rows and thread rows so all
        // sidebar buttons share the same width and right-edge alignment.
        margin: '2px 4px',
        padding: '2px 6px 2px 12px',
        borderRadius: 'var(--sidebar-row-radius)',
        backgroundColor: hovered ? 'var(--sidebar-control-hover)' : 'transparent',
        cursor: 'pointer',
        userSelect: 'none'
      }}
    >
      <ProjectGlyph project={project} collapsed={collapsed} cold={cold} active={active} />
      <div style={{ minWidth: 0, display: 'flex', alignItems: 'center', gap: '4px' }}>
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
        {!cold && <CollapseChevron collapsed={collapsed} visible={hovered} size={13} />}
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
        {showActions ? (
          <>
            <IconButton
              icon={<SquarePen size={14} aria-hidden />}
              label={t('projectsRail.newChat')}
              tooltipLabel={t('projectsRail.newChat')}
              size={24}
              radius={6}
              className="dc-thread-list-icon-button"
              onClick={() => { void newChat() }}
            />
            <IconButton
              icon={<MoreHorizontal size={15} aria-hidden />}
              label={t('projectsRail.moreActions')}
              tooltipLabel={t('projectsRail.moreActions')}
              size={24}
              radius={6}
              className="dc-thread-list-icon-button"
              aria-expanded={menuOpen}
              onClick={() => {
                if (!menuOpen) updateProjectMenuPosition()
                setMenuOpen((open) => !open)
              }}
            />
            {showErrorIndicator && <ProjectErrorIndicator label={errorLabel} />}
          </>
        ) : showErrorIndicator ? (
          <ProjectErrorIndicator label={errorLabel} />
        ) : collapsed && activity === 'running' ? (
          <span style={projectStatusIndicatorSlotStyle}>
            <RunningSpinner label={t('threadEntry.turnRunning')} />
          </span>
        ) : collapsed && activity === 'waiting' ? (
          <span style={projectStatusIndicatorSlotStyle}>
            <span style={projectWaitingDotStyle} aria-label={t('projectsRail.awaitingResponse')} />
          </span>
        ) : null}
      </div>
      {menuOpen && menuPosition && typeof document !== 'undefined' && createPortal(
        <LayerBoundary>
        <div
          ref={menuRef}
          role="menu"
          aria-label={t('projectsRail.moreActions')}
          style={{
            ...projectMenuStyle,
            top: menuPosition.top,
            left: menuPosition.left,
            width: menuPosition.width
          }}
          onClick={(event) => event.stopPropagation()}
        >
          {!isRemoteProject(project) && (
            <ProjectMenuItem icon={<ExternalLink size={14} aria-hidden />} label={t('projectsRail.openProject')} onClick={() => { setMenuOpen(false); void openProject() }} />
          )}
          <ProjectMenuItem
            icon={<Pin size={14} fill={project.pinned ? 'currentColor' : 'none'} aria-hidden />}
            label={project.pinned ? t('projectsRail.unpinProject') : t('projectsRail.pinProject')}
            onClick={() => { setMenuOpen(false); void toggleProjectPinned() }}
          />
          {!isRemoteProject(project) && (
            <ProjectMenuItem icon={<FolderOpen size={14} aria-hidden />} label={t('workspaceHeader.openInExplorer')} onClick={() => { setMenuOpen(false); void window.api.shell.openPath(project.path) }} />
          )}
          <ProjectMenuItem icon={<Copy size={14} aria-hidden />} label={t('projectsRail.copyPath')} onClick={() => { setMenuOpen(false); void copyPath() }} />
          {!isRemoteProject(project) && (
            <ProjectMenuItem icon={<Settings size={14} aria-hidden />} label={t('projectsRail.editProject')} onClick={() => { setMenuOpen(false); addProject.beginEdit(project, active) }} />
          )}
          {!isRemoteProject(project) && project.running && (
            <ProjectMenuItem icon={<RotateCw size={14} aria-hidden />} label={t('projectsRail.restartWorkspace')} onClick={() => { setMenuOpen(false); void restartWorkspace() }} />
          )}
          {!isRemoteProject(project) && project.running && (
            <ProjectMenuItem icon={<Square size={14} aria-hidden />} label={t('projectsRail.stopWorkspace')} onClick={() => { setMenuOpen(false); void stopWorkspace() }} />
          )}
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
              onClick={() => { setMenuOpen(false); void removeProject() }}
            />
          )}
        </div>
        </LayerBoundary>,
        document.body
      )}
    </div>
    </SidebarEntryDetailsCard>
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

const PROJECT_COLLAPSE_MS = 260
const PROJECT_COLLAPSE_TRANSITION =
  `grid-template-rows ${PROJECT_COLLAPSE_MS}ms cubic-bezier(0.4, 0, 0.2, 1), opacity 180ms ease`

/**
 * The wrapper stays mounted so both directions animate via `grid-template-rows:
 * 1fr ↔ 0fr`; only the rows inside unmount, after the collapse transition.
 * `transitionend` drives that unmount, with a timer for when it never fires.
 */
function CollapsibleThreads({
  collapsed,
  marginTop = -2,
  children
}: {
  collapsed: boolean
  /**
   * Top margin (px) used to cancel the preceding header's bottom margin. Defaults
   * to -2 for the per-project list; group-level wrappers pass 0 because their
   * section headers carry no bottom margin.
   */
  marginTop?: number
  children: ReactNode
}): JSX.Element {
  const [present, setPresent] = useState(!collapsed)
  const [open, setOpen] = useState(!collapsed)
  const closeTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const rafRef = useRef<number | null>(null)

  useEffect(() => {
    const clearClose = (): void => {
      if (closeTimerRef.current != null) {
        clearTimeout(closeTimerRef.current)
        closeTimerRef.current = null
      }
    }
    const clearRaf = (): void => {
      if (rafRef.current != null) {
        cancelAnimationFrame(rafRef.current)
        rafRef.current = null
      }
    }
    clearClose()
    clearRaf()
    if (!collapsed) {
      setPresent(true)
      // The wrapper is already mounted at 0fr; flip to 1fr next frame so the
      // height transitions in instead of snapping.
      rafRef.current = requestAnimationFrame(() => {
        setOpen(true)
        rafRef.current = null
      })
    } else {
      setOpen(false)
      closeTimerRef.current = setTimeout(() => {
        setPresent(false)
        closeTimerRef.current = null
      }, PROJECT_COLLAPSE_MS + 80)
    }
    return () => {
      clearClose()
      clearRaf()
    }
  }, [collapsed])

  return (
    <div
      style={{
        display: 'grid',
        gridTemplateRows: open ? '1fr' : '0fr',
        opacity: open ? 1 : 0,
        marginTop: `${marginTop}px`,
        transition: PROJECT_COLLAPSE_TRANSITION
      }}
      onTransitionEnd={(event) => {
        if (event.propertyName === 'grid-template-rows' && collapsed) {
          if (closeTimerRef.current != null) {
            clearTimeout(closeTimerRef.current)
            closeTimerRef.current = null
          }
          setPresent(false)
        }
      }}
    >
      <div style={{ overflow: 'hidden', minWidth: 0 }} inert={collapsed}>
        {present ? children : null}
      </div>
    </div>
  )
}

function ProjectThreadSkeletonList(): JSX.Element {
  const t = useT()
  const rows = [
    { title: '68%', time: 30 },
    { title: '54%', time: 38 },
    { title: '74%', time: 24 },
    { title: '46%', time: 34 }
  ]

  return (
    <div
      role="status"
      aria-busy="true"
      aria-label={t('threadList.loading')}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: '2px',
        paddingTop: '2px',
        paddingBottom: '4px'
      }}
    >
      {rows.map((row, index) => (
        <div
          key={index}
          data-testid="project-thread-skeleton-row"
          style={{
            display: 'grid',
            gridTemplateColumns: 'minmax(0, 1fr) minmax(24px, max-content)',
            alignItems: 'center',
            columnGap: '7px',
            width: 'calc(100% - 32px)',
            minHeight: '30px',
            margin: '2px 10px 2px 22px',
            padding: '6px 12px',
            boxSizing: 'border-box'
          }}
        >
          <Skeleton width={row.title} height={12} />
          <Skeleton width={row.time} height={10} />
        </div>
      ))}
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

function ProjectDetailsActionRow({
  icon,
  label,
  title,
  ariaLabel,
  affordance = false,
  onClick
}: {
  icon: ReactNode
  label: string
  title?: string
  ariaLabel: string
  affordance?: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <button
      type="button"
      className="sidebar-entry-details-row sidebar-entry-details-action-row"
      aria-label={ariaLabel}
      title={title}
      onClick={onClick}
    >
      {icon}
      <span>{label}</span>
      {affordance && (
        <ArrowUpRight
          className="sidebar-entry-details-action-row__affordance"
          size={14}
          aria-hidden
        />
      )}
    </button>
  )
}

function ProjectHint({
  label,
  alignment = 'thread'
}: {
  label: string
  alignment?: 'thread' | 'section'
}): JSX.Element {
  return (
    <div
      style={{
        padding: alignment === 'section'
          ? `4px ${SIDEBAR_RAIL_CONTENT_INSET} 8px`
          : '4px 16px 8px 32px',
        color: 'var(--text-dimmed)',
        fontSize: 'var(--type-secondary-size)',
        fontWeight: 400,
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
  variant?: 'project' | 'pinned'
}): JSX.Element {
  const locale = useLocale()
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const setPendingProjectThreadOpen = useUIStore((s) => s.setPendingProjectThreadOpen)
  const running = isThreadRunning(thread)
  const waiting = isThreadWaiting(thread)
  const displayName = thread.displayName ?? t('sidebar.newConversation')
  const relativeTime = formatRelativeTime(thread.lastActiveAt, new Date(), locale)
  const subAgent = isSubAgentThread(thread)
  const threadDetails = useThreadEntryDetails({
    thread: { ...thread, displayName },
    project,
    projectName: project.name || project.path,
    relativeTime,
    origin: threadOriginBadge({ thread, isSubAgent: subAgent, t })
  })
  const rowProjectKey = projectIdentity(project)
  const subAgentDepth = getSubAgentDepth(thread)
  const [hovered, setHovered] = useState(false)
  const [pinButtonFocused, setPinButtonFocused] = useState(false)
  const [archiveButtonFocused, setArchiveButtonFocused] = useState(false)
  const [contextMenu, setContextMenu] = useState<ContextMenuPosition | null>(null)
  // Pin/archive route to the target workspace connection by path, so they only
  // apply to local secondary / Chats rows. Remote rows keep the static marker.
  const supportsLocalActions = !subAgent && !isRemoteProject(project)
  const isPinned = pinned
  const showPinAction =
    supportsLocalActions && (hovered || pinButtonFocused || isPinned)
  const showArchiveAction =
    supportsLocalActions && (hovered || archiveButtonFocused)
  // On hover the archive action replaces the status content in a compact 24px
  // slot; otherwise the relative-time / waiting badge slot may grow to fit.
  const statusColumn = showArchiveAction
    ? '24px'
    : running
      ? '24px'
      : waiting
        ? 'minmax(74px, max-content)'
        : 'minmax(24px, max-content)'
  const statusSlotWidth = showArchiveAction ? '24px' : running ? '24px' : 'max-content'
  const statusSlotMinWidth = '24px'
  const statusSlotJustifySelf = showArchiveAction ? 'center' : running ? 'center' : 'end'
  // Center the time/badge within its (>=24px) slot so secondary-project rows line
  // up with the foreground ThreadEntry's centered status slot.
  const statusContentJustify = 'center'

  async function copySessionId(): Promise<void> {
    await navigator.clipboard.writeText(thread.id)
    addToast(t('toast.copied'), 'success')
  }

  async function copyDeepLink(): Promise<void> {
    if (isRemoteProject(project)) return
    await navigator.clipboard.writeText(buildWorkspaceOpenDeepLink(project.path, thread.id))
    addToast(t('toast.copied'), 'success')
  }

  async function openThread(): Promise<void> {
    if (!isRemoteProject(project)) {
      if (project.state !== 'foreground') {
        setPendingProjectThreadOpen({
          projectKey: rowProjectKey,
          workspacePath: project.path,
          threadId: thread.id
        })
        try {
          await window.api.workspace.switch(project.path)
        } catch (err) {
          useUIStore.getState().clearPendingProjectThreadOpen(rowProjectKey, thread.id)
          console.error('Failed to switch workspace for project thread:', err)
        }
        return
      }
    }
    setActiveMainView('conversation')
    useThreadStore.getState().setActiveThreadId(thread.id)
  }

  const statusContent = (
    <span
      aria-hidden={showArchiveAction}
      style={{
        display: showArchiveAction ? 'none' : 'inline-flex',
        alignItems: 'center',
        justifyContent: statusContentJustify,
        width: running ? '100%' : 'auto',
        color: 'var(--text-dimmed)',
        fontSize: 'var(--type-secondary-size)',
        lineHeight: 'var(--type-secondary-line-height)',
        whiteSpace: 'nowrap',
        overflow: 'hidden',
        textOverflow: 'clip',
        opacity: showArchiveAction ? 0 : 1
      }}
    >
      {running ? (
        <RunningSpinner
          label={t('threadEntry.turnRunning')}
          testId={`project-thread-running-indicator-${rowProjectKey}-${thread.id}`}
        />
      ) : waiting ? (
        <span style={readonlyWaitingBadgeStyle}>{t('projectsRail.awaitingResponse')}</span>
      ) : (
        relativeTime
      )}
    </span>
  )

  return (
    <>
    <SidebarEntryDetailsCard
      label={displayName}
      width={240}
      content={threadDetails.content}
      onOpen={threadDetails.onOpen}
      wrapperStyle={{ width: '100%' }}
    >
      <ThreadRowLayout
        isSubAgent={subAgent}
        subAgentDepth={subAgentDepth}
        canPin={!subAgent}
        subAgentLabel={t('threadEntry.subAgent')}
        rowTestId={`project-thread-entry-${rowProjectKey}-${thread.id}`}
        gridTestId={`project-thread-layout-${rowProjectKey}-${thread.id}`}
        statusTestId={`project-thread-status-${rowProjectKey}-${thread.id}`}
        leading={
          subAgent ? undefined : (
            <span
              data-testid={`project-thread-leading-${rowProjectKey}-${thread.id}`}
              style={readonlyLeadingSlotStyle}
            >
              {supportsLocalActions ? (
                <IconButton
                  icon={<PinIcon filled={isPinned} />}
                  label={isPinned ? t('threadEntry.unpin') : t('threadEntry.pin')}
                  tooltipLabel={isPinned ? t('threadEntry.unpin') : t('threadEntry.pin')}
                  tooltipPlacement="right"
                  size={22}
                  radius={6}
                  className="dc-thread-list-icon-button"
                  aria-pressed={isPinned}
                  data-testid={`project-thread-pin-${rowProjectKey}-${thread.id}`}
                  onClick={(e) => {
                    e.stopPropagation()
                    toggleWorkspacePin(project.path, thread.id, project.pinnedThreadIds ?? [])
                  }}
                  onFocus={() => setPinButtonFocused(true)}
                  onBlur={() => setPinButtonFocused(false)}
                  style={{
                    cursor: showPinAction ? 'pointer' : 'default',
                    opacity: showPinAction ? 1 : 0,
                    pointerEvents: showPinAction ? 'auto' : 'none',
                    transition: 'opacity 120ms ease, color 120ms ease'
                  }}
                />
              ) : (
                pinned && (
                  <ReadonlyPinnedIcon
                    label={t('threadGroup.pinned')}
                    testId={`project-thread-pinned-${rowProjectKey}-${thread.id}`}
                  />
                )
              )}
            </span>
          )
        }
        name={displayName}
        nameStyle={{ fontWeight: 'var(--type-ui-weight)' }}
        statusColumn={statusColumn}
        statusSlotWidth={statusSlotWidth}
        statusSlotMinWidth={statusSlotMinWidth}
        statusJustifySelf={statusSlotJustifySelf}
        status={statusContent}
        statusExtra={
          supportsLocalActions ? (
            <IconButton
              icon={<Archive size={14} strokeWidth={2} aria-hidden="true" />}
              label={t('threadEntry.archive')}
              tooltipLabel={t('threadEntry.archive')}
              tooltipPlacement="right"
              size={24}
              radius={8}
              className="dc-thread-list-icon-button"
              data-testid={`project-thread-archive-${rowProjectKey}-${thread.id}`}
              onClick={(e) => {
                e.stopPropagation()
                void archiveWorkspaceThread(project.path, thread.id)
              }}
              onFocus={() => setArchiveButtonFocused(true)}
              onBlur={() => setArchiveButtonFocused(false)}
              style={{
                borderRadius: 'var(--sidebar-icon-control-radius)',
                cursor: showArchiveAction ? 'pointer' : 'default',
                position: 'absolute',
                right: 0,
                top: '50%',
                transform: 'translateY(-50%)',
                opacity: showArchiveAction ? 1 : 0,
                pointerEvents: showArchiveAction ? 'auto' : 'none',
                transition: 'opacity 120ms ease, color 120ms ease',
                zIndex: 2
              }}
            />
          ) : undefined
        }
        containerStyle={{ cursor: 'pointer', textAlign: 'left' }}
        containerProps={{
          onClick: () => void openThread(),
          onContextMenu: (event) => {
            event.preventDefault()
            setContextMenu({ x: event.clientX, y: event.clientY })
          },
          onMouseEnter: (e) => {
            setHovered(true)
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor =
              'var(--sidebar-control-hover)'
          },
          onMouseLeave: (e) => {
            setHovered(false)
            ;(e.currentTarget as HTMLDivElement).style.backgroundColor = 'transparent'
          }
        }}
      />
    </SidebarEntryDetailsCard>
    {contextMenu && (
      <ContextMenu
        position={contextMenu}
        onClose={() => setContextMenu(null)}
        items={[
          {
            label: t('threadEntry.copySessionId'),
            icon: <Copy size={14} aria-hidden />,
            onClick: () => void copySessionId()
          },
          ...(!isRemoteProject(project)
            ? [
                {
                  label: t('threadEntry.copyDeepLink'),
                  icon: <ExternalLink size={14} aria-hidden />,
                  onClick: () => void copyDeepLink()
                }
              ]
            : [])
        ]}
      />
    )}
    </>
  )
}

const readonlyLeadingSlotStyle: CSSProperties = {
  width: '18px',
  minWidth: '18px',
  height: '24px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

function ReadonlyPinnedIcon({
  label,
  testId
}: {
  label: string
  testId: string
}): JSX.Element {
  return (
    <ActionTooltip label={label} placement="right">
      <span
        aria-label={label}
        data-testid={testId}
        style={{
          width: '18px',
          minWidth: '18px',
          height: '24px',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--text-secondary)',
          flexShrink: 0
        }}
      >
        <PinIcon filled />
      </span>
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

// Non-hover status indicators (spinner / waiting dot / error icon) sit in the
// same 24px box as the action buttons they replace, so they stay centered under
// the rightmost action button and line up with the thread rows' status slot.
const projectStatusIndicatorSlotStyle: CSSProperties = {
  width: '24px',
  height: '24px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  flexShrink: 0
}

const projectIconSlotStyle: CSSProperties = {
  position: 'relative',
  width: '18px',
  height: '18px',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center'
}

const projectColdBadgeStyle: CSSProperties = {
  position: 'absolute',
  right: 0,
  bottom: 0,
  color: 'color-mix(in srgb, var(--text-primary) 62%, var(--bg-primary))',
  backgroundColor: 'var(--bg-secondary)',
  borderRadius: '999px',
  boxShadow: [
    '0 0 0 1px var(--bg-secondary)',
    '0 0 0 2px color-mix(in srgb, var(--text-primary) 18%, transparent)'
  ].join(', ')
}

/** Cold/stopped projects use the dashed-circle badge instead of this dot. */
const projectStatusBadgeStyle: CSSProperties = {
  position: 'absolute',
  right: 0,
  bottom: 0,
  width: '7px',
  height: '7px',
  borderRadius: '999px',
  boxShadow: '0 0 0 1.5px var(--bg-primary)'
}

function projectStatusDotColor(state: WorkspaceProjectState): string {
  if (state === 'error') return 'var(--error)'
  if (state === 'connecting') return 'var(--warning)'
  return 'var(--success)'
}

function projectFolderPaths(project: WorkspaceProjectSummary): string[] {
  const folders = [project.path, ...(project.secondaryFolders ?? [])]
  return folders.filter((folder, index) => {
    const key = normalizeWorkspaceProjectKey(folder)
    return key.length > 0 &&
      folders.findIndex((candidate) => normalizeWorkspaceProjectKey(candidate) === key) === index
  })
}

const sidebarSectionHeaderStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '4px',
  minHeight: '28px',
  padding: `8px ${SIDEBAR_RAIL_CONTENT_INSET} 2px`,
  cursor: 'pointer',
  userSelect: 'none'
}

const projectMenuStyle: CSSProperties = {
  position: 'fixed',
  zIndex: 1000,
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
