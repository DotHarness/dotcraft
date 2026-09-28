import { useState } from 'react'
import { SquarePen } from 'lucide-react'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import type { ThreadSummary } from '../../types/thread'
import type { ContextMenuEntry } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
import { ReadonlyThreadRow } from './ReadonlyThreadRow'
import { ThreadEntry } from './ThreadEntry'
import { ThreadListRow } from './ThreadListRow'
import type { ThreadDropPlacement } from './threadOrdering'
import { projectIdentity } from './projectThreads'
import {
  CollapsibleThreads,
  ProjectHint,
  ProjectThreadSkeletonList,
  SectionOptionsMenu,
  SidebarSectionHeader,
  sortChatsMenuEntry
} from './SidebarSectionParts'

export interface RecentsRow {
  key: string
  thread: ThreadSummary
  project: WorkspaceProjectSummary
  interactive: boolean
  pinned: boolean
}

/** The `Recents` group deliberately has no folder icon, project path, or project actions. */
export function RecentsSection({
  chat,
  foreground,
  rows,
  searchQuery,
  opening,
  collapsed,
  onToggle,
  sortMode,
  onSortChange,
  showProjects,
  onShowProjectsChange,
  reorderEnabled,
  onMove
}: {
  chat: WorkspaceProjectSummary
  foreground: boolean
  rows: RecentsRow[]
  searchQuery: string
  opening: boolean
  collapsed: boolean
  onToggle: () => void
  sortMode: SidebarThreadSortMode
  onSortChange?: (mode: SidebarThreadSortMode) => void
  showProjects: boolean
  onShowProjectsChange?: (show: boolean) => void
  reorderEnabled: boolean
  onMove: (movedId: string, targetId: string, placement: ThreadDropPlacement) => void
}): JSX.Element {
  const t = useT()
  const setActiveMainView = useUIStore((s) => s.setActiveMainView)
  const [menuOpen, setMenuOpen] = useState(false)
  const chatKey = projectIdentity(chat)
  const showSkeleton = opening && rows.length === 0
  const optionItems: ContextMenuEntry[] = [
    ...(onSortChange ? [sortChatsMenuEntry(t, sortMode, onSortChange)] : []),
    ...(onSortChange && onShowProjectsChange ? [{ type: 'separator' } as const] : []),
    ...(onShowProjectsChange
      ? [
          { type: 'label', label: t('recentsRail.show') } as const,
          {
            label: t('projectsRail.title'),
            selection: 'checkbox',
            checked: showProjects,
            onClick: () => onShowProjectsChange(!showProjects)
          } as const
        ]
      : [])
  ]

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
      <SidebarSectionHeader
        label={t('recentsRail.title')}
        toggleLabel={t('projectsRail.toggleSection', { section: t('recentsRail.title') })}
        collapsed={collapsed}
        onToggle={onToggle}
        actionsOpen={menuOpen}
        actions={
          <>
            {optionItems.length > 0 && (
              <SectionOptionsMenu
                label={t('recentsRail.options')}
                onOpenChange={setMenuOpen}
                items={optionItems}
              />
            )}
            <IconButton
              icon={<SquarePen size={15} aria-hidden />}
              label={t('sidebar.newThreadLabel')}
              tooltipLabel={t('sidebar.newThreadLabel')}
              size={24}
              radius={6}
              className="dc-thread-list-icon-button"
              onClick={() => { void newChat() }}
            />
          </>
        }
      />
      <CollapsibleThreads collapsed={collapsed} marginTop={0}>
        {showSkeleton ? (
          <ProjectThreadSkeletonList />
        ) : rows.length === 0 ? (
          <ProjectHint
            label={searchQuery ? t('threadList.noSearchResults') : t('projectsRail.noChats')}
            alignment="section"
          />
        ) : (
          <div role="list" aria-label={t('recentsRail.title')}>
            {rows.map((row) => (
              <ThreadListRow
                key={row.key}
                listId="recents"
                threadId={row.thread.id}
                home={row.project.kind === 'chat'}
                reorderable={reorderEnabled && !row.pinned}
                onMove={onMove}
              >
                {row.interactive ? (
                  <ThreadEntry thread={row.thread} />
                ) : (
                  <ReadonlyThreadRow thread={row.thread} project={row.project} pinned={row.pinned} />
                )}
              </ThreadListRow>
            ))}
          </div>
        )}
      </CollapsibleThreads>
    </div>
  )
}
