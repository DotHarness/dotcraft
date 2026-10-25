import type { Store } from './store'

export function shallowEqual<T>(left: T, right: T): boolean {
  if (Object.is(left, right)) return true
  if (!left || !right || typeof left !== 'object' || typeof right !== 'object') return false
  const keys = Object.keys(left)
  return keys.length === Object.keys(right).length && keys.every((key) => Object.hasOwn(right, key) && Object.is(left[key as keyof T], right[key as keyof T]))
}

export function selectStore<S, A, T>(store: Store<S, A>, select: (state: S) => T, equal: (left: T, right: T) => boolean = Object.is) {
  let source = store.getState()
  let selected = select(source)
  const getState = () => {
    const next = store.getState()
    if (source !== next) {
      source = next
      const value = select(next)
      if (!equal(selected, value)) selected = value
    }
    return selected
  }
  return {
    getState,
    subscribe(listener: () => void) {
      let previous = getState()
      return store.subscribe(() => {
        const next = getState()
        if (Object.is(previous, next)) return
        previous = next
        listener()
      })
    },
  }
}
