import type WebSocket from 'ws'
import {
  GatewayClient,
  GatewayError,
  GatewayUnreachableError,
  isUnauthorized,
  parsePairingUrl,
  type DevicePlatform,
  type GatewayProject,
  type PairingOffer,
  type PairPending,
  type PairRequest,
  type PairResult,
  type PinnedRequester,
  type PinnedSocketOptions
} from '@dotcraft/sdk/gateway'
import type {
  AddFolderFailureCode,
  PairedComputer,
  PairedComputerEvent,
  PairedComputerProject,
  PairedComputerResult,
  PairedComputerStatus,
  PairingFailureCode
} from '../../shared/pairedComputers'
import { compareVersions } from '../../shared/sshMachineRemote'
import { openAppServerBridge, type AppServerBridge } from './appServerBridge'
import type { CredentialVault, PairedComputersStorage, StoredComputer, StoredProject } from './pairedComputersStorage'

const RETRY_BASE_MS = 1_000
const RETRY_MAX_MS = 30_000
const CLOSE_REASON_REVOKED = 'deviceRevoked'
const CLOSE_REASON_GATEWAY_OFF = 'gatewayOff'

export interface PairedComputersManagerDeps {
  storage: PairedComputersStorage
  vault: CredentialVault
  native: PinnedRequester
  openSocket: (options: PinnedSocketOptions) => WebSocket
  ownComputerId: () => Promise<string | null>
  appVersion: string
  enforceVersion: boolean
  device: { displayName: string; platform: DevicePlatform; osVersion: string }
  onEvent: (event: PairedComputerEvent) => void
  onRemoved: (computerId: string) => void
}

export interface OpenedPairedProject {
  computer: StoredComputer
  project: StoredProject
  bridge: AppServerBridge
}

interface Runtime {
  client: GatewayClient
  status: PairedComputerStatus
  accessOff: boolean
  listed: GatewayProject[] | null
  socket: WebSocket | null
  retryTimer: ReturnType<typeof setTimeout> | null
  attempt: number
  generation: number
  connecting: Promise<void> | null
}

function statusFailure(error: unknown): PairingFailureCode {
  if (!(error instanceof GatewayError)) return 'unreachable'
  return error.status === 403 ? 'pairingDenied' : 'pairingCodeInvalid'
}

function isPending(result: PairResult | PairPending): result is PairPending {
  return (result as PairPending).status === 'pending'
}

export class PairedComputersManager {
  private readonly computers = new Map<string, StoredComputer>()
  private readonly runtimes = new Map<string, Runtime>()
  private pairingRun = 0
  private started = false
  private disposed = false

  constructor(private readonly deps: PairedComputersManagerDeps) {}

  start(): void {
    if (this.started) return
    this.started = true
    for (const computer of this.deps.storage.load()) {
      this.computers.set(computer.computerId, computer)
      if (!this.startRuntime(computer) && this.deps.vault.available()) this.forget(computer.computerId, true)
    }
  }

  dispose(): void {
    this.disposed = true
    this.pairingRun += 1
    for (const computerId of this.runtimes.keys()) this.stopRuntime(computerId)
  }

  list(): PairedComputer[] {
    this.start()
    return [...this.computers.values()].map((computer) => this.view(computer))
  }

  savedProjects(): Array<{ computer: StoredComputer; project: StoredProject }> {
    this.start()
    return [...this.computers.values()].flatMap((computer) =>
      computer.projects.map((project) => ({ computer, project }))
    )
  }

  find(computerId: string, projectId: string): { computer: StoredComputer; project: StoredProject } | null {
    this.start()
    const computer = this.computers.get(computerId)
    const project = computer?.projects.find((candidate) => candidate.projectId === projectId)
    return computer && project ? { computer, project } : null
  }

  refresh(): void {
    this.start()
    for (const computerId of this.runtimes.keys()) void this.connect(computerId)
  }

  async pair(link: string): Promise<PairedComputerResult<PairedComputer, PairingFailureCode>> {
    this.start()
    const offer = parsePairingUrl(link)
    if (!offer) return { ok: false, code: 'invalidLink' }
    if (!this.deps.vault.available()) return { ok: false, code: 'storageUnavailable' }
    if ((await this.deps.ownComputerId().catch(() => null)) === offer.id) return { ok: false, code: 'ownComputer' }

    const run = ++this.pairingRun
    const client = new GatewayClient(this.deps.native, this.targetFromOffer(offer), null)
    let request: PairRequest = {
      code: offer.code,
      displayName: this.deps.device.displayName,
      platform: this.deps.device.platform,
      osVersion: this.deps.device.osVersion,
      appVersion: this.deps.appVersion
    }
    try {
      let result = await client.pair(request)
      while (isPending(result)) {
        if (run !== this.pairingRun) return { ok: false, code: 'pairingCodeInvalid' }
        if (result.requestKey) request = { ...request, requestKey: result.requestKey }
        this.deps.onEvent({ kind: 'pairingWaiting', name: offer.name })
        result = await client.pair(request)
      }
      if (run !== this.pairingRun) {
        void new GatewayClient(this.deps.native, this.targetFromOffer(offer), result.credential)
          .removeDevice()
          .catch(() => undefined)
        return { ok: false, code: 'pairingCodeInvalid' }
      }
      return { ok: true, value: this.savePairing(offer, result) }
    } catch (error) {
      return { ok: false, code: statusFailure(error) }
    }
  }

  cancelPairing(): void {
    this.pairingRun += 1
  }

  async remove(computerId: string): Promise<void> {
    this.start()
    const runtime = this.runtimes.get(computerId)
    if (runtime) await runtime.client.removeDevice().catch(() => undefined)
    this.forget(computerId, false)
  }

  async addFolder(
    computerId: string,
    path: string
  ): Promise<PairedComputerResult<PairedComputerProject, AddFolderFailureCode>> {
    const runtime = this.runtimes.get(computerId)
    if (!runtime) return { ok: false, code: 'unreachable' }
    try {
      const project = await runtime.client.addProject(path)
      this.saveProject(computerId, project)
      await this.reloadProjects(computerId)
      return { ok: true, value: { ...this.storedProject(project), saved: true, unavailable: false } }
    } catch (error) {
      if (error instanceof GatewayError && error.code === 'workspaceNotFound') return { ok: false, code: 'workspaceNotFound' }
      if (error instanceof GatewayError && error.code === 'invalidRequest') return { ok: false, code: 'relativePath' }
      return { ok: false, code: 'unreachable' }
    }
  }

  removeProject(computerId: string, projectId: string): void {
    const computer = this.computers.get(computerId)
    if (!computer) return
    computer.projects = computer.projects.filter((project) => project.projectId !== projectId)
    this.persist()
    this.emitChanged()
  }

  async openProject(computerId: string, projectId: string): Promise<OpenedPairedProject> {
    this.start()
    const computer = this.computers.get(computerId)
    const runtime = this.runtimes.get(computerId)
    if (!computer || !runtime) throw new Error('This computer is no longer paired.')
    if (runtime.status !== 'connected') await this.connect(computerId)
    if (runtime.status === 'updateRequired') {
      throw new Error(`Update DotCraft on ${computer.name} to ${this.deps.appVersion} or newer first.`)
    }
    if (runtime.status !== 'connected') throw new Error(`${computer.name} is not reachable right now.`)

    runtime.listed = await runtime.client.projects()
    const listed = runtime.listed.find((project) => project.projectId === projectId)
    if (!listed) throw new Error('This project is no longer on the computer.')
    const ensured = listed.running ? listed : await runtime.client.ensure(projectId)
    this.saveProject(computerId, ensured)
    const path = `/m/projects/${encodeURIComponent(projectId)}/appserver`
    const bridge = await openAppServerBridge(() => this.deps.openSocket(runtime.client.socket(path)))
    return { computer, project: this.storedProject(ensured), bridge }
  }

  private targetFromOffer(offer: PairingOffer) {
    return {
      id: offer.id,
      fingerprint: offer.fingerprint,
      port: offer.port,
      addresses: offer.addresses,
      relay: offer.relay
    }
  }

  private savePairing(offer: PairingOffer, paired: PairResult): PairedComputer {
    const previous = this.computers.get(paired.computer.computerId)
    const previousClient = this.runtimes.get(paired.computer.computerId)?.client
    if (previous) this.stopRuntime(previous.computerId)
    if (previousClient) void previousClient.removeDevice().catch(() => undefined)

    const computer: StoredComputer = {
      computerId: paired.computer.computerId,
      name: paired.computer.name,
      deviceId: paired.deviceId,
      port: paired.computer.port,
      fingerprint: paired.computer.fingerprint,
      addresses: paired.computer.addresses,
      relay: offer.relay,
      pairedAt: new Date().toISOString(),
      lastConnectedAt: null,
      version: null,
      credential: this.deps.vault.seal(paired.credential),
      projects: previous?.projects ?? []
    }
    this.computers.set(computer.computerId, computer)
    this.persist()
    this.startRuntime(computer)
    this.emitChanged()
    return this.view(computer)
  }

  private startRuntime(computer: StoredComputer): boolean {
    const credential = this.deps.vault.open(computer.credential)
    if (!credential) return false
    this.runtimes.set(computer.computerId, {
      client: new GatewayClient(
        this.deps.native,
        {
          id: computer.computerId,
          fingerprint: computer.fingerprint,
          port: computer.port,
          addresses: computer.addresses,
          relay: computer.relay
        },
        credential
      ),
      status: 'connecting',
      accessOff: false,
      listed: null,
      socket: null,
      retryTimer: null,
      attempt: 0,
      generation: 0,
      connecting: null
    })
    void this.connect(computer.computerId)
    return true
  }

  private stopRuntime(computerId: string): void {
    const runtime = this.runtimes.get(computerId)
    if (!runtime) return
    runtime.generation += 1
    if (runtime.retryTimer) clearTimeout(runtime.retryTimer)
    runtime.socket?.removeAllListeners()
    runtime.socket?.on('error', () => undefined)
    runtime.socket?.terminate()
    this.runtimes.delete(computerId)
  }

  private forget(computerId: string, notify: boolean): void {
    const computer = this.computers.get(computerId)
    this.stopRuntime(computerId)
    if (!computer) return
    this.computers.delete(computerId)
    this.persist()
    this.deps.onRemoved(computerId)
    if (notify) this.deps.onEvent({ kind: 'revoked', name: computer.name })
    this.emitChanged()
  }

  private connect(computerId: string): Promise<void> {
    const runtime = this.runtimes.get(computerId)
    if (!runtime || this.disposed) return Promise.resolve()
    if (runtime.connecting) return runtime.connecting
    if (runtime.retryTimer) {
      clearTimeout(runtime.retryTimer)
      runtime.retryTimer = null
    }
    const run = this.runConnect(computerId, runtime).finally(() => {
      runtime.connecting = null
    })
    runtime.connecting = run
    return run
  }

  private async runConnect(computerId: string, runtime: Runtime): Promise<void> {
    const generation = ++runtime.generation
    if (runtime.status !== 'connected' && runtime.status !== 'updateRequired') this.setStatus(runtime, 'connecting')
    try {
      const hello = await runtime.client.hello()
      if (generation !== runtime.generation) return
      const computer = this.computers.get(computerId)
      if (!computer) return
      computer.name = hello.name
      computer.port = hello.port
      computer.addresses = hello.addresses.length > 0 ? hello.addresses : computer.addresses
      computer.relay = hello.relay
      computer.version = hello.version
      computer.lastConnectedAt = new Date().toISOString()
      this.persist()
      runtime.listed = await runtime.client.projects()
      if (generation !== runtime.generation) return
      this.openEvents(computerId, runtime, generation)
      runtime.attempt = 0
      runtime.accessOff = false
      this.setStatus(runtime, this.versionTooOld(hello.version) ? 'updateRequired' : 'connected')
    } catch (error) {
      if (generation !== runtime.generation) return
      this.handleFailure(computerId, runtime, error)
    }
  }

  private versionTooOld(version: string): boolean {
    return this.deps.enforceVersion && compareVersions(version, this.deps.appVersion) < 0
  }

  private handleFailure(computerId: string, runtime: Runtime, error: unknown): void {
    if (isUnauthorized(error)) {
      this.forget(computerId, true)
      return
    }
    if (error instanceof GatewayUnreachableError && error.mismatch) {
      this.setStatus(runtime, 'identityChanged')
      return
    }
    this.setStatus(runtime, runtime.accessOff ? 'accessOff' : 'offline')
    this.scheduleRetry(computerId, runtime)
  }

  private scheduleRetry(computerId: string, runtime: Runtime): void {
    if (runtime.retryTimer || this.disposed) return
    const ceiling = Math.min(RETRY_MAX_MS, RETRY_BASE_MS * 2 ** runtime.attempt)
    runtime.attempt += 1
    runtime.retryTimer = setTimeout(() => {
      runtime.retryTimer = null
      void this.connect(computerId)
    }, ceiling * (0.5 + Math.random() / 2))
  }

  private openEvents(computerId: string, runtime: Runtime, generation: number): void {
    if (runtime.socket) {
      runtime.socket.removeAllListeners()
      runtime.socket.on('error', () => undefined)
      runtime.socket.terminate()
    }
    const socket = this.deps.openSocket(runtime.client.socket('/m/events'))
    runtime.socket = socket
    socket.on('message', (data) => {
      if (generation !== runtime.generation) return
      const message = (JSON.parse(data.toString()) as { type?: string }).type
      if (message === CLOSE_REASON_GATEWAY_OFF) runtime.accessOff = true
      if (message === 'projectStarted' || message === 'projectStopped') void this.reloadProjects(computerId).catch(() => undefined)
    })
    socket.on('error', () => undefined)
    socket.on('close', (_code, reason) => {
      if (runtime.socket === socket) runtime.socket = null
      if (generation !== runtime.generation) return
      const text = reason.toString()
      if (text === CLOSE_REASON_REVOKED) {
        this.forget(computerId, true)
        return
      }
      if (text === CLOSE_REASON_GATEWAY_OFF) runtime.accessOff = true
      this.setStatus(runtime, runtime.accessOff ? 'accessOff' : 'offline')
      this.scheduleRetry(computerId, runtime)
    })
  }

  private async reloadProjects(computerId: string): Promise<void> {
    const runtime = this.runtimes.get(computerId)
    if (!runtime) return
    runtime.listed = await runtime.client.projects()
    this.emitChanged()
  }

  private storedProject(project: GatewayProject): StoredProject {
    return { projectId: project.projectId, displayName: project.displayName, path: project.path, kind: project.kind }
  }

  private saveProject(computerId: string, project: GatewayProject): void {
    const computer = this.computers.get(computerId)
    if (!computer) return
    computer.projects = [
      this.storedProject(project),
      ...computer.projects.filter((candidate) => candidate.projectId !== project.projectId)
    ]
    this.persist()
    this.emitChanged()
  }

  private setStatus(runtime: Runtime, status: PairedComputerStatus): void {
    runtime.status = status
    this.emitChanged()
  }

  private view(computer: StoredComputer): PairedComputer {
    const runtime = this.runtimes.get(computer.computerId)
    const listed = runtime?.listed ?? null
    const savedIds = new Set(computer.projects.map((project) => project.projectId))
    const listedIds = new Set(listed?.map((project) => project.projectId))
    const projects: PairedComputerProject[] = [
      ...(listed ?? []).map((project) => ({
        ...this.storedProject(project),
        saved: savedIds.has(project.projectId),
        unavailable: false
      })),
      ...computer.projects
        .filter((project) => !listedIds.has(project.projectId))
        .map((project) => ({ ...project, saved: true, unavailable: listed !== null }))
    ]
    const status = runtime?.status ?? 'offline'
    return {
      computerId: computer.computerId,
      name: computer.name,
      status,
      version: computer.version,
      requiredVersion: status === 'updateRequired' ? this.deps.appVersion : null,
      lastConnectedAt: computer.lastConnectedAt,
      projects
    }
  }

  private persist(): void {
    this.deps.storage.save([...this.computers.values()])
  }

  private emitChanged(): void {
    this.deps.onEvent({ kind: 'changed', computers: [...this.computers.values()].map((computer) => this.view(computer)) })
  }
}
