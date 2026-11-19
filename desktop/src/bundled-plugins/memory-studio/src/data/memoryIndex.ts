import { memoryName } from './memoryFile'

export const INDEX_FILE = 'MEMORY.md'
export const INDEX_MAX_LINES = 200
export const INDEX_MAX_BYTES = 25_000
const INDEX_MAX_POINTER_LENGTH = 200

/** Core's pointer grammar for an index line. */
const POINTER = /^\s*[-*+]\s+\[([^\]]*)\]\(([^)\s]+)\)(.*)$/

export interface IndexPointer {
  title: string
  target: string
  hook: string
}

export function readPointer(line: string): IndexPointer | null {
  const match = POINTER.exec(line)
  if (!match) return null
  const target = match[2].replace(/\\/g, '/').replace(/^\.\//, '')
  const hook = match[3].replace(/^\s*(?:[—–-]+|:)\s*/, '').trim()
  return { title: match[1], target, hook }
}

export function formatPointer({ title, target, hook }: IndexPointer): string {
  return hook ? `- [${title}](${target}) — ${hook}` : `- [${title}](${target})`
}

export function defaultTitle(fileName: string): string {
  const words = memoryName(fileName).replace(/[-_.]+/g, ' ').trim()
  return words ? words[0].toUpperCase() + words.slice(1) : fileName
}

/** Falls back to a case-insensitive match only when no line matches exactly, so `Role.md` and `role.md` stay separate. */
function pointerLines(lines: readonly string[], file: string): number[] {
  const targets = lines.map((line) => readPointer(line)?.target)
  const exact = targets.flatMap((target, index) => target === file ? [index] : [])
  return exact.length > 0 ? exact : targets.flatMap((target, index) => target?.toLowerCase() === file.toLowerCase() ? [index] : [])
}

function splitLines(text: string): string[] {
  return text.replace(/\r\n/g, '\n').split('\n')
}

function joinLines(lines: readonly string[]): string {
  const body = lines.join('\n').replace(/\s+$/, '')
  return body ? `${body}\n` : ''
}

export interface IndexEntryChange {
  /** The entry's file before the save; absent for a new entry. */
  previousFile?: string
  file: string
  /** The description before the save, so a hook that followed it keeps following it. */
  previousDescription?: string
  description: string
}

/** Adds or updates the entry's pointer line, renaming it with the file and keeping a user-written title and hook. */
export function upsertIndexEntry(indexText: string, change: IndexEntryChange): string {
  const lines = splitLines(indexText)
  const at = pointerLines(lines, change.previousFile ?? change.file)[0] ?? -1
  if (at < 0) {
    const line = formatPointer({ title: defaultTitle(change.file), target: change.file, hook: change.description.trim() })
    while (lines.length > 0 && lines[lines.length - 1].trim() === '') lines.pop()
    return joinLines([...lines, line])
  }
  const pointer = readPointer(lines[at])!
  const hookFollowsDescription = !pointer.hook || pointer.hook === change.previousDescription?.trim()
  const next = [...lines]
  next[at] = formatPointer({
    title: pointer.title,
    target: change.file,
    hook: hookFollowsDescription ? change.description.trim() : pointer.hook
  })
  return joinLines(next)
}

export function removeIndexEntry(indexText: string, file: string): string {
  const lines = splitLines(indexText)
  const remove = new Set(pointerLines(lines, file))
  return joinLines(lines.filter((_, index) => !remove.has(index)))
}

export type IndexLoad = 'ok' | 'near' | 'over'

export interface IndexUsage {
  lines: number
  bytes: number
  load: IndexLoad
}

/** Measures the index as Core loads it: trimmed, CRLF folded, lines and UTF-8 bytes, near past 80% of a limit. */
export function measureIndex(indexText: string | null): IndexUsage {
  const trimmed = (indexText ?? '').replace(/\r\n/g, '\n').trim()
  const lines = trimmed ? trimmed.split('\n').length : 0
  const bytes = new TextEncoder().encode(trimmed).length
  const load: IndexLoad = lines > INDEX_MAX_LINES || bytes > INDEX_MAX_BYTES
    ? 'over'
    : lines * 5 > INDEX_MAX_LINES * 4 || bytes * 5 > INDEX_MAX_BYTES * 4 ? 'near' : 'ok'
  return { lines, bytes, load }
}

const LINK_TARGET = /\]\(([^)\s]+)\)/g

function decodeTarget(target: string): string {
  try {
    return decodeURIComponent(target)
  } catch {
    return target
  }
}

function normalizeTarget(target: string): string {
  return decodeTarget(target).replace(/\\/g, '/').replace(/^\.\//, '').toLowerCase()
}

function isMemoryFileTarget(target: string): boolean {
  return /^[^/:]+\.md$/i.test(target)
}

function newPointer(file: string, description: string): string | null {
  const title = defaultTitle(file)
  const room = INDEX_MAX_POINTER_LENGTH - formatPointer({ title, target: file, hook: 'x' }).length + 1
  const hook = description.trim().length > room ? `${description.trim().slice(0, Math.max(room - 1, 0)).trimEnd()}…` : description.trim()
  const line = formatPointer({ title, target: file, hook })
  return readPointer(line)?.target === file && line.length <= INDEX_MAX_POINTER_LENGTH ? line : null
}

export function syncIndex(indexText: string | null, files: readonly string[], descriptions: ReadonlyMap<string, string>): string | null {
  const present = new Set(files.map((file) => file.toLowerCase()))
  const lines = splitLines(indexText ?? '')
  const kept = lines.filter((line) => {
    const target = readPointer(line)?.target
    return target == null || !isMemoryFileTarget(target) || present.has(normalizeTarget(target))
  })
  const linked = new Set(kept.flatMap((line) => [...line.matchAll(LINK_TARGET)].map((match) => normalizeTarget(match[1]))))
  const added = files
    .filter((file) => !linked.has(file.toLowerCase()))
    .flatMap((file) => newPointer(file, descriptions.get(file) ?? '') ?? [])
  if (kept.length === lines.length && added.length === 0) return null
  while (kept.length > 0 && kept[kept.length - 1].trim() === '') kept.pop()
  return joinLines([...kept, ...added])
}
