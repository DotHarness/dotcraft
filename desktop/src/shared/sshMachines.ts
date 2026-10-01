import { normalizeRemoteStacks, type RemoteStack } from './dockerDeployments'
import {
  generateId,
  isAbsoluteRemoteFolderPath,
  isValidPort,
  normalizeRemoteFolderPath,
  remoteBaseName
} from './sshShell'

export const DEFAULT_SSH_CONNECT_TIMEOUT_SEC = 10

export type SshMachineSource = 'sshConfig' | 'manual'

export interface RemoteProject {
  id: string
  path: string
  label: string
}

export interface SshMachine {
  id: string
  name: string
  source: SshMachineSource
  alias?: string
  hostname?: string
  port?: number
  identityFile?: string
  autoConnect: boolean
  projects: RemoteProject[]
  stacks: RemoteStack[]
}

export type SshMachineStatusKind =
  | 'notConnected'
  | 'connecting'
  | 'connected'
  | 'notInstalled'
  | 'installing'
  | 'updateRequired'
  | 'setupModel'
  | 'failed'
  | 'unsupported'

export type UnsupportedSystem = 'windows' | 'linuxArm' | 'other'

export interface SshMachineStatus {
  kind: SshMachineStatusKind
  message?: string
  installedVersion?: string
  unsupported?: UnsupportedSystem
  system?: string
}

export interface SshMachineSystemInfo {
  os: string
  arch: string
  osName?: string
  osVersion?: string
  home?: string
  dotcraftVersion?: string
  hasDocker: boolean
}

export interface SshResolvedHost {
  user?: string
  hostname?: string
  port?: number
  proxyJump?: string
  identityFiles: string[]
}

export interface SshMachineView extends SshMachine {
  status: SshMachineStatus
  system?: SshMachineSystemInfo
  resolved?: SshResolvedHost
}

export interface SshMachinesPayload {
  machines: SshMachineView[]
}

export type SshMachineAddEntry =
  | { source: 'sshConfig'; alias: string; name?: string }
  | { source: 'manual'; name: string; hostname: string; port?: number; identityFile?: string }

export interface SshMachineEditPatch {
  name?: string
  hostname?: string
  port?: number | null
  identityFile?: string | null
}

export type SshMachineField = 'name' | 'alias' | 'hostname' | 'port' | 'identityFile'
export type SshMachineFieldError = 'required' | 'duplicate' | 'invalid'
export type SshMachineValidation = Partial<Record<SshMachineField, SshMachineFieldError>>

export interface LocalSshIdentity {
  path: string
  source: 'default' | 'config'
  exists: boolean
  hostAliases?: string[]
}

export interface DiscoveredSshHost {
  alias: string
  resolvedHost?: string
  added: boolean
  error?: string
}

export interface SshHostDiscovery {
  sshDir: string
  configPath: string
  configExists: boolean
  agentAvailable: boolean
  hosts: DiscoveredSshHost[]
  identities: LocalSshIdentity[]
  error?: string
}

export interface RemoteFolderEntry {
  name: string
  path: string
  saved: boolean
}

export interface RemoteFolderListing {
  path: string
  home: string
  folders: RemoteFolderEntry[]
}

export interface SshTarget {
  destination: string
  port?: number
  identityFile?: string
}

export interface SshExecOptions {
  connectTimeoutSec?: number
}

const SSH_TARGET_RE = /^[A-Za-z0-9][A-Za-z0-9._@:-]*$/
const SSH_ALIAS_RE = /^[A-Za-z0-9_][A-Za-z0-9._-]*$/
const MAX_NAME_LENGTH = 80

export function isValidSshTarget(target: unknown): boolean {
  if (typeof target !== 'string') return false
  const t = target.trim()
  if (!t || t.length > 255) return false
  return SSH_TARGET_RE.test(t)
}

export function isValidSshAlias(alias: unknown): boolean {
  return typeof alias === 'string' && alias.length <= 255 && SSH_ALIAS_RE.test(alias)
}

export function isValidIdentityFile(p: unknown): boolean {
  if (typeof p !== 'string') return false
  const t = p.trim()
  if (!t || t.length > 1024) return false
  // eslint-disable-next-line no-control-regex
  if (/[\s\u0000-\u001f]/.test(t)) return false
  if (t.startsWith('-')) return false
  return true
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : undefined
}

function trimmedString(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function sameName(a: string, b: string): boolean {
  return a.trim().toLocaleLowerCase() === b.trim().toLocaleLowerCase()
}

export function isMachineNameTaken(name: string, machines: SshMachine[], exceptId?: string): boolean {
  return machines.some((m) => m.id !== exceptId && sameName(m.name, name))
}

function uniqueName(base: string, taken: string[]): string {
  if (!taken.some((n) => sameName(n, base))) return base
  for (let i = 2; ; i += 1) {
    const candidate = `${base} (${i})`
    if (!taken.some((n) => sameName(n, candidate))) return candidate
  }
}

function normalizeProjects(input: unknown, genId: (prefix: string) => string): RemoteProject[] {
  if (!Array.isArray(input)) return []
  const seenIds = new Set<string>()
  const seenPaths = new Set<string>()
  const projects: RemoteProject[] = []
  for (const entry of input) {
    const raw = asRecord(entry)
    if (!raw || !isAbsoluteRemoteFolderPath(raw.path)) continue
    const path = normalizeRemoteFolderPath(raw.path)
    if (seenPaths.has(path)) continue
    let id = trimmedString(raw.id) || genId('p')
    if (seenIds.has(id)) id = genId('p')
    const label = trimmedString(raw.label) || remoteBaseName(path) || path
    seenIds.add(id)
    seenPaths.add(path)
    projects.push({ id, path, label })
  }
  return projects
}

function normalizeMachine(
  input: unknown,
  genId: (prefix: string) => string
): SshMachine | undefined {
  const raw = asRecord(input)
  if (!raw) return undefined

  const legacyTarget = raw.source === undefined ? trimmedString(raw.sshTarget) : ''
  const source: SshMachineSource | undefined = legacyTarget
    ? 'manual'
    : raw.source === 'sshConfig' || raw.source === 'manual'
      ? raw.source
      : undefined
  if (!source) return undefined

  const id = trimmedString(raw.id) || genId('m')
  const stacks = normalizeRemoteStacks(raw.stacks, genId)
  const identityFile = isValidIdentityFile(raw.identityFile) ? trimmedString(raw.identityFile) : undefined

  if (legacyTarget) {
    if (!isValidSshTarget(legacyTarget)) return undefined
    return {
      id,
      name: trimmedString(raw.name) || legacyTarget,
      source: 'manual',
      hostname: legacyTarget,
      ...(identityFile ? { identityFile } : {}),
      autoConnect: false,
      projects: [],
      stacks
    }
  }

  const base = {
    id,
    autoConnect: raw.autoConnect === true,
    projects: normalizeProjects(raw.projects, genId),
    stacks
  }

  if (source === 'sshConfig') {
    const alias = trimmedString(raw.alias)
    if (!isValidSshAlias(alias)) return undefined
    return { ...base, name: trimmedString(raw.name) || alias, source, alias }
  }

  const hostname = trimmedString(raw.hostname)
  if (!isValidSshTarget(hostname)) return undefined
  return {
    ...base,
    name: trimmedString(raw.name) || hostname,
    source,
    hostname,
    ...(isValidPort(raw.port) ? { port: raw.port as number } : {}),
    ...(identityFile ? { identityFile } : {})
  }
}

export function normalizeSshMachines(
  input: unknown,
  genId: (prefix: string) => string = generateId
): SshMachine[] {
  if (!Array.isArray(input)) return []
  const seen = new Set<string>()
  const machines: SshMachine[] = []
  for (const entry of input) {
    const machine = normalizeMachine(entry, genId)
    if (!machine || seen.has(machine.id)) continue
    seen.add(machine.id)
    machine.name = uniqueName(machine.name.slice(0, MAX_NAME_LENGTH), machines.map((m) => m.name))
    machines.push(machine)
  }
  return machines
}

export function validateMachineEntry(
  entry: SshMachineAddEntry,
  machines: SshMachine[],
  exceptId?: string
): SshMachineValidation {
  const errors: SshMachineValidation = {}
  if (entry.source === 'sshConfig') {
    if (!entry.alias?.trim()) errors.alias = 'required'
    else if (!isValidSshAlias(entry.alias.trim())) errors.alias = 'invalid'
    else if (machines.some((m) => m.id !== exceptId && m.source === 'sshConfig' && m.alias === entry.alias.trim())) {
      errors.alias = 'duplicate'
    }
    const name = entry.name?.trim() || entry.alias?.trim() || ''
    if (name.length > MAX_NAME_LENGTH) errors.name = 'invalid'
    else if (name && isMachineNameTaken(name, machines, exceptId)) errors.name = 'duplicate'
    return errors
  }

  const name = entry.name?.trim() ?? ''
  if (!name) errors.name = 'required'
  else if (name.length > MAX_NAME_LENGTH) errors.name = 'invalid'
  else if (isMachineNameTaken(name, machines, exceptId)) errors.name = 'duplicate'

  const hostname = entry.hostname?.trim() ?? ''
  if (!hostname) errors.hostname = 'required'
  else if (!isValidSshTarget(hostname)) errors.hostname = 'invalid'

  if (entry.port !== undefined && !isValidPort(entry.port)) errors.port = 'invalid'
  if (entry.identityFile !== undefined && entry.identityFile.trim() && !isValidIdentityFile(entry.identityFile)) {
    errors.identityFile = 'invalid'
  }
  return errors
}

export function hasValidationErrors(errors: SshMachineValidation): boolean {
  return Object.keys(errors).length > 0
}

export function createMachineFromEntry(
  entry: SshMachineAddEntry,
  genId: (prefix: string) => string = generateId
): SshMachine {
  if (entry.source === 'sshConfig') {
    const alias = entry.alias.trim()
    return {
      id: genId('m'),
      name: entry.name?.trim() || alias,
      source: 'sshConfig',
      alias,
      autoConnect: true,
      projects: [],
      stacks: []
    }
  }
  const identityFile = entry.identityFile?.trim()
  return {
    id: genId('m'),
    name: entry.name.trim(),
    source: 'manual',
    hostname: entry.hostname.trim(),
    ...(entry.port !== undefined ? { port: entry.port } : {}),
    ...(identityFile ? { identityFile } : {}),
    autoConnect: true,
    projects: [],
    stacks: []
  }
}

export function applyMachineEdit(machine: SshMachine, patch: SshMachineEditPatch): SshMachine {
  const next: SshMachine = { ...machine }
  if (patch.name !== undefined) next.name = patch.name.trim()
  if (machine.source === 'manual') {
    if (patch.hostname !== undefined) next.hostname = patch.hostname.trim()
    if (patch.port !== undefined) {
      if (patch.port === null) delete next.port
      else next.port = patch.port
    }
    if (patch.identityFile !== undefined) {
      const identity = patch.identityFile?.trim()
      if (identity) next.identityFile = identity
      else delete next.identityFile
    }
  }
  return next
}

export function machineEditEntry(machine: SshMachine): SshMachineAddEntry {
  return machine.source === 'sshConfig'
    ? { source: 'sshConfig', alias: machine.alias ?? '', name: machine.name }
    : {
        source: 'manual',
        name: machine.name,
        hostname: machine.hostname ?? '',
        ...(machine.port !== undefined ? { port: machine.port } : {}),
        ...(machine.identityFile ? { identityFile: machine.identityFile } : {})
      }
}

export function machineSshTarget(machine: SshMachine): SshTarget {
  if (machine.source === 'sshConfig') return { destination: machine.alias ?? '' }
  return {
    destination: machine.hostname ?? '',
    ...(machine.port !== undefined ? { port: machine.port } : {}),
    ...(machine.identityFile ? { identityFile: machine.identityFile } : {})
  }
}

function baseSshOptions(opts: SshExecOptions): string[] {
  const timeout = opts.connectTimeoutSec ?? DEFAULT_SSH_CONNECT_TIMEOUT_SEC
  return [
    '-o',
    'BatchMode=yes',
    '-o',
    `ConnectTimeout=${Math.max(1, Math.floor(timeout))}`,
    '-o',
    'StrictHostKeyChecking=accept-new'
  ]
}

function targetOptions(target: SshTarget): string[] {
  const args: string[] = []
  if (target.port !== undefined && isValidPort(target.port)) args.push('-p', String(target.port))
  const identity = target.identityFile?.trim()
  if (identity && isValidIdentityFile(identity)) args.push('-i', identity, '-o', 'IdentitiesOnly=yes')
  return args
}

export function buildSshArgs(target: SshTarget, remoteCommand: string, opts: SshExecOptions = {}): string[] {
  return [...baseSshOptions(opts), ...targetOptions(target), '--', target.destination, remoteCommand]
}

export function buildSshTunnelArgs(
  target: SshTarget,
  localPort: number,
  remotePort: number,
  opts: SshExecOptions = {}
): string[] {
  return [
    '-N',
    ...baseSshOptions(opts),
    '-o',
    'ExitOnForwardFailure=yes',
    '-L',
    `127.0.0.1:${localPort}:127.0.0.1:${remotePort}`,
    ...targetOptions(target),
    '--',
    target.destination
  ]
}

export function buildSshResolveArgs(alias: string): string[] {
  return ['-G', '--', alias]
}

export function parseSshResolveOutput(raw: string): SshResolvedHost {
  const resolved: SshResolvedHost = { identityFiles: [] }
  for (const line of raw.split(/\r?\n/)) {
    const match = /^(\S+)\s+(.*)$/.exec(line.trim())
    if (!match) continue
    const key = match[1].toLowerCase()
    const value = match[2].trim()
    if (!value) continue
    if (key === 'user') resolved.user = value
    else if (key === 'hostname') resolved.hostname = value
    else if (key === 'port') {
      const port = Number(value)
      if (isValidPort(port)) resolved.port = port
    } else if (key === 'proxyjump' && value.toLowerCase() !== 'none') resolved.proxyJump = value
    else if (key === 'identityfile') resolved.identityFiles.push(value)
  }
  return resolved
}

export function formatResolvedHost(resolved: SshResolvedHost): string | undefined {
  if (!resolved.hostname) return undefined
  const host = resolved.user ? `${resolved.user}@${resolved.hostname}` : resolved.hostname
  return resolved.port && resolved.port !== 22 ? `${host}:${resolved.port}` : host
}

export function machineHasHub(status: SshMachineStatus): boolean {
  return status.kind === 'connected' || status.kind === 'setupModel'
}
