export function isRemoteProjectKey(value: string | null | undefined): boolean {
  return (value ?? '').trim().toLowerCase().startsWith('remote:')
}

export function normalizeWorkspaceProjectKey(value: string | null | undefined): string {
  const trimmed = (value ?? '').trim()
  if (!trimmed) return ''
  if (isRemoteProjectKey(trimmed)) return trimmed
  return normalizeLocalWorkspaceProjectKey(trimmed)
}

export function sameWorkspaceProjectKey(
  left: string | null | undefined,
  right: string | null | undefined
): boolean {
  const leftKey = normalizeWorkspaceProjectKey(left)
  const rightKey = normalizeWorkspaceProjectKey(right)
  return Boolean(leftKey && rightKey && leftKey === rightKey)
}

function normalizeLocalWorkspaceProjectKey(value: string): string {
  const slashPath = value.replace(/\\/g, '/')
  const { prefix, rest } = splitPathPrefix(slashPath)
  const segments: string[] = []

  for (const rawSegment of rest.split('/')) {
    const segment = rawSegment.trim()
    if (!segment || segment === '.') continue
    if (segment === '..') {
      if (segments.length > 0 && segments[segments.length - 1] !== '..') {
        segments.pop()
      } else if (!prefix) {
        segments.push(segment)
      }
      continue
    }
    segments.push(segment)
  }

  const body = segments.join('/')
  const combined = combinePathPrefix(prefix, body)
  return stripTrailingSlash(combined).toLowerCase()
}

function splitPathPrefix(path: string): { prefix: string; rest: string } {
  const drive = /^([A-Za-z]:)(?:\/|$)/u.exec(path)
  if (drive) {
    return {
      prefix: drive[1],
      rest: path.slice(drive[0].length)
    }
  }

  if (path.startsWith('//')) {
    return { prefix: '//', rest: path.slice(2) }
  }

  if (path.startsWith('/')) {
    return { prefix: '/', rest: path.slice(1) }
  }

  return { prefix: '', rest: path }
}

function combinePathPrefix(prefix: string, body: string): string {
  if (!prefix) return body
  if (prefix === '/') return body ? `/${body}` : '/'
  if (prefix === '//') return body ? `//${body}` : '//'
  return body ? `${prefix}/${body}` : prefix
}

function stripTrailingSlash(path: string): string {
  if (path === '/' || path === '//' || /^[A-Za-z]:$/u.test(path)) return path
  return path.replace(/\/+$/u, '')
}
