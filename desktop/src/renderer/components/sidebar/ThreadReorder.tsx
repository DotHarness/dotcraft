import type { ReactNode } from 'react'
import { create } from 'zustand'
import type { ThreadDropPlacement } from './threadOrdering'

const THREAD_REORDER_MIME = 'application/x-dotcraft-thread-reorder'

interface ThreadReorderState {
  source: { listId: string; threadId: string } | null
  target: { listId: string; threadId: string; placement: ThreadDropPlacement } | null
}

const useThreadReorderStore = create<ThreadReorderState>(() => ({ source: null, target: null }))

function clearReorder(): void {
  useThreadReorderStore.setState({ source: null, target: null })
}

export function ReorderableThreadRow({
  listId,
  threadId,
  enabled,
  onMove,
  children
}: {
  listId: string
  threadId: string
  enabled: boolean
  onMove: (movedId: string, targetId: string, placement: ThreadDropPlacement) => void
  children: ReactNode
}): JSX.Element {
  const dragging = useThreadReorderStore((s) => s.source?.listId === listId && s.source.threadId === threadId)
  const placement = useThreadReorderStore((s) =>
    s.target?.listId === listId && s.target.threadId === threadId ? s.target.placement : null
  )

  if (!enabled) return <>{children}</>

  function acceptsDrop(): boolean {
    const { source } = useThreadReorderStore.getState()
    return source != null && source.listId === listId && source.threadId !== threadId
  }

  return (
    <div
      className="dc-thread-reorder-row"
      data-testid={`thread-reorder-${listId}-${threadId}`}
      data-dragging={dragging ? 'true' : undefined}
      data-drop-placement={placement ?? undefined}
      draggable
      onDragStart={(event) => {
        event.dataTransfer.setData(THREAD_REORDER_MIME, threadId)
        // Foreground rows also offer a `link` drag for composer thread references.
        event.dataTransfer.effectAllowed = event.dataTransfer.effectAllowed === 'link' ? 'linkMove' : 'move'
        useThreadReorderStore.setState({ source: { listId, threadId }, target: null })
      }}
      onDragOver={(event) => {
        if (!acceptsDrop()) return
        event.preventDefault()
        event.dataTransfer.dropEffect = 'move'
        const rect = event.currentTarget.getBoundingClientRect()
        const next: ThreadDropPlacement = event.clientY < rect.top + rect.height / 2 ? 'before' : 'after'
        const { target } = useThreadReorderStore.getState()
        if (target?.listId !== listId || target.threadId !== threadId || target.placement !== next) {
          useThreadReorderStore.setState({ target: { listId, threadId, placement: next } })
        }
      }}
      onDragLeave={(event) => {
        if (event.currentTarget.contains(event.relatedTarget as Node | null)) return
        const { target } = useThreadReorderStore.getState()
        if (target?.listId === listId && target.threadId === threadId) {
          useThreadReorderStore.setState({ target: null })
        }
      }}
      onDrop={(event) => {
        if (!acceptsDrop()) return
        event.preventDefault()
        const { source, target } = useThreadReorderStore.getState()
        clearReorder()
        if (source && target?.threadId === threadId) onMove(source.threadId, threadId, target.placement)
      }}
      onDragEnd={clearReorder}
    >
      {children}
    </div>
  )
}
