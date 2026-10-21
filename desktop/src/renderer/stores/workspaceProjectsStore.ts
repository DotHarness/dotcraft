import { create } from 'zustand'
import type { WorkspaceProjectsPayload, WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import { isSubAgentThread } from '../utils/subAgentThreads'
import type { ThreadSummary } from '../types/thread'

interface WorkspaceProjectsState {
  foregroundWorkspacePath: string
  foregroundProjectId: string
  secondaryLimit: number
  projects: WorkspaceProjectSummary[]
  /** Default Chat workspace, rendered as the dedicated `Chats` group (never a Project). */
  chat: WorkspaceProjectSummary | null
  /** Threads of non-foreground projects that stopped running since they were last opened, by normalized project key. */
  unreadCompletedByProject: Readonly<Record<string, readonly string[]>>
  setPayload(payload: WorkspaceProjectsPayload): void
  /** Hands a project's unread marks to the foreground thread list when the project is promoted. */
  takeUnreadCompleted(projectKey: string): string[]
  /** Keeps a demoted project's unread marks so its secondary rows still show them. */
  keepUnreadCompleted(projectKey: string, threadIds: Iterable<string>): void
  reset(): void
}

const initialState = {
  foregroundWorkspacePath: '',
  foregroundProjectId: '',
  secondaryLimit: 8,
  projects: [],
  chat: null,
  unreadCompletedByProject: {}
}

export function localProjectsByLastOpened(projects: WorkspaceProjectSummary[]): WorkspaceProjectSummary[] {
  return projects
    .filter((project) => project.kind === 'local')
    .sort((left, right) => (Date.parse(right.lastOpenedAt ?? '') || 0) - (Date.parse(left.lastOpenedAt ?? '') || 0))
}

export function projectUnreadKey(project: WorkspaceProjectSummary): string {
  return normalizeWorkspaceProjectKey(project.projectId?.trim() || project.path)
}

function projectThreads(project: WorkspaceProjectSummary | null | undefined): ThreadSummary[] {
  return (project?.threads ?? []).filter((thread): thread is ThreadSummary =>
    Boolean(thread && typeof thread === 'object' && typeof (thread as { id?: unknown }).id === 'string'))
}

function nextUnreadCompleted(
  current: Readonly<Record<string, readonly string[]>>,
  previous: readonly WorkspaceProjectSummary[],
  next: readonly WorkspaceProjectSummary[]
): Record<string, readonly string[]> {
  const previousByKey = new Map(previous.map((project) => [projectUnreadKey(project), project]))
  const result: Record<string, readonly string[]> = {}
  for (const project of next) {
    const key = projectUnreadKey(project)
    const unread = new Set(current[key] ?? [])
    if (project.state !== 'foreground') {
      const wasRunning = new Set(projectThreads(previousByKey.get(key))
        .filter((thread) => thread.runtime?.running === true)
        .map((thread) => thread.id))
      const threads = projectThreads(project)
      const present = new Set(threads.map((thread) => thread.id))
      for (const id of [...unread]) if (!present.has(id)) unread.delete(id)
      for (const thread of threads) {
        if (thread.runtime?.running === true) unread.delete(thread.id)
        else if (wasRunning.has(thread.id) && !isSubAgentThread(thread)) unread.add(thread.id)
      }
    }
    if (unread.size > 0) result[key] = [...unread]
  }
  return result
}

export const useWorkspaceProjectsStore = create<WorkspaceProjectsState>((set, get) => ({
  ...initialState,

  setPayload(payload) {
    set((state) => {
      const projects = Array.isArray(payload.projects) ? payload.projects : []
      const chat = payload.chat ?? null
      const withChat = (list: WorkspaceProjectSummary[], entry: WorkspaceProjectSummary | null) => entry ? [...list, entry] : list
      return {
        foregroundWorkspacePath: payload.foregroundWorkspacePath ?? '',
        foregroundProjectId: payload.foregroundProjectId ?? payload.foregroundWorkspacePath ?? '',
        secondaryLimit: payload.secondaryLimit ?? 8,
        projects,
        chat,
        unreadCompletedByProject: nextUnreadCompleted(
          state.unreadCompletedByProject,
          withChat(state.projects, state.chat),
          withChat(projects, chat)
        )
      }
    })
  },

  takeUnreadCompleted(projectKey) {
    const key = normalizeWorkspaceProjectKey(projectKey)
    const taken = get().unreadCompletedByProject[key]
    if (!taken) return []
    set((state) => {
      const unreadCompletedByProject = { ...state.unreadCompletedByProject }
      delete unreadCompletedByProject[key]
      return { unreadCompletedByProject }
    })
    return [...taken]
  },

  keepUnreadCompleted(projectKey, threadIds) {
    const key = normalizeWorkspaceProjectKey(projectKey)
    const ids = [...new Set([...(get().unreadCompletedByProject[key] ?? []), ...threadIds])]
    if (!key || ids.length === 0) return
    set((state) => ({ unreadCompletedByProject: { ...state.unreadCompletedByProject, [key]: ids } }))
  },

  reset() {
    set(initialState)
  }
}))
