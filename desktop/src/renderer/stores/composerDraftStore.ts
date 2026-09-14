import type { ComposerContextRecord } from '../../shared/composerContext'
import { readPlainComposerDraft, savePlainComposerDraft } from '../utils/plainComposerDraft'
import { create } from 'zustand'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerFileAttachment, ImageAttachment } from '../types/conversation'

export interface ThreadComposerDraft {
  clientUserMessageId?: string
  text: string
  contexts?: ComposerContextRecord[]
  segments: ComposerDraftSegment[]
  images: ImageAttachment[]
  files: ComposerFileAttachment[]
  updatedAt: number
}

/** The subset a caller supplies; `updatedAt` is stamped by the store on save. */
export type ThreadComposerDraftInput = Omit<ThreadComposerDraft, 'updatedAt'>

export function threadComposerDraftHasContent(
  draft: Pick<ThreadComposerDraft, 'text' | 'images' | 'files' | 'contexts'>
): boolean {
  return draft.text.trim().length > 0 || draft.images.length > 0 || draft.files.length > 0 || (draft.contexts?.length ?? 0) > 0
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
    const draft = get().draftsByThread[threadId]
    if (draft) return draft
    const text = readPlainComposerDraft(threadId)
    return text ? { text, segments: [{ type: 'text', value: text }], images: [], files: [], updatedAt: 0 } : null
  },

  saveDraft(threadId, draft) {
    if (!threadId) return
    const next: ThreadComposerDraft = {
      clientUserMessageId: draft.clientUserMessageId,
      text: draft.text,
      contexts: draft.contexts,
      segments: [...draft.segments],
      images: [...draft.images],
      files: [...draft.files],
      updatedAt: Date.now()
    }
    savePlainComposerDraft(threadId, draft.text)
    set((state) => ({
      draftsByThread: { ...state.draftsByThread, [threadId]: next }
    }))
  },

  clearDraft(threadId) {
    savePlainComposerDraft(threadId, '')
    set((state) => {
      if (!(threadId in state.draftsByThread)) return state
      const draftsByThread = { ...state.draftsByThread }
      delete draftsByThread[threadId]
      return { draftsByThread }
    })
  }
}))
