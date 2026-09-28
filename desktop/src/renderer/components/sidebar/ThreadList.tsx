import { useEffect, useRef, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { useShallow } from 'zustand/react/shallow'
import {
  AlertCircle,
  ArrowUpRight,
  Cloud,
  Copy,
  CircleDashed,
  ExternalLink,
  Folder,
  FolderOpen,
  LogOut,
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
import { projectOrderKey, useSidebarThreadOrderStore } from '../../stores/sidebarThreadOrderStore'
import type { ThreadSummary } from '../../types/thread'
import { isSubAgentThread } from '../../utils/subAgentThreads'
import { Skeleton } from '../ui/Skeleton'
import { Spinner } from '../ui/Spinner'
import { ActionTooltip } from '../ui/ActionTooltip'
import { IconButton } from '../ui/IconButton'
import { MoreActionsButton } from '../ui/MoreActionsButton'
import { DisclosureChevron } from '../ui/DisclosureChevron'
import { useConfirmDialog } from '../ui/ConfirmDialog'
import type { WorkspaceProjectSummary, WorkspaceProjectState } from '../../../shared/workspaceProjects'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { addToast } from '../../stores/toastStore'
import { ThreadEntry } from './ThreadEntry'
import { useAddProjectFlow } from '../projects/AddProject'
import { SIDEBAR_ROW_MIN_HEIGHT } from './sidebarNavRowStyles'
import { SidebarEntryDetailsCard } from './SidebarEntryDetailsCard'
import { ReadonlyThreadRow } from './ReadonlyThreadRow'
import { ProjectsSectionHeader } from './ProjectsSectionHeader'
import { RecentsSection, type RecentsRow } from './RecentsSection'
import { ThreadListRow } from './ThreadListRow'
import {
  collectPinnedProjectRows,
  orderPinnedRows,
  PinnedProjectSection,
  PinnedSectionHeader
} from './PinnedSection'
import { CollapsibleThreads, ProjectHint, ProjectThreadSkeletonList } from './SidebarSectionParts'
import {
  filterProjectThreads,
  filterThreadsByQuery,
  isColdProject,
  isForegroundThreadListForProject,
  isProjectForeground,
  isRemoteProject,
  isThreadRunning,
  isThreadWaiting,
  projectIdentity,
  visibleProjectThreads
} from './projectThreads'
import {
  excludePinnedThreadTrees,
  moveThreadId,
  orderSubAgentsAfterParents,
  orderThreadsBySortMode,
  partitionPinnedThreads,
  sortThreadsByRecentActivity,
  topLevelThreadIds
} from './threadOrdering'

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
  const {
    recentsSort,
    projectsSort,
    pinnedSort,
    recentsShowProjects,
    recentsOrder,
    pinnedOrder,
    projectOrders,
    setRecentsSort,
    setProjectsSort,
    setPinnedSort,
    setRecentsShowProjects,
    setRecentsOrder,
    setPinnedOrder,
    setProjectOrder
  } = useSidebarThreadOrderStore()
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
    sameWorkspaceProjectKey(chat.path, workspacePath || foregroundWorkspacePath || foregroundProjectId)
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
      ? normalizeWorkspaceProjectKey(openingWorkspacePath || workspacePath || '')
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
    const collectPinnedRows = (query: string) => collectPinnedProjectRows(
      projectsForRender,
      effectiveForegroundProjectId,
      effectiveForegroundWorkspacePath,
      threadListProjectKey,
      orderedThreads,
      pinnedThreadIds,
      query
    )
    const pinnedThreadRows = orderPinnedRows(collectPinnedRows(searchQuery), pinnedSort, pinnedOrder)
    const pinnedSortable = collectPinnedRows('').length > 1
    const pinnedProjects = projectsForRender.filter((project) => project.pinned === true)
    const ordinaryProjects = projectsForRender.filter((project) => project.pinned !== true)
    const searching = searchQuery.trim().length > 0
    const foregroundListThreads = threadList.filter(
      (thread) => thread.status !== 'archived' && !isSubAgentThread(thread)
    )

    const projectListSource = (
      project: WorkspaceProjectSummary,
      query: string
    ): { live: boolean; threads: ThreadSummary[]; pinnedIds: string[] } => {
      const live =
        isProjectForeground(project, effectiveForegroundProjectId, effectiveForegroundWorkspacePath) &&
        isForegroundThreadListForProject(threadListProjectKey, projectIdentity(project))
      return {
        live,
        threads: live ? filterThreadsByQuery(foregroundListThreads, query) : filterProjectThreads(project, query),
        pinnedIds: live ? pinnedThreadIds : (project.pinnedThreadIds ?? [])
      }
    }

    const snapshotProjectOrders = (): Record<string, string[]> => {
      const snapshots: Record<string, string[]> = {}
      for (const project of projectsForRender) {
        const { threads, pinnedIds } = projectListSource(project, '')
        const ids = topLevelThreadIds(sortThreadsByRecentActivity(excludePinnedThreadTrees(threads, pinnedIds)))
        if (ids.length > 0) snapshots[projectIdentity(project)] = ids
      }
      return snapshots
    }

    const projectsSortable = projectsForRender.some((project) => {
      const { threads, pinnedIds } = projectListSource(project, '')
      return excludePinnedThreadTrees(threads, pinnedIds).length > 1
    })

    const changeProjectsSort = (mode: SidebarThreadSortMode): void => {
      if (mode === projectsSort) return
      setProjectsSort(mode, mode === 'manual' ? snapshotProjectOrders() : undefined)
    }

    const buildRecentsRows = (
      query: string,
      mode: SidebarThreadSortMode,
      manualOrder: readonly string[]
    ): RecentsRow[] => {
      if (!chat) return []
      const chatKey = projectIdentity(chat)
      const chatThreads = chatForegroundListMatches
        ? filterThreadsByQuery(foregroundListThreads, query)
        : filterProjectThreads(chat, query)
      const chatPinnedIds = chatForegroundListMatches ? pinnedThreadIds : (chat.pinnedThreadIds ?? [])
      const chatPartition = partitionPinnedThreads(chatThreads, chatPinnedIds)
      const pinnedRows: RecentsRow[] = chatPartition.pinnedThreads.map((thread) => ({
        key: `${chatKey}:${thread.id}`,
        thread,
        project: chat,
        interactive: chatForegroundListMatches,
        pinned: true
      }))
      const candidates = new Map<string, Omit<RecentsRow, 'key' | 'pinned'>>()
      for (const thread of chatPartition.unpinnedThreads) {
        candidates.set(thread.id, { thread, project: chat, interactive: chatForegroundListMatches })
      }
      if (recentsShowProjects) {
        for (const project of projectsForRender) {
          const { live, threads, pinnedIds } = projectListSource(project, query)
          for (const thread of excludePinnedThreadTrees(threads, pinnedIds)) {
            candidates.set(thread.id, { thread, project, interactive: live })
          }
        }
      }
      const ordered = orderThreadsBySortMode(
        [...candidates.values()].map((candidate) => candidate.thread),
        mode,
        manualOrder
      )
      return [
        ...pinnedRows,
        ...ordered.map((thread) => {
          const candidate = candidates.get(thread.id)!
          return {
            ...candidate,
            key: `${projectIdentity(candidate.project)}:${thread.id}`,
            pinned: false
          }
        })
      ]
    }

    const changeRecentsSort = (mode: SidebarThreadSortMode): void => {
      if (mode === recentsSort) return
      const snapshot = mode === 'manual'
        ? topLevelThreadIds(buildRecentsRows('', 'updated', []).filter((row) => !row.pinned).map((row) => row.thread))
        : undefined
      setRecentsSort(mode, snapshot)
    }

    const changeRecentsShowProjects = (show: boolean): void => {
      setRecentsShowProjects(show)
      if (!show || recentsSort !== 'manual') return
      const saved = new Set(recentsOrder)
      const projectThreads = projectsForRender.flatMap((project) => {
        const { threads, pinnedIds } = projectListSource(project, '')
        return excludePinnedThreadTrees(threads, pinnedIds)
      })
      const appended = topLevelThreadIds(sortThreadsByRecentActivity(projectThreads))
        .filter((id) => !saved.has(id))
      if (appended.length > 0) setRecentsOrder([...recentsOrder, ...appended])
    }

    const recentsRows = buildRecentsRows(searchQuery, recentsSort, recentsOrder)
    const recentsReorderIds = topLevelThreadIds(
      recentsRows.filter((row) => !row.pinned).map((row) => row.thread)
    )
    const recentsSortable =
      buildRecentsRows('', recentsSort, recentsOrder).filter((row) => !row.pinned).length > 1
    const recentsCanShowProjects = recentsShowProjects || projectsForRender.length > 0

    const renderProjectBlock = (project: WorkspaceProjectSummary): JSX.Element => {
      const projectKey = projectIdentity(project)
      const isForeground = isProjectForeground(project, effectiveForegroundProjectId, effectiveForegroundWorkspacePath)
      const {
        live: foregroundListMatchesProject,
        threads: rawProjectThreads,
        pinnedIds: projectPinnedIds
      } = projectListSource(project, searchQuery)
      const openingProject = isForeground && (
        foregroundOpening ||
        project.state === 'connecting' ||
        (loading && !foregroundListMatchesProject)
      )
      const cold = isColdProject(project) && !openingProject
      const collapsed = cold || (!openingProject && collapsedProjects.has(projectKey))
      const detailThreads = foregroundListMatchesProject
        ? orderSubAgentsAfterParents(visibleProjectThreads(threadList))
        : orderSubAgentsAfterParents(filterProjectThreads(project, ''))
      const projectThreads = orderSubAgentsAfterParents(orderThreadsBySortMode(
        excludePinnedThreadTrees(rawProjectThreads, projectPinnedIds),
        projectsSort,
        projectOrders[projectOrderKey(projectKey)] ?? []
      ))
      const projectReorderIds = topLevelThreadIds(projectThreads)
      const reorderEnabled = projectsSort === 'manual' && !searching
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
                {projectThreads.length > 0 && (
                  <div role="list" aria-label={project.name || project.path}>
                    {projectThreads.map((thread) => (
                      <ThreadListRow
                        key={thread.id}
                        listId={`project:${projectKey}`}
                        threadId={thread.id}
                        reorderable={reorderEnabled}
                        onMove={(movedId, targetId, placement) =>
                          setProjectOrder(projectKey, moveThreadId(projectReorderIds, movedId, targetId, placement))
                        }
                      >
                        {isForeground ? (
                          <ThreadEntryWrapper thread={thread} />
                        ) : (
                          <ReadonlyThreadRow thread={thread} project={project} />
                        )}
                      </ThreadListRow>
                    ))}
                  </div>
                )}
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
            sortMode={pinnedSort}
            onSortChange={pinnedSortable ? setPinnedSort : undefined}
            reorderEnabled={pinnedSort === 'manual' && !searching}
            onMove={(movedId, targetId, placement) =>
              setPinnedOrder(moveThreadId(
                pinnedThreadRows.map((row) => row.thread.id),
                movedId,
                targetId,
                placement
              ))
            }
          />
        )}
        {showProjects && (
          <ProjectsSectionHeader
            workspacePath={hasProjectRows || !chatIsCurrentWorkspace ? (workspacePath || foregroundWorkspacePath) : ''}
            localWorkspacePath={localWorkspacePath}
            localActionsDisabled={localActionsDisabled}
            collapsed={projectsSectionCollapsed}
            onToggle={() => setProjectsSectionCollapsed(!projectsSectionCollapsed)}
            sortMode={projectsSort}
            onSortChange={projectsSortable ? changeProjectsSort : undefined}
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
          <RecentsSection
            chat={chat}
            foreground={chatIsForeground}
            rows={recentsRows}
            searchQuery={searchQuery}
            opening={chatIsForeground && (foregroundOpening || chat.state === 'connecting')}
            collapsed={chatsSectionCollapsed}
            onToggle={() => setChatsSectionCollapsed(!chatsSectionCollapsed)}
            sortMode={recentsSort}
            onSortChange={recentsSortable ? changeRecentsSort : undefined}
            showProjects={recentsShowProjects}
            onShowProjectsChange={recentsCanShowProjects ? changeRecentsShowProjects : undefined}
            reorderEnabled={recentsSort === 'manual' && !searching}
            onMove={(movedId, targetId, placement) =>
              setRecentsOrder(moveThreadId(recentsReorderIds, movedId, targetId, placement))
            }
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


function getProjectActivity(threads: ThreadSummary[]): ProjectActivity {
  if (threads.some(isThreadRunning)) return 'running'
  if (threads.some(isThreadWaiting)) return 'waiting'
  return null
}

type ProjectActivity = 'running' | 'waiting' | null

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
  const [menuOpen, setMenuOpen] = useState(false)
  const rowRef = useRef<HTMLDivElement>(null)
  const menuRef = useRef<HTMLDivElement>(null)
  const [menuPosition, setMenuPosition] = useState<{ top: number; left: number; width: number } | null>(null)
  const addProject = useAddProjectFlow()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const label = project.name || project.path
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
      className="dotcraft-sidebar-row-radius dc-project-row"
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
              onClick={() => { void newChat() }}
            />
            <MoreActionsButton
              label={t('projectsRail.moreActions')}
              size={24}
              radius={6}
              iconSize={15}
              tooltipPlacement="top"
              className="dc-thread-list-icon-button"
              open={menuOpen}
              onClick={() => {
                if (!menuOpen) updateProjectMenuPosition()
                setMenuOpen((open) => !open)
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

const emptyStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px 16px'
}
