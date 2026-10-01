export const REDACTION_MASK = '[redacted]'

export function generateId(prefix: string): string {
  const uuid =
    (typeof globalThis !== 'undefined' && globalThis.crypto?.randomUUID?.()) ||
    `${Date.now().toString(16)}${Math.floor(Math.random() * 0xffffffff).toString(16)}`
  return `${prefix}_${uuid.replace(/-/g, '').slice(0, 24)}`
}

export function isValidPort(port: unknown): boolean {
  return typeof port === 'number' && Number.isInteger(port) && port >= 1 && port <= 65535
}

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

function hasControlCharacter(value: string): boolean {
  for (let i = 0; i < value.length; i += 1) {
    const code = value.charCodeAt(i)
    if (code < 0x20 || code === 0x7f) return true
  }
  return false
}

export function isValidRemoteFolderPath(p: unknown): boolean {
  if (typeof p !== 'string') return false
  if (!p || p.length > 4096 || p !== p.trim() || hasControlCharacter(p)) return false
  if (!(p === '~' || p.startsWith('~/') || p.startsWith('/'))) return false
  const segments = p.split('/')
  return !segments.includes('..') && !segments.includes('.')
}

export function isAbsoluteRemoteFolderPath(p: unknown): p is string {
  return isValidRemoteFolderPath(p) && (p as string).startsWith('/')
}

export function shellSingleQuote(value: string): string {
  return `'${value.replace(/'/g, `'\\''`)}'`
}

export function quoteRemotePath(p: string): string {
  const t = p.trim()
  if (t === '~') return '~'
  if (t.startsWith('~/')) return `~/${shellSingleQuote(t.slice(2))}`
  return shellSingleQuote(t)
}

export function remoteChildPath(base: string, child: string): string {
  return `${base.replace(/\/+$/, '')}/${child.replace(/^\/+/, '')}`
}

export function remoteBaseName(path: string): string {
  return path.replace(/\/+$/, '').split('/').filter(Boolean).pop() ?? ''
}

export function normalizeRemoteFolderPath(path: string): string {
  const collapsed = path.replace(/\/{2,}/g, '/')
  return collapsed.length > 1 ? collapsed.replace(/\/+$/, '') : collapsed
}

const SECRET_KEY_RE =
  /\b([A-Za-z0-9_]*(?:TOKEN|SECRET|PASSWORD|PASSWD|API_?KEY|AES_KEY|KEY))\b(\s*[=:]\s*)(["']?)([^\s"'#]+)\3/gi
const TOKEN_QUERY_RE = /([?&]token=)([^&\s"']+)/gi
const BEARER_RE = /(Bearer\s+)([A-Za-z0-9._~+/=-]{4,})/g

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
  out = out.replace(BEARER_RE, (_m, prefix) => `${prefix}${REDACTION_MASK}`)
  return out
}

export function firstLine(text: string): string {
  return (text || '')
    .split('\n')
    .map((l) => l.trim())
    .filter(Boolean)[0] ?? ''
}
