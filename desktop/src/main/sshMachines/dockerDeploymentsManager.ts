import {
  buildDiscoverStacksCommand,
  parseDiscoverStacksOutput,
  buildStatusCommand,
  parseStatusOutput,
  buildLogsCommand,
  buildStartCommand,
  buildStopCommand,
  buildRestartCommand,
  buildBackupCommand,
  buildPullCommand,
  buildUpCommand,
  buildReadTokenCommand,
  buildReadOratorioTokenCommand,
  buildTunnelWsUrl,
  buildDashboardUrl,
  effectiveWorkspaceDir,
  updateChangedFromOutput,
  DEFAULT_LOG_TAIL,
  type RemoteStack,
  type RemoteStackStatus,
  type OperationResult,
  type RemoteStackAction,
  type DiscoveredStack
} from '../../shared/dockerDeployments'
import { buildReadConfigFilesCommand, parseConfigFilesOutput } from '../../shared/sshMachineRemote'
import { machineSshTarget, type SshMachine } from '../../shared/sshMachines'
import { firstLine, redactSecrets, remoteChildPath } from '../../shared/sshShell'
import { runSshCommand, type SshRunner } from './sshExecutor'
import { TunnelManager, type Tunnels } from './tunnelManager'

export interface DockerDeploymentsManagerDeps {
  runner?: SshRunner
  tunnels?: Tunnels
  now?: () => number
}

export interface LogsResult {
  text: string
  service?: string
  tail: number
}

export interface AppServerTunnelResult {
  localPort: number
  /** `ws://127.0.0.1:<port>/ws?token=…` for the existing connect path. */
  wsUrl: string
  /** Returned for immediate use; never persisted. */
  token?: string
  /** Safe diagnostic bit; do not log or return the token value to renderer. */
  tokenPresent: boolean
}

export interface DashboardTunnelResult {
  localPort: number
  url: string
}

export interface OratorioTunnelResult {
  localPort: number
  endpoint: string
  /** Returned only to the Main-process provider and never exposed over IPC. */
  token: string
}

export interface RemoteCoreConfigResult {
  workspaceRaw: string
  userDefaultsRaw: string
}

function errorStatus(stackId: string, error: string): RemoteStackStatus {
  return {
    stackId,
    health: 'unknown',
    dockerOk: false,
    composeOk: false,
    envOk: false,
    configOk: false,
    tokenPresent: false,
    services: [],
    servicesUp: 0,
    servicesTotal: 0,
    error
  }
}

function stackSlot(stackId: string, kind: 'appserver' | 'oratorio' | 'dashboard'): string {
  return `stack:${stackId}:${kind}`
}

export class DockerDeploymentsManager {
  private readonly runner: SshRunner
  private readonly tunnels: Tunnels
  private readonly now: () => number

  constructor(deps: DockerDeploymentsManagerDeps = {}) {
    this.runner = deps.runner ?? runSshCommand
    this.tunnels = deps.tunnels ?? new TunnelManager()
    this.now = deps.now ?? (() => Date.now())
  }

  async discoverStacks(machine: SshMachine): Promise<DiscoveredStack[]> {
    const res = await this.runner(machineSshTarget(machine), buildDiscoverStacksCommand(), { timeoutMs: 20_000, connectTimeoutSec: 8 })
    if (res.timedOut) throw new Error('Stack discovery timed out.')
    if (!/DISCOVER_BEGIN/.test(res.stdout)) {
      throw new Error(redactSecrets(firstLine(res.stderr) || 'Stack discovery failed.'))
    }
    return parseDiscoverStacksOutput(res.stdout)
  }

  async status(machine: SshMachine, stack: RemoteStack): Promise<RemoteStackStatus> {
    const res = await this.runner(machineSshTarget(machine), buildStatusCommand(stack), { timeoutMs: 25_000 })
    if (res.timedOut) return errorStatus(stack.id, 'Status check timed out.')
    if (!/STATUS_BEGIN/.test(res.stdout)) {
      return errorStatus(stack.id, redactSecrets(firstLine(res.stderr) || 'Status check failed.'))
    }
    const status = parseStatusOutput(res.stdout, stack.id)
    status.checkedAt = this.now()
    return status
  }

  async logs(
    machine: SshMachine,
    stack: RemoteStack,
    service?: string,
    tail: number = DEFAULT_LOG_TAIL,
    knownSecrets: string[] = []
  ): Promise<LogsResult> {
    const res = await this.runner(machineSshTarget(machine), buildLogsCommand(stack, service, tail), { timeoutMs: 20_000 })
    const raw = res.stdout || res.stderr || ''
    return { text: redactSecrets(raw, knownSecrets), service, tail }
  }

  async action(machine: SshMachine, stack: RemoteStack, action: RemoteStackAction): Promise<OperationResult> {
    if (action === 'update') return this.update(machine, stack)

    const command =
      action === 'start'
        ? buildStartCommand(stack)
        : action === 'stop'
          ? buildStopCommand(stack)
          : buildRestartCommand(stack)

    const res = await this.runner(machineSshTarget(machine), command, { timeoutMs: 60_000 })
    const ok = !res.timedOut && res.code === 0
    const result: OperationResult = {
      ok,
      action,
      message: ok ? undefined : redactSecrets(firstLine(res.stderr) || `${action} failed.`)
    }
    if (ok) result.status = await this.status(machine, stack)
    return result
  }

  /** Ordered update: backup → pull → up → status refresh. */
  private async update(machine: SshMachine, stack: RemoteStack): Promise<OperationResult> {
    const backup = await this.runner(machineSshTarget(machine), buildBackupCommand(stack), { timeoutMs: 30_000 })
    if (backup.timedOut || backup.code !== 0) {
      return { ok: false, action: 'update', message: redactSecrets(firstLine(backup.stderr) || 'Backup step failed.') }
    }

    const pull = await this.runner(machineSshTarget(machine), buildPullCommand(stack), { timeoutMs: 300_000 })
    if (pull.timedOut || pull.code !== 0) {
      return { ok: false, action: 'update', message: redactSecrets(firstLine(pull.stderr) || 'Pull step failed.') }
    }

    const up = await this.runner(machineSshTarget(machine), buildUpCommand(stack), { timeoutMs: 180_000 })
    if (up.timedOut || up.code !== 0) {
      return { ok: false, action: 'update', message: redactSecrets(firstLine(up.stderr) || 'Recreate step failed.') }
    }

    const changed = updateChangedFromOutput(`${pull.stdout}\n${pull.stderr}`, `${up.stdout}\n${up.stderr}`)
    const status = await this.status(machine, stack)
    return {
      ok: true,
      action: 'update',
      changed,
      status,
      message: changed ? 'Updated.' : 'Already up to date.'
    }
  }

  /** Read the remote AppServer token (used only at connect time; never persisted). */
  async readToken(machine: SshMachine, stack: RemoteStack): Promise<string> {
    const res = await this.runner(machineSshTarget(machine), buildReadTokenCommand(stack), { timeoutMs: 30_000, connectTimeoutSec: 8 })
    if (res.timedOut) {
      throw new Error('Remote AppServer token read timed out.')
    }
    if (res.code !== 0) {
      throw new Error(redactSecrets(firstLine(res.stderr) || 'Remote AppServer token read failed.'))
    }
    const token = res.stdout.trim()
    if (!token) {
      throw new Error(
        'Remote AppServer token was not found for this stack. Check that the DotCraft container has started and the workspace .craft/appserver.token file exists.'
      )
    }
    return token
  }

  private async readOratorioToken(machine: SshMachine, stack: RemoteStack): Promise<string> {
    const res = await this.runner(machineSshTarget(machine), buildReadOratorioTokenCommand(stack), { timeoutMs: 30_000, connectTimeoutSec: 8 })
    if (res.timedOut) throw new Error('Remote Oratorio token read timed out.')
    if (res.code !== 0 || !res.stdout.trim()) {
      throw new Error(redactSecrets(firstLine(res.stderr) || 'Remote Oratorio service token was not found for this stack.'))
    }
    return res.stdout.trim()
  }

  async readCoreConfig(machine: SshMachine, stack: RemoteStack): Promise<RemoteCoreConfigResult> {
    const configPath = remoteChildPath(effectiveWorkspaceDir(stack), '.craft/config.json')
    const res = await this.runner(machineSshTarget(machine), buildReadConfigFilesCommand(configPath), {
      timeoutMs: 20_000,
      connectTimeoutSec: 8
    })
    if (res.timedOut) throw new Error('Remote workspace config read timed out.')
    const parsed = res.code === 0 ? parseConfigFilesOutput(res.stdout) : null
    if (!parsed) throw new Error(redactSecrets(firstLine(res.stderr) || 'Remote workspace config read failed.'))
    return parsed
  }

  async openAppServerTunnel(
    machine: SshMachine,
    stack: RemoteStack,
    options: { forceNew?: boolean } = {}
  ): Promise<AppServerTunnelResult> {
    if (options.forceNew) {
      this.tunnels.closeOne(machine.id, stackSlot(stack.id, 'appserver'))
    }
    const token = await this.readToken(machine, stack)
    const info = await this.tunnels.open(machineSshTarget(machine), machine.id, stackSlot(stack.id, 'appserver'), stack.appServerPort)
    return { localPort: info.localPort, wsUrl: buildTunnelWsUrl(info.localPort, token), token, tokenPresent: true }
  }

  async openDashboardTunnel(machine: SshMachine, stack: RemoteStack): Promise<DashboardTunnelResult> {
    const info = await this.tunnels.open(machineSshTarget(machine), machine.id, stackSlot(stack.id, 'dashboard'), stack.dashboardPort)
    return { localPort: info.localPort, url: buildDashboardUrl(info.localPort) }
  }

  async openOratorioTunnel(machine: SshMachine, stack: RemoteStack): Promise<OratorioTunnelResult> {
    const token = await this.readOratorioToken(machine, stack)
    const info = await this.tunnels.open(machineSshTarget(machine), machine.id, stackSlot(stack.id, 'oratorio'), stack.oratorioPort)
    return { localPort: info.localPort, endpoint: `http://127.0.0.1:${info.localPort}`, token }
  }

  closeStackTunnels(machineId: string, stackId: string): void {
    this.tunnels.closeMatching(machineId, `stack:${stackId}:`)
  }

  closeMachineTunnels(machineId: string): void {
    this.tunnels.closeMatching(machineId, 'stack:')
  }
}
