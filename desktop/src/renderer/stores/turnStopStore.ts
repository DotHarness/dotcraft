import { create } from 'zustand'

interface TurnStopState {
  requested: ReadonlySet<string>
  setRequested(threadId: string, turnId: string, requested: boolean): void
}

export const turnStopKey = (threadId: string, turnId: string): string => JSON.stringify([threadId, turnId])

/** Window-local provenance; intentionally absent from persisted conversation data. */
export const useTurnStopStore = create<TurnStopState>((set) => ({
  requested: new Set(),
  setRequested: (threadId, turnId, requested) => set((state) => {
    const next = new Set(state.requested)
    const key = turnStopKey(threadId, turnId)
    if (requested) next.add(key)
    else next.delete(key)
    return { requested: next }
  })
}))
