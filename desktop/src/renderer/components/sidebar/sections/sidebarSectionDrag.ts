import type { DragEvent, HTMLAttributes } from 'react'
import { create } from 'zustand'
import { sidebarSectionItemKey, type SidebarSectionItem } from '../../../../shared/sidebarSections'

const SECTION_ITEM_MIME = 'application/x-dotcraft-sidebar-section-item'

interface SectionDrag {
  item: SidebarSectionItem
  beforeMove?: () => Promise<void> | void
}

export const useSidebarSectionDragStore = create<{
  source: SectionDrag | null
  target: string | null
}>(() => ({ source: null, target: null }))

export function clearSidebarSectionDrag(): void {
  useSidebarSectionDragStore.setState({ source: null, target: null })
}

export function useSidebarSectionDrag(
  item: SidebarSectionItem | null,
  beforeMove?: () => Promise<void> | void
): {
  dragging: boolean
  sourceProps: Pick<HTMLAttributes<HTMLElement>, 'draggable' | 'onDragStart' | 'onDragEnd'>
} {
  const key = item ? sidebarSectionItemKey(item) : null
  const dragging = useSidebarSectionDragStore((state) =>
    key != null && state.source != null && sidebarSectionItemKey(state.source.item) === key
  )
  return {
    dragging,
    sourceProps: {
      draggable: item != null || undefined,
      onDragStart(event: DragEvent<HTMLElement>) {
        if (!item) return
        event.dataTransfer.setData(SECTION_ITEM_MIME, sidebarSectionItemKey(item))
        const effect = event.dataTransfer.effectAllowed
        event.dataTransfer.effectAllowed = effect === 'link' || effect === 'linkMove' ? 'linkMove' : 'move'
        useSidebarSectionDragStore.setState({ source: { item, beforeMove }, target: null })
      },
      onDragEnd: clearSidebarSectionDrag
    }
  }
}
