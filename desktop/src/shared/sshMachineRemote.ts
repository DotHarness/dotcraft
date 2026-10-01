import type { RemoteFolderListing, SshMachineStatus, SshMachineSystemInfo } from './sshMachines'
import {
  isAbsoluteRemoteFolderPath,
  isValidRemoteFolderPath,
  normalizeRemoteFolderPath,
  quoteRemotePath,
  remoteChildPath,
  shellSingleQuote
} from './sshShell'

export const REMOTE_DOTCRAFT_BIN = '"$HOME/.craft/bin/dotcraft"'
export const REMOTE_HUB_DIR = '"$HOME/.craft/hub"'
export const CONNECT_RETRY_DELAYS_MS = [1_000, 2_000, 4_000, 8_000, 16_000]
export const INSTALL_REQUIRED_TOOLS = ['bash', 'curl', 'tar'] as const

export interface MachineProbe {
  os: string
  arch: string
  osName?: string
  osVersion?: string
  home?: string
  dotcraftPresent: boolean
  dotcraftVersion?: string
  tools: Record<string, boolean>
}

export type ProbeParseResult =
  | { kind: 'probe'; probe: MachineProbe }
  | { kind: 'windows' }
  | { kind: 'invalid' }

export interface RemoteHubLock {
  pid: number
  apiBaseUrl: string
  port: number
  token: string
  version?: string
}

export interface LoopbackEndpoint {
  port: number
  token?: string
}

export interface ProviderListLike {
  managedBy?: unknown
  providers?: unknown
}

export function buildProbeCommand(): string {
  return [
    'echo DOTCRAFT_PROBE_BEGIN;',
    `printf 'os=%s\\n' "$(uname -s 2>/dev/null)";`,
    `printf 'arch=%s\\n' "$(uname -m 2>/dev/null)";`,
    `printf 'home=%s\\n' "$HOME";`,
    `if [ -r /etc/os-release ]; then (. /etc/os-release; printf 'osName=%s\\n' "\${NAME:-}"; printf 'osVersion=%s\\n' "\${VERSION_ID:-}"); fi;`,
    `if command -v sw_vers >/dev/null 2>&1; then printf 'osName=%s\\n' "$(sw_vers -productName 2>/dev/null)"; printf 'osVersion=%s\\n' "$(sw_vers -productVersion 2>/dev/null)"; fi;`,
    `if [ -x ${REMOTE_DOTCRAFT_BIN} ]; then echo dotcraftPresent=1; printf 'dotcraft=%s\\n' "$(${REMOTE_DOTCRAFT_BIN} --version 2>/dev/null | head -n 1)"; else echo dotcraftPresent=0; fi;`,
    `for t in docker bash curl tar setsid; do if command -v "$t" >/dev/null 2>&1; then echo "tool_$t=1"; else echo "tool_$t=0"; fi; done;`,
    'echo DOTCRAFT_PROBE_END'
  ].join(' ')
}

const WINDOWS_SHELL_PATTERNS = [
  /is not recognized as an internal or external command/i,
  /is not recognized as the name of a cmdlet/i,
  /CommandNotFoundException/,
  /was unexpected at this time/i,
  /\bWindows PowerShell\b/i
]

export function isWindowsUname(os: string): boolean {
  return /^(Windows_NT|MINGW|MSYS|CYGWIN)/i.test(os.trim())
}

export function parseProbeOutput(stdout: string, stderr = ''): ProbeParseResult {
  const begin = stdout.indexOf('DOTCRAFT_PROBE_BEGIN\n')
  const end = stdout.indexOf('DOTCRAFT_PROBE_END')
  if (begin < 0 || end < 0) {
    const combined = `${stdout}\n${stderr}`
    return WINDOWS_SHELL_PATTERNS.some((pattern) => pattern.test(combined)) ? { kind: 'windows' } : { kind: 'invalid' }
  }

  const fields: Record<string, string> = {}
  const tools: Record<string, boolean> = {}
  for (const line of stdout.slice(begin, end).split(/\r?\n/)) {
    const idx = line.indexOf('=')
    if (idx <= 0) continue
    const key = line.slice(0, idx).trim()
    const value = line.slice(idx + 1).trim()
    if (key.startsWith('tool_')) tools[key.slice('tool_'.length)] = value === '1'
    else if (value || !(key in fields)) fields[key] = value
  }

  const os = fields.os ?? ''
  if (isWindowsUname(os)) return { kind: 'windows' }
  const dotcraftVersion = parseDotCraftVersion(fields.dotcraft ?? '')
  return {
    kind: 'probe',
    probe: {
      os,
      arch: fields.arch ?? '',
      ...(fields.osName ? { osName: fields.osName } : {}),
      ...(fields.osVersion ? { osVersion: fields.osVersion } : {}),
      ...(fields.home ? { home: fields.home } : {}),
      dotcraftPresent: fields.dotcraftPresent === '1',
      ...(dotcraftVersion ? { dotcraftVersion } : {}),
      tools
    }
  }
}

const VERSION_RE = /(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?/

export function parseDotCraftVersion(raw: string): string | undefined {
  const match = VERSION_RE.exec(raw)
  if (!match) return undefined
  return match[4] ? `${match[1]}.${match[2]}.${match[3]}-${match[4]}` : `${match[1]}.${match[2]}.${match[3]}`
}

export function compareVersions(a: string, b: string): number {
  const left = VERSION_RE.exec(a)
  const right = VERSION_RE.exec(b)
  if (!left || !right) return 0
  for (let i = 1; i <= 3; i += 1) {
    const diff = Number(left[i]) - Number(right[i])
    if (diff !== 0) return diff < 0 ? -1 : 1
  }
  const lp = left[4] ?? ''
  const rp = right[4] ?? ''
  if (lp === rp) return 0
  if (!lp) return 1
  if (!rp) return -1
  return lp < rp ? -1 : 1
}

export function windowsUnsupportedStatus(): SshMachineStatus {
  return { kind: 'unsupported', unsupported: 'windows', system: 'Windows' }
}

export function classifyProbe(probe: MachineProbe, minimumVersion?: string): SshMachineStatus | null {
  const os = probe.os.trim()
  const arch = probe.arch.trim()
  if (isWindowsUname(os)) return windowsUnsupportedStatus()
  if (os === 'Linux') {
    if (/^(aarch64|arm64|arm)/i.test(arch)) {
      return { kind: 'unsupported', unsupported: 'linuxArm', system: `Linux ${arch}` }
    }
    if (!/^(x86_64|amd64)$/i.test(arch)) {
      return { kind: 'unsupported', unsupported: 'other', system: `Linux ${arch || 'unknown'}` }
    }
  } else if (os === 'Darwin') {
    if (!/^(x86_64|amd64|arm64|aarch64)$/i.test(arch)) {
      return { kind: 'unsupported', unsupported: 'other', system: `macOS ${arch || 'unknown'}` }
    }
  } else {
    return { kind: 'unsupported', unsupported: 'other', system: [os || 'unknown', arch].filter(Boolean).join(' ') }
  }

  if (!probe.dotcraftPresent) return { kind: 'notInstalled' }
  if (!probe.dotcraftVersion) return { kind: 'updateRequired', installedVersion: '' }
  if (minimumVersion && compareVersions(probe.dotcraftVersion, minimumVersion) < 0) {
    return { kind: 'updateRequired', installedVersion: probe.dotcraftVersion }
  }
  return null
}

export function systemInfoFromProbe(probe: MachineProbe): SshMachineSystemInfo {
  return {
    os: probe.os,
    arch: probe.arch,
    ...(probe.osName ? { osName: probe.osName } : {}),
    ...(probe.osVersion ? { osVersion: probe.osVersion } : {}),
    ...(probe.home ? { home: probe.home } : {}),
    ...(probe.dotcraftVersion ? { dotcraftVersion: probe.dotcraftVersion } : {}),
    hasDocker: probe.tools.docker === true
  }
}

export function missingInstallTools(probe: MachineProbe): string[] {
  return INSTALL_REQUIRED_TOOLS.filter((tool) => probe.tools[tool] !== true)
}

export function resolveInstallVersion(appVersion: string, packaged: boolean): string {
  const version = parseDotCraftVersion(appVersion)
  return packaged && version ? `v${version}` : 'latest'
}

export function minimumDotCraftVersion(appVersion: string, packaged: boolean): string | undefined {
  return packaged ? parseDotCraftVersion(appVersion) : undefined
}

export function buildInstallCommand(version: string): string {
  return `DOTCRAFT_VERSION=${shellSingleQuote(version)} bash -s`
}

export function buildReadHubLockCommand(): string {
  return `echo HUB_LOCK_BEGIN; cat ${REMOTE_HUB_DIR}/hub.lock 2>/dev/null; echo; echo HUB_LOCK_END`
}

export function buildStartHubCommand(): string {
  const out = `${REMOTE_HUB_DIR}/hub.out`
  return [
    `mkdir -p ${REMOTE_HUB_DIR} && cd "$HOME" &&`,
    `if command -v setsid >/dev/null 2>&1;`,
    `then setsid ${REMOTE_DOTCRAFT_BIN} hub </dev/null >>${out} 2>&1 &`,
    `else nohup ${REMOTE_DOTCRAFT_BIN} hub </dev/null >>${out} 2>&1 &`,
    `fi; echo HUB_STARTED`
  ].join(' ')
}

export function buildHubOutTailCommand(): string {
  return `tail -n 1 ${REMOTE_HUB_DIR}/hub.out 2>/dev/null || true`
}

function readField(record: Record<string, unknown>, ...keys: string[]): unknown {
  for (const key of keys) {
    if (key in record) return record[key]
  }
  return undefined
}

function loopbackHttpPort(rawUrl: string): number | undefined {
  try {
    const url = new URL(rawUrl.trim())
    if (url.protocol !== 'http:' || !url.port) return undefined
    const host = url.hostname.replace(/^\[|\]$/g, '')
    if (host !== 'localhost' && !host.startsWith('127.') && host !== '::1') return undefined
    const port = Number(url.port)
    return Number.isInteger(port) && port > 0 && port <= 65535 ? port : undefined
  } catch {
    return undefined
  }
}

export function parseHubLock(raw: string): RemoteHubLock | null {
  const match = /HUB_LOCK_BEGIN\r?\n?([\s\S]*?)\r?\n?HUB_LOCK_END/.exec(raw)
  const text = (match ? match[1] : raw).trim()
  if (!text) return null
  let parsed: unknown
  try {
    parsed = JSON.parse(text)
  } catch {
    return null
  }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) return null
  const record = parsed as Record<string, unknown>
  const pid = readField(record, 'pid', 'Pid')
  const apiBaseUrl = readField(record, 'apiBaseUrl', 'ApiBaseUrl')
  const token = readField(record, 'token', 'Token')
  const version = readField(record, 'version', 'Version')
  if (typeof pid !== 'number' || typeof apiBaseUrl !== 'string' || typeof token !== 'string' || !token) return null
  const port = loopbackHttpPort(apiBaseUrl)
  if (port === undefined) return null
  return {
    pid,
    apiBaseUrl: apiBaseUrl.trim(),
    port,
    token,
    ...(typeof version === 'string' && version.trim() ? { version: version.trim() } : {})
  }
}

export function parseLoopbackEndpoint(endpoint: string | undefined | null): LoopbackEndpoint | null {
  if (!endpoint?.trim()) return null
  try {
    const url = new URL(endpoint.trim())
    if (url.protocol !== 'ws:' || !url.port) return null
    const host = url.hostname.replace(/^\[|\]$/g, '')
    if (host !== 'localhost' && !host.startsWith('127.') && host !== '::1') return null
    const port = Number(url.port)
    if (!Number.isInteger(port) || port <= 0 || port > 65535) return null
    const token = url.searchParams.get('token') ?? undefined
    return token ? { port, token } : { port }
  } catch {
    return null
  }
}

export function forwardedEndpointUrl(endpoint: string, localPort: number): string {
  const url = new URL(endpoint.trim())
  url.hostname = '127.0.0.1'
  url.port = String(localPort)
  return url.toString()
}

export function forwardedEndpointDisplay(localPort: number): string {
  return `ws://127.0.0.1:${localPort}/ws`
}

export function buildListFoldersCommand(path: string): string {
  if (!isValidRemoteFolderPath(path)) throw new Error('Invalid remote folder path.')
  return [
    `cd -- ${quoteRemotePath(path)} 2>/dev/null || { echo FOLDERS_MISSING; exit 0; };`,
    `printf 'cwd=%s\\n' "$(pwd)"; printf 'home=%s\\n' "$HOME"; echo FOLDERS_BEGIN;`,
    `for d in ./*/; do if [ -d "$d" ]; then printf '%s\\n' "\${d#./}"; fi; done;`,
    'echo FOLDERS_END'
  ].join(' ')
}

export function parseFolderListing(raw: string, savedPaths: string[] = []): RemoteFolderListing | null {
  if (/(^|\n)FOLDERS_MISSING(\r?\n|$)/.test(raw)) return null
  const cwd = /(^|\n)cwd=([^\r\n]*)/.exec(raw)?.[2] ?? ''
  const home = /(^|\n)home=([^\r\n]*)/.exec(raw)?.[2] ?? ''
  const block = /FOLDERS_BEGIN\r?\n([\s\S]*?)FOLDERS_END/.exec(raw)
  if (!isAbsoluteRemoteFolderPath(cwd) || !block) return null
  const path = normalizeRemoteFolderPath(cwd)
  const saved = new Set(savedPaths.map(normalizeRemoteFolderPath))
  const folders = block[1]
    .split(/\r?\n/)
    .map((line) => line.replace(/\/+$/, ''))
    .filter((name) => name && !name.startsWith('.') && !name.includes('/'))
    .map((name) => {
      const child = normalizeRemoteFolderPath(remoteChildPath(path, name))
      return { name, path: child, saved: saved.has(child) }
    })
    .filter((entry) => isAbsoluteRemoteFolderPath(entry.path))
    .sort((a, b) => a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }))
  return { path, home: isAbsoluteRemoteFolderPath(home) ? normalizeRemoteFolderPath(home) : path, folders }
}

export function buildEnsureProjectWorkspaceCommand(path: string): string {
  if (!isAbsoluteRemoteFolderPath(path)) throw new Error('Remote project path must be absolute.')
  const dir = quoteRemotePath(path)
  return `if [ -d ${dir} ]; then mkdir -p -- ${quoteRemotePath(remoteChildPath(path, '.craft'))} && echo WORKSPACE_READY; else echo WORKSPACE_MISSING; fi`
}

export function buildEnsureChatWorkspaceCommand(): string {
  return [
    'd="$HOME/.craft/workspaces/chats";',
    'mkdir -p "$d/.craft/memory" "$d/.craft/skills" "$d/.craft/security" &&',
    `{ [ -f "$d/.craft/config.json" ] || printf '{}\\n' > "$d/.craft/config.json"; } &&`,
    `printf 'path=%s\\n' "$d"`
  ].join(' ')
}

export function parseChatWorkspacePath(raw: string): string | null {
  const path = /(^|\n)path=([^\r\n]*)/.exec(raw)?.[2] ?? ''
  return isAbsoluteRemoteFolderPath(path) ? normalizeRemoteFolderPath(path) : null
}

export function buildReadConfigFilesCommand(workspaceConfigPath: string): string {
  return [
    `read_cfg(){ if [ -f "$1" ]; then (base64 -w 0 "$1" 2>/dev/null || base64 "$1" 2>/dev/null | tr -d '\\n' || true); fi; };`,
    'echo CONFIG_BEGIN;',
    `printf 'workspace='; read_cfg ${quoteRemotePath(workspaceConfigPath)}; echo;`,
    `printf 'userDefaults='; read_cfg ${quoteRemotePath('~/.craft/config.json')}; echo;`,
    'echo CONFIG_END'
  ].join(' ')
}

function decodeBase64Utf8(value: string): string {
  const binary = atob(value)
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i += 1) bytes[i] = binary.charCodeAt(i)
  return new TextDecoder().decode(bytes)
}

export function parseConfigFilesOutput(raw: string): { workspaceRaw: string; userDefaultsRaw: string } | null {
  if (!/CONFIG_BEGIN/.test(raw)) return null
  const fields: Record<string, string> = {}
  for (const line of raw.split(/\r?\n/)) {
    const idx = line.indexOf('=')
    if (idx <= 0) continue
    const key = line.slice(0, idx).trim()
    const value = line.slice(idx + 1).trim()
    if (key !== 'workspace' && key !== 'userDefaults') continue
    try {
      fields[key] = value ? decodeBase64Utf8(value) : ''
    } catch {
      fields[key] = ''
    }
  }
  return { workspaceRaw: fields.workspace ?? '', userDefaultsRaw: fields.userDefaults ?? '' }
}

function parseJsonObject(raw: string): Record<string, unknown> | null {
  if (!raw.trim()) return null
  try {
    const parsed = JSON.parse(raw)
    return parsed && typeof parsed === 'object' && !Array.isArray(parsed) ? (parsed as Record<string, unknown>) : null
  } catch {
    return null
  }
}

function readProviderId(config: Record<string, unknown> | null): { present: boolean; value: string } {
  if (!config) return { present: false, value: '' }
  const key = Object.keys(config).find((candidate) => candidate.toLowerCase() === 'providerid')
  if (!key || typeof config[key] !== 'string') return { present: false, value: '' }
  return { present: true, value: (config[key] as string).trim() }
}

export function resolveEffectiveProviderId(workspaceRaw: string, userRaw: string): string {
  const workspace = readProviderId(parseJsonObject(workspaceRaw))
  if (workspace.present) return workspace.value
  return readProviderId(parseJsonObject(userRaw)).value
}

export function hasUsableModel(result: ProviderListLike | null | undefined, providerId: string): boolean {
  if (!providerId || !result || !Array.isArray(result.providers)) return false
  const serviceManaged = result.managedBy === 'modelService'
  const provider = (result.providers as unknown[]).find((entry): entry is Record<string, unknown> =>
    Boolean(entry) &&
    typeof entry === 'object' &&
    typeof (entry as Record<string, unknown>).id === 'string' &&
    ((entry as Record<string, unknown>).id as string).toLowerCase() === providerId.toLowerCase()
  )
  if (!provider) return false
  if (serviceManaged || provider.managedBy === 'modelService') return provider.isAuthenticated === true
  const authMethod = typeof provider.authMethod === 'string' ? provider.authMethod.toLowerCase() : ''
  if (authMethod === 'chatgptoauth') {
    return provider.isAuthenticated === true ||
      (typeof provider.chatGptAccountId === 'string' && provider.chatGptAccountId.trim() !== '')
  }
  return provider.hasApiKey === true || provider.isAuthenticated === true
}

export function isEndpointChangeEvent(
  event: { kind?: unknown; workspacePath?: unknown; data?: unknown },
  workspacePath: string
): boolean {
  if (event.kind !== 'appserver.running' && event.kind !== 'port.allocated') return false
  if (typeof event.workspacePath !== 'string') return false
  if (normalizeRemoteFolderPath(event.workspacePath) !== normalizeRemoteFolderPath(workspacePath)) return false
  if (event.kind === 'port.allocated') {
    const service = (event.data as { service?: unknown } | undefined)?.service
    return service === undefined || service === 'appServerWebSocket'
  }
  return true
}

export function eventEndpointPort(event: { kind?: unknown; data?: unknown }): number | undefined {
  const data = event.data as { port?: unknown; endpoints?: { appServerWebSocket?: unknown } } | undefined
  if (event.kind === 'port.allocated') return typeof data?.port === 'number' ? data.port : undefined
  const endpoint = data?.endpoints?.appServerWebSocket
  return typeof endpoint === 'string' ? parseLoopbackEndpoint(endpoint)?.port : undefined
}

export function hubOutLastLine(raw: string): string {
  return raw
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .pop() ?? ''
}
