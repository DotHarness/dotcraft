import { systemTimers, type Timers } from './backoff'
import { ComputerLink } from './computerLink'
import { GatewayClient, GatewayError } from './gateway'
import { forgetImages } from './imageCache'
import { LiveSession, type LiveNotifier } from './liveSession'
import type { PairingOffer } from './pairing'
import type { PinnedNative } from './pinned'
import { PinnedSockets } from './sockets'
import { initialState, persistable, reducer, type Action, type ComputerRecord, type MobileState, type PersistedState } from './state'
import { createStore, type Store } from './store'

export { CantStartProjectError } from './computerLink'

export interface CredentialStore {
  get(computerId: string): Promise<string | null>
  set(computerId: string, credential: string): Promise<void>
  clear(computerId: string): Promise<void>
}

export interface StateStorage {
  load(): Promise<PersistedState | null>
  save(state: PersistedState): Promise<void>
  clear(): Promise<void>
}

export interface DeviceInfo {
  displayName: string
  platform: 'ios' | 'android'
  osVersion: string
  appVersion: string
}

export interface SessionPlatform {
  native: PinnedNative
  credentials: CredentialStore
  storage: StateStorage
  device: DeviceInfo
  live?: LiveNotifier
  timers?: Timers
  random?: () => number
}

export class MobileSession {
  readonly store: Store<MobileState, Action> = createStore(reducer, initialState())
  private readonly sockets: PinnedSockets
  private readonly timers: Timers
  private readonly live: LiveSession | null
  private readonly links = new Map<string, ComputerLink>()
  private pairingGateway: GatewayClient | null = null
  private foreground = true
  private network: string | null = null
  private saveHandle: unknown = null
  private booted = false

  constructor(private readonly platform: SessionPlatform) {
    this.sockets = new PinnedSockets(platform.native)
    this.timers = platform.timers ?? systemTimers
    this.store.subscribe(() => this.scheduleSave())
    this.live = platform.live
      ? new LiveSession(platform.live, {
          store: this.store,
          timers: this.timers,
          openChat: (computerId, key) => this.links.get(computerId)?.openChat(key),
          closeChat: (computerId, key) => this.links.get(computerId)?.closeChat(key),
          decide: (computerId, key, requestId, decision) => this.links.get(computerId)?.decide(key, requestId, decision),
          ended: () => this.sleep(),
        })
      : null
  }

  private get state(): MobileState {
    return this.store.getState()
  }

  private dispatch(action: Action): void {
    this.store.dispatch(action)
  }

  private get awake(): boolean {
    return this.foreground || Boolean(this.live?.running)
  }

  computer(computerId: string): ComputerLink | undefined {
    return this.links.get(computerId)
  }

  private link(computer: ComputerRecord, credential: string): ComputerLink {
    const link = new ComputerLink(computer.id, new GatewayClient(this.platform.native, computer, credential), this.store, {
      sockets: this.sockets,
      timers: this.timers,
      random: this.platform.random,
      appVersion: this.platform.device.appVersion,
      awake: () => this.awake,
      revoked: (computerId) => void this.forget(computerId, true),
    })
    this.links.set(computer.id, link)
    return link
  }

  async boot(): Promise<void> {
    if (this.booted) return
    this.booted = true
    const loaded = await this.platform.storage.load()
    const persisted = Array.isArray(loaded?.order) ? loaded : null
    if (loaded && !persisted) await this.platform.storage.clear()
    const credentials = new Map<string, string>()
    for (const id of persisted?.order ?? []) {
      const credential = persisted!.computers[id] ? await this.platform.credentials.get(id) : null
      if (credential) credentials.set(id, credential)
    }
    const kept: PersistedState | null = persisted && { ...persisted, order: persisted.order.filter((id) => credentials.has(id)) }
    this.dispatch({ type: 'hydrated', persisted: kept })
    for (const id of this.state.order) void this.link(this.state.computers[id].computer, credentials.get(id)!).connect()
  }

  select(computerId: string): void {
    this.dispatch({ type: 'selected', computerId })
  }

  setForeground(active: boolean): void {
    if (active === this.foreground) return
    this.foreground = active
    if (active) {
      const kept = this.live?.enterForeground() ?? false
      for (const link of this.links.values()) {
        if (!kept || link.store.getState().link !== 'online') link.restart()
      }
      return
    }
    if (!this.live?.enterBackground()) this.sleep()
  }

  private sleep(): void {
    for (const link of this.links.values()) link.sleep()
    this.flushSave()
  }

  networkChanged(network: string): void {
    if (network === this.network) return
    this.network = network
    for (const link of this.links.values()) link.networkChanged()
  }

  private async forget(computerId: string, revoked: boolean): Promise<void> {
    const link = this.links.get(computerId)
    if (!link) return
    link.halt()
    this.links.delete(computerId)
    forgetImages(computerId)
    this.dispatch({ type: 'forgotten', computerId, revoked })
    this.flushSave()
    await this.platform.credentials.clear(computerId)
  }

  async removeComputer(computerId: string): Promise<void> {
    await this.links.get(computerId)?.removeDevice()
    await this.forget(computerId, false)
  }

  acknowledgeRevoked(): void {
    this.dispatch({ type: 'revokedNoticeSeen' })
  }

  async beginPairing(offer: PairingOffer): Promise<void> {
    const gateway = new GatewayClient(this.platform.native, offer, null)
    this.pairingGateway = gateway
    this.dispatch({ type: 'pairing', pairing: { step: 'reaching', offer } })
    try {
      await gateway.probe()
      if (this.pairingGateway === gateway) this.dispatch({ type: 'pairing', pairing: { step: 'allow', offer, allowing: false } })
    } catch {
      if (this.pairingGateway === gateway) this.dispatch({ type: 'pairing', pairing: { step: 'unreachable', offer } })
    }
  }

  async allow(): Promise<void> {
    const pairing = this.state.pairing
    const gateway = this.pairingGateway
    if (pairing.step !== 'allow' || !gateway || pairing.allowing) return
    const offer = pairing.offer
    this.dispatch({ type: 'pairing', pairing: { step: 'allow', offer, allowing: true } })
    let result
    try {
      result = await gateway.pair({ code: offer.code, ...this.platform.device })
    } catch (error) {
      if (this.pairingGateway === gateway) {
        this.dispatch({ type: 'pairing', pairing: error instanceof GatewayError ? { step: 'invalid' } : { step: 'unreachable', offer } })
      }
      return
    }
    if (this.pairingGateway !== gateway) return
    this.pairingGateway = null
    const computer: ComputerRecord = {
      id: result.computer.computerId,
      name: result.computer.name,
      version: null,
      port: result.computer.port,
      fingerprint: offer.fingerprint,
      addresses: result.computer.addresses,
      relay: offer.relay,
      lastAddress: gateway.address,
      deviceId: result.deviceId,
      pairedAt: new Date().toISOString(),
    }
    const previous = this.links.get(computer.id)
    previous?.halt()
    await this.platform.credentials.set(computer.id, result.credential)
    forgetImages(computer.id)
    const link = this.link(computer, result.credential)
    this.dispatch({ type: 'paired', computer })
    this.dispatch({ type: 'pairing', pairing: { step: 'connected', name: computer.name } })
    this.flushSave()
    void previous?.removeDevice()
    void link.connect()
  }

  retryPairing(): void {
    const pairing = this.state.pairing
    if (pairing.step === 'unreachable') void this.beginPairing(pairing.offer)
  }

  resetPairing(): void {
    this.pairingGateway = null
    this.dispatch({ type: 'pairing', pairing: { step: 'idle' } })
  }

  private scheduleSave(): void {
    if (this.saveHandle !== null) this.timers.clearTimeout(this.saveHandle)
    this.saveHandle = this.timers.setTimeout(() => this.flushSave(), 400)
  }

  private flushSave(): void {
    if (this.saveHandle !== null) this.timers.clearTimeout(this.saveHandle)
    this.saveHandle = null
    if (!this.state.hydrated) return
    if (this.state.order.length === 0) void this.platform.storage.clear()
    else void this.platform.storage.save(persistable(this.state))
  }

  dispose(): void {
    for (const link of this.links.values()) link.halt()
    this.sockets.dispose()
  }
}
