import { useEffect } from 'react'
import { create } from 'zustand'
import type { LinkedChatSummary } from '../../shared/linkedChats'

const EMPTY: readonly LinkedChatSummary[] = []

interface LinkedChatsState {
  byParent: Readonly<Record<string, readonly LinkedChatSummary[]>>
  setLinkedChats(parentThreadId: string, chats: readonly LinkedChatSummary[]): void
}

export const useLinkedChatsStore = create<LinkedChatsState>((set) => ({
  byParent: {},
  setLinkedChats(parentThreadId, chats) {
    set((state) => ({ byParent: { ...state.byParent, [parentThreadId]: chats } }))
  }
}))

const tracked = new Map<string, number>()
let stopListening: (() => void) | null = null

export function loadLinkedChats(parentThreadId: string): Promise<readonly LinkedChatSummary[]> {
  const read = window.api?.threadTasks?.linkedChats
  if (!read) return Promise.resolve(useLinkedChatsStore.getState().byParent[parentThreadId] ?? EMPTY)
  return read(parentThreadId).then((chats) => {
    if (JSON.stringify(chats) !== JSON.stringify(useLinkedChatsStore.getState().byParent[parentThreadId] ?? EMPTY)) {
      useLinkedChatsStore.getState().setLinkedChats(parentThreadId, chats)
    }
    return chats
  }, () => useLinkedChatsStore.getState().byParent[parentThreadId] ?? EMPTY)
}

export function trackLinkedChats(parentThreadId: string): () => void {
  tracked.set(parentThreadId, (tracked.get(parentThreadId) ?? 0) + 1)
  if (tracked.get(parentThreadId) === 1) void loadLinkedChats(parentThreadId)
  stopListening ??= window.api?.threadTasks?.onLinkedChatsChanged?.(() => {
    for (const parent of tracked.keys()) void loadLinkedChats(parent)
  }) ?? null
  return () => {
    const count = (tracked.get(parentThreadId) ?? 1) - 1
    if (count > 0) tracked.set(parentThreadId, count)
    else tracked.delete(parentThreadId)
    if (tracked.size === 0) {
      stopListening?.()
      stopListening = null
    }
  }
}

export function useLinkedChats(parentThreadId: string | null | undefined): readonly LinkedChatSummary[] {
  useEffect(() => (parentThreadId ? trackLinkedChats(parentThreadId) : undefined), [parentThreadId])
  return useLinkedChatsStore((state) => (parentThreadId ? state.byParent[parentThreadId] : undefined) ?? EMPTY)
}
