import { useState, type ReactNode } from 'react'
import { Pencil, Trash2 } from 'lucide-react'
import type { SidebarSection } from '../../../../shared/sidebarSections'
import { useT } from '../../../contexts/LocaleContext'
import { useSidebarSectionsStore } from '../../../stores/sidebarSectionsStore'
import type { ContextMenuEntry } from '../../ui/ContextMenu'
import { CollapsibleThreads, ProjectHint, SectionOptionsMenu, SidebarSectionHeader } from '../SidebarSectionParts'
import { openSectionDialog } from './SectionDialog'
import { SidebarSectionDropTarget } from './SidebarSectionDropTarget'

export function CustomSidebarSection({
  section,
  empty,
  children
}: {
  section: SidebarSection
  empty: boolean
  children: ReactNode
}): JSX.Element {
  const t = useT()
  const [menuOpen, setMenuOpen] = useState(false)
  const collapsed = section.collapsed === true
  const items: ContextMenuEntry[] = [
    {
      label: t('sidebarSections.edit'),
      icon: <Pencil size={14} aria-hidden />,
      onClick: () => openSectionDialog({ section })
    },
    { type: 'separator' },
    {
      label: t('sidebarSections.remove'),
      icon: <Trash2 size={14} aria-hidden />,
      onClick: () => useSidebarSectionsStore.getState().removeSection(section.id)
    }
  ]

  return (
    <SidebarSectionDropTarget sectionId={section.id}>
      <SidebarSectionHeader
        label={section.name}
        toggleLabel={t('projectsRail.toggleSection', { section: section.name })}
        collapsed={collapsed}
        onToggle={() => useSidebarSectionsStore.getState().setSectionCollapsed(section.id, !collapsed)}
        actionsOpen={menuOpen}
        actions={(
          <SectionOptionsMenu
            label={t('sidebarSections.options', { section: section.name })}
            items={items}
            onOpenChange={setMenuOpen}
          />
        )}
      />
      <CollapsibleThreads collapsed={collapsed} marginTop={0}>
        {empty ? <ProjectHint label={t('sidebarSections.empty')} alignment="section" /> : children}
      </CollapsibleThreads>
    </SidebarSectionDropTarget>
  )
}
