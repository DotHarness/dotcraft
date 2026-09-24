import { parsePatch, type StructuredPatchHunk } from 'diff'
import type { DiffHunk, DiffLine, FileDiff } from '../types/toolCall'
import type { FileChangeEntry, FileChangeStructuredContent, TurnFileChange } from '../types/turnDiff'
import {
  DEV_NULL,
  decodePatchName,
  encodePatchName,
  splitFileHeaderName,
  splitGitHeaderNames,
  stripSidePrefix
} from './gitPatchPaths'
import { toWorkspaceRelativePath } from './workspacePaths'

export interface ParsedFilePatch {
  diff: FileDiff
  patchText: string
  error?: true
}

const GIT_HEADER = 'diff --git '

function gitDiffChunks(text: string): string[] {
  const starts = [0]
  for (let i = text.indexOf(`\n${GIT_HEADER}`); i !== -1; i = text.indexOf(`\n${GIT_HEADER}`, i + 1)) {
    starts.push(i + 1)
  }
  return starts.map((start, index) => text.slice(start, starts[index + 1]))
}

function splitGitDiff(text: string): string[] {
  return gitDiffChunks(text).filter((chunk) => chunk.trim() !== '')
}

/** File headers end where the first hunk starts; `---`/`+++` lines after that are hunk content. */
function headerLength(chunk: string): number {
  if (chunk.startsWith('@@')) return 0
  const hunk = chunk.indexOf('\n@@')
  return hunk === -1 ? chunk.length : hunk + 1
}

function withoutCarriageReturn(line: string): string {
  return line.endsWith('\r') ? line.slice(0, -1) : line
}

interface ChunkHeader {
  gitName?: string
  oldName?: string
  newName?: string
  newFileMode: boolean
}

function readChunkHeader(chunk: string): ChunkHeader {
  const header: ChunkHeader = { newFileMode: false }
  for (const rawLine of chunk.slice(0, headerLength(chunk)).split('\n')) {
    const line = withoutCarriageReturn(rawLine)
    if (line.startsWith(GIT_HEADER)) {
      const names = splitGitHeaderNames(line.slice(GIT_HEADER.length))
      if (names) header.gitName = decodePatchName(names[1])
    } else if (line.startsWith('new file mode')) {
      header.newFileMode = true
    } else if (line.startsWith('--- ')) {
      header.oldName = decodePatchName(splitFileHeaderName(line.slice(4))[0])
    } else if (line.startsWith('+++ ')) {
      header.newName = decodePatchName(splitFileHeaderName(line.slice(4))[0])
    }
  }
  return header
}

function headerPath(header: ChunkHeader, workspacePath?: string): string | null {
  const name = [header.newName, header.oldName].find((candidate) => candidate && candidate !== DEV_NULL)
    ?? header.gitName
  if (!name) return null
  const path = stripSidePrefix(name).replace(/\\/g, '/')
  return workspacePath ? toWorkspaceRelativePath(workspacePath, path) : path
}

function toDiffHunk(hunk: StructuredPatchHunk): DiffHunk {
  const lines: DiffLine[] = []
  for (const line of hunk.lines) {
    const operation = line[0]
    if (operation === '\\') continue
    lines.push({
      type: operation === '+' ? 'add' : operation === '-' ? 'remove' : 'context',
      content: withoutCarriageReturn(line.slice(1))
    })
  }
  // parsePatch moves the start of an empty range one line down; keep the numbers the @@ header shows.
  return {
    oldStart: hunk.oldLines === 0 ? hunk.oldStart - 1 : hunk.oldStart,
    oldLines: hunk.oldLines,
    newStart: hunk.newLines === 0 ? hunk.newStart - 1 : hunk.newStart,
    newLines: hunk.newLines,
    lines
  }
}

function emptyFileDiff(filePath: string, isNewFile: boolean): FileDiff {
  return {
    filePath,
    additions: 0,
    deletions: 0,
    diffHunks: [],
    status: 'written',
    isNewFile
  }
}

function parseChunk(chunk: string, workspacePath?: string): ParsedFilePatch | null {
  const header = readChunkHeader(chunk)
  const filePath = headerPath(header, workspacePath)
  if (!filePath) return null
  const base = emptyFileDiff(filePath, header.oldName === DEV_NULL || header.newFileMode)
  let diffHunks: DiffHunk[]
  try {
    diffHunks = parsePatch(chunk).flatMap((patch) => patch.hunks.map(toDiffHunk))
  } catch {
    return { diff: base, patchText: chunk, error: true }
  }
  let additions = 0
  let deletions = 0
  for (const line of diffHunks.flatMap((hunk) => hunk.lines)) {
    if (line.type === 'add') additions++
    else if (line.type === 'remove') deletions++
  }
  return { diff: { ...base, additions, deletions, diffHunks }, patchText: chunk }
}

export function parseUnifiedDiff(text: string, workspacePath?: string): ParsedFilePatch[] {
  return splitGitDiff(text).flatMap((chunk) => parseChunk(chunk, workspacePath) ?? [])
}

const isRecord = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

const isCount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)

function parseFileChangeEntry(value: unknown): FileChangeEntry | null {
  if (!isRecord(value)) return null
  const { path, kind, diff, additions, deletions, truncated } = value
  if (typeof path !== 'string' || (kind !== 'add' && kind !== 'update')) return null
  if (!isCount(additions) || !isCount(deletions)) return null
  // A serializer that writes nulls instead of omitting the optional fields still means "absent".
  if (diff != null && typeof diff !== 'string') return null
  if (truncated != null && typeof truncated !== 'boolean') return null
  return {
    path,
    kind,
    additions,
    deletions,
    ...(typeof diff === 'string' ? { diff } : {}),
    ...(typeof truncated === 'boolean' ? { truncated } : {})
  }
}

export function parseFileChangeStructuredContent(value: unknown): FileChangeStructuredContent | null {
  if (!isRecord(value) || value.kind !== 'fileChange' || !Array.isArray(value.changes)) return null
  const changes: FileChangeEntry[] = []
  for (const change of value.changes) {
    const entry = parseFileChangeEntry(change)
    if (!entry) return null
    changes.push(entry)
  }
  return { kind: 'fileChange', changes }
}

/** Changed lines without hunks mean the diff was cut or could not be parsed; a 0/0 change without a diff is a no-op write. */
export function isFileChangeTruncated(entry: FileChangeEntry, diff: FileDiff): boolean {
  return entry.truncated === true || (diff.diffHunks.length === 0 && entry.additions + entry.deletions > 0)
}

export function fileChangeEntryToTurnFileChange(
  entry: FileChangeEntry,
  turnId: string,
  itemId: string,
  workspacePath?: string
): TurnFileChange {
  const parsed = entry.diff ? parseUnifiedDiff(entry.diff, workspacePath)[0] : undefined
  const filePath = workspacePath ? toWorkspaceRelativePath(workspacePath, entry.path) : entry.path.replace(/\\/g, '/')
  const diff: FileDiff = {
    ...(parsed?.diff ?? emptyFileDiff(filePath, false)),
    additions: entry.additions,
    deletions: entry.deletions,
    isNewFile: entry.kind === 'add'
  }
  return {
    key: `${turnId}::${itemId}`,
    turnId,
    diff,
    patchText: entry.diff ?? '',
    truncated: isFileChangeTruncated(entry, diff)
  }
}

function relativeName(token: string, workspacePath: string): string {
  const name = decodePatchName(token)
  if (name === DEV_NULL) return token
  const side = /^[ab]\//.test(name) ? name.slice(0, 2) : ''
  const rewritten = side + toWorkspaceRelativePath(workspacePath, name.slice(side.length))
  if (rewritten === name) return token
  return token.startsWith('"') ? encodePatchName(rewritten) : rewritten
}

function relativeHeaderLine(rawLine: string, workspacePath: string): string {
  const line = withoutCarriageReturn(rawLine)
  const lineEnd = rawLine.slice(line.length)
  if (line.startsWith('--- ') || line.startsWith('+++ ')) {
    const [token, trailer] = splitFileHeaderName(line.slice(4))
    return `${line.slice(0, 4)}${relativeName(token, workspacePath)}${trailer}${lineEnd}`
  }
  if (line.startsWith(GIT_HEADER)) {
    const names = splitGitHeaderNames(line.slice(GIT_HEADER.length))
    if (!names) return rawLine
    const [oldToken, newToken] = names.map((token) => relativeName(token, workspacePath))
    return `${GIT_HEADER}${oldToken} ${newToken}${lineEnd}`
  }
  return rawLine
}

export function toWorkspaceRelativePatch(patchText: string, workspacePath: string): string {
  return gitDiffChunks(patchText)
    .map((chunk) => {
      const end = headerLength(chunk)
      const header = chunk.slice(0, end).split('\n').map((line) => relativeHeaderLine(line, workspacePath))
      return header.join('\n') + chunk.slice(end)
    })
    .join('')
}
