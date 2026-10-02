import { create } from 'zustand'
import {
  normalizeManualThreadOrder,
  normalizeProjectKeyList,
  normalizeThreadOrderByProject,
  type SidebarThreadOrderSettings,
  type SidebarThreadSortMode
} from '../../shared/sidebarThreadOrder'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

interface SidebarThreadOrderState {
  recentsSort: SidebarThreadSortMode
  projectsSort: SidebarThreadSortMode
  pinnedSort: SidebarThreadSortMode
  recentsShowProjects: boolean
  recentsOrder: string[]
  pinnedOrder: string[]
  projectOrders: Record<string, string[]>
  projectSort: SidebarThreadSortMode
  projectOrder: string[]
  collapsedProjectIds: string[]
  hydrate(settings: SidebarThreadOrderSettings): void
  setRecentsSort(mode: SidebarThreadSortMode, snapshot?: string[]): void
  setProjectsSort(mode: SidebarThreadSortMode, snapshots?: Record<string, string[]>): void
  setPinnedSort(mode: SidebarThreadSortMode): void
  setRecentsShowProjects(show: boolean): void
  setRecentsOrder(threadIds: string[]): void
  setPinnedOrder(threadIds: string[]): void
  setProjectOrder(projectKey: string, threadIds: string[]): void
  setProjectSort(mode: SidebarThreadSortMode, snapshot?: string[]): void
  setProjectListOrder(projectKeys: string[]): void
  setProjectsCollapsed(projectKeys: string[], collapsed: boolean): void
}

function persist(partial: SidebarThreadOrderSettings): void {
  void window.api?.settings
    ?.set(partial)
    .catch((err: unknown) => console.error('settings:set sidebar thread order failed:', err))
}

export function projectOrderKey(projectKey: string): string {
  return normalizeWorkspaceProjectKey(projectKey)
}

export const useSidebarThreadOrderStore = create<SidebarThreadOrderState>((set, get) => ({
  recentsSort: 'updated',
  projectsSort: 'updated',
  pinnedSort: 'manual',
  recentsShowProjects: false,
  recentsOrder: [],
  pinnedOrder: [],
  projectOrders: {},
  projectSort: 'manual',
  projectOrder: [],
  collapsedProjectIds: [],

  hydrate(settings) {
    set({
      recentsSort: settings.recentsThreadSort === 'manual' ? 'manual' : 'updated',
      projectsSort: settings.projectsThreadSort === 'manual' ? 'manual' : 'updated',
      pinnedSort: settings.pinnedThreadSort === 'updated' ? 'updated' : 'manual',
      recentsShowProjects: settings.recentsShowProjects === true,
      recentsOrder: normalizeManualThreadOrder(settings.recentsThreadOrder) ?? [],
      pinnedOrder: normalizeManualThreadOrder(settings.pinnedThreadOrder) ?? [],
      projectOrders: normalizeThreadOrderByProject(settings.threadOrderByProject) ?? {},
      projectSort: settings.projectSort === 'updated' ? 'updated' : 'manual',
      projectOrder: normalizeProjectKeyList(settings.projectOrder) ?? [],
      collapsedProjectIds: normalizeProjectKeyList(settings.collapsedProjectIds) ?? []
    })
  },

  setRecentsSort(mode, snapshot) {
    if (mode === 'manual' && snapshot) {
      const order = normalizeManualThreadOrder(snapshot) ?? []
      set({ recentsSort: mode, recentsOrder: order })
      persist({ recentsThreadSort: mode, recentsThreadOrder: order })
      return
    }
    set({ recentsSort: mode })
    persist({ recentsThreadSort: mode })
  },

  setProjectsSort(mode, snapshots) {
    if (mode === 'manual' && snapshots) {
      const orders = normalizeThreadOrderByProject(snapshots) ?? {}
      set((state) => ({ projectsSort: mode, projectOrders: { ...state.projectOrders, ...orders } }))
      persist({ projectsThreadSort: mode, threadOrderByProject: orders })
      return
    }
    set({ projectsSort: mode })
    persist({ projectsThreadSort: mode })
  },

  setPinnedSort(mode) {
    set({ pinnedSort: mode })
    persist({ pinnedThreadSort: mode })
  },

  setRecentsShowProjects(show) {
    set({ recentsShowProjects: show })
    persist({ recentsShowProjects: show })
  },

  setRecentsOrder(threadIds) {
    const order = normalizeManualThreadOrder(threadIds) ?? []
    set({ recentsOrder: order })
    persist({ recentsThreadOrder: order })
  },

  setPinnedOrder(threadIds) {
    const order = normalizeManualThreadOrder(threadIds) ?? []
    set({ pinnedOrder: order })
    persist({ pinnedThreadOrder: order })
  },

  setProjectOrder(projectKey, threadIds) {
    const key = projectOrderKey(projectKey)
    if (!key) return
    const order = normalizeManualThreadOrder(threadIds) ?? []
    set((state) => ({ projectOrders: { ...state.projectOrders, [key]: order } }))
    persist({ threadOrderByProject: { [key]: order } })
  },

  setProjectSort(mode, snapshot) {
    if (mode === 'manual' && snapshot) {
      const order = normalizeProjectKeyList(snapshot) ?? []
      set({ projectSort: mode, projectOrder: order })
      persist({ projectSort: mode, projectOrder: order })
      return
    }
    set({ projectSort: mode })
    persist({ projectSort: mode })
  },

  setProjectListOrder(projectKeys) {
    const order = normalizeProjectKeyList(projectKeys) ?? []
    set({ projectOrder: order })
    persist({ projectOrder: order })
  },

  setProjectsCollapsed(projectKeys, collapsed) {
    const keys = new Set(projectKeys.map(projectOrderKey).filter(Boolean))
    const current = get().collapsedProjectIds
    const next = collapsed
      ? [...new Set([...current, ...keys])]
      : current.filter((key) => !keys.has(key))
    set({ collapsedProjectIds: next })
    persist({ collapsedProjectIds: next })
  }
}))
