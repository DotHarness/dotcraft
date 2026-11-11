import { create } from 'zustand'
import type { WorkspaceProjectsPayload, WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import type { ThreadSummary } from '../types/thread'
import { filterPendingThreadArchives } from './pendingThreadArchives'

interface WorkspaceProjectsState {
  foregroundWorkspacePath: string
  foregroundProjectId: string
  secondaryLimit: number
  projects: WorkspaceProjectSummary[]
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

export function localProjectsByLastOpened(projects: WorkspaceProjectSummary[]): WorkspaceProjectSummary[] {
  return projects
    .filter((project) => project.kind === 'local')
    .sort((left, right) => (Date.parse(right.lastOpenedAt ?? '') || 0) - (Date.parse(left.lastOpenedAt ?? '') || 0))
}

export function filterPendingProjectArchives(project: WorkspaceProjectSummary): WorkspaceProjectSummary {
  const threads = project.threads as ThreadSummary[]
  const visible = filterPendingThreadArchives(project.projectId ?? project.path, threads)
  return visible === threads ? project : {
    ...project,
    threads: visible,
    threadCount: Math.max(0, project.threadCount - (threads.length - visible.length))
  }
}

export const useWorkspaceProjectsStore = create<WorkspaceProjectsState>((set) => ({
  ...initialState,

  setPayload(payload) {
    set({
      foregroundWorkspacePath: payload.foregroundWorkspacePath ?? '',
      foregroundProjectId: payload.foregroundProjectId ?? payload.foregroundWorkspacePath ?? '',
      secondaryLimit: payload.secondaryLimit ?? 8,
      projects: Array.isArray(payload.projects) ? payload.projects.map(filterPendingProjectArchives) : [],
      chat: payload.chat ? filterPendingProjectArchives(payload.chat) : null
    })
  },

  reset() {
    set(initialState)
  }
}))
