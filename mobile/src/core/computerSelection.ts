import type { Action, ComputerState, MobileState } from './state'
import { selectStore } from './selection'
import type { Store } from './store'

export function selectComputerStore<T>(
  store: Store<MobileState, Action>,
  computerId: string,
  select: (state: ComputerState) => T,
  equal: (left: T, right: T) => boolean,
) {
  let computer = store.getState().computers[computerId]
  return selectStore(
    store,
    (state) => {
      // Removal notifies children before their provider can unmount them.
      computer = state.computers[computerId] ?? computer
      return select(computer)
    },
    equal,
  )
}
