import { create } from 'zustand'
import type {
  RemoteProject,
  SshHostDiscovery,
  SshMachine,
  SshMachineAddEntry,
  SshMachineEditPatch,
  SshMachineValidation,
  SshMachineView,
  SshMachinesPayload
} from '../../shared/sshMachines'
import { useUIStore } from './uiStore'
import { useWorkspaceProjectsStore } from './workspaceProjectsStore'

export type SshMutationResult<T> = { ok: true; value: T } | { ok: false; errors: SshMachineValidation }

export type ModelSetupOutcome = 'opened' | 'needsProject'

interface SshMachinesState {
  machines: SshMachineView[]
  loaded: boolean
  discovery: SshHostDiscovery | null
  discovering: boolean
}

interface SshMachinesStore extends SshMachinesState {
  ensureLoaded(): void
  load(): Promise<void>
  applyPayload(payload: SshMachinesPayload): void
  discoverHosts(): Promise<void>
  add(entries: SshMachineAddEntry[]): Promise<SshMutationResult<SshMachine[]>>
  edit(id: string, patch: SshMachineEditPatch): Promise<SshMutationResult<SshMachine>>
  remove(id: string): Promise<void>
  setAutoConnect(id: string, autoConnect: boolean): Promise<void>
  reconnect(id: string): Promise<void>
  install(id: string): Promise<void>
  update(id: string): Promise<void>
  addProject(id: string, path: string): Promise<RemoteProject>
  removeProject(id: string, projectId: string): Promise<void>
  openProject(id: string, projectId: string): Promise<void>
  setUpModel(id: string, projectId?: string): Promise<ModelSetupOutcome>
}

let subscription: { api: unknown; unsubscribe: () => void } | null = null
let stopModelWatch: (() => void) | null = null

export function sshProjectKey(machineId: string, projectId: string): string {
  return `remote:ssh:${machineId}:${projectId}`
}

function patchMachine(
  machines: SshMachineView[],
  id: string,
  patch: Partial<SshMachineView>
): SshMachineView[] {
  return machines.map((machine) => (machine.id === id ? { ...machine, ...patch } : machine))
}

function watchModelSetup(machineId: string): void {
  stopModelWatch?.()
  const stop = useUIStore.subscribe((state) => {
    if (state.activeMainView === 'settings' && state.activeSettingsTab === 'llmService') return
    stop()
    if (stopModelWatch === stop) stopModelWatch = null
    void window.api.sshMachines.recheckModel(machineId).catch(() => undefined)
  })
  stopModelWatch = stop
}

export const useSshMachinesStore = create<SshMachinesStore>((set, get) => ({
  machines: [],
  loaded: false,
  discovery: null,
  discovering: false,

  ensureLoaded() {
    if (subscription?.api === window.api) return
    subscription?.unsubscribe()
    subscription = {
      api: window.api,
      unsubscribe: window.api.sshMachines.onChanged((payload) => get().applyPayload(payload))
    }
    void get().load()
  },

  async load() {
    try {
      get().applyPayload(await window.api.sshMachines.list())
    } catch {
      set({ loaded: true })
    }
  },

  applyPayload(payload) {
    set({ machines: Array.isArray(payload.machines) ? payload.machines : [], loaded: true })
  },

  async discoverHosts() {
    set({ discovering: true })
    try {
      set({ discovery: await window.api.sshMachines.discoverHosts(), discovering: false })
    } catch (error) {
      set({
        discovering: false,
        discovery: {
          sshDir: '',
          configPath: '',
          configExists: false,
          agentAvailable: false,
          hosts: [],
          identities: [],
          error: error instanceof Error ? error.message : String(error)
        }
      })
    }
  },

  async add(entries) {
    const result = await window.api.sshMachines.add(entries)
    if (result.ok) await get().load()
    return result
  },

  async edit(id, patch) {
    const result = await window.api.sshMachines.edit(id, patch)
    if (result.ok) await get().load()
    return result
  },

  async remove(id) {
    await window.api.sshMachines.delete(id)
    set((state) => ({ machines: state.machines.filter((machine) => machine.id !== id) }))
  },

  async setAutoConnect(id, autoConnect) {
    const previous = get().machines.find((machine) => machine.id === id)
    set((state) => ({ machines: patchMachine(state.machines, id, { autoConnect }) }))
    try {
      await window.api.sshMachines.setAutoConnect(id, autoConnect)
    } catch (error) {
      if (previous) set((state) => ({ machines: patchMachine(state.machines, id, { autoConnect: previous.autoConnect }) }))
      throw error
    }
  },

  async reconnect(id) {
    await window.api.sshMachines.reconnect(id)
  },

  async install(id) {
    set((state) => ({ machines: patchMachine(state.machines, id, { status: { kind: 'installing' } }) }))
    await window.api.sshMachines.installDotCraft(id)
  },

  async update(id) {
    set((state) => ({ machines: patchMachine(state.machines, id, { status: { kind: 'installing' } }) }))
    await window.api.sshMachines.updateDotCraft(id)
  },

  async addProject(id, path) {
    const project = await window.api.sshMachines.addProject(id, path)
    await get().load()
    return project
  },

  async removeProject(id, projectId) {
    await window.api.sshMachines.removeProject(id, projectId)
    await get().load()
  },

  async openProject(id, projectId) {
    await window.api.sshMachines.openProject(id, projectId)
  },

  async setUpModel(id, projectId) {
    const machine = get().machines.find((candidate) => candidate.id === id)
    const foreground = useWorkspaceProjectsStore.getState().foregroundProjectId
    const onMachine = machine?.projects.some((project) => foreground === sshProjectKey(id, project.id)) ?? false
    if (!onMachine) {
      const target = projectId ?? machine?.projects[0]?.id
      if (!target) return 'needsProject'
      await window.api.sshMachines.openProject(id, target)
    }
    const ui = useUIStore.getState()
    ui.setActiveSettingsTab('llmService')
    ui.setActiveMainView('settings')
    watchModelSetup(id)
    return 'opened'
  }
}))
