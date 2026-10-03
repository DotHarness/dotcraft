import { baseName } from './transcript'

type LinkTarget =
  | { kind: 'file'; path: string; label: string }
  | { kind: 'web'; url: string; label: string }
  | { kind: 'reject'; label: string }

const WINDOWS_PATH = /^[A-Za-z]:[\\/]/
const DRIVE_AFTER_SLASH = /^\/[A-Za-z]:[\\/]/
const SCHEME = /^[A-Za-z][A-Za-z0-9+.-]*:/

function decode(value: string): string {
  if (!value.includes('%')) return value
  try {
    return decodeURIComponent(value)
  } catch {
    return value
  }
}

function withoutDecorations(target: string): string {
  const path = target.replace(/[?#].*$/, '')
  const line = /^(.*?):\d+(?::\d+)?$/.exec(path)
  return line && !/^[A-Za-z]$/.test(line[1]) ? line[1] : path
}

function simplify(path: string): string {
  const normalized = path.replace(/\\/g, '/')
  const drive = /^[A-Za-z]:/.exec(normalized)?.[0]
  const prefix = drive ? `${drive}/` : normalized.startsWith('//') ? '//' : normalized.startsWith('/') ? '/' : ''
  const stack: string[] = []
  for (const part of normalized.slice(drive?.length ?? 0).split('/')) {
    if (!part || part === '.') continue
    if (part === '..' && stack.length > 0 && stack[stack.length - 1] !== '..') stack.pop()
    else if (part !== '..' || !prefix) stack.push(part)
  }
  return `${prefix}${stack.join('/')}` || prefix || '.'
}

function localPath(target: string, workspacePath: string | null): string | null {
  if (/^file:\/\//i.test(target)) {
    const rest = decode(withoutDecorations(target.replace(/^file:\/\/(localhost)?/i, '')))
    return simplify(DRIVE_AFTER_SLASH.test(rest) ? rest.slice(1) : rest)
  }
  if (SCHEME.test(target) && !WINDOWS_PATH.test(target)) return null
  const path = decode(withoutDecorations(target))
  if (DRIVE_AFTER_SLASH.test(path)) return simplify(path.slice(1))
  if (WINDOWS_PATH.test(path) || path.startsWith('/') || path.startsWith('\\')) return simplify(path)
  return simplify(workspacePath ? `${workspacePath.replace(/[\\/]+$/, '')}/${path}` : path)
}

function shortUrl(url: string): string {
  const match = /^[a-z]+:\/\/([^/?#]+)([^?#]*)/i.exec(url)
  if (!match) return url
  const path = match[2].replace(/\/+$/, '')
  if (!path) return match[1]
  return path.length <= 18 ? `${match[1]}${path}` : `${match[1]}/${path.split('/').filter(Boolean)[0] ?? ''}`
}

export function resolveLink(href: string, text: string, workspacePath: string | null): LinkTarget {
  const target = href.trim()
  const custom = text.trim() && text.trim() !== target ? text.trim() : null
  if (!target) return { kind: 'reject', label: text }
  if (/^(https?|mailto|tel):/i.test(target)) return { kind: 'web', url: target, label: custom ?? shortUrl(target) }
  const path = localPath(target, workspacePath)
  if (path) return { kind: 'file', path, label: custom ?? baseName(path) }
  return { kind: 'reject', label: custom ?? target }
}

export function workspaceFile(path: string, workspacePath: string | null): string {
  return localPath(path, workspacePath) ?? path
}
