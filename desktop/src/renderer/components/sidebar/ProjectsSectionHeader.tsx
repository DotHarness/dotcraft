import { useState } from 'react'
import { FolderPlus } from 'lucide-react'
import type { SidebarThreadSortMode } from '../../../shared/sidebarThreadOrder'
import { useT } from '../../contexts/LocaleContext'
import { useAddProjectFlow } from '../projects/AddProject'
import { IconButton } from '../ui/IconButton'
import { SidebarSectionHeader, sortChatsMenuEntry } from './SidebarSectionParts'
import { WorkspaceOptionsMenu } from './WorkspaceHeader'

export function ProjectsSectionHeader({
  workspacePath,
  localWorkspacePath,
  localActionsDisabled,
  collapsed,
  onToggle,
  sortMode,
  onSortChange
}: {
  workspacePath: string
  localWorkspacePath?: string
  localActionsDisabled: boolean
  collapsed: boolean
  onToggle: () => void
  sortMode: SidebarThreadSortMode
  onSortChange?: (mode: SidebarThreadSortMode) => void
}): JSX.Element {
  const t = useT()
  const [menuOpen, setMenuOpen] = useState(false)
  const addProject = useAddProjectFlow()
  const sortItems = onSortChange ? [sortChatsMenuEntry(t, sortMode, onSortChange)] : []
  const showMenu = workspacePath.trim().length > 0 || sortItems.length > 0

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
                leadingItems={sortItems}
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
