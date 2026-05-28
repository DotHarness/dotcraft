import { create } from 'zustand'

/**
 * Active drag session state shared between the drag source (TaskCard) and the
 * drop targets (ThreadEntry / ThreadList) so the UI can light up non-hovered
 * parts of the tree (dim archived threads, show a global hint bar, mark the
 * already-bound thread, etc.) without prop drilling.
 *
 * `alreadyBoundThreadId` lets the drop target distinguish the currently-bound
 * thread (rejects new drops + shows a "currently bound" badge) from normal
 * drop candidates.
 */
export type DragSession =
  | {
      kind: 'automation-task'
      taskId: string
      title: string
      alreadyBoundThreadId: string | null
    }
  | null

interface DragDropState {
  active: DragSession
  start: (session: Exclude<DragSession, null>) => void
  end: () => void
}

export const useDragDropStore = create<DragDropState>((set) => ({
  active: null,
  start: (session) => set({ active: session }),
  end: () => set({ active: null })
}))
