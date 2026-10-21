import { buildViewerUrlRenderer } from '../../../utils/viewerUrl'

/** Resolves a Markdown image reference for the preview, or null when it must not load (remote, script, or empty). */
export function resolveMarkdownImageSource(reference: string, documentPath: string): string | null {
  const target = reference.trim().replace(/^<(.*)>$/, '$1').trim()
  if (!target) return null
  if (/^data:image\//i.test(target)) return target
  if (/^file:/i.test(target)) return localSource(decodePath(target.replace(/^file:\/*/i, '/')))
  if (/^[a-z][a-z0-9+.-]*:/i.test(target) && !/^[a-z]:[\\/]/i.test(target)) return null
  const path = decodePath(target.split(/[?#]/, 1)[0]!)
  if (/^([a-z]:)?[\\/]/i.test(path)) return localSource(path)
  return localSource(joinPath(parentFolder(documentPath), path))
}

function localSource(path: string): string {
  return buildViewerUrlRenderer(normalizePath(path.replace(/^\/([a-z]:)/i, '$1')))
}

function decodePath(value: string): string {
  try {
    return decodeURI(value)
  } catch {
    return value
  }
}

function parentFolder(path: string): string {
  const normalized = path.replace(/\\/g, '/')
  const index = normalized.lastIndexOf('/')
  return index < 0 ? '' : normalized.slice(0, index)
}

function joinPath(folder: string, relative: string): string {
  return folder ? `${folder}/${relative}` : relative
}

function normalizePath(path: string): string {
  const normalized = path.replace(/\\/g, '/')
  const leading = normalized.startsWith('/') ? '/' : ''
  const parts: string[] = []
  for (const part of normalized.split('/')) {
    if (!part || part === '.') continue
    if (part === '..' && parts.length > 0 && parts[parts.length - 1] !== '..') parts.pop()
    else parts.push(part)
  }
  return leading + parts.join('/')
}
