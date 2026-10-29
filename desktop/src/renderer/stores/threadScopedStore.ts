import { createStore, useStore, type StoreApi } from 'zustand'

export interface ActiveThreadStoreHook<T> {
  <U>(selector: (state: T) => U): U
  getState: StoreApi<T>['getState']
  setState: StoreApi<T>['setState']
  subscribe: StoreApi<T>['subscribe']
}

export interface ThreadScopedStores<T> {
  useActive: ActiveThreadStoreHook<T>
  active(): StoreApi<T>
  get(threadId: string): StoreApi<T> | undefined
  create(threadId: string): StoreApi<T>
  /** Falls back to a fresh unregistered store when the thread has none. */
  activate(threadId: string | null): StoreApi<T>
  release(threadId: string): void
}

export function createThreadScopedStores<T>(
  createThreadStore: (threadId: string | null, active: T | undefined) => StoreApi<T>
): ThreadScopedStores<T> {
  const stores = new Map<string, StoreApi<T>>()
  const activeStore = createStore<{ store: StoreApi<T> }>(() => ({ store: createThreadStore(null, undefined) }))
  const active = (): StoreApi<T> => activeStore.getState().store
  const fresh = (threadId: string | null): StoreApi<T> => createThreadStore(threadId, active().getState())

  function useActiveStore<U>(selector: (state: T) => U): U {
    return useStore(useStore(activeStore, (state) => state.store), selector)
  }

  const setState = ((...args: Parameters<StoreApi<T>['setState']>) => {
    active().setState(...args)
  }) as StoreApi<T>['setState']

  const subscribe: StoreApi<T>['subscribe'] = (listener) => {
    let store = active()
    let unsubscribeStore = store.subscribe(listener)
    const unsubscribeActive = activeStore.subscribe(({ store: next }) => {
      if (next === store) return
      const previous = store.getState()
      unsubscribeStore()
      store = next
      unsubscribeStore = next.subscribe(listener)
      listener(next.getState(), previous)
    })
    return () => {
      unsubscribeActive()
      unsubscribeStore()
    }
  }

  return {
    useActive: Object.assign(useActiveStore, {
      getState: () => active().getState(),
      setState,
      subscribe
    }),
    active,
    get: (threadId) => stores.get(threadId),
    create(threadId) {
      const store = fresh(threadId)
      stores.set(threadId, store)
      return store
    },
    activate(threadId) {
      const store = (threadId != null ? stores.get(threadId) : undefined) ?? fresh(threadId)
      activeStore.setState({ store })
      return store
    },
    release(threadId) {
      stores.delete(threadId)
    }
  }
}
