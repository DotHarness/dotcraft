import { create } from 'zustand'
import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ThreadComposerDraftInput } from './composerDraftStore'

const EMPTY_CONTEXTS: ComposerContextRecord[] = []
interface ComposerContextStore {
  byThread: Record<string, ComposerContextRecord[]>
  restoreRequests: Record<string, ThreadComposerDraftInput | undefined>
  requestRestore(threadId: string, draft: ThreadComposerDraftInput): void
  consumeRestore(threadId: string): void
  getContexts(threadId: string): ComposerContextRecord[]
  setContexts(threadId: string, contexts: ComposerContextRecord[]): void
  addContext(threadId: string, context: ComposerContextRecord): void
  removeContext(threadId: string, id: string): void
  clearContexts(threadId: string): void
}

export const useComposerContextStore = create<ComposerContextStore>((set, get) => ({
  byThread: {},
  restoreRequests: {},
  requestRestore: (threadId, draft) => set((state) => ({ restoreRequests: { ...state.restoreRequests, [threadId]: draft } })),
  consumeRestore: (threadId) => set((state) => ({ restoreRequests: { ...state.restoreRequests, [threadId]: undefined } })),
  getContexts: (threadId) => get().byThread[threadId] ?? EMPTY_CONTEXTS,
  setContexts: (threadId, contexts) => set((state) => ({ byThread: { ...state.byThread, [threadId]: [...contexts] } })),
  addContext: (threadId, context) => get().setContexts(threadId, [
    ...get().getContexts(threadId).filter((item) => item.id !== context.id), context
  ]),
  removeContext: (threadId, id) => get().setContexts(threadId, get().getContexts(threadId).filter((item) => item.id !== id)),
  clearContexts: (threadId) => get().setContexts(threadId, [])
}))
