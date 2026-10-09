export interface LinkNavigationHint {
  line?: number
  endLine?: number
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
const LEADING_SLASH_DRIVE_RE = /^\/[A-Za-z]:[\\/]/
const UNC_PATH_RE = /^(?:\\\\|\/\/)[^\\/]+[\\/]/
const SCHEME_RE = /^[A-Za-z][A-Za-z0-9+.-]*:/
const LINE_HINT_RE = /^(.*?):(\d+)(?::(\d+)|-(\d+))?$/
const LINE_FRAGMENT_RE = /^L(\d+)(?:C(\d+))?(?:-L?(\d+)(?:C(\d+))?)?$/

function parseLineLocation(
  lineText: string,
  columnText?: string,
  endLineText?: string,
  endColumnText?: string
): LinkNavigationHint | undefined {
  const values = [lineText, columnText, endLineText, endColumnText]
    .filter((value) => value !== undefined).map(Number)
  if (values.some((value) => !Number.isSafeInteger(value) || value <= 0)) return undefined
  const line = Number(lineText)
  const column = columnText === undefined ? undefined : Number(columnText)
  const endLine = endLineText === undefined ? undefined : Number(endLineText)
  if (endLine !== undefined) {
    if (endLine < line) return undefined
    if (endLine === line && column !== undefined && endColumnText !== undefined && Number(endColumnText) < column) return undefined
  }
  return {
    line,
    ...(column === undefined ? {} : { column }),
    ...(endLine === undefined ? {} : { endLine })
  }
}

/** Only a filename with an extension and a valid location can bypass Markdown's scheme filter. */
export function isBareFileLocationLinkTarget(target: string): boolean {
  const match = target.split(/[?#]/, 1)[0].match(LINE_HINT_RE)
  if (!match || !parseLineLocation(match[2], match[3], match[4])) return false
  try {
    const filename = decodeURIComponent(match[1])
    return /^[^\\/:?#\u0000-\u001f]+\.[A-Za-z0-9][A-Za-z0-9._-]*$/.test(filename)
  } catch {
    return false
  }
}

function normalizeSlashes(value: string): string {
  return value.replace(/\\/g, '/')
}

/** Markdown renderers percent-encode hrefs; local paths are decoded back, URLs are left alone. */
function decodeLocalPathTarget(target: string): string {
  const isUrl = SCHEME_RE.test(target) && !/^[A-Za-z]:/.test(target)
  if (isUrl || !target.includes('%')) return target
  try {
    return decodeURIComponent(target)
  } catch {
    return target
  }
}

function simplifyPathSegments(value: string): string {
  const normalized = normalizeSlashes(value)
  if (normalized.startsWith('//')) {
    return `//${simplifyPathSegments(normalized.slice(2)).replace(/^\/+/, '')}`
  }
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

  let location: LinkNavigationHint | undefined
  const lineMatch = pathLike.match(LINE_HINT_RE)
  if (lineMatch && !/^[A-Za-z]:$/.test(lineMatch[1] ?? '')) {
    pathLike = lineMatch[1] ?? pathLike
    location = parseLineLocation(lineMatch[2], lineMatch[3], lineMatch[4])
  }

  const lineFragment = fragment?.match(LINE_FRAGMENT_RE)
  if (lineFragment && !location) {
    location = parseLineLocation(lineFragment[1], lineFragment[2], lineFragment[3], lineFragment[4])
    if (location) fragment = undefined
  }

  const hint: LinkNavigationHint = { ...location }
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
  return !hasScheme(target) &&
    !target.startsWith('/') &&
    !target.startsWith('\\') &&
    !WINDOWS_ABSOLUTE_PATH_RE.test(target)
}

function isAbsoluteLocalPathTarget(target: string): boolean {
  return WINDOWS_ABSOLUTE_PATH_RE.test(target) ||
    UNC_PATH_RE.test(target) ||
    target.startsWith('/') ||
    target.startsWith('\\')
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

  if (hasScheme(decodeLocalPathTarget(trimmed)) &&
    !trimmed.toLowerCase().startsWith('file://') &&
    !isBareFileLocationLinkTarget(trimmed)) {
    try {
      const parsed = new URL(trimmed)
      if ((parsed.protocol === 'http:' || parsed.protocol === 'https:') && /^https?:\/\//i.test(trimmed)) {
        return { kind: 'browser', url: parsed.href }
      }
      if (SAFE_EXTERNAL_SCHEMES.has(parsed.protocol)) {
        return { kind: 'external', url: parsed.href }
      }
      return { kind: 'reject', reason: 'unsupported-scheme' }
    } catch {
      return { kind: 'reject', reason: 'malformed' }
    }
  }

  const { pathLikeTarget, hint } = splitDecorations(trimmed)
  const local = decodeLocalPathTarget(pathLikeTarget)

  if (isRelativePathTarget(local)) {
    const baseDir = params.sourceContextDir?.trim() || params.workspacePath
    return {
      kind: 'file',
      absolutePath: joinBaseAndRelative(baseDir, local),
      ...(hint ? { hint } : {})
    }
  }

  if (isAbsoluteLocalPathTarget(local)) {
    const withoutDriveSlash = LEADING_SLASH_DRIVE_RE.test(local)
      ? local.slice(1)
      : local
    return {
      kind: 'file',
      absolutePath: simplifyPathSegments(withoutDriveSlash),
      ...(hint ? { hint } : {})
    }
  }
  if (trimmed.toLowerCase().startsWith('file://')) {
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

  return { kind: 'reject', reason: 'malformed' }
}
