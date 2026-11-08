import { create } from 'zustand'

interface TurnActivityDisclosureState {
  expanded: ReadonlyMap<string, boolean>
  setExpanded(key: string, expanded: boolean): void
}

export const useTurnActivityDisclosureStore = create<TurnActivityDisclosureState>((set) => ({
  expanded: new Map(),
  setExpanded: (key, expanded) => set((state) => {
    if (state.expanded.get(key) === expanded) return state
    const next = new Map(state.expanded)
    next.set(key, expanded)
    return { expanded: next }
  })
}))
