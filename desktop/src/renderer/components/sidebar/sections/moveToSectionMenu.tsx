import { Layers, Plus } from 'lucide-react'
import type { SidebarSectionItem } from '../../../../shared/sidebarSections'
import { useT } from '../../../contexts/LocaleContext'
import { useSidebarSectionsStore } from '../../../stores/sidebarSectionsStore'
import type { ContextMenuEntry, ContextMenuItem } from '../../ui/ContextMenu'
import { openSectionDialog } from './SectionDialog'
import { sectionOfItem } from './sectionPlacement'

export function useMoveToSectionMenuEntry(
  item: SidebarSectionItem | null,
  unpin?: () => Promise<void> | void
): ContextMenuItem | null {
  const t = useT()
  const sections = useSidebarSectionsStore((s) => s.sections)
  if (!item) return null
  const current = sectionOfItem(sections, item)
  const submenu: ContextMenuEntry[] = [
    ...sections.map((section) => ({
      label: section.name,
      selection: 'checkbox' as const,
      checked: section.id === current?.id,
      onClick: () => {
        const store = useSidebarSectionsStore.getState()
        if (section.id === current?.id) {
          store.removeItems([item])
          return
        }
        void Promise.resolve(unpin?.()).then(() => store.moveItems(section.id, [item]))
      }
    })),
    ...(sections.length > 0 ? [{ type: 'separator' as const }] : []),
    {
      label: t('sidebarSections.newSectionEllipsis'),
      icon: <Plus size={14} aria-hidden />,
      onClick: () => openSectionDialog({ items: [item], afterSectionId: current?.id, beforeMove: unpin })
    }
  ]
  return {
    label: t('sidebarSections.moveTo'),
    icon: <Layers size={14} aria-hidden />,
    onClick: () => {},
    submenu
  }
}

export function newSectionMenuItem(t: ReturnType<typeof useT>): ContextMenuItem {
  return {
    label: t('sidebarSections.newSection'),
    icon: <Plus size={14} aria-hidden />,
    onClick: () => openSectionDialog()
  }
}
