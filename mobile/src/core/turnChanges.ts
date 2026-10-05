import { isComplete, type ChatHistory } from './history'
import { decodeUtf8, encodeUtf8 } from './utf8'

export interface DiffLine {
  kind: 'add' | 'remove' | 'context'
  text: string
  oldLine: number | null
  newLine: number | null
}

export interface DiffHunk {
  header: string
  lines: DiffLine[]
}

export interface FileChange {
  path: string
  added: number
  removed: number
  hunks: DiffHunk[]
  truncated: boolean
}

export interface TurnChanges {
  turnId: string
  files: FileChange[]
  added: number
  removed: number
}

const GIT_HEADER = 'diff --git '
const DEV_NULL = '/dev/null'
const HUNK = /^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/
const ESCAPES: Record<string, number> = { a: 7, b: 8, t: 9, n: 10, v: 11, f: 12, r: 13, '"': 34, '\\': 92 }

function decodeName(token: string): string {
  if (!(token.length > 1 && token.startsWith('"') && token.endsWith('"'))) return token
  const inner = token.slice(1, -1)
  const bytes: number[] = []
  for (let index = 0; index < inner.length; ) {
    if (inner[index] === '\\') {
      const octal = /^[0-7]{3}/.exec(inner.slice(index + 1, index + 4))
      if (octal) {
        bytes.push(parseInt(octal[0], 8))
        index += 4
        continue
      }
      const escaped = ESCAPES[inner[index + 1]]
      if (escaped !== undefined) {
        bytes.push(escaped)
        index += 2
        continue
      }
    }
    const char = String.fromCodePoint(inner.codePointAt(index)!)
    bytes.push(...encodeUtf8(char))
    index += char.length
  }
  try {
    return decodeUtf8(bytes)
  } catch {
    return inner
  }
}

function headerName(value: string): string {
  const quoted = value.startsWith('"') ? /^"(?:[^"\\]|\\.)*"/.exec(value)?.[0] : undefined
  const token = quoted ?? (value.includes('\t') ? value.slice(0, value.indexOf('\t')) : value)
  return decodeName(token.trimEnd())
}

function gitHeaderName(value: string): string | null {
  if (value.startsWith('"')) {
    const quoted = /^"(?:[^"\\]|\\.)*" (.+)$/.exec(value)
    return quoted ? decodeName(quoted[1]) : null
  }
  const split = value.indexOf(' b/')
  return split < 0 ? null : value.slice(split + 1)
}

function withoutSide(name: string): string {
  return /^[ab]\//.test(name) ? name.slice(2) : name
}

// Windows hosts compare paths without case; other hosts keep files that differ only by case apart.
function pathKey(path: string, workspacePath: string | null): string {
  return workspacePath && /^([A-Za-z]:|\\\\)/.test(workspacePath) ? path.toLowerCase() : path
}

export function displayPath(path: string, workspacePath: string | null): string {
  const normalized = path.replace(/\\/g, '/')
  if (!workspacePath) return normalized
  const root = workspacePath.replace(/\\/g, '/').replace(/\/+$/, '')
  return pathKey(normalized, workspacePath).startsWith(pathKey(`${root}/`, workspacePath)) ? normalized.slice(root.length + 1) : normalized
}

function chunks(text: string): string[][] {
  const out: string[][] = []
  for (const raw of text.split('\n')) {
    const line = raw.endsWith('\r') ? raw.slice(0, -1) : raw
    if (line.startsWith(GIT_HEADER) || out.length === 0) out.push([])
    out[out.length - 1].push(line)
  }
  return out
}

function parseChunk(lines: string[]): FileChange | null {
  let gitName: string | null = null
  let oldName: string | null = null
  let newName: string | null = null
  const hunks: DiffHunk[] = []
  let hunk: DiffHunk | null = null
  let oldLeft = 0
  let newLeft = 0
  let oldLine = 0
  let newLine = 0
  let added = 0
  let removed = 0
  for (const line of lines) {
    if (hunk && (oldLeft > 0 || newLeft > 0)) {
      const marker = line[0]
      if (marker === '\\') continue
      if (marker === '+') {
        hunk.lines.push({ kind: 'add', text: line.slice(1), oldLine: null, newLine: newLine++ })
        newLeft -= 1
        added += 1
      } else if (marker === '-') {
        hunk.lines.push({ kind: 'remove', text: line.slice(1), oldLine: oldLine++, newLine: null })
        oldLeft -= 1
        removed += 1
      } else {
        hunk.lines.push({ kind: 'context', text: line.slice(1), oldLine: oldLine++, newLine: newLine++ })
        oldLeft -= 1
        newLeft -= 1
      }
      continue
    }
    const range = HUNK.exec(line)
    if (range) {
      hunk = { header: line, lines: [] }
      hunks.push(hunk)
      oldLine = Number(range[1])
      oldLeft = range[2] === undefined ? 1 : Number(range[2])
      newLine = Number(range[3])
      newLeft = range[4] === undefined ? 1 : Number(range[4])
    } else if (hunks.length === 0 && line.startsWith(GIT_HEADER)) {
      gitName = gitHeaderName(line.slice(GIT_HEADER.length))
    } else if (hunks.length === 0 && line.startsWith('--- ')) {
      oldName = headerName(line.slice(4))
    } else if (hunks.length === 0 && line.startsWith('+++ ')) {
      newName = headerName(line.slice(4))
    }
  }
  const name = [newName, oldName].find((candidate) => candidate && candidate !== DEV_NULL) ?? gitName
  if (!name) return null
  return { path: withoutSide(name), added, removed, hunks, truncated: false }
}

export function parseUnifiedDiff(text: string): FileChange[] {
  return chunks(text).flatMap((lines) => parseChunk(lines) ?? [])
}

function mergeByPath(files: FileChange[], workspacePath: string | null): FileChange[] {
  const merged = new Map<string, FileChange>()
  for (const file of files) {
    const path = displayPath(file.path, workspacePath)
    const key = pathKey(path, workspacePath)
    const existing = merged.get(key)
    merged.set(
      key,
      existing
        ? {
            path: existing.path,
            added: existing.added + file.added,
            removed: existing.removed + file.removed,
            hunks: [...existing.hunks, ...file.hunks],
            truncated: existing.truncated || file.truncated,
          }
        : { ...file, path },
    )
  }
  return [...merged.values()]
}

interface FileChangeEntry {
  path?: unknown
  diff?: unknown
  additions?: unknown
  deletions?: unknown
  truncated?: unknown
}

function lineCount(value: unknown): number {
  return typeof value === 'number' && Number.isFinite(value) ? Math.max(0, value) : 0
}

function recordedChanges(history: ChatHistory, turnId: string): FileChange[] {
  return history.items.flatMap((item) => {
    if (item.turnId !== turnId || item.type !== 'toolResult') return []
    const content = item.payload.structuredContent as { kind?: string; changes?: FileChangeEntry[] } | undefined
    if (content?.kind !== 'fileChange' || !Array.isArray(content.changes)) return []
    return content.changes.flatMap((entry): FileChange[] => {
      if (typeof entry.path !== 'string' || !entry.path) return []
      const parsed = typeof entry.diff === 'string' && entry.diff ? parseUnifiedDiff(entry.diff)[0] : undefined
      const added = lineCount(entry.additions)
      const removed = lineCount(entry.deletions)
      const hunks = parsed?.hunks ?? []
      return [{ path: entry.path, added, removed, hunks, truncated: entry.truncated === true || (hunks.length === 0 && added + removed > 0) }]
    })
  })
}

export function turnChanges(history: ChatHistory, turnId: string, workspacePath: string | null): TurnChanges | null {
  const live = history.diffs?.[turnId]
  const parsed = live ? parseUnifiedDiff(live) : []
  const files = mergeByPath(parsed.length > 0 ? parsed : recordedChanges(history, turnId), workspacePath)
  if (files.length === 0) return null
  return {
    turnId,
    files,
    added: files.reduce((sum, file) => sum + file.added, 0),
    removed: files.reduce((sum, file) => sum + file.removed, 0),
  }
}

export function runningTurnChanges(history: ChatHistory, workspacePath: string | null): TurnChanges | null {
  const turn = history.turns.at(-1)
  return turn && !isComplete(turn) ? turnChanges(history, turn.id, workspacePath) : null
}
