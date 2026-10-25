import { create } from 'zustand'
import type { FollowUpQueueMode } from '../../shared/desktopSettings'
import { normalizeNewTaskPromptMode, type NewTaskPromptMode } from '../../shared/threadTasks'

interface ComposerPreferencesState {
  followUpQueueMode: FollowUpQueueMode
  newTaskPrompt: NewTaskPromptMode
  saving: boolean
  hydrate: (settings: { followUpQueueMode?: unknown; newTaskPrompt?: unknown }) => void
  saveFollowUpQueueMode: (mode: FollowUpQueueMode) => Promise<void>
  saveNewTaskPrompt: (mode: NewTaskPromptMode) => Promise<void>
}

export const useComposerPreferencesStore = create<ComposerPreferencesState>((set, get) => ({
  followUpQueueMode: 'steer',
  newTaskPrompt: 'ask',
  saving: false,
  hydrate: (settings) => {
    set({
      followUpQueueMode: settings.followUpQueueMode === 'queue' ? 'queue' : 'steer',
      newTaskPrompt: normalizeNewTaskPromptMode(settings.newTaskPrompt)
    })
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
  },
  saveNewTaskPrompt: async (mode) => {
    await window.api.settings.set({ newTaskPrompt: mode })
    set({ newTaskPrompt: mode })
  }
}))
