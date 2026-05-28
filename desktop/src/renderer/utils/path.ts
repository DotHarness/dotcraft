/**
 * Browser-safe path utilities (Node's `path` module is not available in renderer).
 */

/**
 * Returns the last segment of a file/directory path.
 * Works with both forward slashes and backslashes.
 */
export function basename(p: string): string {
  const normalized = p.replace(/\\/g, '/')
  const parts = normalized.split('/').filter(Boolean)
  return parts[parts.length - 1] ?? p
}

/**
 * Directory portion of a file path (browser-safe).
 */
export function dirname(p: string): string {
  const normalized = p.replace(/\\/g, '/').replace(/\/+$/, '')
  const idx = normalized.lastIndexOf('/')
  if (idx <= 0) return normalized.startsWith('/') ? '/' : '.'
  return normalized.slice(0, idx) || '/'
}
