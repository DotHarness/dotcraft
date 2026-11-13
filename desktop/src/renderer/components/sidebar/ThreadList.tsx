import { useState, type CSSProperties, type ReactNode } from 'react'
import { useShallow } from 'zustand/react/shallow'
import { useT } from '../../contexts/LocaleContext'
import { useDragDropStore } from '../../stores/dragDropStore'
import { useThreadStore, selectFilteredThreads } from '../../stores/threadStore'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { useUIStore } from '../../stores/uiStore'
import { projectOrderKey, useSidebarThreadOrderStore } from '../../stores/sidebarThreadOrderStore'
import type { ThreadSummary } from '../../types/thread'
import { isSubAgentThread } from '../../utils/subAgentThreads'
import { Skeleton } from '../ui/Skeleton'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { ThreadEntry } from './ThreadEntry'
import { ReadonlyThreadRow } from './ReadonlyThreadRow'
import { workspacePathName } from './ThreadEntryDetails'
import { deviceProjectRef } from './sshRemoteProject'
import { ProjectsSectionHeader } from './ProjectsSectionHeader'
import { getProjectActivity, ProjectHeader } from './ProjectHeader'
import { useSidebarSectionsStore } from '../../stores/sidebarSectionsStore'
import { buildSectionPlacement } from './sections/sectionPlacement'
import { CustomSidebarSection } from './sections/CustomSidebarSection'
import { SectionDialogHost } from './sections/SectionDialog'
import { SidebarSectionDropTarget } from './sections/SidebarSectionDropTarget'
import { sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { sidebarSectionItemKey, type SidebarSection } from '../../../shared/sidebarSections'
import { RecentsSection, type RecentsRow } from './RecentsSection'
import { ThreadListRow, useReorderItem } from './ThreadListRow'
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
  orderProjectsBySortMode,
  projectIdentity,
  projectOrderIdentity,
  visibleProjectThreads
} from './projectThreads'
import {
  excludePinnedThreadTrees,
  moveThreadId,
  orderSubAgentsAfterParents,
  orderThreadsBySortMode,
  partitionPinnedThreads,
  sortThreadsByRecentActivity,
  topLevelThreadIds,
  type ThreadDropPlacement
} from './threadOrdering'

const PROJECT_THREAD_PREVIEW_COUNT = 5

interface ThreadListProps {
  workspacePath?: string
  foregroundOpening?: boolean
  openingWorkspacePath?: string
}

export function ThreadList({
  workspacePath,
  foregroundOpening = false,
  openingWorkspacePath
}: ThreadListProps = {}): JSX.Element {
  const t = useT()
  const { threadList, threadListProjectKey, searchQuery, loading, pinnedThreadIds, activeThreadId } = useThreadStore()
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
    projectSort,
    projectOrder,
    collapsedProjectIds,
    setRecentsSort,
    setProjectsSort,
    setPinnedSort,
    setRecentsShowProjects,
    setRecentsOrder,
    setPinnedOrder,
    setProjectOrder,
    setProjectSort,
    setProjectListOrder,
    setProjectsCollapsed
  } = useSidebarThreadOrderStore()
  const sections = useSidebarSectionsStore((s) => s.sections)
  const [expandedThreadLists, setExpandedThreadLists] = useState<Set<string>>(() => new Set())
  // The selector allocates an array, so Zustand needs shallow equality for a stable snapshot.
  const filteredThreads = useThreadStore(useShallow(selectFilteredThreads))
  const dragActive = useDragDropStore((s) => s.active)
  const dragHintTitle =
    dragActive?.kind === 'automation-task' ? dragActive.title : null

  const showChats = chat != null
  const hasProjectRows = projects.length > 0
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
    const chatIsForeground =
      chat != null &&
      isProjectForeground(chat, effectiveForegroundProjectId, effectiveForegroundWorkspacePath)
    const chatForegroundListMatches =
      chatIsForeground && isForegroundThreadListForProject(threadListProjectKey, projectIdentity(chat!))
    const projectsForRender = foregroundProject || chatIsForeground
      ? projects
      : [
          {
            projectId: effectiveForegroundProjectId || effectiveForegroundWorkspacePath,
            kind: 'local',
            path: effectiveForegroundWorkspacePath,
            identityWorkspacePath: effectiveForegroundWorkspacePath,
            name: workspacePathName(effectiveForegroundWorkspacePath) ?? effectiveForegroundWorkspacePath,
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
    const searching = searchQuery.trim().length > 0
    const sectionPlacement = buildSectionPlacement(sections)
    const foregroundListThreads = threadList.filter(
      (thread) => thread.status !== 'archived' && !isSubAgentThread(thread)
    )

    const projectListSource = (
      project: WorkspaceProjectSummary,
      query: string
    ): { live: boolean; threads: ThreadSummary[]; pinnedIds: string[]; excludedIds: string[] } => {
      const live =
        isProjectForeground(project, effectiveForegroundProjectId, effectiveForegroundWorkspacePath) &&
        isForegroundThreadListForProject(threadListProjectKey, projectIdentity(project))
      const pinnedIds = live ? pinnedThreadIds : (project.pinnedThreadIds ?? [])
      return {
        live,
        threads: live ? filterThreadsByQuery(foregroundListThreads, query) : filterProjectThreads(project, query),
        pinnedIds,
        excludedIds: [...pinnedIds, ...sectionPlacement.threadIds(projectIdentity(project))]
      }
    }

    const projectLastActivity = (project: WorkspaceProjectSummary): number => {
      const latest = Math.max(0, ...projectListSource(project, '').threads.map((thread) => Date.parse(thread.lastActiveAt) || 0))
      return latest || Date.parse(project.lastOpenedAt ?? '') || 0
    }
    const orderedProjects = orderProjectsBySortMode(projectsForRender, projectSort, projectOrder, projectLastActivity)
    const orderedProjectKeys = orderedProjects.map(projectOrderIdentity)
    const pinnedProjects = orderedProjects.filter((project) => project.pinned === true)
    const ordinaryProjects = orderedProjects.filter((project) =>
      project.pinned !== true && !sectionPlacement.hasProject(projectIdentity(project))
    )
    const collapsedProjectKeys = new Set(collapsedProjectIds)
    const collapsibleProjectKeys = ordinaryProjects
      .filter((project) => !isColdProject(project))
      .map(projectOrderIdentity)
    const allProjectsCollapsed =
      collapsibleProjectKeys.length > 0 && collapsibleProjectKeys.every((key) => collapsedProjectKeys.has(key))

    const changeProjectSort = (mode: SidebarThreadSortMode): void => {
      if (mode === projectSort) return
      setProjectSort(mode, mode === 'manual' ? orderedProjectKeys : undefined)
    }

    const moveProject = (movedKey: string, targetKey: string, placement: ThreadDropPlacement): void => {
      setProjectListOrder(moveThreadId(orderedProjectKeys, movedKey, targetKey, placement))
    }

    const snapshotProjectOrders = (): Record<string, string[]> => {
      const snapshots: Record<string, string[]> = {}
      for (const project of projectsForRender) {
        const { threads, excludedIds } = projectListSource(project, '')
        const ids = topLevelThreadIds(sortThreadsByRecentActivity(excludePinnedThreadTrees(threads, excludedIds)))
        if (ids.length > 0) snapshots[projectIdentity(project)] = ids
      }
      return snapshots
    }

    const projectsSortable = projectsForRender.some((project) => {
      const { threads, excludedIds } = projectListSource(project, '')
      return excludePinnedThreadTrees(threads, excludedIds).length > 1
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
      for (const thread of excludePinnedThreadTrees(chatPartition.unpinnedThreads, sectionPlacement.threadIds(chatKey))) {
        candidates.set(thread.id, { thread, project: chat, interactive: chatForegroundListMatches })
      }
      if (recentsShowProjects) {
        for (const project of projectsForRender) {
          const { live, threads, excludedIds } = projectListSource(project, query)
          for (const thread of excludePinnedThreadTrees(threads, excludedIds)) {
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
        const { threads, excludedIds } = projectListSource(project, '')
        return excludePinnedThreadTrees(threads, excludedIds)
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

    interface SectionSlot {
      listId: string
      itemKey: string
      onMove: (movedKey: string, targetKey: string, placement: ThreadDropPlacement) => void
    }

    const renderProjectBlock = (project: WorkspaceProjectSummary, slot?: SectionSlot): JSX.Element => {
      const projectKey = projectIdentity(project)
      const orderKey = projectOrderIdentity(project)
      const isForeground = isProjectForeground(project, effectiveForegroundProjectId, effectiveForegroundWorkspacePath)
      const {
        live: foregroundListMatchesProject,
        threads: rawProjectThreads,
        pinnedIds: projectPinnedIds,
        excludedIds: projectExcludedIds
      } = projectListSource(project, searchQuery)
      const openingProject = isForeground && (
        foregroundOpening ||
        project.state === 'connecting' ||
        (loading && !foregroundListMatchesProject)
      )
      const cold = isColdProject(project) && !openingProject
      const detailThreads = foregroundListMatchesProject
        ? orderSubAgentsAfterParents(visibleProjectThreads(threadList))
        : orderSubAgentsAfterParents(filterProjectThreads(project, ''))
      const projectThreads = orderSubAgentsAfterParents(orderThreadsBySortMode(
        excludePinnedThreadTrees(rawProjectThreads, projectExcludedIds),
        projectsSort,
        projectOrders[projectOrderKey(projectKey)] ?? []
      ))
      const projectReorderIds = topLevelThreadIds(projectThreads)
      const reorderEnabled = projectsSort === 'manual' && !searching
      const threadListExpanded = searching || expandedThreadLists.has(orderKey)
      const threadListTruncatable = projectThreads.length > PROJECT_THREAD_PREVIEW_COUNT
      const visibleThreads = threadListExpanded || !threadListTruncatable
        ? projectThreads
        : projectThreads.filter((thread, index) =>
          index < PROJECT_THREAD_PREVIEW_COUNT || thread.id === activeThreadId
        )
      const activity = getProjectActivity(detailThreads)
      const showProjectThreadSkeleton =
        openingProject &&
        (foregroundOpening || project.state === 'connecting' || (projectThreads.length === 0 && !project.threadListLoaded))
      const collapsed = cold || (!showProjectThreadSkeleton && collapsedProjectKeys.has(orderKey))
      return (
        <ProjectListItem
          key={projectKey}
          listId={slot?.listId ?? (project.pinned ? 'projects:pinned' : 'projects:ordinary')}
          projectKey={slot?.itemKey ?? orderKey}
          reorderable={slot ? !searching : projectSort === 'manual' && !searching}
          onMove={slot?.onMove ?? moveProject}
          header={<ProjectHeader
            project={project}
            active={isForeground}
            collapsed={collapsed}
            activity={activity}
            cold={cold}
            detailThreads={detailThreads}
            archivableThreads={foregroundListMatchesProject || !isRemoteProject(project) || deviceProjectRef(project) != null
              ? excludePinnedThreadTrees(detailThreads, projectPinnedIds)
              : []}
            live={foregroundListMatchesProject}
            onToggle={() => {
              if (cold) return
              setProjectsCollapsed([orderKey], !collapsedProjectKeys.has(orderKey))
            }}
          />}
        >
          <CollapsibleThreads collapsed={collapsed}>
            {showProjectThreadSkeleton ? (
              <ProjectThreadSkeletonList />
            ) : (
              <>
                {project.threadListLoaded && rawProjectThreads.length === 0 && (
                  <ProjectHint
                    label={searchQuery
                      ? t('threadList.noSearchResults')
                      : t('projectsRail.noChats')}
                  />
                )}
                {projectThreads.length > 0 && (
                  <div role="list" aria-label={project.name || project.path}>
                    {visibleThreads.map((thread) => (
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
                {threadListTruncatable && !searching && (
                  <ProjectThreadListToggle
                    expanded={threadListExpanded}
                    onToggle={() =>
                      setExpandedThreadLists((current) => {
                        const next = new Set(current)
                        if (next.has(orderKey)) next.delete(orderKey)
                        else next.add(orderKey)
                        return next
                      })
                    }
                  />
                )}
              </>
            )}
          </CollapsibleThreads>
        </ProjectListItem>
      )
    }
    const sectionProjects = [...projectsForRender, ...(chat ? [chat] : [])]
    const renderSectionItems = (section: SidebarSection): JSX.Element[] => {
      const listId = `section:${section.id}`
      const itemKeys = section.items.map(sidebarSectionItemKey)
      const onMove = (movedKey: string, targetKey: string, placement: ThreadDropPlacement): void =>
        useSidebarSectionsStore.getState().reorderItems(section.id, moveThreadId(itemKeys, movedKey, targetKey, placement))
      return section.items.flatMap((item, index) => {
        const slot: SectionSlot = { listId, itemKey: itemKeys[index], onMove }
        const project = sectionProjects.find((candidate) =>
          sameWorkspaceProjectKey(projectIdentity(candidate), item.projectKey)
        )
        if (!project) return []
        if (item.kind === 'project') {
          if (project.pinned || project.kind === 'chat') return []
          if (searching && projectListSource(project, searchQuery).threads.length === 0) return []
          return [renderProjectBlock(project, slot)]
        }
        const { live, threads, pinnedIds } = projectListSource(project, searchQuery)
        const thread = threads.find((candidate) => candidate.id === item.threadId)
        if (!thread || pinnedIds.includes(thread.id)) return []
        return [
          <ThreadListRow
            key={`${projectIdentity(project)}:${thread.id}`}
            listId={listId}
            threadId={slot.itemKey}
            home={project.kind === 'chat'}
            reorderable={!searching}
            onMove={onMove}
          >
            {live ? <ThreadEntryWrapper thread={thread} /> : <ReadonlyThreadRow thread={thread} project={project} />}
          </ThreadListRow>
        ]
      })
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
        <SectionDialogHost />
        {dragHintTitle !== null && <DragHint title={dragHintTitle} />}
        {(pinnedThreadRows.length > 0 || pinnedProjects.length > 0) && (
          <PinnedProjectSection
            rows={pinnedThreadRows}
            projects={pinnedProjects}
            renderProject={(project) => renderProjectBlock(project)}
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
        {sections.map((section) => {
          const rows = renderSectionItems(section)
          if (searching && rows.length === 0) return null
          return (
            <CustomSidebarSection key={section.id} section={section} empty={rows.length === 0}>
              {rows}
            </CustomSidebarSection>
          )
        })}
        {showProjects && (
          <SidebarSectionDropTarget kind="project">
            <ProjectsSectionHeader
              collapsed={projectsSectionCollapsed}
              onToggle={() => setProjectsSectionCollapsed(!projectsSectionCollapsed)}
              sortMode={projectsSort}
              onSortChange={projectsSortable ? changeProjectsSort : undefined}
              projectSortMode={projectSort}
              onProjectSortChange={orderedProjects.length > 1 ? changeProjectSort : undefined}
              allProjectsCollapsed={allProjectsCollapsed}
              onAllProjectsCollapsedChange={collapsibleProjectKeys.length > 0
                ? (collapsed) => setProjectsCollapsed(collapsibleProjectKeys, collapsed)
                : undefined}
            />
            <CollapsibleThreads collapsed={projectsSectionCollapsed} marginTop={0}>
              {projectsForRender.length === 0 && (
                <ProjectHint label={t('projectsRail.noProjects')} alignment="section" />
              )}
              {ordinaryProjects.map((project) => renderProjectBlock(project))}
            </CollapsibleThreads>
          </SidebarSectionDropTarget>
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

function ProjectListItem({
  listId,
  projectKey,
  reorderable,
  onMove,
  header,
  children
}: {
  listId: string
  projectKey: string
  reorderable: boolean
  onMove: (movedKey: string, targetKey: string, placement: ThreadDropPlacement) => void
  header: ReactNode
  children: ReactNode
}): JSX.Element {
  const { dragging, dropPlacement, sourceProps, targetProps } = useReorderItem(listId, projectKey, reorderable, onMove)
  return (
    <div
      className="dc-thread-list-row"
      data-dragging={dragging ? 'true' : undefined}
      data-drop-placement={dropPlacement ?? undefined}
      style={{ marginBottom: '6px' }}
      {...targetProps}
    >
      <div {...sourceProps}>{header}</div>
      {children}
    </div>
  )
}

function ProjectThreadListToggle({ expanded, onToggle }: { expanded: boolean; onToggle: () => void }): JSX.Element {
  const t = useT()
  return (
    <button
      type="button"
      className="dc-project-thread-list-toggle"
      aria-expanded={expanded}
      onClick={onToggle}
    >
      {expanded ? t('threadList.showLess') : t('threadList.showMore')}
    </button>
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


const emptyStyle: CSSProperties = {
  flex: 1,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px 16px'
}
