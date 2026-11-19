import { memoryName } from './memoryFile'

export const INDEX_FILE = 'MEMORY.md'
export const INDEX_MAX_LINES = 200
export const INDEX_MAX_BYTES = 25_000
export const INDEX_MAX_POINTER_LENGTH = 200

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

function sameFile(a: string, b: string): boolean {
  return a.toLowerCase() === b.toLowerCase()
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
  const lookup = change.previousFile ?? change.file
  const at = lines.findIndex((line) => {
    const pointer = readPointer(line)
    return pointer != null && sameFile(pointer.target, lookup)
  })
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
  return joinLines(splitLines(indexText).filter((line) => {
    const pointer = readPointer(line)
    return pointer == null || !sameFile(pointer.target, file)
  }))
}

export interface IndexIssueLine {
  lineNumber: number
  text: string
}

export interface IndexHealth {
  lines: number
  bytes: number
  overLines: boolean
  overBytes: boolean
  nonPointerLines: IndexIssueLine[]
  longPointerLines: IndexIssueLine[]
  /** Memory files the index has no line for. */
  missing: string[]
  /** Pointer targets with no memory file. */
  dangling: IndexIssueLine[]
}

/** Measures the index as Core loads it: trimmed, CRLF folded, lines and UTF-8 bytes. */
export function analyzeIndex(indexText: string | null, memoryFiles: readonly string[]): IndexHealth {
  const trimmed = (indexText ?? '').replace(/\r\n/g, '\n').trim()
  const lines = trimmed ? trimmed.split('\n') : []
  const targets = new Set<string>()
  const health: IndexHealth = {
    lines: lines.length,
    bytes: new TextEncoder().encode(trimmed).length,
    overLines: lines.length > INDEX_MAX_LINES,
    overBytes: false,
    nonPointerLines: [],
    longPointerLines: [],
    missing: [],
    dangling: []
  }
  health.overBytes = health.bytes > INDEX_MAX_BYTES
  const files = new Set(memoryFiles.map((file) => file.toLowerCase()))
  lines.forEach((text, index) => {
    if (!text.trim()) return
    const issue = { lineNumber: index + 1, text }
    const pointer = readPointer(text)
    if (!pointer) {
      health.nonPointerLines.push(issue)
      return
    }
    if (text.length > INDEX_MAX_POINTER_LENGTH) health.longPointerLines.push(issue)
    const target = pointer.target.toLowerCase()
    targets.add(target)
    if (!target.includes('://') && !files.has(target)) health.dangling.push(issue)
  })
  health.missing = memoryFiles.filter((file) => !targets.has(file.toLowerCase()))
  return health
}

export function removeIndexLine(indexText: string, lineNumber: number, expectedText: string): string | null {
  const lines = splitLines(indexText.replace(/^\s+/, ''))
  return lines[lineNumber - 1] === expectedText ? joinLines(lines.filter((_, index) => index !== lineNumber - 1)) : null
}
