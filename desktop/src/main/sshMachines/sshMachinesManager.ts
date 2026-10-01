import type { HubEvent } from '@dotcraft/sdk/hub'
import type { RemoteStack } from '../../shared/dockerDeployments'
import {
  applyMachineEdit,
  buildSshResolveArgs,
  createMachineFromEntry,
  hasValidationErrors,
  machineEditEntry,
  machineHasHub,
  machineSshTarget,
  parseSshResolveOutput,
  validateMachineEntry,
  type RemoteFolderListing,
  type RemoteProject,
  type SshHostDiscovery,
  type SshMachine,
  type SshMachineAddEntry,
  type SshMachineEditPatch,
  type SshMachineStatus,
  type SshMachineSystemInfo,
  type SshMachinesPayload,
  type SshMachineValidation,
  type SshResolvedHost
} from '../../shared/sshMachines'
import {
  buildEnsureProjectWorkspaceCommand,
  buildListFoldersCommand,
  buildReadConfigFilesCommand,
  classifyProbe,
  CONNECT_RETRY_DELAYS_MS,
  eventEndpointPort,
  forwardedEndpointUrl,
  isEndpointChangeEvent,
  minimumDotCraftVersion,
  missingInstallTools,
  parseConfigFilesOutput,
  parseFolderListing,
  parseLoopbackEndpoint,
  resolveInstallVersion,
  systemInfoFromProbe,
  windowsUnsupportedStatus,
  type ProviderListLike
} from '../../shared/sshMachineRemote'
import {
  firstLine,
  generateId,
  isAbsoluteRemoteFolderPath,
  isValidRemoteFolderPath,
  normalizeRemoteFolderPath,
  redactSecrets,
  remoteBaseName,
  remoteChildPath
} from '../../shared/sshShell'
import { DockerDeploymentsManager } from './dockerDeploymentsManager'
import { discoverSshHosts, type SshProcessRunner } from './localSshConfig'
import {
  ConnectFailure,
  connectHub,
  evaluateModelReadiness,
  HUB_SLOT,
  MODEL_SLOT,
  probeMachine,
  runInstaller,
  stopRemoteHub,
  type ConnectorContext,
  type HubSession
} from './machineConnector'
import { createRemoteHubClient, type RemoteHubClient } from './remoteHubClient'
import { runSshCommand, runSshProcess, type SshRunner } from './sshExecutor'
import { TunnelManager, type Tunnels } from './tunnelManager'

export interface SshMachinesManagerDeps {
  loadMachines: () => SshMachine[]
  saveMachines: (machines: SshMachine[]) => void | Promise<void>
  installScript: () => string
  appVersion: string
  packaged: boolean
  runner?: SshRunner
  runProcess?: SshProcessRunner
  tunnels?: Tunnels
  createHubClient?: (options: { baseUrl: string; token: string }) => RemoteHubClient
  checkProviders?: (wsUrl: string) => Promise<ProviderListLike | null>
  sleep?: (ms: number) => Promise<void>
  now?: () => number
  retryDelaysMs?: number[]
  hubStartTimeoutMs?: number
  onChanged?: (payload: SshMachinesPayload) => void
  onMachineDisconnected?: (machineId: string) => void
  onProjectEndpointChanged?: (machineId: string, projectId: string) => void
  getForegroundProject?: () => { machineId: string; projectId: string } | null
}

export interface OpenedRemoteProject {
  machine: SshMachine
  project: RemoteProject
  wsUrl: string
  localPort: number
  remotePort: number
  tokenPresent: boolean
}

export class SshMachineInputError extends Error {
  constructor(readonly errors: SshMachineValidation) {
    super(`Invalid SSH connection: ${Object.entries(errors).map(([field, error]) => `${field} ${error}`).join(', ')}`)
    this.name = 'SshMachineInputError'
  }
}

interface MachineRuntime {
  status: SshMachineStatus
  system?: SshMachineSystemInfo
  resolved?: SshResolvedHost
  hub?: HubSession
  generation: number
  inFlight?: Promise<SshMachineStatus>
  eventsAbort?: AbortController
  wanted: boolean
}

const NOT_CONNECTED: SshMachineStatus = { kind: 'notConnected' }

export class SshMachinesManager {
  readonly docker: DockerDeploymentsManager
  private readonly runtimes = new Map<string, MachineRuntime>()
  private readonly projectRemotePorts = new Map<string, number>()
  private readonly ctx: ConnectorContext
  private readonly runProcess: SshProcessRunner
  private readonly retryDelaysMs: number[]
  private readonly checkProviders: (wsUrl: string) => Promise<ProviderListLike | null>

  constructor(private readonly deps: SshMachinesManagerDeps) {
    const runner = deps.runner ?? runSshCommand
    const tunnels = deps.tunnels ?? new TunnelManager()
    this.ctx = {
      runner,
      tunnels,
      createHubClient:
        deps.createHubClient ??
        ((options) =>
          createRemoteHubClient({ ...options, clientName: 'dotcraft-desktop', clientVersion: deps.appVersion })),
      sleep: deps.sleep ?? ((ms) => new Promise((resolve) => setTimeout(resolve, ms))),
      now: deps.now ?? (() => Date.now()),
      hubStartTimeoutMs: deps.hubStartTimeoutMs ?? 20_000
    }
    this.runProcess = deps.runProcess ?? runSshProcess
    this.retryDelaysMs = deps.retryDelaysMs ?? CONNECT_RETRY_DELAYS_MS
    this.checkProviders = deps.checkProviders ?? (async () => null)
    this.docker = new DockerDeploymentsManager({ runner, tunnels })
  }

  list(): SshMachinesPayload {
    return {
      machines: this.deps.loadMachines().map((machine) => {
        const rt = this.runtimes.get(machine.id)
        return {
          ...machine,
          status: rt?.status ?? NOT_CONNECTED,
          ...(rt?.system ? { system: rt.system } : {}),
          ...(rt?.resolved ? { resolved: rt.resolved } : {})
        }
      })
    }
  }

  getMachine(id: unknown): SshMachine {
    const machine = this.deps.loadMachines().find((m) => m.id === id)
    if (!machine) throw new Error('SSH connection not found.')
    return machine
  }

  getProject(machine: SshMachine, projectId: unknown): RemoteProject {
    const project = machine.projects.find((p) => p.id === projectId)
    if (!project) throw new Error('Remote project not found.')
    return project
  }

  discoverHosts(): Promise<SshHostDiscovery> {
    return discoverSshHosts(this.deps.loadMachines(), { run: this.runProcess })
  }

  async add(entries: SshMachineAddEntry[]): Promise<SshMachine[]> {
    const machines = this.deps.loadMachines()
    const created: SshMachine[] = []
    for (const entry of entries) {
      const errors = validateMachineEntry(entry, [...machines, ...created])
      if (hasValidationErrors(errors)) throw new SshMachineInputError(errors)
      created.push(createMachineFromEntry(entry, generateId))
    }
    await this.deps.saveMachines([...machines, ...created])
    this.emit()
    for (const machine of created) void this.connect(machine.id)
    return created
  }

  async edit(id: string, patch: SshMachineEditPatch): Promise<SshMachine> {
    const machines = this.deps.loadMachines()
    const index = machines.findIndex((m) => m.id === id)
    if (index < 0) throw new Error('SSH connection not found.')
    const previous = machines[index]
    const next = applyMachineEdit(previous, patch)
    const errors = validateMachineEntry(machineEditEntry(next), machines, id)
    if (hasValidationErrors(errors)) throw new SshMachineInputError(errors)
    machines[index] = next
    await this.deps.saveMachines(machines)
    const targetChanged =
      previous.hostname !== next.hostname || previous.port !== next.port || previous.identityFile !== next.identityFile
    if (targetChanged) {
      this.disconnect(id, NOT_CONNECTED)
      if (next.autoConnect) void this.connect(id)
    }
    this.emit()
    return next
  }

  async delete(id: string): Promise<void> {
    this.disconnect(id, NOT_CONNECTED)
    this.ctx.tunnels.closeForOwner(id)
    this.runtimes.delete(id)
    await this.deps.saveMachines(this.deps.loadMachines().filter((m) => m.id !== id))
    this.emit()
  }

  async setAutoConnect(id: string, autoConnect: boolean): Promise<void> {
    const machine = this.getMachine(id)
    if (autoConnect && this.runtimes.get(id)?.status.kind === 'unsupported') {
      throw new Error('This machine is not supported yet.')
    }
    await this.updateMachine(id, (m) => ({ ...m, autoConnect }))
    if (autoConnect) void this.connect(machine.id)
    else this.disconnect(id, NOT_CONNECTED)
    this.emit()
  }

  reconnect(id: string): Promise<SshMachineStatus> {
    this.getMachine(id)
    this.disconnect(id, NOT_CONNECTED, false)
    return this.connect(id)
  }

  connect(id: string): Promise<SshMachineStatus> {
    const rt = this.runtime(id)
    rt.wanted = true
    if (rt.inFlight) return rt.inFlight
    if (rt.hub && machineHasHub(rt.status)) return Promise.resolve(rt.status)
    const promise = this.runConnect(id, rt.generation).finally(() => {
      if (rt.inFlight === promise) rt.inFlight = undefined
    })
    rt.inFlight = promise
    return promise
  }

  connectAutoMachines(): void {
    for (const machine of this.deps.loadMachines()) {
      if (machine.autoConnect) void this.connect(machine.id)
    }
  }

  async install(id: string, mode: 'install' | 'update'): Promise<SshMachineStatus> {
    const machine = this.getMachine(id)
    const target = machineSshTarget(machine)
    const rt = this.runtime(id)
    this.disconnect(id, { kind: 'installing' }, false)
    const generation = rt.generation
    this.emit()
    try {
      const probe = await probeMachine(this.ctx, target)
      if (probe.kind === 'windows') return this.finish(id, generation, windowsUnsupportedStatus())
      rt.system = systemInfoFromProbe(probe.probe)
      const unsupported = classifyProbe(probe.probe)
      if (unsupported?.kind === 'unsupported') return this.finish(id, generation, unsupported)
      const missing = missingInstallTools(probe.probe)
      if (missing.length > 0) {
        return this.finish(id, generation, { kind: 'failed', message: `Missing required tool on the machine: ${missing.join(', ')}` })
      }
      if (mode === 'update') await stopRemoteHub(this.ctx, id, target)
      if (generation !== rt.generation) return rt.status
      await runInstaller(this.ctx, target, this.deps.installScript(), resolveInstallVersion(this.deps.appVersion, this.deps.packaged))
    } catch (error) {
      return this.finish(id, generation, { kind: 'failed', message: this.failureMessage(error) })
    }
    if (generation !== rt.generation) return rt.status
    rt.inFlight = undefined
    return this.connect(id)
  }

  async recheckModel(id: string): Promise<SshMachineStatus> {
    const machine = this.getMachine(id)
    const rt = this.runtime(id)
    if (!rt.hub || !machineHasHub(rt.status)) return this.connect(id)
    const generation = rt.generation
    try {
      const status = await this.modelStatus(machine, rt.hub)
      return this.finish(id, generation, status)
    } catch (error) {
      return this.finish(id, generation, { kind: 'failed', message: this.failureMessage(error) })
    }
  }

  async setStacks(id: string, stacks: RemoteStack[]): Promise<void> {
    await this.updateMachine(id, (m) => ({ ...m, stacks }))
    this.emit()
  }

  async listFolders(id: string, path?: string): Promise<RemoteFolderListing> {
    const machine = this.getMachine(id)
    const requested = path ?? '~'
    if (!isValidRemoteFolderPath(requested)) throw new Error('Invalid remote folder path.')
    const res = await this.ctx.runner(machineSshTarget(machine), buildListFoldersCommand(requested), { timeoutMs: 20_000 })
    if (res.timedOut) throw new Error('Listing folders timed out.')
    const listing = res.code === 0
      ? parseFolderListing(res.stdout, machine.projects.map((p) => p.path))
      : null
    if (!listing) {
      throw new Error(redactSecrets(firstLine(res.stderr)) || 'This folder is not available on the machine.')
    }
    return listing
  }

  async addProject(id: string, path: string): Promise<RemoteProject> {
    const machine = this.getMachine(id)
    if (!isAbsoluteRemoteFolderPath(path)) throw new Error('Remote project path must be absolute.')
    const normalized = normalizeRemoteFolderPath(path)
    if (machine.projects.some((p) => p.path === normalized)) throw new Error('This folder is already a project on this machine.')
    const res = await this.ctx.runner(machineSshTarget(machine), buildEnsureProjectWorkspaceCommand(normalized), { timeoutMs: 20_000 })
    if (res.timedOut || res.code !== 0 || !/WORKSPACE_READY/.test(res.stdout)) {
      throw new Error(
        /WORKSPACE_MISSING/.test(res.stdout)
          ? 'This folder no longer exists on the machine.'
          : redactSecrets(firstLine(res.stderr)) || 'Could not prepare the folder on the machine.'
      )
    }
    const project: RemoteProject = { id: generateId('p'), path: normalized, label: remoteBaseName(normalized) || normalized }
    await this.updateMachine(id, (m) => ({ ...m, projects: [...m.projects, project] }))
    this.emit()
    return project
  }

  async removeProject(id: string, projectId: string): Promise<void> {
    this.closeProjectForward(id, projectId)
    await this.updateMachine(id, (m) => ({ ...m, projects: m.projects.filter((p) => p.id !== projectId) }))
    this.emit()
  }

  async openProject(id: string, projectId: string): Promise<OpenedRemoteProject> {
    const machine = this.getMachine(id)
    const project = this.getProject(machine, projectId)
    const status = await this.connect(id)
    const hub = this.runtimes.get(id)?.hub
    if (!hub || !machineHasHub(status)) throw new Error(this.describeUnavailable(machine, status))

    const ensured = await hub.client.ensureAppServer(project.path)
    const rawEndpoint = ensured.endpoints?.appServerWebSocket
    const endpoint = parseLoopbackEndpoint(rawEndpoint)
    if (!rawEndpoint || !endpoint) throw new Error('The machine did not return an AppServer endpoint for this project.')
    const info = await this.ctx.tunnels.open(machineSshTarget(machine), id, projectSlot(projectId), endpoint.port)
    this.projectRemotePorts.set(projectKey(id, projectId), endpoint.port)
    return {
      machine,
      project,
      wsUrl: forwardedEndpointUrl(rawEndpoint, info.localPort),
      localPort: info.localPort,
      remotePort: endpoint.port,
      tokenPresent: Boolean(endpoint.token)
    }
  }

  closeProjectForward(id: string, projectId: string): void {
    this.projectRemotePorts.delete(projectKey(id, projectId))
    this.ctx.tunnels.closeOne(id, projectSlot(projectId))
  }

  async readProjectConfig(id: string, projectId: string): Promise<{ workspaceRaw: string; userDefaultsRaw: string }> {
    const machine = this.getMachine(id)
    const project = this.getProject(machine, projectId)
    const res = await this.ctx.runner(
      machineSshTarget(machine),
      buildReadConfigFilesCommand(remoteChildPath(project.path, '.craft/config.json')),
      { timeoutMs: 20_000, connectTimeoutSec: 8 }
    )
    if (res.timedOut) throw new Error('Remote workspace config read timed out.')
    const parsed = res.code === 0 ? parseConfigFilesOutput(res.stdout) : null
    if (!parsed) throw new Error(redactSecrets(firstLine(res.stderr)) || 'Remote workspace config read failed.')
    return parsed
  }

  dispose(): void {
    for (const rt of this.runtimes.values()) {
      rt.generation += 1
      rt.inFlight = undefined
      rt.wanted = false
      rt.eventsAbort?.abort()
      rt.eventsAbort = undefined
      rt.hub = undefined
      if (rt.status.kind !== 'unsupported') rt.status = NOT_CONNECTED
    }
    this.projectRemotePorts.clear()
    this.ctx.tunnels.closeAll()
  }

  private runtime(id: string): MachineRuntime {
    let rt = this.runtimes.get(id)
    if (!rt) {
      rt = { status: NOT_CONNECTED, generation: 0, wanted: false }
      this.runtimes.set(id, rt)
    }
    return rt
  }

  private emit(): void {
    this.deps.onChanged?.(this.list())
  }

  private setStatus(id: string, generation: number, status: SshMachineStatus): boolean {
    const rt = this.runtimes.get(id)
    if (!rt || rt.generation !== generation) return false
    rt.status = status
    this.emit()
    return true
  }

  private finish(id: string, generation: number, status: SshMachineStatus): SshMachineStatus {
    this.setStatus(id, generation, status)
    return this.runtimes.get(id)?.status ?? status
  }

  private disconnect(id: string, status: SshMachineStatus, notify = true): void {
    const rt = this.runtimes.get(id)
    if (!rt) return
    rt.generation += 1
    rt.inFlight = undefined
    rt.wanted = false
    rt.eventsAbort?.abort()
    rt.eventsAbort = undefined
    rt.hub = undefined
    this.ctx.tunnels.closeOne(id, HUB_SLOT)
    this.ctx.tunnels.closeOne(id, MODEL_SLOT)
    this.ctx.tunnels.closeMatching(id, 'project:')
    for (const key of [...this.projectRemotePorts.keys()]) {
      if (key.startsWith(`${id}::`)) this.projectRemotePorts.delete(key)
    }
    rt.status = status
    if (notify) this.deps.onMachineDisconnected?.(id)
  }

  private async updateMachine(id: string, update: (machine: SshMachine) => SshMachine): Promise<void> {
    const machines = this.deps.loadMachines()
    const index = machines.findIndex((m) => m.id === id)
    if (index < 0) throw new Error('SSH connection not found.')
    machines[index] = update(machines[index])
    await this.deps.saveMachines(machines)
  }

  private failureMessage(error: unknown): string {
    return redactSecrets(error instanceof Error ? error.message : String(error)) || 'Connection failed.'
  }

  private describeUnavailable(machine: SshMachine, status: SshMachineStatus): string {
    switch (status.kind) {
      case 'notInstalled':
        return `DotCraft is not installed on ${machine.name}.`
      case 'updateRequired':
        return `DotCraft on ${machine.name} needs an update.`
      case 'unsupported':
        return `${machine.name} is not supported yet.`
      case 'failed':
        return status.message || `Could not connect to ${machine.name}.`
      default:
        return `${machine.name} is not connected.`
    }
  }

  private async runConnect(id: string, generation: number): Promise<SshMachineStatus> {
    const rt = this.runtime(id)
    for (let attempt = 0; ; attempt += 1) {
      if (generation !== rt.generation) return rt.status
      this.setStatus(id, generation, { kind: 'connecting' })
      try {
        return this.finish(id, generation, await this.attempt(id, generation))
      } catch (error) {
        if (generation !== rt.generation) return rt.status
        const transient = error instanceof ConnectFailure ? error.transient : false
        if (!transient || attempt >= this.retryDelaysMs.length) {
          return this.finish(id, generation, { kind: 'failed', message: this.failureMessage(error) })
        }
        await this.ctx.sleep(this.retryDelaysMs[attempt])
      }
    }
  }

  private async attempt(id: string, generation: number): Promise<SshMachineStatus> {
    const machine = this.getMachine(id)
    const rt = this.runtime(id)
    const target = machineSshTarget(machine)
    if (machine.source === 'sshConfig' && machine.alias) {
      const resolved = await this.runProcess(buildSshResolveArgs(machine.alias), { timeoutMs: 5_000 })
      rt.resolved = resolved.code === 0 ? parseSshResolveOutput(resolved.stdout) : undefined
    }

    const probe = await probeMachine(this.ctx, target)
    if (probe.kind === 'windows') return this.markUnsupported(id, windowsUnsupportedStatus())
    rt.system = systemInfoFromProbe(probe.probe)
    const blocked = classifyProbe(probe.probe, minimumDotCraftVersion(this.deps.appVersion, this.deps.packaged))
    if (blocked?.kind === 'unsupported') return this.markUnsupported(id, blocked)
    if (blocked) return blocked
    if (generation !== rt.generation) return rt.status

    const hub = await connectHub(this.ctx, id, target, () => this.handleDrop(id, generation))
    if (generation !== rt.generation) {
      this.ctx.tunnels.closeOne(id, HUB_SLOT)
      return rt.status
    }
    rt.hub = hub
    this.subscribeHubEvents(id, generation, hub)
    return this.modelStatus(machine, hub)
  }

  private async modelStatus(machine: SshMachine, hub: HubSession): Promise<SshMachineStatus> {
    const foreground = this.deps.getForegroundProject?.()
    const foregroundPath = foreground?.machineId === machine.id
      ? machine.projects.find((p) => p.id === foreground.projectId)?.path
      : undefined
    const ready = await evaluateModelReadiness(
      this.ctx,
      machine.id,
      machineSshTarget(machine),
      hub,
      foregroundPath,
      this.checkProviders
    )
    return ready ? { kind: 'connected' } : { kind: 'setupModel' }
  }

  private async markUnsupported(id: string, status: SshMachineStatus): Promise<SshMachineStatus> {
    if (this.getMachine(id).autoConnect) await this.updateMachine(id, (m) => ({ ...m, autoConnect: false }))
    return status
  }

  private subscribeHubEvents(id: string, generation: number, hub: HubSession): void {
    const rt = this.runtime(id)
    rt.eventsAbort?.abort()
    const abort = new AbortController()
    rt.eventsAbort = abort
    const onEvent = (event: HubEvent): void => {
      const foreground = this.deps.getForegroundProject?.()
      if (!foreground || foreground.machineId !== id) return
      const project = this.deps.loadMachines().find((m) => m.id === id)?.projects.find((p) => p.id === foreground.projectId)
      if (!project || !isEndpointChangeEvent(event, project.path)) return
      const port = eventEndpointPort(event)
      if (port !== undefined && port === this.projectRemotePorts.get(projectKey(id, project.id))) return
      this.deps.onProjectEndpointChanged?.(id, project.id)
    }
    hub.client.subscribeEvents(onEvent, abort.signal).then(
      () => {
        if (!abort.signal.aborted) this.handleDrop(id, generation)
      },
      () => {
        if (!abort.signal.aborted) this.handleDrop(id, generation)
      }
    )
  }

  private handleDrop(id: string, generation: number): void {
    const rt = this.runtimes.get(id)
    if (!rt || rt.generation !== generation) return
    const machine = this.deps.loadMachines().find((m) => m.id === id)
    const keep = Boolean(machine && (machine.autoConnect || rt.wanted))
    this.disconnect(id, keep ? { kind: 'connecting' } : NOT_CONNECTED, !keep)
    this.emit()
    if (keep) {
      rt.wanted = true
      void this.connect(id)
    }
  }
}

function projectSlot(projectId: string): string {
  return `project:${projectId}`
}

function projectKey(machineId: string, projectId: string): string {
  return `${machineId}::${projectId}`
}
