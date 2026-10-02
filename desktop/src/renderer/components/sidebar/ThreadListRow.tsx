import { useMemo, type HTMLAttributes, type ReactNode } from 'react'
import { create } from 'zustand'
import type { ThreadDropPlacement } from './threadOrdering'
import { ThreadRowPlacementContext } from './threadRowSelection'

const THREAD_REORDER_MIME = 'application/x-dotcraft-thread-reorder'

interface ThreadReorderState {
  source: { listId: string; threadId: string } | null
  target: { listId: string; threadId: string; placement: ThreadDropPlacement } | null
}

const useThreadReorderStore = create<ThreadReorderState>(() => ({ source: null, target: null }))

function clearReorder(): void {
  useThreadReorderStore.setState({ source: null, target: null })
}

export interface ReorderItem {
  dragging: boolean
  dropPlacement: ThreadDropPlacement | null
  sourceProps: Pick<HTMLAttributes<HTMLElement>, 'draggable' | 'onDragStart' | 'onDragEnd'>
  targetProps: Pick<HTMLAttributes<HTMLElement>, 'onDragOver' | 'onDragLeave' | 'onDrop'>
}

export function useReorderItem(
  listId: string,
  itemId: string,
  reorderable: boolean,
  onMove: (movedId: string, targetId: string, placement: ThreadDropPlacement) => void
): ReorderItem {
  const dragging = useThreadReorderStore((s) => s.source?.listId === listId && s.source.threadId === itemId)
  const dropPlacement = useThreadReorderStore((s) =>
    s.target?.listId === listId && s.target.threadId === itemId ? s.target.placement : null
  )

  function acceptsDrop(): boolean {
    if (!reorderable) return false
    const { source } = useThreadReorderStore.getState()
    return source != null && source.listId === listId && source.threadId !== itemId
  }

  return {
    dragging,
    dropPlacement,
    sourceProps: {
      draggable: reorderable || undefined,
      onDragStart: (event) => {
        if (!reorderable) return
        event.dataTransfer.setData(THREAD_REORDER_MIME, itemId)
        // Foreground rows also offer a `link` drag for composer thread references.
        event.dataTransfer.effectAllowed = event.dataTransfer.effectAllowed === 'link' ? 'linkMove' : 'move'
        useThreadReorderStore.setState({ source: { listId, threadId: itemId }, target: null })
      },
      onDragEnd: clearReorder
    },
    targetProps: {
      onDragOver: (event) => {
        if (!acceptsDrop()) return
        event.preventDefault()
        event.dataTransfer.dropEffect = 'move'
        const rect = event.currentTarget.getBoundingClientRect()
        const next: ThreadDropPlacement = event.clientY < rect.top + rect.height / 2 ? 'before' : 'after'
        const { target } = useThreadReorderStore.getState()
        if (target?.listId !== listId || target.threadId !== itemId || target.placement !== next) {
          useThreadReorderStore.setState({ target: { listId, threadId: itemId, placement: next } })
        }
      },
      onDragLeave: (event) => {
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return
        const { target } = useThreadReorderStore.getState()
        if (target?.listId === listId && target.threadId === itemId) {
          useThreadReorderStore.setState({ target: null })
        }
      },
      onDrop: (event) => {
        if (!acceptsDrop()) return
        event.preventDefault()
        const { source, target } = useThreadReorderStore.getState()
        clearReorder()
        if (source && target?.threadId === itemId) onMove(source.threadId, itemId, target.placement)
      }
    }
  }
}

export function ThreadListRow({
  listId,
  threadId,
  home = true,
  reorderable,
  onMove,
  children
}: {
  listId: string
  threadId: string
  home?: boolean
  reorderable: boolean
  onMove: (movedId: string, targetId: string, placement: ThreadDropPlacement) => void
  children: ReactNode
}): JSX.Element {
  const rowPlacement = useMemo(() => ({ listId, home }), [listId, home])
  const { dragging, dropPlacement, sourceProps, targetProps } = useReorderItem(listId, threadId, reorderable, onMove)

  return (
    <ThreadRowPlacementContext.Provider value={rowPlacement}>
      <div
        className="dc-thread-list-row"
        role="listitem"
        data-testid={`thread-list-row-${listId}-${threadId}`}
        data-dragging={dragging ? 'true' : undefined}
        data-drop-placement={dropPlacement ?? undefined}
        {...sourceProps}
        {...targetProps}
      >
        {children}
      </div>
    </ThreadRowPlacementContext.Provider>
  )
}
