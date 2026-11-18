import { create } from 'zustand'
import type { FollowUpQueueMode } from '../../shared/desktopSettings'
import { normalizeNewTaskPromptMode, type NewTaskPromptMode } from '../../shared/threadTasks'

export type NewChatApprovalPolicy = 'prompt' | 'autoApprove'

interface ComposerPreferencesState {
  followUpQueueMode: FollowUpQueueMode
  newTaskPrompt: NewTaskPromptMode
  newChatApprovalPolicy: NewChatApprovalPolicy | null
  saving: boolean
  hydrate: (settings: { followUpQueueMode?: unknown; newTaskPrompt?: unknown; newChatApprovalPolicy?: unknown }) => void
  saveFollowUpQueueMode: (mode: FollowUpQueueMode) => Promise<void>
  saveNewTaskPrompt: (mode: NewTaskPromptMode) => Promise<void>
  saveNewChatApprovalPolicy: (policy: NewChatApprovalPolicy | null) => Promise<void>
}

export const useComposerPreferencesStore = create<ComposerPreferencesState>((set, get) => ({
  followUpQueueMode: 'steer',
  newTaskPrompt: 'ask',
  newChatApprovalPolicy: null,
  saving: false,
  hydrate: (settings) => {
    set({
      followUpQueueMode: settings.followUpQueueMode === 'queue' ? 'queue' : 'steer',
      newTaskPrompt: normalizeNewTaskPromptMode(settings.newTaskPrompt),
      newChatApprovalPolicy: settings.newChatApprovalPolicy === 'prompt' || settings.newChatApprovalPolicy === 'autoApprove'
        ? settings.newChatApprovalPolicy
        : null
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
  },
  saveNewChatApprovalPolicy: async (policy) => {
    const previous = get().newChatApprovalPolicy
    set({ newChatApprovalPolicy: policy })
    try {
      await window.api.settings.set({ newChatApprovalPolicy: policy ?? undefined })
    } catch (error) {
      set({ newChatApprovalPolicy: previous })
      throw error
    }
  }
}))
