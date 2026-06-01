import { create } from 'zustand'
import type {
  RemoteHost,
  RemoteStack,
  RemoteStackStatus,
  RemoteStackAction,
  SshTestResult,
  OperationResult
} from '../../shared/remoteServers'

export interface ActiveStackRef {
  hostId: string
  stackId: string
}

interface RemoteServersState {
  hosts: RemoteHost[]
  loaded: boolean
  loading: boolean
  selectedHostId: string | null
  /** SSH test in flight, keyed by hostId (or 'draft'). */
  testing: Record<string, boolean>
  /** Last SSH test result, keyed by hostId. */
  testResults: Record<string, SshTestResult>
  /** Stack status, keyed by stackId. */
  statuses: Record<string, RemoteStackStatus>
  statusLoading: Record<string, boolean>
  /** Lifecycle/update operation in flight, keyed by stackId. */
  busyStacks: Record<string, boolean>
  /** The stack whose AppServer Desktop is currently connected to, if any. */
  activeStack: ActiveStackRef | null
  error: string | null
}

interface RemoteServersStore extends RemoteServersState {
  load(): Promise<void>
  selectHost(id: string | null): void
  createHost(input: {
    name: string
    sshTarget: string
    identityFile?: string
    stacks?: RemoteStack[]
  }): Promise<RemoteHost | null>
  updateHost(id: string, patch: Partial<Omit<RemoteHost, 'id'>>): Promise<RemoteHost | null>
  deleteHost(id: string): Promise<void>
  testHost(input: {
    id?: string
    draft?: { name?: string; sshTarget?: string; identityFile?: string }
  }): Promise<SshTestResult | null>
  refreshStatus(hostId: string, stackId: string): Promise<void>
  runAction(hostId: string, stackId: string, action: RemoteStackAction): Promise<OperationResult | null>
  openInDesktop(hostId: string, stackId: string): Promise<boolean>
  openDashboard(hostId: string, stackId: string): Promise<void>
  disconnect(hostId: string, stackId: string): Promise<void>
  clearError(): void
}

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong.'
}

export const useRemoteServersStore = create<RemoteServersStore>((set) => ({
  hosts: [],
  loaded: false,
  loading: false,
  selectedHostId: null,
  testing: {},
  testResults: {},
  statuses: {},
  statusLoading: {},
  busyStacks: {},
  activeStack: null,
  error: null,

  async load() {
    set({ loading: true, error: null })
    try {
      const hosts = await window.api.remoteServers.list()
      set({ hosts, loaded: true, loading: false })
    } catch (error) {
      set({ loading: false, error: messageOf(error) })
    }
  },

  selectHost(id) {
    set({ selectedHostId: id })
  },

  async createHost(input) {
    try {
      const created = await window.api.remoteServers.create(input)
      set((state) => ({ hosts: [...state.hosts, created] }))
      return created
    } catch (error) {
      set({ error: messageOf(error) })
      return null
    }
  },

  async updateHost(id, patch) {
    try {
      const updated = await window.api.remoteServers.update(id, patch)
      set((state) => ({ hosts: state.hosts.map((h) => (h.id === id ? updated : h)) }))
      return updated
    } catch (error) {
      set({ error: messageOf(error) })
      return null
    }
  },

  async deleteHost(id) {
    try {
      await window.api.remoteServers.delete(id)
      set((state) => ({
        hosts: state.hosts.filter((h) => h.id !== id),
        selectedHostId: state.selectedHostId === id ? null : state.selectedHostId,
        activeStack: state.activeStack?.hostId === id ? null : state.activeStack
      }))
    } catch (error) {
      set({ error: messageOf(error) })
    }
  },

  async testHost(input) {
    const key = input.id ?? 'draft'
    set((state) => ({ testing: { ...state.testing, [key]: true } }))
    try {
      const result = await window.api.remoteServers.test(input)
      set((state) => ({
        testing: { ...state.testing, [key]: false },
        testResults: input.id ? { ...state.testResults, [input.id]: result } : state.testResults
      }))
      return result
    } catch (error) {
      set((state) => ({ testing: { ...state.testing, [key]: false }, error: messageOf(error) }))
      return null
    }
  },

  async refreshStatus(hostId, stackId) {
    set((state) => ({ statusLoading: { ...state.statusLoading, [stackId]: true } }))
    try {
      const status = await window.api.remoteServers.status(hostId, stackId)
      set((state) => ({
        statuses: { ...state.statuses, [stackId]: status },
        statusLoading: { ...state.statusLoading, [stackId]: false }
      }))
    } catch (error) {
      set((state) => ({ statusLoading: { ...state.statusLoading, [stackId]: false }, error: messageOf(error) }))
    }
  },

  async runAction(hostId, stackId, action) {
    set((state) => ({ busyStacks: { ...state.busyStacks, [stackId]: true } }))
    try {
      const result = await window.api.remoteServers.action(hostId, stackId, action)
      set((state) => ({
        busyStacks: { ...state.busyStacks, [stackId]: false },
        statuses: result.status ? { ...state.statuses, [stackId]: result.status } : state.statuses,
        error: result.ok ? state.error : result.message ?? state.error
      }))
      return result
    } catch (error) {
      set((state) => ({ busyStacks: { ...state.busyStacks, [stackId]: false }, error: messageOf(error) }))
      return null
    }
  },

  async openInDesktop(hostId, stackId) {
    set((state) => ({ busyStacks: { ...state.busyStacks, [stackId]: true } }))
    try {
      await window.api.remoteServers.openInDesktop(hostId, stackId)
      set((state) => ({
        busyStacks: { ...state.busyStacks, [stackId]: false },
        activeStack: { hostId, stackId }
      }))
      return true
    } catch (error) {
      set((state) => ({ busyStacks: { ...state.busyStacks, [stackId]: false }, error: messageOf(error) }))
      return false
    }
  },

  async openDashboard(hostId, stackId) {
    try {
      await window.api.remoteServers.openDashboard(hostId, stackId)
    } catch (error) {
      set({ error: messageOf(error) })
    }
  },

  async disconnect(hostId, stackId) {
    try {
      await window.api.remoteServers.disconnect(hostId, stackId)
    } catch (error) {
      set({ error: messageOf(error) })
    } finally {
      set((state) => ({
        activeStack:
          state.activeStack?.hostId === hostId && state.activeStack?.stackId === stackId
            ? null
            : state.activeStack
      }))
    }
  },

  clearError() {
    set({ error: null })
  }
}))
