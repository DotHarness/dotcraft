import { create } from 'zustand'
import type {
  Satellite,
  SatelliteEvent,
  SatelliteInvite,
  SatelliteWorkspace
} from '../../shared/satellites'

/** One machine's lease overlay, keyed by workspace id. */
export type SatelliteLeases = Record<string, { busy: boolean; busyOwner?: string; leaseExpiresAt?: string }>

/** `remoteToolHost/list`, narrowed to what the overlay needs. */
export interface SatelliteBusyHost {
  hostId: string
  workspaces: {
    workspaceId: string
    available: boolean
    busyOwner?: string | null
    leaseExpiresAt?: string | null
  }[]
}

interface SatellitesState {
  satellites: Satellite[]
  supported: boolean
  loaded: boolean
  bootstrapped: boolean
  error: string | null
  selectedPeerId: string | null
  /** The last minted invitation. Memory only: it is never written to settings. */
  invite: SatelliteInvite | null
  creatingInvite: boolean
  inviteError: string | null
  revoking: string | null
  /** Recent events per peer id, newest first. */
  activity: Record<string, SatelliteEvent[]>
  busy: Record<string, SatelliteLeases>
}

interface SatellitesStore extends SatellitesState {
  load(): Promise<void>
  select(peerId: string | null): void
  createInvite(input: { purpose?: string }): Promise<SatelliteInvite | null>
  clearInvite(): void
  revoke(peerId: string): Promise<boolean>
  loadActivity(peerId: string): Promise<void>
  applyEvent(event: SatelliteEvent): void
  applyBusy(hosts: SatelliteBusyHost[]): void
}

const ACTIVITY_LIMIT = 20

function messageOf(error: unknown): string | null {
  return error instanceof Error && error.message.trim() !== '' ? error.message : null
}

function prependActivity(
  activity: Record<string, SatelliteEvent[]>,
  event: SatelliteEvent
): Record<string, SatelliteEvent[]> {
  const existing = activity[event.peerId] ?? []
  return { ...activity, [event.peerId]: [event, ...existing].slice(0, ACTIVITY_LIMIT) }
}

function without<T>(map: Record<string, T>, key: string): Record<string, T> {
  const { [key]: _removed, ...rest } = map
  return rest
}

/**
 * Overlays the AppServer's lease view onto the Hub records so a row can say "in use".
 * Without a connected AppServer `busy` is empty and every online machine reads ready.
 */
export function withLeases(satellites: Satellite[], busy: Record<string, SatelliteLeases>): Satellite[] {
  return satellites.map((satellite) => {
    const leases = busy[satellite.peerId]
    if (!leases) return satellite
    const workspaces: SatelliteWorkspace[] = satellite.workspaces.map((workspace) => {
      const lease = leases[workspace.workspaceId]
      if (!lease) return workspace
      return {
        ...workspace,
        busy: lease.busy,
        ...(lease.busyOwner ? { busyOwner: lease.busyOwner } : {}),
        ...(lease.leaseExpiresAt ? { leaseExpiresAt: lease.leaseExpiresAt } : {})
      }
    })
    const held = workspaces.find((workspace) => workspace.busy)
    return {
      ...satellite,
      workspaces,
      ...(held
        ? {
            activeLease: {
              workspaceId: held.workspaceId,
              ...(held.busyOwner ? { owner: held.busyOwner } : {}),
              ...(held.leaseExpiresAt ? { expiresAt: held.leaseExpiresAt } : {})
            }
          }
        : {})
    }
  })
}

type SatelliteEventListener = (event: SatelliteEvent) => void

const eventListeners = new Set<SatelliteEventListener>()

/** Follow the store's one Hub subscription instead of opening a second one. */
export function onSatelliteEvent(listener: SatelliteEventListener): () => void {
  eventListeners.add(listener)
  return () => {
    eventListeners.delete(listener)
  }
}

export const useSatellitesStore = create<SatellitesStore>((set, get) => ({
  satellites: [],
  supported: true,
  loaded: false,
  bootstrapped: false,
  error: null,
  selectedPeerId: null,
  invite: null,
  creatingInvite: false,
  inviteError: null,
  revoking: null,
  activity: {},
  busy: {},

  async load() {
    try {
      const result = await window.api.satellites.list()
      set({
        supported: result.supported,
        satellites: result.satellites,
        error: result.error ?? null,
        loaded: true
      })
    } catch (error) {
      set({ loaded: true, error: messageOf(error) })
    }
  },

  select(peerId) {
    set({ selectedPeerId: peerId })
  },

  async createInvite(input) {
    set({ creatingInvite: true, inviteError: null })
    try {
      const invite = await window.api.satellites.createInvite(input)
      set({ invite, creatingInvite: false })
      return invite
    } catch (error) {
      set({ creatingInvite: false, inviteError: messageOf(error) ?? '' })
      return null
    }
  },

  clearInvite() {
    set({ invite: null, inviteError: null })
  },

  async revoke(peerId) {
    set({ revoking: peerId })
    try {
      await window.api.satellites.revoke(peerId)
      set((state) => ({
        revoking: null,
        satellites: state.satellites.filter((satellite) => satellite.peerId !== peerId),
        selectedPeerId: state.selectedPeerId === peerId ? null : state.selectedPeerId,
        activity: without(state.activity, peerId),
        busy: without(state.busy, peerId)
      }))
      return true
    } catch {
      set({ revoking: null })
      return false
    }
  },

  async loadActivity(peerId) {
    try {
      const events = await window.api.satellites.activity(peerId)
      set((state) => ({ activity: { ...state.activity, [peerId]: events.slice(0, ACTIVITY_LIMIT) } }))
    } catch {
      // The detail page renders an empty list when nothing was read.
    }
  },

  applyEvent(event) {
    if (event.kind === 'revoked') {
      set((state) => ({
        satellites: state.satellites.filter((satellite) => satellite.peerId !== event.peerId),
        selectedPeerId: state.selectedPeerId === event.peerId ? null : state.selectedPeerId,
        activity: without(state.activity, event.peerId),
        busy: without(state.busy, event.peerId)
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
      const known = index >= 0 ? state.satellites[index] : null
      const merged = event.satellite ?? (known
        ? {
            ...known,
            connected: event.kind === 'online',
            ...(event.kind === 'offline' ? { lastSeenAt: event.at } : {})
          }
        : null)
      if (!merged) return { activity: prependActivity(state.activity, event) }
      const satellites = index >= 0
        ? state.satellites.map((satellite, at) => (at === index ? merged : satellite))
        : [...state.satellites, merged]
      return { satellites, activity: prependActivity(state.activity, event) }
    })
  },

  applyBusy(hosts) {
    const busy: Record<string, SatelliteLeases> = {}
    for (const host of hosts) {
      const leases: SatelliteLeases = {}
      for (const workspace of host.workspaces ?? []) {
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

/** Every consumer may call this; only the first takes the one Hub subscription. */
export function bootstrapSatellites(): void {
  const store = useSatellitesStore.getState()
  if (store.bootstrapped) return
  useSatellitesStore.setState({ bootstrapped: true })
  void store.load()
  window.api.satellites.onEvent((event) => {
    useSatellitesStore.getState().applyEvent(event)
    for (const listener of eventListeners) listener(event)
  })
}
