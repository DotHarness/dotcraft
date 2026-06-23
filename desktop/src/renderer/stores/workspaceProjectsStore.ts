import { create } from 'zustand'
import type { WorkspaceProjectsPayload, WorkspaceProjectSummary } from '../../shared/workspaceProjects'

interface WorkspaceProjectsState {
  foregroundWorkspacePath: string
  foregroundProjectId: string
  secondaryLimit: number
  projects: WorkspaceProjectSummary[]
  /** Default Chat workspace, rendered as the dedicated `Chats` group (never a Project). */
  chat: WorkspaceProjectSummary | null
  setPayload(payload: WorkspaceProjectsPayload): void
  reset(): void
}

const initialState = {
  foregroundWorkspacePath: '',
  foregroundProjectId: '',
  secondaryLimit: 8,
  projects: [],
  chat: null
}

export const useWorkspaceProjectsStore = create<WorkspaceProjectsState>((set) => ({
  ...initialState,

  setPayload(payload) {
    set({
      foregroundWorkspacePath: payload.foregroundWorkspacePath ?? '',
      foregroundProjectId: payload.foregroundProjectId ?? payload.foregroundWorkspacePath ?? '',
      secondaryLimit: payload.secondaryLimit ?? 8,
      projects: Array.isArray(payload.projects) ? payload.projects : [],
      chat: payload.chat ?? null
    })
  },

  reset() {
    set(initialState)
  }
}))
