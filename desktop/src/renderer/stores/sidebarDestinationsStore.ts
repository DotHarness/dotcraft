import { create } from 'zustand'
import {
  normalizeSidebarDestinationPins,
  type SidebarDestinationPins
} from '../../shared/sidebarDestinations'

interface SidebarDestinationsState {
  pins: SidebarDestinationPins
  hydrate(settings: { sidebarDestinationPins?: unknown }): void
  setPinned(id: string, pinned: boolean): void
}

export const useSidebarDestinationsStore = create<SidebarDestinationsState>((set, get) => ({
  pins: {},

  hydrate(settings) {
    set({ pins: normalizeSidebarDestinationPins(settings.sidebarDestinationPins) ?? {} })
  },

  setPinned(id, pinned) {
    const pins = { ...get().pins, [id]: pinned }
    set({ pins })
    void window.api?.settings
      ?.set({ sidebarDestinationPins: pins })
      .catch((err: unknown) => console.error('settings:set sidebar destination pins failed:', err))
  }
}))
