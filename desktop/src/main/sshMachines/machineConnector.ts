import type { SshTarget } from '../../shared/sshMachines'
import {
  buildEnsureChatWorkspaceCommand,
  buildHubOutTailCommand,
  buildInstallCommand,
  buildProbeCommand,
  buildReadConfigFilesCommand,
  buildReadHubLockCommand,
  buildStartHubCommand,
  forwardedEndpointUrl,
  hasUsableModel,
  hubOutLastLine,
  parseChatWorkspacePath,
  parseConfigFilesOutput,
  parseHubLock,
  parseLoopbackEndpoint,
  parseProbeOutput,
  resolveEffectiveProviderId,
  type ProbeParseResult,
  type ProviderListLike,
  type RemoteHubLock
} from '../../shared/sshMachineRemote'
import { firstLine, redactSecrets, remoteChildPath } from '../../shared/sshShell'
import type { RemoteHubClient } from './remoteHubClient'
import type { SshRunner, SshRunResult } from './sshExecutor'
import type { Tunnels } from './tunnelManager'

export const HUB_SLOT = 'hub'
export const MODEL_SLOT = 'model'

export class ConnectFailure extends Error {
  constructor(message: string, readonly transient: boolean) {
    super(message)
    this.name = 'ConnectFailure'
  }
}

export interface ConnectorContext {
  runner: SshRunner
  tunnels: Tunnels
  createHubClient: (options: { baseUrl: string; token: string }) => RemoteHubClient
  sleep: (ms: number) => Promise<void>
  now: () => number
  hubStartTimeoutMs: number
}

export interface HubSession {
  client: RemoteHubClient
  lock: RemoteHubLock
  localPort: number
}

const SSH_CONNECTION_ERROR = 255

function sshFailure(res: SshRunResult, fallback: string, secrets: string[] = []): ConnectFailure {
  if (res.timedOut) return new ConnectFailure('Connection timed out.', true)
  const message = redactSecrets(firstLine(res.stderr), secrets) || fallback
  return new ConnectFailure(message, res.code === SSH_CONNECTION_ERROR || res.code === null)
}

export async function probeMachine(
  ctx: ConnectorContext,
  target: SshTarget
): Promise<Exclude<ProbeParseResult, { kind: 'invalid' }>> {
  const res = await ctx.runner(target, buildProbeCommand(), { timeoutMs: 30_000 })
  if (res.timedOut) throw new ConnectFailure('Connection timed out.', true)
  const parsed = parseProbeOutput(res.stdout, res.stderr)
  if (parsed.kind === 'invalid') throw sshFailure(res, 'The machine did not answer the DotCraft probe.')
  return parsed
}

export async function readHubLock(ctx: ConnectorContext, target: SshTarget): Promise<RemoteHubLock | null> {
  const res = await ctx.runner(target, buildReadHubLockCommand(), { timeoutMs: 20_000 })
  if (res.timedOut || res.code !== 0 || !/HUB_LOCK_BEGIN/.test(res.stdout)) {
    throw sshFailure(res, 'Could not read the DotCraft Hub lock.')
  }
  return parseHubLock(res.stdout)
}

async function attachHub(
  ctx: ConnectorContext,
  machineId: string,
  target: SshTarget,
  lock: RemoteHubLock,
  onDrop: () => void
): Promise<HubSession | null> {
  let localPort: number
  try {
    localPort = (await ctx.tunnels.open(target, machineId, HUB_SLOT, lock.port, { onUnexpectedClose: onDrop })).localPort
  } catch (error) {
    throw new ConnectFailure(error instanceof Error ? error.message : String(error), true)
  }
  const client = ctx.createHubClient({ baseUrl: `http://127.0.0.1:${localPort}`, token: lock.token })
  try {
    await client.getStatus()
    return { client, lock, localPort }
  } catch {
    ctx.tunnels.closeOne(machineId, HUB_SLOT)
    return null
  }
}

function sameLock(a: RemoteHubLock | null, b: RemoteHubLock | null): boolean {
  return Boolean(a && b && a.pid === b.pid && a.port === b.port && a.token === b.token)
}

export async function connectHub(
  ctx: ConnectorContext,
  machineId: string,
  target: SshTarget,
  onDrop: () => void
): Promise<HubSession> {
  const existing = await readHubLock(ctx, target)
  if (existing) {
    const session = await attachHub(ctx, machineId, target, existing, onDrop)
    if (session) return session
  }

  const started = await ctx.runner(target, buildStartHubCommand(), { timeoutMs: 20_000 })
  if (started.timedOut || started.code !== 0 || !/HUB_STARTED/.test(started.stdout)) {
    throw sshFailure(started, 'Could not start the DotCraft Hub.')
  }

  const deadline = ctx.now() + ctx.hubStartTimeoutMs
  let tried = existing
  while (ctx.now() < deadline) {
    await ctx.sleep(500)
    const lock = await readHubLock(ctx, target)
    if (!lock || sameLock(lock, tried)) continue
    tried = lock
    const session = await attachHub(ctx, machineId, target, lock, onDrop)
    if (session) return session
  }

  const tail = await ctx.runner(target, buildHubOutTailCommand(), { timeoutMs: 15_000 })
  const lastLine = redactSecrets(hubOutLastLine(tail.stdout))
  throw new ConnectFailure(lastLine ? `DotCraft Hub did not start: ${lastLine}` : 'DotCraft Hub did not start.', false)
}

async function resolveReadinessWorkspace(
  ctx: ConnectorContext,
  target: SshTarget,
  foregroundPath: string | undefined
): Promise<string> {
  if (foregroundPath) return foregroundPath
  const res = await ctx.runner(target, buildEnsureChatWorkspaceCommand(), { timeoutMs: 20_000 })
  const path = res.code === 0 ? parseChatWorkspacePath(res.stdout) : null
  if (!path) throw sshFailure(res, 'Could not prepare the Chats workspace on the machine.')
  return path
}

export async function evaluateModelReadiness(
  ctx: ConnectorContext,
  machineId: string,
  target: SshTarget,
  hub: HubSession,
  foregroundPath: string | undefined,
  checkProviders: (wsUrl: string) => Promise<ProviderListLike | null>
): Promise<boolean> {
  const workspacePath = await resolveReadinessWorkspace(ctx, target, foregroundPath)
  const configRes = await ctx.runner(
    target,
    buildReadConfigFilesCommand(remoteChildPath(workspacePath, '.craft/config.json')),
    { timeoutMs: 20_000 }
  )
  const config = configRes.code === 0 ? parseConfigFilesOutput(configRes.stdout) : null
  if (!config) throw sshFailure(configRes, 'Could not read the model configuration on the machine.')
  const providerId = resolveEffectiveProviderId(config.workspaceRaw, config.userDefaultsRaw)
  if (!providerId) return false

  const ensured = await hub.client.ensureAppServer(workspacePath)
  const rawEndpoint = ensured.endpoints?.appServerWebSocket
  const endpoint = parseLoopbackEndpoint(rawEndpoint)
  if (!rawEndpoint || !endpoint) throw new ConnectFailure('The machine did not return an AppServer endpoint.', false)
  try {
    const info = await ctx.tunnels.open(target, machineId, MODEL_SLOT, endpoint.port)
    const result = await checkProviders(forwardedEndpointUrl(rawEndpoint, info.localPort))
    return hasUsableModel(result, providerId)
  } finally {
    ctx.tunnels.closeOne(machineId, MODEL_SLOT)
  }
}

export async function stopRemoteHub(ctx: ConnectorContext, machineId: string, target: SshTarget): Promise<void> {
  const lock = await readHubLock(ctx, target)
  if (!lock) return
  const session = await attachHub(ctx, machineId, target, lock, () => {})
  if (!session) return
  try {
    await session.client.shutdown()
  } catch {
    void 0
  } finally {
    ctx.tunnels.closeOne(machineId, HUB_SLOT)
  }
  await ctx.sleep(1_500)
}

export async function runInstaller(
  ctx: ConnectorContext,
  target: SshTarget,
  script: string,
  version: string
): Promise<void> {
  const res = await ctx.runner(target, buildInstallCommand(version), {
    input: script.replace(/\r\n?/g, '\n'),
    timeoutMs: 600_000
  })
  if (res.timedOut) throw new ConnectFailure('Installing DotCraft timed out.', false)
  if (res.code !== 0) {
    const lines = res.stderr.split(/\r?\n/).map((line) => line.trim()).filter(Boolean)
    const message = redactSecrets(lines.find((line) => line.startsWith('error:')) ?? lines.pop() ?? '')
    throw new ConnectFailure(message || 'Installing DotCraft failed.', res.code === SSH_CONNECTION_ERROR)
  }
}
