import { create } from 'zustand'
import {
  DEFAULT_LINK_PREFERENCES,
  normalizeLinkPreferences,
  type LinkPreferences
} from '../../shared/desktopSettings'

interface LinkPreferencesState {
  preferences: LinkPreferences
  hydrate: (settings: { linkPreferences?: unknown }) => void
  save: (preferences: LinkPreferences) => Promise<void>
}

export const useLinkPreferencesStore = create<LinkPreferencesState>((set) => ({
  preferences: DEFAULT_LINK_PREFERENCES,
  hydrate: (settings) => {
    set({ preferences: normalizeLinkPreferences(settings.linkPreferences) })
  },
  save: async (preferences) => {
    await window.api.settings.set({ linkPreferences: preferences })
    set({ preferences })
  }
}))
