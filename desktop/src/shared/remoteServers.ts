/**
 * Shared types and pure helpers for the Desktop "Servers" surface: managing
 * remote DotCraft Docker stacks over SSH.
 *
 * This module is imported by both the main process (which executes the system
 * `ssh` binary) and the renderer (which renders state). It contains only pure,
 * serializable types and deterministic helpers — no Node or Electron APIs — so
 * it is fully unit-testable.
 *
 * Security model: the renderer never supplies command strings. The main process
 * chooses a fixed, allow-listed operation and a saved host/stack; this module
 * builds the exact argv/remote-command from validated, individually-quoted
 * parameters. See specs/features/remote-server-management.md.
 */

// ─────────────────────────────────────────────────────────────────────────────
// Constants
// ─────────────────────────────────────────────────────────────────────────────

export const DEFAULT_APP_SERVER_PORT = 9100
export const DEFAULT_ORATORIO_PORT = 5087
export const DEFAULT_DASHBOARD_PORT = 8080
export const DEFAULT_APP_SERVER_WORKSPACE_PATH = '/workspace'

/** Default and maximum bounds for the `logs --tail` window. */
export const DEFAULT_LOG_TAIL = 200
export const MAX_LOG_TAIL = 2000

/** Default ssh ConnectTimeout (seconds) for a single remote operation. */
export const DEFAULT_SSH_CONNECT_TIMEOUT_SEC = 10

/** Mask written in place of any redacted secret value. */
export const REDACTION_MASK = '[redacted]'

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

export type StackHealth = 'running' | 'partial' | 'stopped' | 'unhealthy' | 'unknown'
export type SshReachability = 'unknown' | 'reachable' | 'unreachable' | 'checking'
export type RemoteStackAction = 'start' | 'stop' | 'restart' | 'update'

/** One DotCraft Compose deployment on a host. */
export interface RemoteStack {
  id: string
  name: string
  /** Directory containing the compose file and `.env`. */
  composeDir: string
  /** Mounted runtime dir; defaults to `<composeDir>/workspace`. */
  workspaceDir?: string
  /** Workspace path as seen by the AppServer process; Docker stacks default to `/workspace`. */
  appServerWorkspacePath?: string
  /** `docker compose -p <projectName>`; optional. */
  projectName?: string
  appServerPort: number
  oratorioPort: number
  dashboardPort: number
  /** When true, operations pass `--profile sandbox`. */
  sandboxProfile: boolean
}

/** A saved SSH target with its DotCraft stacks. */
export interface RemoteHost {
  id: string
  name: string
  /** `user@host`, `host`, or a `~/.ssh/config` alias. */
  sshTarget: string
  /** Optional local identity file; key/agent auth only. */
  identityFile?: string
  stacks: RemoteStack[]
}

export interface ServiceState {
  name: string
  /** Raw compose state, e.g. `running`, `exited`, `restarting`. */
  state: string
  /** undefined when the service declares no healthcheck. */
  healthy?: boolean
}

export interface RemoteStackStatus {
  stackId: string
  health: StackHealth
  dockerOk: boolean
  composeOk: boolean
  envOk: boolean
  configOk: boolean
  /** Presence only — the token value is never read into status. */
  tokenPresent: boolean
  /** DotCraft AppServer runtime version from `.craft/appserver.lock`, when available. */
  appVersion?: string
  imageTag?: string
  imageDigestShort?: string
  services: ServiceState[]
  servicesUp: number
  servicesTotal: number
  checkedAt?: number
  error?: string
}

export interface DiscoveredStack {
  name: string
  composeDir: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  projectName?: string
  appServerPort: number
  oratorioPort: number
  dashboardPort: number
  sandboxProfile: boolean
  hasSandbox?: boolean
  image?: string
  services?: string[]
}

export interface SshTestResult {
  reachable: boolean
  latencyMs?: number
  dockerOk?: boolean
  composeOk?: boolean
  discoveredStacks?: DiscoveredStack[]
  errorCode?: string
  message?: string
}

export type LocalSshIdentitySource = 'default' | 'config'

export interface LocalSshIdentity {
  /** Display path suitable for the `ssh -i` option, usually `~/.ssh/<key>`. */
  path: string
  source: LocalSshIdentitySource
  exists: boolean
  hostAliases?: string[]
}

export interface LocalSshHostAlias {
  alias: string
  hostName?: string
  user?: string
  port?: string
  identityFiles: string[]
}

export interface LocalSshConfigInfo {
  sshDir: string
  configPath: string
  configExists: boolean
  agentAvailable: boolean
  aliases: LocalSshHostAlias[]
  identities: LocalSshIdentity[]
  error?: string
}

export interface OperationResult {
  ok: boolean
  action: RemoteStackAction
  message?: string
  /** For `update`: whether anything was actually recreated. */
  changed?: boolean
  status?: RemoteStackStatus
}

export interface TunnelInfo {
  localPort: number
  localUrl: string
}

// ─────────────────────────────────────────────────────────────────────────────
// IDs
// ─────────────────────────────────────────────────────────────────────────────

/** Generate a stable id with the given single-letter prefix (`h` host, `s` stack). */
export function generateId(prefix: string): string {
  const uuid =
    (typeof globalThis !== 'undefined' && globalThis.crypto?.randomUUID?.()) ||
    `${Date.now().toString(16)}${Math.floor(Math.random() * 0xffffffff).toString(16)}`
  return `${prefix}_${uuid.replace(/-/g, '').slice(0, 24)}`
}

type IdFactory = (prefix: 'h' | 's') => string

// ─────────────────────────────────────────────────────────────────────────────
// Validation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * SSH targets must be a `user@host`, bare host, or alias — never an option.
 * Must start with an alphanumeric (rejects `-oProxyCommand=…` option injection)
 * and contain no whitespace, control, or shell-significant characters.
 */
const SSH_TARGET_RE = /^[A-Za-z0-9][A-Za-z0-9._@:-]*$/

export function isValidSshTarget(target: unknown): boolean {
  if (typeof target !== 'string') return false
  const t = target.trim()
  if (!t || t.length > 255) return false
  return SSH_TARGET_RE.test(t)
}

/**
 * Remote paths must be absolute (`/…`) or home-relative (`~`, `~/…`), contain no
 * control characters, and no `..` traversal segment.
 */
export function isValidRemotePath(p: unknown): boolean {
  if (typeof p !== 'string') return false
  const t = p.trim()
  if (!t) return false
  // eslint-disable-next-line no-control-regex
  if (/[\s\u0000-\u001f]/.test(t)) return false
  if (!(t === '~' || t.startsWith('~/') || t.startsWith('/'))) return false
  if (t.split('/').includes('..')) return false
  return true
}

/** Local identity file path: non-empty, not an option, no control characters. */
export function isValidIdentityFile(p: unknown): boolean {
  if (typeof p !== 'string') return false
  const t = p.trim()
  if (!t) return false
  // eslint-disable-next-line no-control-regex
  if (/[\s\u0000-\u001f]/.test(t)) return false
  if (t.startsWith('-')) return false
  return true
}

const SERVICE_NAME_RE = /^[A-Za-z0-9][A-Za-z0-9._-]*$/

export function isValidServiceName(name: unknown): boolean {
  return typeof name === 'string' && SERVICE_NAME_RE.test(name.trim())
}

export function isValidPort(port: unknown): boolean {
  return typeof port === 'number' && Number.isInteger(port) && port >= 1 && port <= 65535
}

// ─────────────────────────────────────────────────────────────────────────────
// Shell quoting
// ─────────────────────────────────────────────────────────────────────────────

/** POSIX single-quote a value so the remote shell treats it as a literal. */
export function shellSingleQuote(value: string): string {
  return `'${value.replace(/'/g, `'\\''`)}'`
}

/**
 * Quote a validated remote path for use in a remote shell command. Home-relative
 * paths keep `~/` unquoted (so the shell expands it) and single-quote the rest.
 * Callers must pass paths that pass {@link isValidRemotePath}.
 */
export function quoteRemotePath(p: string): string {
  const t = p.trim()
  if (t === '~') return '~'
  if (t.startsWith('~/')) return `~/${shellSingleQuote(t.slice(2))}`
  return shellSingleQuote(t)
}

/** Join a remote base directory with a child path, normalizing slashes. */
export function remoteChildPath(base: string, child: string): string {
  return `${base.replace(/\/+$/, '')}/${child.replace(/^\/+/, '')}`
}

export function effectiveWorkspaceDir(stack: RemoteStack): string {
  const explicit = stack.workspaceDir?.trim()
  if (explicit) return explicit
  return remoteChildPath(stack.composeDir, 'workspace')
}

export function effectiveAppServerWorkspacePath(stack: RemoteStack): string {
  const explicit = stack.appServerWorkspacePath?.trim()
  if (explicit) return explicit
  return DEFAULT_APP_SERVER_WORKSPACE_PATH
}

// ─────────────────────────────────────────────────────────────────────────────
// Normalization
// ─────────────────────────────────────────────────────────────────────────────

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined
}

function asStringRecord(value: unknown): Record<string, string> {
  const raw = asRecord(value)
  if (!raw) return {}
  const out: Record<string, string> = {}
  for (const [key, entry] of Object.entries(raw)) {
    if (typeof entry === 'string') out[key] = entry
  }
  return out
}

function normalizeStack(input: unknown, genId: IdFactory): RemoteStack | undefined {
  const raw = asRecord(input)
  if (!raw) return undefined

  const name = typeof raw.name === 'string' ? raw.name.trim() : ''
  const composeDir = typeof raw.composeDir === 'string' ? raw.composeDir.trim() : ''
  if (!name || !isValidRemotePath(composeDir)) return undefined

  const id = typeof raw.id === 'string' && raw.id.trim() ? raw.id.trim() : genId('s')

  const workspaceDir =
    typeof raw.workspaceDir === 'string' && isValidRemotePath(raw.workspaceDir.trim())
      ? raw.workspaceDir.trim()
      : undefined
  const appServerWorkspacePath =
    typeof raw.appServerWorkspacePath === 'string' && isValidRemotePath(raw.appServerWorkspacePath.trim())
      ? raw.appServerWorkspacePath.trim()
      : undefined
  const projectName =
    typeof raw.projectName === 'string' && raw.projectName.trim() ? raw.projectName.trim() : undefined

  return {
    id,
    name,
    composeDir,
    workspaceDir,
    appServerWorkspacePath,
    projectName,
    appServerPort: isValidPort(raw.appServerPort) ? (raw.appServerPort as number) : DEFAULT_APP_SERVER_PORT,
    oratorioPort: isValidPort(raw.oratorioPort) ? (raw.oratorioPort as number) : DEFAULT_ORATORIO_PORT,
    dashboardPort: isValidPort(raw.dashboardPort) ? (raw.dashboardPort as number) : DEFAULT_DASHBOARD_PORT,
    sandboxProfile: raw.sandboxProfile === true
  }
}

function normalizeHost(input: unknown, genId: IdFactory): RemoteHost | undefined {
  const raw = asRecord(input)
  if (!raw) return undefined

  const name = typeof raw.name === 'string' ? raw.name.trim() : ''
  const sshTarget = typeof raw.sshTarget === 'string' ? raw.sshTarget.trim() : ''
  if (!name || !isValidSshTarget(sshTarget)) return undefined

  const id = typeof raw.id === 'string' && raw.id.trim() ? raw.id.trim() : genId('h')

  const identityFile =
    typeof raw.identityFile === 'string' && isValidIdentityFile(raw.identityFile.trim())
      ? raw.identityFile.trim()
      : undefined

  const stacksInput = Array.isArray(raw.stacks) ? raw.stacks : []
  const seenStackIds = new Set<string>()
  const stacks: RemoteStack[] = []
  for (const entry of stacksInput) {
    const stack = normalizeStack(entry, genId)
    if (!stack || seenStackIds.has(stack.id)) continue
    seenStackIds.add(stack.id)
    stacks.push(stack)
  }

  return { id, name, sshTarget, identityFile, stacks }
}

/**
 * Normalize persisted/unknown input into a clean `RemoteHost[]`. Invalid entries
 * are dropped; missing optional fields fall back to documented defaults; missing
 * ids are filled via `genId`. The AppServer token is never part of this model.
 */
export function normalizeRemoteHosts(input: unknown, genId: IdFactory = generateId): RemoteHost[] {
  if (!Array.isArray(input)) return []
  const seen = new Set<string>()
  const hosts: RemoteHost[] = []
  for (const entry of input) {
    const host = normalizeHost(entry, genId)
    if (!host || seen.has(host.id)) continue
    seen.add(host.id)
    hosts.push(host)
  }
  return hosts
}

// ─────────────────────────────────────────────────────────────────────────────
// SSH argv + remote command builders (allow-listed operations only)
// ─────────────────────────────────────────────────────────────────────────────

export interface SshExecOptions {
  connectTimeoutSec?: number
}

/**
 * Build the argv for the system `ssh` binary. Options are key/agent only
 * (`BatchMode=yes` — never prompts for a password) with a bounded connect
 * timeout. `--` terminates option parsing so neither the target nor the command
 * can be misread as an ssh option.
 */
export function buildSshArgs(host: RemoteHost, remoteCommand: string, opts: SshExecOptions = {}): string[] {
  const timeout = opts.connectTimeoutSec ?? DEFAULT_SSH_CONNECT_TIMEOUT_SEC
  const args: string[] = [
    '-o',
    'BatchMode=yes',
    '-o',
    `ConnectTimeout=${Math.max(1, Math.floor(timeout))}`,
    '-o',
    'StrictHostKeyChecking=accept-new'
  ]
  const identity = host.identityFile?.trim()
  if (identity && isValidIdentityFile(identity)) {
    args.push('-i', identity, '-o', 'IdentitiesOnly=yes')
  }
  args.push('--', host.sshTarget, remoteCommand)
  return args
}

/**
 * Build argv for a local SSH tunnel: `ssh -N -L 127.0.0.1:<local>:127.0.0.1:<remote>`.
 * Binds the local end to loopback only and exits if the forward cannot bind.
 */
export function buildSshTunnelArgs(
  host: RemoteHost,
  localPort: number,
  remotePort: number,
  opts: SshExecOptions = {}
): string[] {
  const timeout = opts.connectTimeoutSec ?? DEFAULT_SSH_CONNECT_TIMEOUT_SEC
  const args: string[] = [
    '-N',
    '-o',
    'BatchMode=yes',
    '-o',
    `ConnectTimeout=${Math.max(1, Math.floor(timeout))}`,
    '-o',
    'StrictHostKeyChecking=accept-new',
    '-o',
    'ExitOnForwardFailure=yes',
    '-L',
    `127.0.0.1:${localPort}:127.0.0.1:${remotePort}`
  ]
  const identity = host.identityFile?.trim()
  if (identity && isValidIdentityFile(identity)) {
    args.push('-i', identity, '-o', 'IdentitiesOnly=yes')
  }
  args.push('--', host.sshTarget)
  return args
}

/** Remote command that prints a stack's AppServer token (used only at connect time). */
export function buildReadTokenCommand(stack: RemoteStack): string {
  const tokenPath = quoteRemotePath(remoteChildPath(effectiveWorkspaceDir(stack), '.craft/appserver.token'))
  return `cat ${tokenPath} 2>/dev/null`
}

/** Remote command that prints only the Oratorio service token from the stack environment. */
export function buildReadOratorioTokenCommand(stack: RemoteStack): string {
  return `${cdInto(stack)} && awk -F= '$1=="ORATORIO_SERVICE_TOKEN"{sub(/^[^=]*=/,""); print; exit}' .env 2>/dev/null`
}

/** Read only the config files needed for Desktop workspace-core settings. */
export function buildReadCoreConfigCommand(stack: RemoteStack): string {
  const workspaceConfigPath = quoteRemotePath(remoteChildPath(effectiveWorkspaceDir(stack), '.craft/config.json'))
  const userConfigPath = quoteRemotePath('~/.craft/config.json')
  return [
    `read_cfg(){ if [ -f "$1" ]; then (base64 -w 0 "$1" 2>/dev/null || base64 "$1" 2>/dev/null | tr -d '\\n' || true); fi; };`,
    `echo CONFIG_BEGIN;`,
    `printf 'workspace='; read_cfg ${workspaceConfigPath}; echo;`,
    `printf 'userDefaults='; read_cfg ${userConfigPath}; echo;`,
    `echo CONFIG_END`
  ].join(' ')
}

/** `docker compose [-p name] [--profile sandbox]` prefix for a stack. */
export function composePrefix(stack: RemoteStack): string {
  const parts = ['docker', 'compose']
  const project = stack.projectName?.trim()
  if (project) parts.push('-p', shellSingleQuote(project))
  if (stack.sandboxProfile) parts.push('--profile', 'sandbox')
  return parts.join(' ')
}

function cdInto(stack: RemoteStack): string {
  return `cd ${quoteRemotePath(stack.composeDir)}`
}

/** Status probe: docker/compose/.env/config/token presence + `ps -a` JSON. */
export function buildStatusCommand(stack: RemoteStack): string {
  const ws = effectiveWorkspaceDir(stack)
  const configPath = quoteRemotePath(remoteChildPath(ws, '.craft/config.json'))
  const tokenPath = quoteRemotePath(remoteChildPath(ws, '.craft/appserver.token'))
  const lockPath = quoteRemotePath(remoteChildPath(ws, '.craft/appserver.lock'))
  const compose = composePrefix(stack)
  return [
    `${cdInto(stack)} 2>/dev/null && {`,
    `echo STATUS_BEGIN;`,
    `(command -v docker >/dev/null 2>&1 && echo docker=ok || echo docker=missing);`,
    `(docker compose version >/dev/null 2>&1 && echo compose=ok || echo compose=missing);`,
    `(test -f .env && echo env=ok || echo env=missing);`,
    `(test -f ${configPath} && echo config=ok || echo config=missing);`,
    `(test -f ${tokenPath} && echo token=present || echo token=missing);`,
    `echo LOCK_BEGIN; (test -f ${lockPath} && cat ${lockPath} 2>/dev/null || true); echo LOCK_END;`,
    `echo PS_BEGIN; (${compose} ps -a --format json 2>/dev/null || true); echo PS_END;`,
    `echo STATUS_END;`,
    `} || echo DIR_MISSING`
  ].join(' ')
}

export function buildLogsCommand(stack: RemoteStack, service?: string, tail: number = DEFAULT_LOG_TAIL): string {
  const n = Math.max(1, Math.min(MAX_LOG_TAIL, Math.floor(tail) || DEFAULT_LOG_TAIL))
  const compose = composePrefix(stack)
  const svc = service && isValidServiceName(service) ? ` ${shellSingleQuote(service.trim())}` : ''
  return `${cdInto(stack)} && ${compose} logs --no-color --tail ${n}${svc}`
}

export function buildStartCommand(stack: RemoteStack): string {
  return `${cdInto(stack)} && ${composePrefix(stack)} up -d`
}

export function buildStopCommand(stack: RemoteStack): string {
  return `${cdInto(stack)} && ${composePrefix(stack)} stop`
}

export function buildRestartCommand(stack: RemoteStack): string {
  return `${cdInto(stack)} && ${composePrefix(stack)} restart`
}

/** Update step 1: timestamped backup of `.env` and `.craft/` metadata. */
export function buildBackupCommand(stack: RemoteStack): string {
  const ws = quoteRemotePath(effectiveWorkspaceDir(stack))
  return [
    `${cdInto(stack)} &&`,
    `ts=$(date +%Y%m%d-%H%M%S) &&`,
    `bdir=.dotcraft-backups/$ts &&`,
    `mkdir -p "$bdir" &&`,
    `(cp .env "$bdir"/.env 2>/dev/null || true) &&`,
    `(cp -r ${ws}/.craft "$bdir"/craft 2>/dev/null || true) &&`,
    `echo "BACKUP_OK $bdir"`
  ].join(' ')
}

/** Update step 2: pull updated service images. */
export function buildPullCommand(stack: RemoteStack): string {
  return `${cdInto(stack)} && ${composePrefix(stack)} pull`
}

/** Update step 3: recreate changed containers, preserving volumes. */
export function buildUpCommand(stack: RemoteStack): string {
  return `${cdInto(stack)} && ${composePrefix(stack)} up -d --remove-orphans`
}

/** SSH reachability + docker/compose availability probe. */
export function buildSshTestCommand(): string {
  return [
    `echo SSH_OK;`,
    `(command -v docker >/dev/null 2>&1 && echo docker=ok || echo docker=missing);`,
    `(docker compose version >/dev/null 2>&1 && echo compose=ok || echo compose=missing)`
  ].join(' ')
}

/** Discover Compose-managed DotCraft containers from Docker labels. */
export function buildDiscoverStacksCommand(): string {
  return [
    `echo DISCOVER_BEGIN;`,
    `(docker ps -a --filter 'label=com.docker.compose.project' --format '{{.ID}}' 2>/dev/null | while IFS= read -r id; do`,
    `if [ -n "$id" ]; then docker inspect --format '{{json .}}' "$id" 2>/dev/null; fi;`,
    `done) || true;`,
    `echo DISCOVER_END`
  ].join(' ')
}

// ─────────────────────────────────────────────────────────────────────────────
// Output parsing
// ─────────────────────────────────────────────────────────────────────────────

interface ComposePsEntry {
  Name?: string
  Service?: string
  State?: string
  Health?: string
  Image?: string
}

interface DockerInspectContainer {
  Config?: {
    Image?: string
    Labels?: Record<string, string>
    Env?: string[]
  }
  Mounts?: Array<{
    Source?: string
    Destination?: string
  }>
  NetworkSettings?: {
    Ports?: Record<string, null | Array<{ HostIp?: string; HostPort?: string }>>
  }
}

function parseComposePsBlock(block: string): ComposePsEntry[] {
  const text = block.trim()
  if (!text) return []
  // Newer compose emits a JSON array; older emits one object per line (NDJSON).
  if (text.startsWith('[')) {
    try {
      const arr = JSON.parse(text)
      return Array.isArray(arr) ? (arr as ComposePsEntry[]) : []
    } catch {
      return []
    }
  }
  const entries: ComposePsEntry[] = []
  for (const line of text.split('\n')) {
    const t = line.trim()
    if (!t || !t.startsWith('{')) continue
    try {
      entries.push(JSON.parse(t) as ComposePsEntry)
    } catch {
      // Skip unparseable lines.
    }
  }
  return entries
}

function parseImageRef(image: string | undefined): { tag?: string; digestShort?: string } {
  if (!image) return {}
  const atIdx = image.indexOf('@')
  let digestShort: string | undefined
  let ref = image
  if (atIdx >= 0) {
    const digest = image.slice(atIdx + 1)
    ref = image.slice(0, atIdx)
    const hex = digest.split(':').pop() ?? ''
    digestShort = hex ? hex.slice(0, 12) : undefined
  }
  const lastColon = ref.lastIndexOf(':')
  const lastSlash = ref.lastIndexOf('/')
  const tag = lastColon > lastSlash ? ref.slice(lastColon + 1) : undefined
  return { tag, digestShort }
}

function parseAppServerLockVersion(raw: string): string | undefined {
  const text = raw.trim()
  if (!text) return undefined
  try {
    const parsed = JSON.parse(text)
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return undefined
    const record = parsed as Record<string, unknown>
    const value = record.version ?? record.Version
    return typeof value === 'string' && value.trim() ? value.trim() : undefined
  } catch {
    return undefined
  }
}

function deriveHealth(p: {
  dockerOk: boolean
  composeOk: boolean
  servicesTotal: number
  servicesUp: number
  anyUnhealthy: boolean
}): StackHealth {
  if (!p.dockerOk || !p.composeOk) return 'unknown'
  if (p.servicesTotal === 0) return 'stopped'
  if (p.anyUnhealthy) return 'unhealthy'
  if (p.servicesUp === 0) return 'stopped'
  if (p.servicesUp < p.servicesTotal) return 'partial'
  return 'running'
}

/** Parse the output of {@link buildStatusCommand} into a {@link RemoteStackStatus}. */
export function parseStatusOutput(raw: string, stackId: string): RemoteStackStatus {
  const base: RemoteStackStatus = {
    stackId,
    health: 'unknown',
    dockerOk: false,
    composeOk: false,
    envOk: false,
    configOk: false,
    tokenPresent: false,
    services: [],
    servicesUp: 0,
    servicesTotal: 0
  }

  if (/(^|\n)\s*DIR_MISSING\s*(\n|$)/.test(raw) || !/STATUS_BEGIN/.test(raw)) {
    return { ...base, error: 'deploy directory not found or unreachable' }
  }

  const flags: Record<string, string> = {}
  for (const line of raw.split('\n')) {
    const m = /^(docker|compose|env|config|token)=(\S+)/.exec(line.trim())
    if (m) flags[m[1]] = m[2]
  }

  const psMatch = /PS_BEGIN\n?([\s\S]*?)\nPS_END/.exec(raw)
  const psEntries = psMatch ? parseComposePsBlock(psMatch[1]) : []
  const lockMatch = /LOCK_BEGIN\n?([\s\S]*?)\nLOCK_END/.exec(raw)
  const appVersion = lockMatch ? parseAppServerLockVersion(lockMatch[1]) : undefined

  const services: ServiceState[] = psEntries.map((e) => ({
    name: (e.Service || e.Name || 'service').trim(),
    state: (e.State || 'unknown').trim(),
    healthy: e.Health ? e.Health.toLowerCase() === 'healthy' : undefined
  }))
  const servicesTotal = services.length
  const servicesUp = services.filter((s) => s.state.toLowerCase().startsWith('running')).length
  const anyUnhealthy = services.some(
    (s) => s.healthy === false || s.state.toLowerCase().startsWith('restarting')
  )

  const image = parseImageRef(psEntries.find((e) => e.Image)?.Image)
  const dockerOk = flags.docker === 'ok'
  const composeOk = flags.compose === 'ok'

  return {
    stackId,
    health: deriveHealth({ dockerOk, composeOk, servicesTotal, servicesUp, anyUnhealthy }),
    dockerOk,
    composeOk,
    envOk: flags.env === 'ok',
    configOk: flags.config === 'ok',
    tokenPresent: flags.token === 'present',
    appVersion,
    imageTag: image.tag,
    imageDigestShort: image.digestShort,
    services,
    servicesUp,
    servicesTotal
  }
}

/** Parse the output of {@link buildSshTestCommand}. */
export function parseSshTestOutput(raw: string, latencyMs?: number): SshTestResult {
  if (!/SSH_OK/.test(raw)) {
    return { reachable: false, errorCode: 'unreachable', message: 'Could not establish an SSH session.' }
  }
  return {
    reachable: true,
    latencyMs,
    dockerOk: /(^|\n)\s*docker=ok/.test(raw),
    composeOk: /(^|\n)\s*compose=ok/.test(raw)
  }
}

function posixDirname(remotePath: string): string {
  const trimmed = remotePath.replace(/\/+$/, '')
  const idx = trimmed.lastIndexOf('/')
  if (idx <= 0) return '/'
  return trimmed.slice(0, idx)
}

function posixBasename(remotePath: string): string {
  return remotePath.replace(/\/+$/, '').split('/').filter(Boolean).pop() ?? ''
}

function composeDirFromLabels(labels: Record<string, string>): string | undefined {
  const workingDir = labels['com.docker.compose.project.working_dir']?.trim()
  if (workingDir && isValidRemotePath(workingDir)) return workingDir

  const configFiles = labels['com.docker.compose.project.config_files']?.trim()
  const firstConfig = configFiles?.split(',').map((p) => p.trim()).find(Boolean)
  if (!firstConfig) return undefined
  const dir = posixDirname(firstConfig)
  return isValidRemotePath(dir) ? dir : undefined
}

function discoveredName(composeDir: string, projectName?: string): string {
  const base = posixBasename(composeDir)
  const parent = posixBasename(posixDirname(composeDir))
  if (base && ['deploy', 'docker', 'compose'].includes(base.toLowerCase()) && parent) return parent
  return base || projectName || 'DotCraft'
}

function isDotCraftImage(image: string): boolean {
  return /(^|[/:])dotcraft(?::|@|$)/i.test(image)
}

function isDotCraftContainer(container: DockerInspectContainer, service: string): boolean {
  const image = container.Config?.Image ?? ''
  if (service.toLowerCase() === 'dotcraft') return true
  if (isDotCraftImage(image)) return true
  return (container.Config?.Env ?? []).some((entry) => entry.startsWith('DOTCRAFT_'))
}

function workspaceMount(container: DockerInspectContainer): string | undefined {
  const mount = (container.Mounts ?? []).find((m) => m.Destination === '/workspace')
  const source = mount?.Source?.trim()
  return source && isValidRemotePath(source) ? source : undefined
}

function appServerWorkspaceMount(container: DockerInspectContainer): string | undefined {
  const mount = (container.Mounts ?? []).find((m) => m.Destination === '/workspace')
  const destination = mount?.Destination?.trim()
  return destination && isValidRemotePath(destination) ? destination : undefined
}

function hostBoundPort(
  container: DockerInspectContainer,
  containerPort: number,
  fallback: number
): number {
  const bindings = container.NetworkSettings?.Ports?.[`${containerPort}/tcp`]
  if (!Array.isArray(bindings) || bindings.length === 0) return fallback
  const records = bindings
    .map((entry) => asRecord(entry))
    .filter((entry): entry is Record<string, unknown> => Boolean(entry))
  const preferred =
    records.find((entry) => entry.HostIp === '127.0.0.1') ??
    records.find((entry) => entry.HostIp === '0.0.0.0') ??
    records[0]
  const value = typeof preferred?.HostPort === 'string' ? Number(preferred.HostPort) : NaN
  return isValidPort(value) ? value : fallback
}

interface DiscoveryGroup {
  projectName: string
  composeDir: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  appServerPort: number
  oratorioPort: number
  dashboardPort: number
  sandboxProfile: boolean
  hasSandbox: boolean
  image?: string
  services: Set<string>
  dotcraft: boolean
}

/** Parse the output of {@link buildDiscoverStacksCommand}. */
export function parseDiscoverStacksOutput(raw: string): DiscoveredStack[] {
  const match = /DISCOVER_BEGIN\n?([\s\S]*?)\nDISCOVER_END/.exec(raw)
  if (!match) return []

  const groups = new Map<string, DiscoveryGroup>()
  for (const line of match[1].split('\n')) {
    const text = line.trim()
    if (!text.startsWith('{')) continue

    let container: DockerInspectContainer
    try {
      container = JSON.parse(text) as DockerInspectContainer
    } catch {
      continue
    }

    const labels = asStringRecord(container.Config?.Labels)
    const projectName = labels['com.docker.compose.project']?.trim()
    if (!projectName) continue

    const composeDir = composeDirFromLabels(labels)
    if (!composeDir) continue

    const key = `${projectName}\u0000${composeDir}`
    let group = groups.get(key)
    if (!group) {
      group = {
        projectName,
        composeDir,
        appServerPort: DEFAULT_APP_SERVER_PORT,
        oratorioPort: DEFAULT_ORATORIO_PORT,
        dashboardPort: DEFAULT_DASHBOARD_PORT,
        sandboxProfile: false,
        hasSandbox: false,
        services: new Set<string>(),
        dotcraft: false
      }
      groups.set(key, group)
    }

    const service = labels['com.docker.compose.service']?.trim() ?? ''
    if (service) group.services.add(service)

    const lowerService = service.toLowerCase()
    if (lowerService.includes('sandbox')) {
      group.hasSandbox = true
      group.sandboxProfile = true
    }

    if (lowerService === 'oratorio') {
      group.workspaceDir ??= workspaceMount(container)
      group.oratorioPort = hostBoundPort(container, DEFAULT_ORATORIO_PORT, DEFAULT_ORATORIO_PORT)
      continue
    }

    if (!isDotCraftContainer(container, service)) continue

    group.dotcraft = true
    group.image ??= container.Config?.Image
    group.workspaceDir ??= workspaceMount(container)
    group.appServerWorkspacePath ??= appServerWorkspaceMount(container)
    group.appServerPort = hostBoundPort(container, DEFAULT_APP_SERVER_PORT, DEFAULT_APP_SERVER_PORT)
    group.dashboardPort = hostBoundPort(container, DEFAULT_DASHBOARD_PORT, DEFAULT_DASHBOARD_PORT)
  }

  return [...groups.values()]
    .filter((group) => group.dotcraft)
    .map((group) => ({
      name: discoveredName(group.composeDir, group.projectName),
      composeDir: group.composeDir,
      workspaceDir: group.workspaceDir,
      appServerWorkspacePath: group.appServerWorkspacePath ?? DEFAULT_APP_SERVER_WORKSPACE_PATH,
      projectName: group.projectName,
      appServerPort: group.appServerPort,
      oratorioPort: group.oratorioPort,
      dashboardPort: group.dashboardPort,
      sandboxProfile: group.sandboxProfile,
      hasSandbox: group.hasSandbox,
      image: group.image,
      services: [...group.services].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }))
    }))
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
}

/** Detect whether `docker compose pull`/`up` output reflects an actual change. */
export function updateChangedFromOutput(pullOutput: string, upOutput: string): boolean {
  const combined = `${pullOutput}\n${upOutput}`.toLowerCase()
  if (/\b(recreat|creating|started|pulling|downloaded newer image|pull complete)\b/.test(combined)) {
    return true
  }
  // Everything up to date and nothing recreated.
  return false
}

// ─────────────────────────────────────────────────────────────────────────────
// Tunnel URLs
// ─────────────────────────────────────────────────────────────────────────────

export function buildTunnelWsUrl(localPort: number, token?: string): string {
  const base = `ws://127.0.0.1:${localPort}/ws`
  const t = token?.trim()
  if (!t) return base
  const url = new URL(base)
  url.searchParams.set('token', t)
  return url.toString()
}

export function buildDashboardUrl(localPort: number): string {
  return `http://127.0.0.1:${localPort}/dashboard`
}

// ─────────────────────────────────────────────────────────────────────────────
// Redaction
// ─────────────────────────────────────────────────────────────────────────────

const SECRET_KEY_RE =
  /\b([A-Za-z0-9_]*(?:TOKEN|SECRET|PASSWORD|PASSWD|API_?KEY|AES_KEY|KEY))\b(\s*[=:]\s*)(["']?)([^\s"'#]+)\3/gi
const TOKEN_QUERY_RE = /([?&]token=)([^&\s"']+)/gi

/**
 * Redact secrets before any SSH output, error, settings snapshot, or operation
 * record leaves the main process. Masks: explicit `extraSecrets` values (e.g. a
 * known AppServer token), `KEY=value` assignments for secret-like keys, and
 * `token=` URL query parameters.
 */
export function redactSecrets(input: string, extraSecrets: string[] = []): string {
  if (!input) return input
  let out = input
  for (const secret of extraSecrets) {
    const s = secret?.trim()
    if (!s || s.length < 4) continue
    out = out.split(s).join(REDACTION_MASK)
  }
  out = out.replace(SECRET_KEY_RE, (_m, key, sep) => `${key}${sep}${REDACTION_MASK}`)
  out = out.replace(TOKEN_QUERY_RE, (_m, prefix) => `${prefix}${REDACTION_MASK}`)
  return out
}
