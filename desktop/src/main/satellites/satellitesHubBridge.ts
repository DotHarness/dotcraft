import { BrowserWindow } from 'electron'
import {
  normalizeSatellites,
  parseSatelliteEvent,
  withEventPresence,
  type Satellite,
  type SatelliteEvent
} from '../../shared/satellites'
import type { DesktopHubClient } from '../desktopHub'

const MAX_EVENTS_PER_PEER = 20
const MAX_EVENTS_TOTAL = 200
const FALLBACK_POLL_INTERVAL_MS = 30_000

const SATELLITES_EVENT_CHANNEL = 'satellites:event'

export interface SatellitesHubBridgeDeps {
  getHubClient: () => DesktopHubClient
  /** Defaults to every live window; injected in tests. */
  broadcast?: (event: SatelliteEvent) => void
}

function broadcastToWindows(event: SatelliteEvent): void {
  for (const window of BrowserWindow.getAllWindows()) {
    if (!window.isDestroyed()) window.webContents.send(SATELLITES_EVENT_CHANNEL, event)
  }
}

export class SatellitesHubBridge {
  private deps: SatellitesHubBridgeDeps
  private refCount = 0
  private subscription: AbortController | null = null
  private pollTimer: ReturnType<typeof setInterval> | null = null
  private readonly events: SatelliteEvent[] = []
  private readonly snapshot = new Map<string, Satellite>()
  private hasSnapshot = false

  constructor(deps: SatellitesHubBridgeDeps) {
    this.deps = deps
  }

  updateDeps(deps: SatellitesHubBridgeDeps): void {
    this.deps = deps
  }

  acquire(): void {
    this.refCount += 1
    if (this.refCount === 1) this.startSubscription()
  }

  release(): void {
    this.refCount = Math.max(0, this.refCount - 1)
    if (this.refCount > 0) return
    this.subscription?.abort()
    this.subscription = null
    this.stopFallbackPoll()
  }

  /** Caches the latest Hub list so events can carry the machine they name. */
  remember(satellites: Satellite[]): void {
    this.snapshot.clear()
    for (const satellite of satellites) this.snapshot.set(satellite.peerId, satellite)
    this.hasSnapshot = true
  }

  /** Recent activity, newest first; scoped to one machine when a peer is given. */
  recentActivity(peerId?: string): SatelliteEvent[] {
    const scoped = peerId ? this.events.filter((event) => event.peerId === peerId) : this.events
    return scoped.slice().reverse()
  }

  private startSubscription(): void {
    if (this.subscription) return
    const controller = new AbortController()
    this.subscription = controller
    this.stopFallbackPoll()

    const finish = (): void => {
      if (this.subscription !== controller) return
      this.subscription = null
      if (this.refCount > 0) this.startFallbackPoll()
    }
    try {
      void this.deps
        .getHubClient()
        .subscribeEvents((event) => this.handleHubEvent(event), controller.signal)
        .then(finish, finish)
    } catch {
      finish()
    }
  }

  /** Waits a full interval before its first tick: a failed resubscribe settles
   *  immediately, so an eager tick would spin. */
  private startFallbackPoll(): void {
    if (this.pollTimer) return
    this.pollTimer = setInterval(() => {
      void this.pollPresence().finally(() => this.startSubscription())
    }, FALLBACK_POLL_INTERVAL_MS)
  }

  private stopFallbackPoll(): void {
    if (!this.pollTimer) return
    clearInterval(this.pollTimer)
    this.pollTimer = null
  }

  private async pollPresence(): Promise<void> {
    let satellites: Satellite[]
    try {
      satellites = normalizeSatellites(await this.deps.getHubClient().listSatellites())
    } catch {
      return
    }
    const previous = this.hasSnapshot ? new Map(this.snapshot) : null
    this.remember(satellites)
    if (!previous) return

    const at = new Date().toISOString()
    for (const satellite of satellites) {
      const before = previous.get(satellite.peerId)
      if (!before) {
        this.publish({ kind: 'joined', at, peerId: satellite.peerId, satellite })
      } else if (before.connected !== satellite.connected) {
        this.publish({
          kind: satellite.connected ? 'online' : 'offline',
          at,
          peerId: satellite.peerId,
          satellite
        })
      }
    }
    for (const peerId of previous.keys()) {
      if (!this.snapshot.has(peerId)) this.publish({ kind: 'revoked', at, peerId })
    }
  }

  private handleHubEvent(raw: unknown): void {
    const event = parseSatelliteEvent(raw)
    if (!event) return
    void this.publishWithMachine(event)
  }

  private async publishWithMachine(event: SatelliteEvent): Promise<void> {
    let satellite = this.snapshot.get(event.peerId)
    if (!satellite && event.kind !== 'revoked') {
      try {
        this.remember(normalizeSatellites(await this.deps.getHubClient().listSatellites()))
        satellite = this.snapshot.get(event.peerId)
      } catch {
        // Presence still fans out without the machine record.
      }
    }
    if (event.kind === 'revoked') this.snapshot.delete(event.peerId)
    if (!satellite) {
      this.publish(event)
      return
    }
    const reconciled = withEventPresence(satellite, event)
    if (event.kind !== 'revoked') this.snapshot.set(event.peerId, reconciled)
    this.publish({ ...event, satellite: reconciled })
  }

  private publish(event: SatelliteEvent): void {
    this.record(event)
    ;(this.deps.broadcast ?? broadcastToWindows)(event)
  }

  private record(event: SatelliteEvent): void {
    this.events.push(event)
    let peerCount = 0
    for (let i = this.events.length - 1; i >= 0; i--) {
      if (this.events[i].peerId !== event.peerId) continue
      peerCount += 1
      if (peerCount > MAX_EVENTS_PER_PEER) this.events.splice(i, 1)
    }
    if (this.events.length > MAX_EVENTS_TOTAL) {
      this.events.splice(0, this.events.length - MAX_EVENTS_TOTAL)
    }
  }
}

let sharedBridge: SatellitesHubBridge | null = null

/** Enrollment outlives a workspace switch, so the bridge is a process singleton. */
export function getSatellitesHubBridge(deps: SatellitesHubBridgeDeps): SatellitesHubBridge {
  if (sharedBridge) sharedBridge.updateDeps(deps)
  else sharedBridge = new SatellitesHubBridge(deps)
  return sharedBridge
}
