import { create } from 'zustand'
import type { FollowUpQueueMode } from '../../shared/desktopSettings'

interface ComposerPreferencesState {
  followUpQueueMode: FollowUpQueueMode
  saving: boolean
  hydrate: (settings: { followUpQueueMode?: unknown }) => void
  saveFollowUpQueueMode: (mode: FollowUpQueueMode) => Promise<void>
}

export const useComposerPreferencesStore = create<ComposerPreferencesState>((set, get) => ({
  followUpQueueMode: 'steer',
  saving: false,
  hydrate: (settings) => {
    set({ followUpQueueMode: settings.followUpQueueMode === 'queue' ? 'queue' : 'steer' })
  },
  saveFollowUpQueueMode: async (mode) => {
    if (get().saving) return
    set({ saving: true })
    try {
      await window.api.settings.set({ followUpQueueMode: mode })
      set({ followUpQueueMode: mode })
    } finally {
      set({ saving: false })
    }
  }
}))
