import { create } from 'zustand'
import { useUIStore } from './uiStore'

export interface BoardFocus {
  threadId: string
  topicId: string
  messageId: string | null
  key: number
}

interface BoardState {
  revisionByRoot: Record<string, number>
  focus: BoardFocus | null
  markChanged(rootThreadId: string): void
  openTopic(threadId: string, topicId: string, messageId?: string | null): void
  clearFocus(key: number): void
}

export const useBoardStore = create<BoardState>((set, get) => ({
  revisionByRoot: {},
  focus: null,

  markChanged(rootThreadId) {
    set((state) => ({
      revisionByRoot: { ...state.revisionByRoot, [rootThreadId]: (state.revisionByRoot[rootThreadId] ?? 0) + 1 }
    }))
  },

  openTopic(threadId, topicId, messageId = null) {
    set({ focus: { threadId, topicId, messageId, key: (get().focus?.key ?? 0) + 1 } })
    useUIStore.getState().setActiveDetailTab('board')
  },

  clearFocus(key) {
    if (get().focus?.key === key) set({ focus: null })
  }
}))
