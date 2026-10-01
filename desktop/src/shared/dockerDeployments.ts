import {
  isValidPort,
  isValidRemotePath,
  quoteRemotePath,
  remoteChildPath,
  shellSingleQuote
} from './sshShell'

export const DEFAULT_APP_SERVER_PORT = 9100
export const DEFAULT_ORATORIO_PORT = 5087
export const DEFAULT_DASHBOARD_PORT = 8080
export const DEFAULT_APP_SERVER_WORKSPACE_PATH = '/workspace'

/** Default and maximum bounds for the `logs --tail` window. */
export const DEFAULT_LOG_TAIL = 200
export const MAX_LOG_TAIL = 2000

export type StackHealth = 'running' | 'partial' | 'stopped' | 'unhealthy' | 'unknown'
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
  /** Technical Compose identifier passed to `docker compose -p`; optional. */
  composeProjectName?: string
  appServerPort: number
  oratorioPort: number
  dashboardPort: number
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
  composeProjectName?: string
  appServerPort: number
  oratorioPort: number
  dashboardPort: number
  image?: string
  services?: string[]
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

const SERVICE_NAME_RE = /^[A-Za-z0-9][A-Za-z0-9._-]*$/
const COMPOSE_PROJECT_NAME_RE = /^[a-z0-9][a-z0-9_-]*$/

export function isValidServiceName(name: unknown): boolean {
  return typeof name === 'string' && SERVICE_NAME_RE.test(name.trim())
}

/** Docker Compose project identifiers are technical, lowercase command arguments. */
export function isValidComposeProjectName(name: unknown): boolean {
  return typeof name === 'string' && COMPOSE_PROJECT_NAME_RE.test(name.trim())
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

function normalizeStack(input: unknown, genId: (prefix: string) => string): RemoteStack | undefined {
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
  const composeProjectNameCandidate =
    typeof raw.composeProjectName === 'string' ? raw.composeProjectName.trim() : ''
  const composeProjectName = isValidComposeProjectName(composeProjectNameCandidate)
    ? composeProjectNameCandidate
    : undefined

  return {
    id,
    name,
    composeDir,
    workspaceDir,
    appServerWorkspacePath,
    composeProjectName,
    appServerPort: isValidPort(raw.appServerPort) ? (raw.appServerPort as number) : DEFAULT_APP_SERVER_PORT,
    oratorioPort: isValidPort(raw.oratorioPort) ? (raw.oratorioPort as number) : DEFAULT_ORATORIO_PORT,
    dashboardPort: isValidPort(raw.dashboardPort) ? (raw.dashboardPort as number) : DEFAULT_DASHBOARD_PORT
  }
}

export function normalizeRemoteStacks(input: unknown, genId: (prefix: string) => string): RemoteStack[] {
  if (!Array.isArray(input)) return []
  const seen = new Set<string>()
  const stacks: RemoteStack[] = []
  for (const entry of input) {
    const stack = normalizeStack(entry, genId)
    if (!stack || seen.has(stack.id)) continue
    seen.add(stack.id)
    stacks.push(stack)
  }
  return stacks
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

/** `docker compose [-p name]` prefix for a stack. */
export function composePrefix(stack: RemoteStack): string {
  const parts = ['docker', 'compose']
  const project = stack.composeProjectName?.trim()
  if (project) parts.push('-p', shellSingleQuote(project))
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

function discoveredName(composeDir: string, composeProjectName?: string): string {
  const base = posixBasename(composeDir)
  const parent = posixBasename(posixDirname(composeDir))
  if (base && ['deploy', 'docker', 'compose'].includes(base.toLowerCase()) && parent) return parent
  return base || composeProjectName || 'DotCraft'
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
  composeProjectName: string
  composeDir: string
  workspaceDir?: string
  appServerWorkspacePath?: string
  appServerPort: number
  oratorioPort: number
  dashboardPort: number
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
    const composeProjectName = labels['com.docker.compose.project']?.trim()
    if (!composeProjectName) continue

    const composeDir = composeDirFromLabels(labels)
    if (!composeDir) continue

    const key = `${composeProjectName}\u0000${composeDir}`
    let group = groups.get(key)
    if (!group) {
      group = {
        composeProjectName,
        composeDir,
        appServerPort: DEFAULT_APP_SERVER_PORT,
        oratorioPort: DEFAULT_ORATORIO_PORT,
        dashboardPort: DEFAULT_DASHBOARD_PORT,
        services: new Set<string>(),
        dotcraft: false
      }
      groups.set(key, group)
    }

    const service = labels['com.docker.compose.service']?.trim() ?? ''
    if (service) group.services.add(service)

    const lowerService = service.toLowerCase()
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
      name: discoveredName(group.composeDir, group.composeProjectName),
      composeDir: group.composeDir,
      workspaceDir: group.workspaceDir,
      appServerWorkspacePath: group.appServerWorkspacePath ?? DEFAULT_APP_SERVER_WORKSPACE_PATH,
      composeProjectName: group.composeProjectName,
      appServerPort: group.appServerPort,
      oratorioPort: group.oratorioPort,
      dashboardPort: group.dashboardPort,
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
  return false
}

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
