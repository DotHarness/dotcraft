import type { ReactNode } from 'react'
import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from '../../../../shared/workspaceProjectKey'
import { useSidebarSectionsStore } from '../../../stores/sidebarSectionsStore'
import { useUIStore } from '../../../stores/uiStore'
import { addToast } from '../../../stores/toastStore'
import { clearThreadReorder, clearThreadReorderTarget } from '../ThreadListRow'
import { sectionOfItem } from './sectionPlacement'
import { clearSidebarSectionDrag, useSidebarSectionDragStore } from './sidebarSectionDrag'

type DropTargetProps = { children: ReactNode } & (
  | { sectionId: string; kind?: never; projectKey?: never; acceptsThread?: never; onRestore?: never }
  | { sectionId?: never; kind?: never; projectKey: string; acceptsThread?: never; onRestore?: (projectKey: string) => void }
  | { sectionId?: never; kind: 'project' | 'thread'; projectKey?: never; acceptsThread?: (projectKey: string) => boolean; onRestore?: (projectKey: string) => void }
)

export function SidebarSectionDropTarget({
  sectionId,
  projectKey,
  kind,
  acceptsThread,
  onRestore,
  children
}: DropTargetProps): JSX.Element {
  const source = useSidebarSectionDragStore((state) => state.source)
  const target = useSidebarSectionDragStore((state) => state.target)
  const sections = useSidebarSectionsStore((state) => state.sections)
  const currentSection = source ? sectionOfItem(sections, source.item) : undefined
  const matchesProjectThread =
    projectKey != null &&
    source?.item.kind === 'thread' &&
    sameWorkspaceProjectKey(source.item.projectKey, projectKey)
  const ownsSource = matchesProjectThread && currentSection == null
  const accepts = source != null && (sectionId
    ? currentSection?.id !== sectionId
    : projectKey != null
      ? matchesProjectThread && currentSection != null
      : currentSection != null && (kind === 'project'
        ? source.item.kind === 'project' || (source.item.kind === 'thread' && acceptsThread?.(source.item.projectKey) === true)
        : source.item.kind === 'thread' && (acceptsThread?.(source.item.projectKey) ?? true)))
  const id = sectionId
    ? `section:${sectionId}`
    : projectKey != null
      ? `project:${normalizeWorkspaceProjectKey(projectKey)}`
      : `default:${kind}`
  const active = accepts && target === id

  return (
    <div
      className={projectKey != null
        ? 'dc-sidebar-section-drop-target dc-sidebar-section-drop-target--project'
        : 'dc-sidebar-section-drop-target'}
      data-sidebar-section-id={sectionId}
      data-drop-active={active || undefined}
      onDragOver={(event) => {
        if (ownsSource) {
          event.stopPropagation()
          if (target != null) useSidebarSectionDragStore.setState({ target: null })
          return
        }
        if (!accepts) return
        event.preventDefault()
        event.stopPropagation()
        event.dataTransfer.dropEffect = 'move'
        clearThreadReorderTarget()
        if (target !== id) useSidebarSectionDragStore.setState({ target: id })
      }}
      onDragLeave={(event) => {
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return
        if (target === id) useSidebarSectionDragStore.setState({ target: null })
      }}
      onDrop={async (event) => {
        if (ownsSource) {
          event.stopPropagation()
          return
        }
        if (!accepts || !source) return
        event.preventDefault()
        event.stopPropagation()
        clearSidebarSectionDrag()
        clearThreadReorder()
        try {
          await source.beforeMove?.()
          const store = useSidebarSectionsStore.getState()
          if (sectionId) {
            store.moveItems(sectionId, [source.item])
          } else if (projectKey != null) {
            store.removeItems([source.item])
            onRestore?.(source.item.projectKey)
          } else {
            store.removeItems([source.item])
            const ui = useUIStore.getState()
            if (kind === 'project') {
              ui.setProjectsSectionCollapsed(false)
              if (source.item.kind === 'thread') onRestore?.(source.item.projectKey)
            } else {
              ui.setChatsSectionCollapsed(false)
            }
          }
        } catch (error) {
          addToast(error instanceof Error ? error.message : String(error), 'error')
        }
      }}
    >
      {children}
    </div>
  )
}
