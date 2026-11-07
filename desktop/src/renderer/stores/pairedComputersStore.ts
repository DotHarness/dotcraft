import { create } from 'zustand'
import { parsePairingUrl } from '@dotcraft/sdk/gateway'
import type {
  AddFolderFailureCode,
  PairedComputer,
  PairedComputerEvent,
  PairingFailureCode
} from '../../shared/pairedComputers'

export type PairFlow =
  | { step: 'input'; failure?: { code: PairingFailureCode; name?: string } }
  | { step: 'connecting'; name: string }
  | { step: 'waiting'; name: string }
  | { step: 'paired'; computer: PairedComputer }

interface PairedComputersStore {
  loaded: boolean
  error: string | null
  computers: PairedComputer[]
  flow: PairFlow
  pendingLink: string | null
  load(): Promise<void>
  takePendingLink(): string | null
  pair(link: string): Promise<void>
  cancelPairing(): void
  resetFlow(): void
  remove(computerId: string): Promise<boolean>
  addFolder(computerId: string, path: string): Promise<AddFolderFailureCode | null>
  removeProject(computerId: string, projectId: string): Promise<boolean>
  openProject(computerId: string, projectId: string): Promise<boolean>
  applyEvent(event: PairedComputerEvent): void
}

const INPUT: PairFlow = { step: 'input' }

let pairingRequest = 0

export const usePairedComputersStore = create<PairedComputersStore>((set, get) => ({
  loaded: false,
  error: null,
  computers: [],
  flow: INPUT,
  pendingLink: null,

  takePendingLink() {
    const link = get().pendingLink
    if (link) set({ pendingLink: null })
    return link
  },

  async load() {
    try {
      set({ loaded: true, error: null, computers: await window.api.pairedComputers.list() })
    } catch (error) {
      set({ loaded: true, error: error instanceof Error ? error.message : String(error) })
    }
  },

  async pair(link) {
    const target = parsePairingUrl(link)
    if (!target) {
      set({ flow: { step: 'input', failure: { code: 'invalidLink' } } })
      return
    }
    const request = ++pairingRequest
    set({ flow: { step: 'connecting', name: target.name } })
    const result = await window.api.pairedComputers.pair(link.trim())
    if (request !== pairingRequest) return
    if (result.ok) {
      set((state) => ({
        flow: { step: 'paired', computer: result.value },
        computers: [
          result.value,
          ...state.computers.filter((computer) => computer.computerId !== result.value.computerId)
        ]
      }))
    } else {
      set({ flow: { step: 'input', failure: { code: result.code as PairingFailureCode, name: target.name } } })
    }
  },

  cancelPairing() {
    pairingRequest += 1
    void window.api.pairedComputers.cancelPairing()
    set({ flow: INPUT })
  },

  resetFlow() {
    pairingRequest += 1
    set({ flow: INPUT })
  },

  async remove(computerId) {
    const result = await window.api.pairedComputers.remove(computerId)
    if (!result.ok) return false
    set((state) => ({ computers: state.computers.filter((computer) => computer.computerId !== computerId) }))
    return true
  },

  async addFolder(computerId, path) {
    const result = await window.api.pairedComputers.addFolder(computerId, path)
    return result.ok ? null : result.code
  },

  async removeProject(computerId, projectId) {
    const result = await window.api.pairedComputers.removeProject(computerId, projectId)
    return result.ok
  },

  async openProject(computerId, projectId) {
    const result = await window.api.pairedComputers.openProject(computerId, projectId)
    return result.ok
  },

  applyEvent(event) {
    if (event.kind === 'changed') {
      set({ computers: event.computers })
      return
    }
    if (event.kind === 'pairingWaiting') {
      const { flow } = get()
      if (flow.step === 'connecting') set({ flow: { step: 'waiting', name: event.name } })
      return
    }
    if (event.kind === 'pairingLink') set({ pendingLink: event.link })
  }
}))

export function connectPairedComputers(): () => void {
  void window.api.pairedComputers.refresh()
  void usePairedComputersStore.getState().load()
  return window.api.pairedComputers.onEvent((event) => {
    if (event.kind !== 'pairingLink') usePairedComputersStore.getState().applyEvent(event)
  })
}
