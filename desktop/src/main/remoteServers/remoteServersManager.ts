import {
  buildSshTestCommand,
  parseSshTestOutput,
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
  buildTunnelWsUrl,
  buildDashboardUrl,
  updateChangedFromOutput,
  redactSecrets,
  DEFAULT_LOG_TAIL,
  type RemoteHost,
  type RemoteStack,
  type RemoteStackStatus,
  type SshTestResult,
  type OperationResult,
  type RemoteStackAction
} from '../../shared/remoteServers'
import { runSshCommand, type SshRunner } from './sshExecutor'
import { TunnelManager } from './tunnelManager'

export interface RemoteServersManagerDeps {
  runner?: SshRunner
  tunnels?: TunnelManager
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
}

export interface DashboardTunnelResult {
  localPort: number
  url: string
}

function firstLine(text: string): string {
  return (text || '')
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)[0] ?? ''
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

/**
 * High-level remote operations for the Servers surface. Every method runs a
 * fixed, allow-listed command built from validated parameters and redacts
 * secrets before returning. There is no method that runs an arbitrary command.
 */
export class RemoteServersManager {
  private readonly runner: SshRunner
  private readonly tunnels: TunnelManager
  private readonly now: () => number

  constructor(deps: RemoteServersManagerDeps = {}) {
    this.runner = deps.runner ?? runSshCommand
    this.tunnels = deps.tunnels ?? new TunnelManager()
    this.now = deps.now ?? (() => Date.now())
  }

  async testHost(host: RemoteHost): Promise<SshTestResult> {
    const start = this.now()
    const res = await this.runner(host, buildSshTestCommand(), { timeoutMs: 15_000, connectTimeoutSec: 8 })
    if (res.timedOut) {
      return { reachable: false, errorCode: 'timeout', message: 'Connection timed out.' }
    }
    if (!/SSH_OK/.test(res.stdout)) {
      return {
        reachable: false,
        errorCode: 'unreachable',
        message: redactSecrets(firstLine(res.stderr) || 'SSH connection failed.')
      }
    }
    return parseSshTestOutput(res.stdout, this.now() - start)
  }

  async status(host: RemoteHost, stack: RemoteStack): Promise<RemoteStackStatus> {
    const res = await this.runner(host, buildStatusCommand(stack), { timeoutMs: 25_000 })
    if (res.timedOut) return errorStatus(stack.id, 'Status check timed out.')
    if (!/STATUS_BEGIN/.test(res.stdout)) {
      return errorStatus(stack.id, redactSecrets(firstLine(res.stderr) || 'Status check failed.'))
    }
    const status = parseStatusOutput(res.stdout, stack.id)
    status.checkedAt = this.now()
    return status
  }

  async logs(
    host: RemoteHost,
    stack: RemoteStack,
    service?: string,
    tail: number = DEFAULT_LOG_TAIL,
    knownSecrets: string[] = []
  ): Promise<LogsResult> {
    const res = await this.runner(host, buildLogsCommand(stack, service, tail), { timeoutMs: 20_000 })
    const raw = res.stdout || res.stderr || ''
    return { text: redactSecrets(raw, knownSecrets), service, tail }
  }

  async action(host: RemoteHost, stack: RemoteStack, action: RemoteStackAction): Promise<OperationResult> {
    if (action === 'update') return this.update(host, stack)

    const command =
      action === 'start'
        ? buildStartCommand(stack)
        : action === 'stop'
          ? buildStopCommand(stack)
          : buildRestartCommand(stack)

    const res = await this.runner(host, command, { timeoutMs: 60_000 })
    const ok = !res.timedOut && res.code === 0
    const result: OperationResult = {
      ok,
      action,
      message: ok ? undefined : redactSecrets(firstLine(res.stderr) || `${action} failed.`)
    }
    if (ok) result.status = await this.status(host, stack)
    return result
  }

  /** Ordered update: backup → pull → up → status refresh. */
  private async update(host: RemoteHost, stack: RemoteStack): Promise<OperationResult> {
    const backup = await this.runner(host, buildBackupCommand(stack), { timeoutMs: 30_000 })
    if (backup.timedOut || backup.code !== 0) {
      return { ok: false, action: 'update', message: redactSecrets(firstLine(backup.stderr) || 'Backup step failed.') }
    }

    const pull = await this.runner(host, buildPullCommand(stack), { timeoutMs: 300_000 })
    if (pull.timedOut || pull.code !== 0) {
      return { ok: false, action: 'update', message: redactSecrets(firstLine(pull.stderr) || 'Pull step failed.') }
    }

    const up = await this.runner(host, buildUpCommand(stack), { timeoutMs: 180_000 })
    if (up.timedOut || up.code !== 0) {
      return { ok: false, action: 'update', message: redactSecrets(firstLine(up.stderr) || 'Recreate step failed.') }
    }

    const changed = updateChangedFromOutput(`${pull.stdout}\n${pull.stderr}`, `${up.stdout}\n${up.stderr}`)
    const status = await this.status(host, stack)
    return {
      ok: true,
      action: 'update',
      changed,
      status,
      message: changed ? 'Updated.' : 'Already up to date.'
    }
  }

  /** Read the remote AppServer token (used only at connect time; never persisted). */
  async readToken(host: RemoteHost, stack: RemoteStack): Promise<string | undefined> {
    const res = await this.runner(host, buildReadTokenCommand(stack), { timeoutMs: 15_000 })
    const token = res.stdout.trim()
    return token || undefined
  }

  async openAppServerTunnel(host: RemoteHost, stack: RemoteStack): Promise<AppServerTunnelResult> {
    const token = await this.readToken(host, stack)
    const info = await this.tunnels.open(host, stack.id, stack.appServerPort, 'appserver')
    return { localPort: info.localPort, wsUrl: buildTunnelWsUrl(info.localPort, token), token }
  }

  async openDashboardTunnel(host: RemoteHost, stack: RemoteStack): Promise<DashboardTunnelResult> {
    const info = await this.tunnels.open(host, stack.id, stack.dashboardPort, 'dashboard')
    return { localPort: info.localPort, url: buildDashboardUrl(info.localPort) }
  }

  closeStackTunnels(hostId: string, stackId: string): void {
    this.tunnels.closeForStack(hostId, stackId)
  }

  closeHostTunnels(hostId: string): void {
    this.tunnels.closeForHost(hostId)
  }

  closeAllTunnels(): void {
    this.tunnels.closeAll()
  }
}
