import { create } from 'zustand'
import type { DiffAnnotationContext } from '../../shared/composerContext'

const EMPTY: DiffAnnotationContext[] = []

interface LineCommentDraftStore {
  byThread: Record<string, DiffAnnotationContext[]>
  getDrafts(threadId: string): DiffAnnotationContext[]
  open(threadId: string, draft: DiffAnnotationContext): void
  update(threadId: string, id: string, comment: string): void
  close(threadId: string, id: string): void
}

export const useLineCommentDraftStore = create<LineCommentDraftStore>((set, get) => ({
  byThread: {},
  getDrafts: (threadId) => get().byThread[threadId] ?? EMPTY,
  open: (threadId, draft) => set((state) => ({
    byThread: { ...state.byThread, [threadId]: [...(state.byThread[threadId] ?? EMPTY), draft] }
  })),
  update: (threadId, id, comment) => set((state) => ({
    byThread: {
      ...state.byThread,
      [threadId]: (state.byThread[threadId] ?? EMPTY).map((draft) => (draft.id === id ? { ...draft, comment } : draft))
    }
  })),
  close: (threadId, id) => set((state) => ({
    byThread: { ...state.byThread, [threadId]: (state.byThread[threadId] ?? EMPTY).filter((draft) => draft.id !== id) }
  }))
}))

export function lineHasComment(
  threadId: string,
  saved: readonly DiffAnnotationContext[],
  path: string,
  side: DiffAnnotationContext['side'],
  endLine: number
): boolean {
  const matches = (entry: DiffAnnotationContext) => entry.path === path && entry.side === side && entry.endLine === endLine
  return saved.some(matches) || useLineCommentDraftStore.getState().getDrafts(threadId).some(matches)
}
