import type { ThreadConfiguration } from '@dotcraft/sdk/contracts'
import { Reconnector, systemTimers, type Timers } from './backoff'
import { plainMessage, type MessageDraft } from './draft'
import { GatewayClient, GatewayError, GatewayUnreachableError, isUnauthorized } from './gateway'
import { forgetImages } from './imageCache'
import { LiveSession, type LiveNotifier } from './liveSession'
import type { PairingOffer } from './pairing'
import type { PinnedNative } from './pinned'
import { NotConnectedError, ProjectConnection, ProjectNotRunningError, type ApprovalDecision } from './projectConnection'
import { PinnedSockets, type SocketEnd, type SocketHandle } from './sockets'
import {
  initialState,
  persistable,
  projectById,
  reducer,
  type Action,
  type ComputerRecord,
  type MobileState,
  type PendingRequest,
  type PersistedState,
} from './state'
import { createStore, type Store } from './store'
import type { ConfigChange } from './threadConfig'

export interface CredentialStore {
  get(): Promise<string | null>
  set(credential: string): Promise<void>
  clear(): Promise<void>
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

export class CantStartProjectError extends Error {
  constructor() {
    super('The computer cannot start this project right now.')
    this.name = 'CantStartProjectError'
  }
}

export class MobileSession {
  readonly store: Store<MobileState, Action> = createStore(reducer, initialState())
  private readonly sockets: PinnedSockets
  private readonly timers: Timers
  private readonly reconnector: Reconnector
  private readonly live: LiveSession | null
  private readonly projectRetries = new Map<string, Reconnector>()
  private readonly connections = new Map<string, ProjectConnection>()
  private readonly opening = new Map<string, Promise<void>>()
  private readonly views = new Map<string, number>()
  private gateway: GatewayClient | null = null
  private pairingGateway: GatewayClient | null = null
  private events: SocketHandle | null = null
  private foreground = true
  private generation = 0
  private saveHandle: unknown = null
  private booted = false

  constructor(private readonly platform: SessionPlatform) {
    this.sockets = new PinnedSockets(platform.native)
    this.timers = platform.timers ?? systemTimers
    this.reconnector = new Reconnector(() => void this.connect(true), this.timers, platform.random)
    this.store.subscribe(() => this.scheduleSave())
    this.live = platform.live
      ? new LiveSession(platform.live, {
          store: this.store,
          timers: this.timers,
          openChat: (key) => this.openChat(key),
          closeChat: (key) => this.closeChat(key),
          decide: (key, requestId, decision) => this.decide(key, requestId, decision),
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

  get reconnectPending(): boolean {
    return this.reconnector.pending
  }

  private get awake(): boolean {
    return this.foreground || Boolean(this.live?.running)
  }

  async boot(): Promise<void> {
    if (this.booted) return
    this.booted = true
    const [persisted, credential] = await Promise.all([this.platform.storage.load(), this.platform.credentials.get()])
    if (persisted?.computer && credential) {
      this.dispatch({ type: 'hydrated', persisted })
      this.gateway = new GatewayClient(this.platform.native, persisted.computer, credential)
      void this.connect()
    } else {
      if (persisted) await this.platform.storage.clear()
      this.dispatch({ type: 'hydrated', persisted: null })
    }
  }

  private stale(generation: number): boolean {
    return generation !== this.generation
  }

  async connect(quiet = false): Promise<void> {
    const gateway = this.gateway
    if (!gateway || !this.awake || this.state.identityChanged) return
    this.halt()
    const generation = this.generation
    if (!quiet) this.dispatch({ type: 'link', link: 'connecting' })
    try {
      const hello = await gateway.hello()
      if (this.stale(generation)) return
      this.dispatch({ type: 'syncing', value: true })
      this.dispatch({ type: 'link', link: 'online' })
      this.dispatch({
        type: 'computerSeen',
        patch: {
          name: hello.name,
          version: hello.version,
          port: hello.port,
          addresses: hello.addresses,
          relay: hello.relay,
          lastAddress: gateway.address,
        },
      })
      if (!(await this.reloadProjects(generation))) return
      this.openEvents(gateway, generation)
      await this.openRunning(generation)
      if (this.stale(generation)) return
      this.dispatch({ type: 'syncing', value: false, at: new Date().toISOString() })
      this.reconnector.reset()
    } catch (error) {
      if (!this.stale(generation)) this.fail(error)
    }
  }

  private async reloadProjects(generation: number): Promise<boolean> {
    try {
      const projects = await this.gateway!.projects()
      if (this.stale(generation)) return false
      this.dispatch({
        type: 'projects',
        projects: projects.map((project) => ({
          id: project.projectId,
          name: project.displayName,
          isChats: project.displayName === 'Chats',
          running: project.running,
          lastActiveAt: project.lastActiveAt,
        })),
      })
      return true
    } catch (error) {
      if (!this.stale(generation)) this.fail(error)
      return false
    }
  }

  private openRunning(generation: number): Promise<void[]> {
    return Promise.all(this.state.projects.filter((project) => project.running).map((project) => this.openProject(project.id, generation)))
  }

  private fail(error: unknown): void {
    if (isUnauthorized(error)) void this.revoked()
    else if (error instanceof GatewayUnreachableError && error.mismatch) this.identityChanged()
    else this.lose()
  }

  private lose(): void {
    const dropped = this.state.link === 'online'
    this.halt()
    this.dispatch({ type: 'syncing', value: false })
    this.dispatch({ type: 'link', link: dropped ? 'connecting' : 'offline' })
    this.reconnector.schedule()
  }

  private halt(): void {
    this.generation += 1
    this.reconnector.cancel()
    this.events?.close(1000, '')
    this.events = null
    for (const retry of this.projectRetries.values()) retry.cancel()
    this.projectRetries.clear()
    for (const projectId of [...this.connections.keys()]) void this.detach(projectId)?.close()
  }

  private detach(projectId: string): ProjectConnection | undefined {
    const connection = this.connections.get(projectId)
    this.connections.delete(projectId)
    this.opening.delete(projectId)
    this.dispatch({ type: 'pendingCleared', projectId })
    this.dispatch({ type: 'phase', projectId, phase: null })
    return connection
  }

  private closedBy(reason: string): boolean {
    if (reason === 'deviceRevoked') void this.revoked()
    else if (reason === 'gatewayOff') this.accessTurnedOff()
    else return false
    return true
  }

  private openEvents(gateway: GatewayClient, generation: number): void {
    this.events = this.sockets.open(gateway.socket('/m/events'), {
      open: () => undefined,
      message: (text) => {
        if (!this.stale(generation)) this.onGatewayEvent(text, generation)
      },
      end: (end) => {
        if (this.stale(generation)) return
        this.events = null
        if (end.kind === 'closed' && this.closedBy(end.reason)) return
        if (end.kind === 'failed' && end.status === 401) void this.revoked()
        else this.lose()
      },
    })
  }

  private onGatewayEvent(text: string, generation: number): void {
    const event = JSON.parse(text) as { type: string; projectId: string }
    if (this.closedBy(event.type)) return
    if (event.type === 'projectStopped') this.projectStopped(event.projectId)
    else if (event.type === 'projectStarted') {
      void this.reloadProjects(generation).then((loaded) => {
        if (loaded) void this.openRunning(generation)
      })
    }
  }

  private projectStopped(projectId: string): void {
    this.dispatch({ type: 'projectRunning', projectId, running: false })
    this.projectRetries.get(projectId)?.cancel()
    this.projectRetries.delete(projectId)
    void this.detach(projectId)?.close()
  }

  private openProject(projectId: string, generation: number): Promise<void> {
    const pending = this.opening.get(projectId)
    if (pending) return pending
    if (this.connections.has(projectId) || this.stale(generation)) return Promise.resolve()
    const connection: ProjectConnection = new ProjectConnection({
      projectId,
      sockets: this.sockets,
      gateway: this.gateway!,
      store: this.store,
      appVersion: this.platform.device.appVersion,
      openThreads: () => this.openThreads(projectId),
      onLost: (end) => this.projectLost(projectId, connection, end, generation),
    })
    this.connections.set(projectId, connection)
    this.dispatch({ type: 'phase', projectId, phase: 'connecting' })
    const opening = connection
      .connect()
      .then(
        () => {
          if (this.connections.get(projectId) !== connection) return
          this.dispatch({ type: 'phase', projectId, phase: 'ready' })
          this.projectRetries.get(projectId)?.reset()
        },
        (error: unknown) => {
          if (this.connections.get(projectId) !== connection) return
          void this.detach(projectId)?.close()
          if (this.stale(generation)) return
          if (error instanceof ProjectNotRunningError) this.dispatch({ type: 'projectRunning', projectId, running: false })
          else if (isUnauthorized(error)) void this.revoked()
          else this.retryProject(projectId, generation)
        },
      )
      .finally(() => {
        if (this.opening.get(projectId) === opening) this.opening.delete(projectId)
      })
    this.opening.set(projectId, opening)
    return opening
  }

  private retryProject(projectId: string, generation: number): void {
    let retry = this.projectRetries.get(projectId)
    if (!retry) {
      retry = new Reconnector(() => void this.openProject(projectId, generation), this.timers, this.platform.random)
      this.projectRetries.set(projectId, retry)
    }
    if (this.state.link === 'online') retry.schedule()
  }

  private projectLost(projectId: string, connection: ProjectConnection, end: SocketEnd, generation: number): void {
    if (this.connections.get(projectId) !== connection || this.stale(generation)) return
    this.detach(projectId)
    if (end.kind === 'closed' && this.closedBy(end.reason)) return
    void this.reloadProjects(generation).then((loaded) => {
      if (!loaded) return
      if (projectById(this.state, projectId)?.running) this.retryProject(projectId, generation)
      else this.projectStopped(projectId)
    })
  }

  private accessTurnedOff(): void {
    this.dispatch({ type: 'accessOff' })
    this.lose()
  }

  private identityChanged(): void {
    this.halt()
    this.dispatch({ type: 'identityChanged' })
  }

  private async forget(revokedBy: string | null): Promise<void> {
    this.halt()
    forgetImages()
    this.gateway = null
    this.views.clear()
    this.dispatch({ type: 'forgotten', revokedBy })
    await Promise.all([this.platform.credentials.clear(), this.platform.storage.clear()])
  }

  private async revoked(): Promise<void> {
    if (this.state.computer) await this.forget(this.state.computer.name)
  }

  setForeground(active: boolean): void {
    if (active === this.foreground) return
    this.foreground = active
    if (active) {
      if (this.live?.enterForeground() && this.state.link === 'online') return
      this.reconnector.reset()
      void this.connect()
      return
    }
    if (!this.live?.enterBackground()) this.sleep()
  }

  private sleep(): void {
    this.halt()
    this.dispatch({ type: 'syncing', value: false })
    this.dispatch({ type: 'link', link: 'idle' })
    this.flushSave()
  }

  networkChanged(): void {
    if (!this.awake || !this.state.computer || this.state.identityChanged) return
    if (this.state.link === 'online' || (this.state.link === 'connecting' && !this.reconnector.pending)) return
    this.reconnector.reset()
    void this.connect()
  }

  private openThreads(projectId: string): string[] {
    const prefix = `${projectId}:`
    return [...this.views.keys()].filter((key) => key.startsWith(prefix)).map((key) => key.slice(prefix.length))
  }

  private split(key: string): { projectId: string; threadId: string } {
    const index = key.indexOf(':')
    return { projectId: key.slice(0, index), threadId: key.slice(index + 1) }
  }

  private connection(projectId: string): ProjectConnection {
    const connection = this.connections.get(projectId)
    if (!connection) throw new NotConnectedError()
    return connection
  }

  openChat(key: string): void {
    const count = this.views.get(key) ?? 0
    this.views.set(key, count + 1)
    if (count > 0) return
    const { projectId, threadId } = this.split(key)
    const connection = this.connections.get(projectId)
    if (connection?.ready && !connection.isOpen(threadId)) void connection.syncChat(threadId).catch(() => undefined)
  }

  closeChat(key: string): void {
    const count = this.views.get(key) ?? 0
    if (count > 1) {
      this.views.set(key, count - 1)
      return
    }
    this.views.delete(key)
    const { projectId, threadId } = this.split(key)
    void this.connections.get(projectId)?.unsubscribe(threadId)
  }

  async send(key: string, draft: MessageDraft): Promise<void> {
    const { projectId, threadId } = this.split(key)
    await this.connection(projectId).send(threadId, draft)
  }

  async setMode(key: string, mode: 'plan' | 'agent'): Promise<void> {
    const { projectId, threadId } = this.split(key)
    await this.connection(projectId).setMode(threadId, mode)
  }

  async implementPlan(key: string): Promise<void> {
    await this.setMode(key, 'agent')
    await this.send(key, plainMessage('Implement the plan.'))
  }

  async loadUsage(projectId: string): Promise<void> {
    const connection = this.connections.get(projectId)
    if (connection?.ready) await connection.loadUsage()
  }

  async readFile(projectId: string, path: string): Promise<string> {
    return await this.connection(projectId).readFile(path)
  }

  async loadReferences(projectId: string): Promise<void> {
    const connection = this.connections.get(projectId)
    if (connection?.ready) await connection.loadReferences()
  }

  async stop(key: string): Promise<void> {
    const { projectId, threadId } = this.split(key)
    await this.connection(projectId).stop(threadId)
  }

  async rename(key: string, title: string): Promise<void> {
    const { projectId, threadId } = this.split(key)
    await this.connection(projectId).rename(threadId, title)
  }

  async fork(key: string): Promise<string> {
    const { projectId, threadId } = this.split(key)
    return await this.connection(projectId).fork(threadId)
  }

  async archive(key: string): Promise<void> {
    const { projectId, threadId } = this.split(key)
    await this.connection(projectId).archive(threadId)
  }

  async updateConfig(key: string, change: ConfigChange): Promise<void> {
    const { projectId, threadId } = this.split(key)
    await this.connection(projectId).updateConfig(threadId, change)
  }

  async loadModels(projectId: string, providerId: string | null = null): Promise<void> {
    const connection = this.connections.get(projectId)
    if (connection?.ready) await connection.loadModels(providerId)
  }

  decide(key: string, requestId: string, decision: ApprovalDecision): void {
    const { projectId, threadId } = this.split(key)
    this.connections.get(projectId)?.decide(threadId, requestId, decision)
  }

  answer(key: string, requestId: string, answers: Record<string, string[]>): void {
    const { projectId, threadId } = this.split(key)
    this.connections.get(projectId)?.answer(threadId, requestId, answers)
  }

  async dismissQuestion(key: string, request: Extract<PendingRequest, { kind: 'question' }>): Promise<void> {
    if (!request.isBlocking) {
      this.answer(key, request.requestId, {})
      return
    }
    await this.stop(key)
    const { projectId, threadId } = this.split(key)
    this.connections.get(projectId)?.cancelRequest(threadId, request.requestId)
  }

  async startProject(projectId: string): Promise<boolean> {
    const gateway = this.gateway
    const generation = this.generation
    if (!gateway || this.state.link !== 'online') {
      this.dispatch({ type: 'phase', projectId, phase: 'cantStart' })
      return false
    }
    if (this.connections.get(projectId)?.ready) return true
    this.dispatch({ type: 'phase', projectId, phase: 'starting' })
    try {
      await gateway.ensure(projectId)
      if (this.stale(generation)) return false
      this.dispatch({ type: 'projectRunning', projectId, running: true })
      await this.openProject(projectId, generation)
    } catch (error) {
      if (isUnauthorized(error)) {
        void this.revoked()
        return false
      }
    }
    if (this.connections.get(projectId)?.ready) return true
    if (!this.stale(generation)) this.dispatch({ type: 'phase', projectId, phase: 'cantStart' })
    return false
  }

  async newChat(projectId: string, title: string, config?: ThreadConfiguration): Promise<string> {
    if (!(await this.startProject(projectId))) throw new CantStartProjectError()
    return await this.connection(projectId).startThread(title, config)
  }

  async removeComputer(): Promise<void> {
    if (this.gateway && !this.state.identityChanged) await this.gateway.removeDevice().catch(() => undefined)
    await this.forget(null)
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
    const previous = this.gateway
    const computer: ComputerRecord = {
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
    await this.platform.credentials.set(result.credential)
    this.halt()
    this.reconnector.reset()
    this.views.clear()
    this.gateway = new GatewayClient(this.platform.native, computer, result.credential)
    forgetImages()
    this.dispatch({ type: 'paired', computer })
    this.dispatch({ type: 'pairing', pairing: { step: 'connected', name: computer.name } })
    this.flushSave()
    void previous?.removeDevice().catch(() => undefined)
    void this.connect()
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
    if (this.state.hydrated && this.state.computer) void this.platform.storage.save(persistable(this.state))
  }

  dispose(): void {
    this.halt()
    this.sockets.dispose()
  }
}
