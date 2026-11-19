/** Paths are the AppServer host's, which may use either separator regardless of this machine. */
export function hostSeparator(path: string): '/' | '\\' {
  return path.includes('\\') && !path.includes('/') ? '\\' : '/'
}

export function joinHostPath(directory: string, name: string): string {
  const separator = hostSeparator(directory)
  return directory.replace(/[\\/]+$/, '') + separator + name
}

export function baseName(path: string): string {
  return path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? path
}

function isAbsolute(path: string): boolean {
  return path.startsWith('/') || path.startsWith('\\') || /^[A-Za-z]:[\\/]/.test(path)
}

/** Comparable form: forward slashes, no trailing slash, case-folded because Windows hosts are case-insensitive. */
export function comparablePath(path: string, workspacePath: string | null): string {
  const absolute = isAbsolute(path) || !workspacePath ? path : joinHostPath(workspacePath, path)
  const segments: string[] = []
  for (const segment of absolute.replace(/\\/g, '/').split('/')) {
    if (segment === '.' || (segment === '' && segments.length > 0)) continue
    if (segment === '..') segments.pop()
    else segments.push(segment)
  }
  return segments.join('/').toLowerCase()
}

export function isWithin(path: string, directory: string, workspacePath: string | null): boolean {
  const target = comparablePath(path, workspacePath)
  const root = comparablePath(directory, workspacePath)
  return target.startsWith(root + '/')
}
