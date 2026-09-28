import { create } from 'zustand'
import {
  normalizeManualThreadOrder,
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
  hydrate(settings: SidebarThreadOrderSettings): void
  setRecentsSort(mode: SidebarThreadSortMode, snapshot?: string[]): void
  setProjectsSort(mode: SidebarThreadSortMode, snapshots?: Record<string, string[]>): void
  setPinnedSort(mode: SidebarThreadSortMode): void
  setRecentsShowProjects(show: boolean): void
  setRecentsOrder(threadIds: string[]): void
  setPinnedOrder(threadIds: string[]): void
  setProjectOrder(projectKey: string, threadIds: string[]): void
}

function persist(partial: SidebarThreadOrderSettings): void {
  void window.api?.settings
    ?.set(partial)
    .catch((err: unknown) => console.error('settings:set sidebar thread order failed:', err))
}

export function projectOrderKey(projectKey: string): string {
  return normalizeWorkspaceProjectKey(projectKey)
}

export const useSidebarThreadOrderStore = create<SidebarThreadOrderState>((set) => ({
  recentsSort: 'updated',
  projectsSort: 'updated',
  pinnedSort: 'manual',
  recentsShowProjects: false,
  recentsOrder: [],
  pinnedOrder: [],
  projectOrders: {},

  hydrate(settings) {
    set({
      recentsSort: settings.recentsThreadSort === 'manual' ? 'manual' : 'updated',
      projectsSort: settings.projectsThreadSort === 'manual' ? 'manual' : 'updated',
      pinnedSort: settings.pinnedThreadSort === 'updated' ? 'updated' : 'manual',
      recentsShowProjects: settings.recentsShowProjects === true,
      recentsOrder: normalizeManualThreadOrder(settings.recentsThreadOrder) ?? [],
      pinnedOrder: normalizeManualThreadOrder(settings.pinnedThreadOrder) ?? [],
      projectOrders: normalizeThreadOrderByProject(settings.threadOrderByProject) ?? {}
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
  }
}))
