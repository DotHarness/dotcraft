import { create } from 'zustand'
import type { ComposerContextRecord, DiffAnnotationContext, LineCommentContext } from '../../shared/composerContext'
import { sameWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

const EMPTY: LineCommentContext[] = []

interface LineCommentDraftStore {
  byThread: Record<string, LineCommentContext[]>
  getDrafts(threadId: string): LineCommentContext[]
  open(threadId: string, draft: LineCommentContext): void
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

export function isLineCommentContext(context: ComposerContextRecord): context is LineCommentContext {
  return context.kind === 'diffAnnotation' || context.kind === 'fileAnnotation'
}

export function lineHasComment(
  threadId: string,
  saved: readonly DiffAnnotationContext[],
  path: string,
  side: DiffAnnotationContext['side'],
  endLine: number
): boolean {
  const matches = (entry: LineCommentContext) =>
    entry.kind === 'diffAnnotation' && sameWorkspaceProjectKey(entry.path, path) && entry.side === side && entry.endLine === endLine
  return saved.some(matches) || useLineCommentDraftStore.getState().getDrafts(threadId).some(matches)
}

export function fileTargetHasComment(
  threadId: string,
  saved: readonly ComposerContextRecord[],
  path: string,
  target: string
): boolean {
  const matches = (entry: ComposerContextRecord) =>
    entry.kind === 'fileAnnotation' && sameWorkspaceProjectKey(entry.path, path) && entry.target === target
  return saved.some(matches) || useLineCommentDraftStore.getState().getDrafts(threadId).some(matches)
}
