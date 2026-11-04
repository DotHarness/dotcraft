import { create } from 'zustand'
import {
  DEFAULT_LINK_OPEN_PREFERENCES,
  normalizeLinkOpenPreferences,
  type LinkOpenPreferences
} from '../../shared/desktopSettings'

interface LinkOpenPreferencesState {
  preferences: LinkOpenPreferences
  hydrate: (settings: { linkOpenPreferences?: unknown }) => void
  save: (preferences: LinkOpenPreferences) => Promise<void>
}

export const useLinkOpenPreferencesStore = create<LinkOpenPreferencesState>((set) => ({
  preferences: DEFAULT_LINK_OPEN_PREFERENCES,
  hydrate: (settings) => {
    set({ preferences: normalizeLinkOpenPreferences(settings.linkOpenPreferences) })
  },
  save: async (preferences) => {
    await window.api.settings.set({ linkOpenPreferences: preferences })
    set({ preferences })
  }
}))
