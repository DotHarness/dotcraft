import { create } from 'zustand'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'

/**
 * Unsent composer input for a single thread, preserved in memory so navigating
 * away (another thread, the Welcome screen, Settings, …) and back does not lose
 * what the user typed. Mirrors the per-workspace `welcomeDraft` in `uiStore`,
 * but keyed per thread.
 *
 * In-memory only: drafts are not persisted to disk and are gone on app restart.
 */
export interface ThreadComposerDraft {
  text: string
  segments: ComposerDraftSegment[]
  images: ImageAttachment[]
  files: ComposerFileAttachment[]
  updatedAt: number
}

/** The subset a caller supplies; `updatedAt` is stamped by the store on save. */
export type ThreadComposerDraftInput = Omit<ThreadComposerDraft, 'updatedAt'>

export function threadComposerDraftHasContent(
  draft: Pick<ThreadComposerDraft, 'text' | 'images' | 'files'>
): boolean {
  return draft.text.trim().length > 0 || draft.images.length > 0 || draft.files.length > 0
}

interface ComposerDraftStore {
  /** Unsent composer drafts keyed by thread id. */
  draftsByThread: Record<string, ThreadComposerDraft>
  getDraft(threadId: string): ThreadComposerDraft | null
  /** Store (or replace) the draft for a thread. No-op for an empty thread id. */
  saveDraft(threadId: string, draft: ThreadComposerDraftInput): void
  /** Drop the saved draft for a thread (e.g. after send or thread deletion). */
  clearDraft(threadId: string): void
}

export const useComposerDraftStore = create<ComposerDraftStore>((set, get) => ({
  draftsByThread: {},

  getDraft(threadId) {
    return get().draftsByThread[threadId] ?? null
  },

  saveDraft(threadId, draft) {
    if (!threadId) return
    const next: ThreadComposerDraft = {
      text: draft.text,
      segments: [...draft.segments],
      images: [...draft.images],
      files: [...draft.files],
      updatedAt: Date.now()
    }
    set((state) => ({
      draftsByThread: { ...state.draftsByThread, [threadId]: next }
    }))
  },

  clearDraft(threadId) {
    set((state) => {
      if (!(threadId in state.draftsByThread)) return state
      const draftsByThread = { ...state.draftsByThread }
      delete draftsByThread[threadId]
      return { draftsByThread }
    })
  }
}))
