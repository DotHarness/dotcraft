import { create } from 'zustand'
import type { DesktopPluginHost } from '@dotcraft/plugin'
import type { Satellite, SatelliteEvent, SatelliteInvite, SatelliteWorkspace } from './types'
import { withEventPresence } from './model'

export type SatelliteLeases = Record<string, { busy: boolean; busyOwner?: string; leaseExpiresAt?: string }>
export interface SatelliteBusyHost {
  hostId: string
  workspaces: { workspaceId: string; available: boolean; busyOwner?: string | null; leaseExpiresAt?: string | null }[]
}
interface SatelliteState {
  satellites: Satellite[]
  supported: boolean
  loaded: boolean
  error: string | null
  selectedPeerId: string | null
  invite: SatelliteInvite | null
  creatingInvite: boolean
  inviteError: string | null
  revoking: string | null
  activity: Record<string, SatelliteEvent[]>
  busy: Record<string, SatelliteLeases>
}
interface SatelliteActions {
  load(): Promise<void>
  select(peerId: string | null): void
  createInvite(): Promise<SatelliteInvite | null>
  clearInvite(): void
  revoke(peerId: string): Promise<boolean>
  loadActivity(peerId: string): Promise<void>
  applyEvent(event: SatelliteEvent): void
  applyBusy(hosts: SatelliteBusyHost[]): void
}
export type SatellitesStore = SatelliteState & SatelliteActions

const initialState = (): SatelliteState => ({
  satellites: [], supported: true, loaded: false, error: null, selectedPeerId: null,
  invite: null, creatingInvite: false, inviteError: null, revoking: null, activity: {}, busy: {}
})
let host: DesktopPluginHost | null = null
let generation = 0
let listing = 0
let invitation = 0

function messageOf(error: unknown): string | null {
  return error instanceof Error ? error.message : null
}

function without<T>(map: Record<string, T>, key: string): Record<string, T> {
  const { [key]: _removed, ...rest } = map
  return rest
}

function prependActivity(activity: Record<string, SatelliteEvent[]>, event: SatelliteEvent): Record<string, SatelliteEvent[]> {
  return { ...activity, [event.peerId]: [event, ...(activity[event.peerId] ?? [])].slice(0, 20) }
}

export function withLeases(satellites: Satellite[], busy: Record<string, SatelliteLeases>): Satellite[] {
  return satellites.map((satellite) => {
    const leases = busy[satellite.peerId]
    if (!leases) return satellite
    const workspaces: SatelliteWorkspace[] = satellite.workspaces.map((workspace) => {
      const lease = leases[workspace.workspaceId]
      return lease
        ? { ...workspace, busy: lease.busy, busyOwner: lease.busyOwner, leaseExpiresAt: lease.leaseExpiresAt }
        : workspace
    })
    const held = workspaces.find((workspace) => workspace.busy)
    return {
      ...satellite, workspaces,
      activeLease: held ? { workspaceId: held.workspaceId, owner: held.busyOwner, expiresAt: held.leaseExpiresAt } : undefined
    }
  })
}

export const useSatellitesStore = create<SatellitesStore>((set, get) => ({
  ...initialState(),
  async load() {
    const api = host?.satellites
    if (!api) return
    const current = generation
    const request = ++listing
    try {
      const result = await api.list()
      if (current !== generation || request !== listing) return
      set({ supported: result.supported, satellites: result.satellites, error: result.error ?? null, loaded: true })
    } catch (error) {
      if (current === generation && request === listing) set({ loaded: true, error: messageOf(error) })
    }
  },
  select(peerId) { set({ selectedPeerId: peerId }) },
  async createInvite() {
    const api = host?.satellites
    if (!api) return null
    const current = generation
    const request = ++invitation
    set({ creatingInvite: true, inviteError: null })
    try {
      const invite = await api.createInvite()
      if (current !== generation || request !== invitation) return null
      set({ invite, creatingInvite: false })
      return invite
    } catch (error) {
      if (current === generation && request === invitation) set({ creatingInvite: false, inviteError: messageOf(error) ?? '' })
      return null
    }
  },
  clearInvite() {
    invitation++
    set({ invite: null, inviteError: null, creatingInvite: false })
  },
  async revoke(peerId) {
    const api = host?.satellites
    if (!api) return false
    const current = generation
    set({ revoking: peerId })
    try {
      await api.revoke(peerId)
      if (current !== generation) return false
      set((state) => ({
        revoking: null,
        satellites: state.satellites.filter((satellite) => satellite.peerId !== peerId),
        selectedPeerId: state.selectedPeerId === peerId ? null : state.selectedPeerId,
        activity: without(state.activity, peerId), busy: without(state.busy, peerId)
      }))
      return true
    } catch {
      if (current === generation) set({ revoking: null })
      return false
    }
  },
  async loadActivity(peerId) {
    const api = host?.satellites
    if (!api) return
    const current = generation
    try {
      const events = await api.activity(peerId)
      if (current === generation) set((state) => ({ activity: { ...state.activity, [peerId]: events.slice(0, 20) } }))
    } catch {}
  },
  applyEvent(event) {
    const invite = get().invite
    if (event.kind === 'joined' && invite && event.inviteId === invite.inviteId) get().clearInvite()
    if (event.kind === 'revoked') {
      set((state) => ({
        satellites: state.satellites.filter((satellite) => satellite.peerId !== event.peerId),
        selectedPeerId: state.selectedPeerId === event.peerId ? null : state.selectedPeerId,
        activity: without(state.activity, event.peerId), busy: without(state.busy, event.peerId)
      }))
      return
    }
    if (event.kind === 'joined' && !event.satellite) {
      void get().load()
      set((state) => ({ activity: prependActivity(state.activity, event) }))
      return
    }
    set((state) => {
      const index = state.satellites.findIndex((satellite) => satellite.peerId === event.peerId)
      const base = event.satellite ?? state.satellites[index]
      const merged = base ? withEventPresence(base, event) : null
      const satellites = !merged ? state.satellites
        : index >= 0 ? state.satellites.map((satellite, at) => at === index ? merged : satellite)
          : [...state.satellites, merged]
      return { satellites, activity: prependActivity(state.activity, event) }
    })
  },
  applyBusy(hosts) {
    const busy: Record<string, SatelliteLeases> = {}
    for (const host of hosts) {
      const leases: SatelliteLeases = {}
      for (const workspace of host.workspaces) {
        leases[workspace.workspaceId] = {
          busy: workspace.available === false,
          ...(workspace.busyOwner ? { busyOwner: workspace.busyOwner } : {}),
          ...(workspace.leaseExpiresAt ? { leaseExpiresAt: workspace.leaseExpiresAt } : {})
        }
      }
      busy[host.hostId] = leases
    }
    set({ busy })
  }
}))

export function startSatellites(nextHost: DesktopPluginHost, onEvent?: (event: SatelliteEvent) => void): () => void {
  const current = ++generation
  host = nextHost
  useSatellitesStore.setState(initialState())
  const unsubscribe = nextHost.satellites.onEvent((event) => {
    if (current !== generation) return
    useSatellitesStore.getState().applyEvent(event)
    onEvent?.(event)
  })
  void useSatellitesStore.getState().load()
  return () => {
    unsubscribe()
    if (current !== generation) return
    generation++
    host = null
    useSatellitesStore.setState(initialState())
  }
}
