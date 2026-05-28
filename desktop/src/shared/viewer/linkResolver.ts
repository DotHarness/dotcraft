export interface LinkNavigationHint {
  line?: number
  column?: number
  fragment?: string
  query?: string
}

export type LinkRejectReason =
  | 'empty'
  | 'malformed'
  | 'unsupported-scheme'

export type ConversationLinkResolution =
  | {
      kind: 'file'
      absolutePath: string
      hint?: LinkNavigationHint
    }
  | {
      kind: 'browser'
      url: string
    }
  | {
      kind: 'external'
      url: string
    }
  | {
      kind: 'reject'
      reason: LinkRejectReason
    }

const SAFE_EXTERNAL_SCHEMES = new Set(['mailto:', 'tel:'])

const WINDOWS_ABSOLUTE_PATH_RE = /^[A-Za-z]:[\\/].+/
const SCHEME_RE = /^[A-Za-z][A-Za-z0-9+.-]*:/
const LINE_HINT_RE = /^(.*?):(\d+)(?::(\d+))?$/

function normalizeSlashes(value: string): string {
  return value.replace(/\\/g, '/')
}

function simplifyPathSegments(value: string): string {
  const normalized = normalizeSlashes(value)
  const driveMatch = normalized.match(/^[A-Za-z]:/)
  const prefix = driveMatch ? `${driveMatch[0]}/` : normalized.startsWith('/') ? '/' : ''
  const withoutPrefix = driveMatch
    ? normalized.slice(driveMatch[0].length).replace(/^\/+/, '')
    : normalized.replace(/^\/+/, '')
  const parts = withoutPrefix.split('/').filter((part) => part.length > 0)
  const stack: string[] = []
  for (const part of parts) {
    if (part === '.') continue
    if (part === '..') {
      if (stack.length > 0 && stack[stack.length - 1] !== '..') {
        stack.pop()
      } else if (!prefix) {
        stack.push(part)
      }
      continue
    }
    stack.push(part)
  }
  return `${prefix}${stack.join('/')}` || prefix || '.'
}

function splitDecorations(rawTarget: string): {
  pathLikeTarget: string
  hint?: LinkNavigationHint
} {
  let pathLike = rawTarget
  let fragment: string | undefined
  let query: string | undefined

  const fragmentIndex = pathLike.indexOf('#')
  if (fragmentIndex >= 0) {
    fragment = pathLike.slice(fragmentIndex + 1) || undefined
    pathLike = pathLike.slice(0, fragmentIndex)
  }

  const queryIndex = pathLike.indexOf('?')
  if (queryIndex >= 0) {
    query = pathLike.slice(queryIndex + 1) || undefined
    pathLike = pathLike.slice(0, queryIndex)
  }

  let line: number | undefined
  let column: number | undefined
  const lineMatch = pathLike.match(LINE_HINT_RE)
  if (lineMatch && !/^[A-Za-z]:$/.test(lineMatch[1] ?? '')) {
    pathLike = lineMatch[1] ?? pathLike
    line = Number(lineMatch[2])
    column = lineMatch[3] ? Number(lineMatch[3]) : undefined
  }

  const hint: LinkNavigationHint = {}
  if (line !== undefined) hint.line = line
  if (column !== undefined) hint.column = column
  if (fragment !== undefined) hint.fragment = fragment
  if (query !== undefined) hint.query = query
  return {
    pathLikeTarget: pathLike,
    hint: Object.keys(hint).length > 0 ? hint : undefined
  }
}

function resolveFileUrlToPath(target: string): string | null {
  try {
    const parsed = new URL(target)
    if (parsed.protocol !== 'file:') return null
    if (parsed.host && parsed.host !== 'localhost') return null
    const decoded = decodeURIComponent(parsed.pathname)
    const windowsPath = decoded.match(/^\/[A-Za-z]:\//) ? decoded.slice(1) : decoded
    return simplifyPathSegments(windowsPath)
  } catch {
    return null
  }
}

function joinBaseAndRelative(baseDir: string, relative: string): string {
  if (!baseDir) return simplifyPathSegments(relative)
  const joined = `${normalizeSlashes(baseDir).replace(/\/+$/, '')}/${normalizeSlashes(relative)}`
  return simplifyPathSegments(joined)
}

function hasScheme(target: string): boolean {
  if (WINDOWS_ABSOLUTE_PATH_RE.test(target)) return false
  return SCHEME_RE.test(target)
}

function isRelativePathTarget(target: string): boolean {
  if (target.startsWith('./') || target.startsWith('../')) return true
  return !hasScheme(target) && !target.startsWith('/') && !WINDOWS_ABSOLUTE_PATH_RE.test(target)
}

export function normalizeBrowserUrl(url: string): string {
  const trimmed = url.trim()
  if (!trimmed) return ''
  try {
    const parsed = new URL(trimmed)
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      return trimmed
    }
    const protocol = parsed.protocol.toLowerCase()
    const hostname = parsed.hostname.toLowerCase()
    const port = parsed.port ? `:${parsed.port}` : ''
    const path = parsed.pathname === '/' ? '' : parsed.pathname
    return `${protocol}//${hostname}${port}${path}${parsed.search}`
  } catch {
    return trimmed
  }
}

export function resolveConversationLink(params: {
  target: string
  workspacePath: string
  sourceContextDir?: string
}): ConversationLinkResolution {
  const trimmed = params.target.trim()
  if (!trimmed) {
    return { kind: 'reject', reason: 'empty' }
  }

  // Rule 1: relative path
  if (isRelativePathTarget(trimmed)) {
    const { pathLikeTarget, hint } = splitDecorations(trimmed)
    const baseDir = params.sourceContextDir?.trim() || params.workspacePath
    return {
      kind: 'file',
      absolutePath: joinBaseAndRelative(baseDir, pathLikeTarget),
      ...(hint ? { hint } : {})
    }
  }

  // Rule 2: absolute local path or file:// URL
  if (WINDOWS_ABSOLUTE_PATH_RE.test(trimmed) || trimmed.startsWith('/')) {
    const { pathLikeTarget, hint } = splitDecorations(trimmed)
    return {
      kind: 'file',
      absolutePath: simplifyPathSegments(pathLikeTarget),
      ...(hint ? { hint } : {})
    }
  }
  if (trimmed.toLowerCase().startsWith('file://')) {
    const { pathLikeTarget, hint } = splitDecorations(trimmed)
    const localPath = resolveFileUrlToPath(pathLikeTarget)
    if (!localPath) {
      return { kind: 'reject', reason: 'malformed' }
    }
    return {
      kind: 'file',
      absolutePath: localPath,
      ...(hint ? { hint } : {})
    }
  }

  // Rule 3: http(s)
  if (trimmed.toLowerCase().startsWith('http://') || trimmed.toLowerCase().startsWith('https://')) {
    try {
      const parsed = new URL(trimmed)
      return { kind: 'browser', url: parsed.href }
    } catch {
      return { kind: 'reject', reason: 'malformed' }
    }
  }

  // Rule 4: well-known external schemes
  if (hasScheme(trimmed)) {
    try {
      const parsed = new URL(trimmed)
      if (SAFE_EXTERNAL_SCHEMES.has(parsed.protocol)) {
        return { kind: 'external', url: parsed.href }
      }
      return { kind: 'reject', reason: 'unsupported-scheme' }
    } catch {
      return { kind: 'reject', reason: 'malformed' }
    }
  }

  // Fallback for malformed targets.
  return { kind: 'reject', reason: 'malformed' }
}
