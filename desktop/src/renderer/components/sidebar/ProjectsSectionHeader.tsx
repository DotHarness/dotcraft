import { useState } from 'react'
import { ArrowDownUp, ChevronsDownUp, ChevronsUpDown, FolderPlus } from 'lucide-react'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import { useT } from '../../contexts/LocaleContext'
import { useAddProjectFlow } from '../projects/AddProject'
import type { ContextMenuEntry } from '../ui/ContextMenu'
import { IconButton } from '../ui/IconButton'
import { SidebarSectionHeader, sortModeMenuItems } from './SidebarSectionParts'
import { WorkspaceOptionsMenu } from './WorkspaceHeader'

export function ProjectsSectionHeader({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled,
  collapsed,
  onToggle,
  sortMode,
  onSortChange,
  projectSortMode,
  onProjectSortChange,
  allProjectsCollapsed,
  onAllProjectsCollapsedChange
}: {
  workspacePath: string
  localWorkspacePath?: string
  localActionsDisabled: boolean
  collapsed: boolean
  onToggle: () => void
  sortMode: SidebarThreadSortMode
  onSortChange?: (mode: SidebarThreadSortMode) => void
  projectSortMode: SidebarThreadSortMode
  onProjectSortChange?: (mode: SidebarThreadSortMode) => void
  allProjectsCollapsed: boolean
  onAllProjectsCollapsedChange?: (collapsed: boolean) => void
}): JSX.Element {
  const t = useT()
  const [menuOpen, setMenuOpen] = useState(false)
  const addProject = useAddProjectFlow()
  const sortSubmenu: ContextMenuEntry[] = [
    ...(onProjectSortChange
      ? [
          { type: 'label' as const, label: t('threadList.sortGroupProjects') },
          ...sortModeMenuItems(t, projectSortMode, onProjectSortChange)
        ]
      : []),
    ...(onProjectSortChange && onSortChange ? [{ type: 'separator' as const }] : []),
    ...(onSortChange
      ? [
          { type: 'label' as const, label: t('threadList.sortGroupChats') },
          ...sortModeMenuItems(t, sortMode, onSortChange)
        ]
      : [])
  ]
  const leadingItems: ContextMenuEntry[] = [
    ...(sortSubmenu.length > 0
      ? [{
          label: t('threadList.sortBy'),
          icon: <ArrowDownUp size={14} aria-hidden />,
          onClick: () => {},
          submenu: sortSubmenu
        }]
      : []),
    ...(onAllProjectsCollapsedChange
      ? [{
          label: allProjectsCollapsed ? t('threadList.expandAllProjects') : t('threadList.collapseAllProjects'),
          icon: allProjectsCollapsed
            ? <ChevronsUpDown size={14} aria-hidden />
            : <ChevronsDownUp size={14} aria-hidden />,
          onClick: () => onAllProjectsCollapsedChange(!allProjectsCollapsed)
        }]
      : [])
  ]
  const showMenu = workspacePath.trim().length > 0 || leadingItems.length > 0

  return (
    <>
      <SidebarSectionHeader
        label={t('projectsRail.title')}
        toggleLabel={t('projectsRail.toggleSection', { section: t('projectsRail.title') })}
        collapsed={collapsed}
        onToggle={onToggle}
        actionsOpen={menuOpen}
        actions={
          <>
            {showMenu && (
              <WorkspaceOptionsMenu
                workspacePath={workspacePath}
                localWorkspacePath={localWorkspacePath}
                localActionsDisabled={localActionsDisabled}
                leadingItems={leadingItems}
                onOpenChange={setMenuOpen}
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
          </>
        }
      />
      {addProject.dialog}
    </>
  )
}
