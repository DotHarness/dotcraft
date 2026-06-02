import { readdir, readFile, stat } from 'fs/promises'
import { isAbsolute, join, relative, resolve, sep } from 'path'
import { homedir } from 'os'
import type {
  LocalSshConfigInfo,
  LocalSshHostAlias,
  LocalSshIdentity,
  LocalSshIdentitySource
} from '../../shared/remoteServers'

const DEFAULT_IDENTITY_NAMES = [
  'id_ed25519',
  'id_ed25519_sk',
  'id_ecdsa',
  'id_ecdsa_sk',
  'id_rsa',
  'id_dsa'
]

const NON_IDENTITY_NAMES = new Set([
  'config',
  'known_hosts',
  'known_hosts.old',
  'authorized_keys',
  'allowed_signers',
  'environment',
  'rc'
])

interface IdentityCandidate {
  path: string
  source: LocalSshIdentitySource
  aliases: Set<string>
}

function stripInlineComment(line: string): string {
  const trimmed = line.trim()
  if (!trimmed || trimmed.startsWith('#')) return ''
  return trimmed.replace(/\s+#.*$/, '').trim()
}

function splitDirective(line: string): { key: string; value: string } | null {
  const eq = /^([^=\s]+)\s*=\s*(.+)$/.exec(line)
  if (eq) return { key: eq[1].toLowerCase(), value: eq[2].trim() }
  const ws = /^(\S+)\s+(.+)$/.exec(line)
  if (!ws) return null
  return { key: ws[1].toLowerCase(), value: ws[2].trim() }
}

function unquote(value: string): string {
  const trimmed = value.trim()
  if (trimmed.length >= 2) {
    const first = trimmed[0]
    const last = trimmed[trimmed.length - 1]
    if ((first === '"' && last === '"') || (first === "'" && last === "'")) {
      return trimmed.slice(1, -1)
    }
  }
  return trimmed
}

function isConcreteAlias(pattern: string): boolean {
  return Boolean(pattern) && !pattern.startsWith('!') && !/[?*]/.test(pattern)
}

function createAlias(pattern: string): LocalSshHostAlias {
  return { alias: pattern, identityFiles: [] }
}

function parsePatterns(value: string): string[] {
  return value
    .split(/\s+/)
    .map((part) => unquote(part))
    .filter(isConcreteAlias)
}

function asDisplayPath(path: string, homeDir: string): string {
  const absolute = resolve(path)
  const home = resolve(homeDir)
  if (absolute === home) return '~'
  if (absolute.startsWith(`${home}${sep}`)) {
    return `~/${relative(home, absolute).replace(/\\/g, '/')}`
  }
  return path.replace(/\\/g, '/')
}

function identityToDisplayPath(raw: string, homeDir: string): string {
  const value = unquote(raw)
    .replace(/%d/g, homeDir)
    .trim()
  if (!value) return ''
  if (value === '~') return '~'
  if (value.startsWith('~/') || value.startsWith('~\\')) {
    return `~/${value.slice(2).replace(/\\/g, '/')}`
  }
  if (isAbsolute(value)) return asDisplayPath(value, homeDir)
  return value.replace(/\\/g, '/')
}

function identityToFilesystemPath(displayPath: string, homeDir: string, sshDir: string): string {
  if (displayPath === '~') return homeDir
  if (displayPath.startsWith('~/')) return join(homeDir, displayPath.slice(2))
  if (isAbsolute(displayPath)) return displayPath
  return join(sshDir, displayPath)
}

async function existsFile(path: string): Promise<boolean> {
  try {
    const entry = await stat(path)
    return entry.isFile()
  } catch {
    return false
  }
}

function shouldIncludeSshDirEntry(name: string): boolean {
  if (!name || name.endsWith('.pub') || name.startsWith('.')) return false
  if (name.startsWith('known_hosts') || name.startsWith('authorized_keys')) return false
  if (NON_IDENTITY_NAMES.has(name)) return false
  if (DEFAULT_IDENTITY_NAMES.includes(name)) return true
  return !name.includes('.')
}

function addCandidate(
  candidates: Map<string, IdentityCandidate>,
  path: string,
  source: LocalSshIdentitySource,
  alias?: string
): void {
  if (!path) return
  const existing = candidates.get(path)
  if (existing) {
    if (existing.source !== 'config' && source === 'config') existing.source = source
    if (alias) existing.aliases.add(alias)
    return
  }
  candidates.set(path, {
    path,
    source,
    aliases: alias ? new Set([alias]) : new Set()
  })
}

export function parseSshConfig(text: string, homeDir: string = homedir()): LocalSshHostAlias[] {
  const aliases = new Map<string, LocalSshHostAlias>()
  let current: LocalSshHostAlias[] = []

  for (const rawLine of text.split(/\r?\n/)) {
    const line = stripInlineComment(rawLine)
    if (!line) continue
    const directive = splitDirective(line)
    if (!directive) continue

    if (directive.key === 'host') {
      current = parsePatterns(directive.value).map((pattern) => {
        const existing = aliases.get(pattern)
        if (existing) return existing
        const alias = createAlias(pattern)
        aliases.set(pattern, alias)
        return alias
      })
      continue
    }

    if (current.length === 0) continue

    if (directive.key === 'hostname') {
      const value = unquote(directive.value)
      current.forEach((alias) => {
        alias.hostName = value
      })
    } else if (directive.key === 'user') {
      const value = unquote(directive.value)
      current.forEach((alias) => {
        alias.user = value
      })
    } else if (directive.key === 'port') {
      const value = unquote(directive.value)
      current.forEach((alias) => {
        alias.port = value
      })
    } else if (directive.key === 'identityfile') {
      const value = identityToDisplayPath(directive.value, homeDir)
      if (!value) continue
      current.forEach((alias) => {
        if (!alias.identityFiles.includes(value)) alias.identityFiles.push(value)
      })
    }
  }

  return [...aliases.values()].sort((a, b) => a.alias.localeCompare(b.alias, undefined, { sensitivity: 'base' }))
}

export async function inspectLocalSshConfig(homeDir: string = homedir()): Promise<LocalSshConfigInfo> {
  const sshDir = join(homeDir, '.ssh')
  const configPath = join(sshDir, 'config')
  const candidates = new Map<string, IdentityCandidate>()
  let configExists = false
  let aliases: LocalSshHostAlias[] = []
  let error: string | undefined

  try {
    const configText = await readFile(configPath, 'utf8')
    configExists = true
    aliases = parseSshConfig(configText, homeDir)
  } catch (err) {
    const code = err && typeof err === 'object' && 'code' in err ? String((err as { code?: unknown }).code) : ''
    if (code !== 'ENOENT') error = err instanceof Error ? err.message : String(err)
  }

  for (const alias of aliases) {
    for (const identity of alias.identityFiles) {
      addCandidate(candidates, identity, 'config', alias.alias)
    }
  }

  try {
    const entries = await readdir(sshDir, { withFileTypes: true })
    for (const entry of entries) {
      if (!entry.isFile() || !shouldIncludeSshDirEntry(entry.name)) continue
      const display = asDisplayPath(join(sshDir, entry.name), homeDir)
      addCandidate(candidates, display, 'default')
    }
  } catch (err) {
    const code = err && typeof err === 'object' && 'code' in err ? String((err as { code?: unknown }).code) : ''
    if (code !== 'ENOENT' && !error) error = err instanceof Error ? err.message : String(err)
  }

  const identities: LocalSshIdentity[] = []
  for (const candidate of candidates.values()) {
    const fsPath = identityToFilesystemPath(candidate.path, homeDir, sshDir)
    identities.push({
      path: candidate.path,
      source: candidate.source,
      exists: await existsFile(fsPath),
      hostAliases: [...candidate.aliases].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: 'base' }))
    })
  }

  identities.sort((a, b) => {
    if (a.exists !== b.exists) return a.exists ? -1 : 1
    if (a.source !== b.source) return a.source === 'config' ? -1 : 1
    return a.path.localeCompare(b.path, undefined, { sensitivity: 'base' })
  })

  return {
    sshDir,
    configPath,
    configExists,
    agentAvailable: Boolean(process.env.SSH_AUTH_SOCK),
    aliases,
    identities,
    ...(error ? { error } : {})
  }
}
