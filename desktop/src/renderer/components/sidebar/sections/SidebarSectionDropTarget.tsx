import type { ReactNode } from 'react'
import { useSidebarSectionsStore } from '../../../stores/sidebarSectionsStore'
import { useUIStore } from '../../../stores/uiStore'
import { addToast } from '../../../stores/toastStore'
import { clearThreadReorder } from '../ThreadListRow'
import { sectionOfItem } from './sectionPlacement'
import { clearSidebarSectionDrag, useSidebarSectionDragStore } from './sidebarSectionDrag'

type DropTargetProps = { children: ReactNode } & (
  | { sectionId: string; kind?: never }
  | { sectionId?: never; kind: 'project' | 'thread' }
)

export function SidebarSectionDropTarget({ sectionId, kind, children }: DropTargetProps): JSX.Element {
  const source = useSidebarSectionDragStore((state) => state.source)
  const target = useSidebarSectionDragStore((state) => state.target)
  const sections = useSidebarSectionsStore((state) => state.sections)
  const currentSection = source ? sectionOfItem(sections, source.item) : undefined
  const accepts = source != null && (sectionId
    ? currentSection?.id !== sectionId
    : source.item.kind === kind && currentSection != null)
  const id = sectionId ? `section:${sectionId}` : `default:${kind}`
  const active = accepts && target === id

  return (
    <div className="dc-sidebar-section-drop-target" data-sidebar-section-id={sectionId}
      data-drop-active={active || undefined}
      onDragOver={(event) => {
        if (!accepts) return
        event.preventDefault()
        event.stopPropagation()
        event.dataTransfer.dropEffect = 'move'
        if (target !== id) useSidebarSectionDragStore.setState({ target: id })
      }}
      onDragLeave={(event) => {
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return
        if (target === id) useSidebarSectionDragStore.setState({ target: null })
      }}
      onDrop={async (event) => {
        if (!accepts || !source) return
        event.preventDefault()
        event.stopPropagation()
        clearSidebarSectionDrag()
        clearThreadReorder()
        try {
          await source.beforeMove?.()
          const store = useSidebarSectionsStore.getState()
          if (sectionId) store.moveItems(sectionId, [source.item])
          else {
            store.removeItems([source.item])
            const ui = useUIStore.getState()
            if (kind === 'project') ui.setProjectsSectionCollapsed(false)
            else ui.setChatsSectionCollapsed(false)
          }
        } catch (error) {
          addToast(error instanceof Error ? error.message : String(error), 'error')
        }
      }}>
      {children}
    </div>
  )
}
