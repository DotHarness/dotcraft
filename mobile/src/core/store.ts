export interface Store<S, A> {
  getState(): S
  dispatch(action: A): void
  subscribe(listener: () => void): () => void
}

export function createStore<S, A>(reducer: (state: S, action: A) => S, initial: S): Store<S, A> {
  let state = initial
  const listeners = new Set<() => void>()
  return {
    getState: () => state,
    dispatch(action) {
      const next = reducer(state, action)
      if (next === state) return
      state = next
      for (const listener of [...listeners]) listener()
    },
    subscribe(listener) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    },
  }
}
