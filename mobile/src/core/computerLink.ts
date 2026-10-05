import type { ThreadConfiguration } from '@dotcraft/sdk/contracts'
import { Reconnector, type Timers } from './backoff'
import { plainMessage, type MessageDraft } from './draft'
import { GatewayClient, GatewayUnreachableError, isUnauthorized } from './gateway'
import { NotConnectedError, ProjectConnection, ProjectNotRunningError, type ApprovalDecision } from './projectConnection'
import type { PinnedSockets, SocketEnd, SocketHandle } from './sockets'
import { projectById, type Action, type ComputerAction, type ComputerState, type MobileState, type PendingRequest } from './state'
import type { Store } from './store'
import type { ConfigChange } from './threadConfig'

export class CantStartProjectError extends Error {
  constructor() {
    super('The computer cannot start this project right now.')
    this.name = 'CantStartProjectError'
  }
}

export interface ComputerLinkHost {
  sockets: PinnedSockets
  timers: Timers
  random?: () => number
  appVersion: string
  awake(): boolean
  revoked(computerId: string): void
}

export class ComputerLink {
  readonly store: Store<ComputerState, ComputerAction>
  private readonly reconnector: Reconnector
  private readonly projectRetries = new Map<string, Reconnector>()
  private readonly connections = new Map<string, ProjectConnection>()
  private readonly opening = new Map<string, Promise<void>>()
  private readonly views = new Map<string, number>()
  private events: SocketHandle | null = null
  private generation = 0

  constructor(
    readonly id: string,
    private readonly gateway: GatewayClient,
    root: Store<MobileState, Action>,
    private readonly host: ComputerLinkHost,
  ) {
    this.store = {
      getState: () => root.getState().computers[id],
      dispatch: (action) => root.dispatch({ type: 'computer', computerId: id, action }),
      subscribe: (listener) => root.subscribe(listener),
    }
    this.reconnector = new Reconnector(() => void this.connect(true), host.timers, host.random)
  }

  private get state(): ComputerState {
    return this.store.getState()
  }

  private dispatch(action: ComputerAction): void {
    this.store.dispatch(action)
  }

  get reconnectPending(): boolean {
    return this.reconnector.pending
  }

  private stale(generation: number): boolean {
    return generation !== this.generation
  }

  async connect(quiet = false): Promise<void> {
    if (!this.host.awake() || this.state.identityChanged) return
    this.halt()
    const generation = this.generation
    if (!quiet) this.dispatch({ type: 'link', link: 'connecting' })
    try {
      const hello = await this.gateway.hello()
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
          lastAddress: this.gateway.address,
        },
      })
      if (!(await this.reloadProjects(generation))) return
      this.openEvents(generation)
      await this.openRunning(generation)
      if (this.stale(generation)) return
      this.dispatch({ type: 'syncing', value: false, at: new Date().toISOString() })
      this.reconnector.reset()
    } catch (error) {
      if (!this.stale(generation)) this.fail(error)
    }
  }

  restart(): void {
    this.reconnector.reset()
    void this.connect()
  }

  private async reloadProjects(generation: number): Promise<boolean> {
    try {
      const projects = await this.gateway.projects()
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
    if (isUnauthorized(error)) this.host.revoked(this.id)
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

  halt(): void {
    this.generation += 1
    this.reconnector.cancel()
    this.events?.close(1000, '')
    this.events = null
    for (const retry of this.projectRetries.values()) retry.cancel()
    this.projectRetries.clear()
    for (const projectId of [...this.connections.keys()]) void this.detach(projectId)?.close()
  }

  sleep(): void {
    this.halt()
    this.dispatch({ type: 'syncing', value: false })
    this.dispatch({ type: 'link', link: 'idle' })
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
    if (reason === 'deviceRevoked') this.host.revoked(this.id)
    else if (reason === 'gatewayOff') this.accessTurnedOff()
    else return false
    return true
  }

  private openEvents(generation: number): void {
    this.events = this.host.sockets.open(this.gateway.socket('/m/events'), {
      open: () => undefined,
      message: (text) => {
        if (!this.stale(generation)) this.onGatewayEvent(text, generation)
      },
      end: (end) => {
        if (this.stale(generation)) return
        this.events = null
        if (end.kind === 'closed' && this.closedBy(end.reason)) return
        if (end.kind === 'failed' && end.status === 401) this.host.revoked(this.id)
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
      sockets: this.host.sockets,
      gateway: this.gateway,
      store: this.store,
      appVersion: this.host.appVersion,
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
          else if (isUnauthorized(error)) this.host.revoked(this.id)
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
      retry = new Reconnector(() => void this.openProject(projectId, generation), this.host.timers, this.host.random)
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

  networkChanged(): void {
    if (!this.host.awake() || this.state.identityChanged) return
    if (this.state.link === 'online' || (this.state.link === 'connecting' && !this.reconnector.pending)) return
    this.restart()
  }

  async removeDevice(): Promise<void> {
    if (!this.state.identityChanged) await this.gateway.removeDevice().catch(() => undefined)
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
    const generation = this.generation
    if (this.state.link !== 'online') {
      this.dispatch({ type: 'phase', projectId, phase: 'cantStart' })
      return false
    }
    if (this.connections.get(projectId)?.ready) return true
    this.dispatch({ type: 'phase', projectId, phase: 'starting' })
    try {
      await this.gateway.ensure(projectId)
      if (this.stale(generation)) return false
      this.dispatch({ type: 'projectRunning', projectId, running: true })
      await this.openProject(projectId, generation)
    } catch (error) {
      if (isUnauthorized(error)) {
        this.host.revoked(this.id)
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
}
