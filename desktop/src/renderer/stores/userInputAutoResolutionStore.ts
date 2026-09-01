import { create } from 'zustand'
import type { UserInputAutoResolutionState } from '../../shared/userInputAutoResolution'

interface UserInputAutoResolutionStore {
  states: Map<string, UserInputAutoResolutionState>
  replace(states: UserInputAutoResolutionState[]): void
}

export const useUserInputAutoResolutionStore = create<UserInputAutoResolutionStore>((set) => ({
  states: new Map(),
  replace(states) {
    set({
      states: new Map(states.map((state) => [
        state.requestId,
        state
      ]))
    })
  }
}))
