import { useState } from 'react'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { useT } from '../../contexts/LocaleContext'
import type { ThreadSummary } from '../../types/thread'
import type { ContextMenuEntry } from '../ui/ContextMenu'
import { ReadonlyThreadRow } from './ReadonlyThreadRow'
import { ThreadEntry } from './ThreadEntry'
import { ReorderableThreadRow } from './ThreadReorder'
import { CollapsibleThreads, SectionOptionsMenu, SidebarSectionHeader } from './SidebarSectionParts'
import {
  filterProjectThreads,
  isForegroundThreadListForProject,
  isProjectForeground,
  projectIdentity
} from './projectThreads'
import {
  applyManualOrder,
  orderSubAgentsAfterParents,
  partitionPinnedThreads,
  sortThreadsByRecentActivity,
  type ThreadDropPlacement
} from './threadOrdering'

export interface PinnedProjectRow {
  project: WorkspaceProjectSummary
  thread: ThreadSummary
  interactiveForeground: boolean
}

export function collectPinnedProjectRows(
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

export function orderPinnedRows(
  rows: PinnedProjectRow[],
  mode: SidebarThreadSortMode,
  manualOrder: readonly string[]
): PinnedProjectRow[] {
  if (mode === 'manual') return applyManualOrder(rows, manualOrder, (row) => row.thread.id)
  const rowById = new Map(rows.map((row) => [row.thread.id, row]))
  return sortThreadsByRecentActivity(rows.map((row) => row.thread)).map((thread) => rowById.get(thread.id)!)
}

export function PinnedSectionHeader({
  collapsed,
  onToggle,
  sortMode,
  onSortChange
}: {
  collapsed: boolean
  onToggle: () => void
  sortMode?: SidebarThreadSortMode
  onSortChange?: (mode: SidebarThreadSortMode) => void
}): JSX.Element {
  const t = useT()
  const [menuOpen, setMenuOpen] = useState(false)
  const items: ContextMenuEntry[] = sortMode && onSortChange
    ? [
        { type: 'label', label: t('threadList.sortPinnedBy') },
        {
          label: t('threadList.sortLastUpdated'),
          selection: 'radio',
          checked: sortMode === 'updated',
          onClick: () => onSortChange('updated')
        },
        {
          label: t('threadList.sortManual'),
          selection: 'radio',
          checked: sortMode === 'manual',
          onClick: () => onSortChange('manual')
        }
      ]
    : []

  return (
    <SidebarSectionHeader
      label={t('threadGroup.pinned')}
      toggleLabel={t('projectsRail.toggleSection', { section: t('threadGroup.pinned') })}
      collapsed={collapsed}
      onToggle={onToggle}
      actionsOpen={menuOpen}
      actions={items.length > 0 ? (
        <SectionOptionsMenu label={t('threadGroup.pinnedOptions')} items={items} onOpenChange={setMenuOpen} />
      ) : null}
    />
  )
}

export function PinnedProjectSection({
  rows,
  projects,
  renderProject,
  collapsed,
  onToggle,
  sortMode,
  onSortChange,
  reorderEnabled,
  onMove
}: {
  rows: PinnedProjectRow[]
  projects: WorkspaceProjectSummary[]
  renderProject: (project: WorkspaceProjectSummary) => JSX.Element
  collapsed: boolean
  onToggle: () => void
  sortMode: SidebarThreadSortMode
  onSortChange?: (mode: SidebarThreadSortMode) => void
  reorderEnabled: boolean
  onMove: (movedId: string, targetId: string, placement: ThreadDropPlacement) => void
}): JSX.Element {
  return (
    <div style={{ marginBottom: '8px' }}>
      <PinnedSectionHeader
        collapsed={collapsed}
        onToggle={onToggle}
        sortMode={sortMode}
        onSortChange={onSortChange}
      />
      <CollapsibleThreads collapsed={collapsed} marginTop={0}>
        {rows.map(({ project, thread, interactiveForeground }) => (
          <ReorderableThreadRow
            key={`${projectIdentity(project)}:${thread.id}`}
            listId="pinned"
            threadId={thread.id}
            enabled={reorderEnabled}
            onMove={onMove}
          >
            {interactiveForeground ? (
              <ThreadEntry thread={thread} />
            ) : (
              <ReadonlyThreadRow thread={thread} project={project} pinned variant="pinned" />
            )}
          </ReorderableThreadRow>
        ))}
        {projects.map(renderProject)}
      </CollapsibleThreads>
    </div>
  )
}
