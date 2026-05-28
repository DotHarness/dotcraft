import { diffLines } from 'diff'
import type { FileDiff, DiffHunk, DiffLine } from '../types/toolCall'
import { reconstructNewContent, reconstructOriginalContent } from './diffReconstruct'

/**
 * Extract the file path from tool result status strings like:
 *   "Successfully wrote 1234 bytes (42 lines) to src/foo.ts"
 *   "Successfully edited src/foo.ts"
 */
export function parseResultPath(resultText: string): string | null {
  // "... to <path>" pattern (WriteFile)
  const toMatch = resultText.match(/\bto\s+(\S+)\s*$/)
  if (toMatch) return toMatch[1]
  // "Successfully edited <path>" pattern (EditFile)
  const editMatch = resultText.match(/Successfully edited\s+(\S+)/)
  if (editMatch) return editMatch[1]
  return null
}

/**
 * Convert jsdiff change objects into our DiffHunk[] format.
 */
export function computeDiffHunks(oldText: string, newText: string): { hunks: DiffHunk[]; additions: number; deletions: number } {
  const changes = diffLines(oldText, newText)
  const lines: DiffLine[] = []

  for (const change of changes) {
    const changeLines = change.value.split('\n')
    // diffLines may include a trailing empty string from the final newline
    if (changeLines[changeLines.length - 1] === '') {
      changeLines.pop()
    }
    const type: DiffLine['type'] = change.added ? 'add' : change.removed ? 'remove' : 'context'
    for (const line of changeLines) {
      lines.push({ type, content: line })
    }
  }

  // Group consecutive lines into hunks (context windows of 3 lines around changes)
  const CONTEXT = 3
  const hunks: DiffHunk[] = []
  let additions = 0
  let deletions = 0

  // Count additions/deletions first
  for (const line of lines) {
    if (line.type === 'add') additions++
    else if (line.type === 'remove') deletions++
  }

  // Build hunks: collect changed line indices and expand with context
  const changedIndices = new Set<number>()
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].type !== 'context') changedIndices.add(i)
  }

  if (changedIndices.size === 0) return { hunks: [], additions: 0, deletions: 0 }

  // Expand ranges with context
  const ranges: Array<[number, number]> = []
  let rangeStart = -1
  let rangeEnd = -1

  const sortedChanged = Array.from(changedIndices).sort((a, b) => a - b)
  for (const idx of sortedChanged) {
    const start = Math.max(0, idx - CONTEXT)
    const end = Math.min(lines.length - 1, idx + CONTEXT)
    if (rangeStart === -1) {
      rangeStart = start
      rangeEnd = end
    } else if (start <= rangeEnd + 1) {
      rangeEnd = Math.max(rangeEnd, end)
    } else {
      ranges.push([rangeStart, rangeEnd])
      rangeStart = start
      rangeEnd = end
    }
  }
  if (rangeStart !== -1) ranges.push([rangeStart, rangeEnd])

  // Build hunk objects with line numbers
  let oldLine = 1
  let newLine = 1
  let lineIdx = 0

  for (const [start, end] of ranges) {
    // Advance counters to the hunk start
    while (lineIdx < start) {
      const l = lines[lineIdx]
      if (l.type !== 'add') oldLine++
      if (l.type !== 'remove') newLine++
      lineIdx++
    }

    const hunkLines = lines.slice(start, end + 1)
    const hunkOldStart = oldLine
    const hunkNewStart = newLine
    let hunkOldLines = 0
    let hunkNewLines = 0

    for (const l of hunkLines) {
      if (l.type !== 'add') hunkOldLines++
      if (l.type !== 'remove') hunkNewLines++
    }

    hunks.push({
      oldStart: hunkOldStart,
      oldLines: hunkOldLines,
      newStart: hunkNewStart,
      newLines: hunkNewLines,
      lines: hunkLines
    })

    // Advance counters through the hunk
    for (const l of hunkLines) {
      if (l.type !== 'add') oldLine++
      if (l.type !== 'remove') newLine++
      lineIdx++
    }
  }

  return { hunks, additions, deletions }
}

/**
 * Extract a FileDiff from a WriteFile tool call.
 * For new files, the entire content is treated as additions.
 */
export function extractDiffFromWriteFile(
  args: Record<string, unknown>,
  resultText: string,
  turnId: string
): FileDiff | null {
  const filePath = (args.path as string | undefined) ?? parseResultPath(resultText)
  if (!filePath) return null

  const content = (args.content as string | undefined) ?? ''
  const contentLines = content.split('\n')
  if (contentLines[contentLines.length - 1] === '') contentLines.pop()

  const hunkLines: DiffLine[] = contentLines.map((line) => ({ type: 'add' as const, content: line }))

  const hunk: DiffHunk = {
    oldStart: 0,
    oldLines: 0,
    newStart: 1,
    newLines: contentLines.length,
    lines: hunkLines
  }

  return {
    filePath,
    turnId,
    turnIds: [turnId],
    additions: contentLines.length,
    deletions: 0,
    diffHunks: hunkLines.length > 0 ? [hunk] : [],
    status: 'written',
    isNewFile: true,
    originalContent: '',
    currentContent: content
  }
}

/**
 * Extract a FileDiff from an EditFile tool call.
 * Supports oldText/newText (search-replace) mode and startLine/endLine/newText (line-range) mode.
 */
export function extractDiffFromEditFile(
  args: Record<string, unknown>,
  resultText: string,
  turnId: string
): FileDiff | null {
  const filePath = (args.path as string | undefined) ?? parseResultPath(resultText)
  if (!filePath) return null

  const oldText = (args.oldText as string | undefined) ?? ''
  const newText = (args.newText as string | undefined) ?? ''

  if (!oldText && !newText) return null

  const { hunks, additions, deletions } = computeDiffHunks(oldText, newText)

  return {
    filePath,
    turnId,
    turnIds: [turnId],
    additions,
    deletions,
    diffHunks: hunks,
    status: 'written',
    isNewFile: false
  }
}

/**
 * Single-tool-call diff for inline ToolCallCard (not cumulative).
 * WriteFile with no prior entry for the path: full-file addition diff.
 * WriteFile after prior edits: diff from previous cumulative content to new args.content.
 */
export function computeIncrementalPerItemDiff(
  toolName: 'WriteFile' | 'EditFile',
  args: Record<string, unknown>,
  resultText: string,
  turnId: string,
  existingForFile: FileDiff | undefined
): FileDiff | null {
  if (toolName === 'EditFile') {
    return extractDiffFromEditFile(args, resultText, turnId)
  }
  if (!existingForFile) {
    return extractDiffFromWriteFile(args, resultText, turnId)
  }
  const filePath = (args.path as string | undefined) ?? parseResultPath(resultText) ?? ''
  if (!filePath) return null
  const oldContent =
    existingForFile.currentContent !== undefined
      ? existingForFile.currentContent
      : reconstructNewContent(existingForFile)
  const newContent = (args.content as string | undefined) ?? ''
  const { hunks, additions, deletions } = computeDiffHunks(oldContent, newContent)
  return {
    filePath,
    turnId,
    turnIds: [turnId],
    additions,
    deletions,
    diffHunks: hunks,
    status: 'written',
    isNewFile: oldContent === '',
    originalContent: oldContent,
    currentContent: newContent
  }
}

/**
 * Join workspace root with a relative file path for IPC read/write (renderer-side).
 */
export function toAbsoluteWorkspacePath(workspacePath: string, filePath: string): string {
  if (filePath.startsWith('/') || /^[A-Za-z]:[\\/]/.test(filePath)) return filePath
  const ws = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const rel = filePath.replace(/\\/g, '/')
  return `${ws}/${rel}`.replace(/\/+/g, '/')
}

const READ_RETRY_ATTEMPTS = 5
const READ_RETRY_DELAY_MS = 20

/**
 * Yield so a workspace write from AppServer is visible to readFile IPC after toolResult.
 */
async function yieldBeforeWorkspaceRead(): Promise<void> {
  await new Promise<void>((resolve) => queueMicrotask(() => resolve()))
  await new Promise<void>((resolve) => setTimeout(resolve, 0))
}

/**
 * Read workspace file after a tool completes. Retries for EditFile when the first read
 * still looks pre-edit (stale) so cumulative diff does not get empty hunks.
 */
export async function readWorkspaceAfterTool(
  readFile: (absPath: string) => Promise<string>,
  absPath: string,
  toolName: 'WriteFile' | 'EditFile',
  args: Record<string, unknown>
): Promise<string> {
  await yieldBeforeWorkspaceRead()

  if (toolName === 'WriteFile') {
    return readFile(absPath)
  }

  const newText = (args.newText as string | undefined) ?? ''
  const oldText = (args.oldText as string | undefined) ?? ''

  let last = ''
  for (let attempt = 0; attempt < READ_RETRY_ATTEMPTS; attempt++) {
    if (attempt > 0) {
      await new Promise<void>((resolve) => setTimeout(resolve, READ_RETRY_DELAY_MS))
    }
    last = await readFile(absPath)

    if (newText !== '') {
      if (last.includes(newText)) return last
      continue
    }

    if (oldText !== '') {
      if (!last.includes(oldText)) return last
      continue
    }

    return last
  }

  return last
}

/**
 * If full-file diff has no hunks, use per-args EditFile diff (same source as inline ToolCallCard).
 */
function withEditFileDisplayFallback(
  diff: FileDiff,
  toolName: string,
  args: Record<string, unknown>,
  resultText: string,
  turnId: string
): FileDiff {
  if (toolName !== 'EditFile' || diff.diffHunks.length > 0) return diff
  const inc = extractDiffFromEditFile(args, resultText, turnId)
  if (!inc || inc.diffHunks.length === 0) return diff
  return {
    ...diff,
    additions: inc.additions,
    deletions: inc.deletions,
    diffHunks: inc.diffHunks
  }
}

/**
 * Reverse one EditFile application on post-edit file content (first occurrence).
 */
export function reverseEditReplace(postContent: string, newText: string, oldText: string): string {
  const idx = postContent.indexOf(newText)
  if (idx === -1) {
    return postContent.replace(newText, oldText)
  }
  return postContent.slice(0, idx) + oldText + postContent.slice(idx + newText.length)
}

/**
 * Merge a new WriteFile/EditFile tool result into an existing FileDiff (sync, no disk read).
 * Used for thread/history rehydration where multiple edits to the same file must accumulate.
 */
export function mergeFileDiffIncrement(
  existing: FileDiff | undefined,
  toolName: 'WriteFile' | 'EditFile',
  args: Record<string, unknown>,
  resultText: string,
  turnId: string
): FileDiff | null {
  const incremental = toolName === 'WriteFile'
    ? extractDiffFromWriteFile(args, resultText, turnId)
    : extractDiffFromEditFile(args, resultText, turnId)
  if (!incremental) return null

  if (!existing) {
    return incremental
  }

  const orig =
    existing.originalContent !== undefined
      ? existing.originalContent
      : existing.isNewFile
        ? ''
        : reconstructOriginalContent(existing)

  let curr =
    existing.currentContent !== undefined
      ? existing.currentContent
      : reconstructNewContent(existing)

  if (toolName === 'WriteFile') {
    curr = (args.content as string | undefined) ?? ''
  } else {
    const oldText = (args.oldText as string | undefined) ?? ''
    const newText = (args.newText as string | undefined) ?? ''
    if (oldText) {
      const idx = curr.indexOf(oldText)
      if (idx !== -1) {
        curr = curr.slice(0, idx) + newText + curr.slice(idx + oldText.length)
      } else {
        curr = curr.replace(oldText, newText)
      }
    }
  }

  let { hunks, additions, deletions } = computeDiffHunks(orig, curr)
  if (toolName === 'EditFile' && hunks.length === 0) {
    const inc = extractDiffFromEditFile(args, resultText, turnId)
    if (inc && inc.diffHunks.length > 0) {
      hunks = inc.diffHunks
      additions = inc.additions
      deletions = inc.deletions
    }
  }
  const baseTurnIds = existing.turnIds?.length ? existing.turnIds : [existing.turnId]
  const turnIds = baseTurnIds.includes(turnId) ? [...baseTurnIds] : [...baseTurnIds, turnId]

  return {
    filePath: incremental.filePath,
    turnId,
    turnIds,
    additions,
    deletions,
    diffHunks: hunks,
    status: existing.status,
    isNewFile: orig === '',
    originalContent: orig,
    currentContent: curr
  }
}

export interface ComputeCumulativeFileDiffOptions {
  filePath: string
  toolName: string
  args: Record<string, unknown>
  resultText: string
  turnId: string
  existing: FileDiff | undefined
  workspacePath: string
  /** Injected for unit tests */
  readFile?: (absPath: string) => Promise<string>
}

/**
 * Computes cumulative file diff after a tool completes (may read disk for EditFile / verification).
 */
export async function computeCumulativeFileDiff(
  options: ComputeCumulativeFileDiffOptions
): Promise<FileDiff | null> {
  const { filePath, resultText, turnId, existing, workspacePath } = options
  const toolName = options.toolName
  if (toolName !== 'WriteFile' && toolName !== 'EditFile') return null

  const args = options.args
  const readFile =
    options.readFile ??
    (async (abs: string) => {
      if (typeof window !== 'undefined' && window.api?.file?.readFile) {
        return window.api.file.readFile(abs)
      }
      return ''
    })

  const absPath = toAbsoluteWorkspacePath(workspacePath, filePath)
  const postDisk = await readWorkspaceAfterTool(readFile, absPath, toolName, args)

  if (!existing && toolName === 'EditFile') {
    const oldText = (args.oldText as string | undefined) ?? ''
    const newText = (args.newText as string | undefined) ?? ''
    if (!oldText && !newText) return null
    const currentContent = postDisk !== '' ? postDisk : newText
    const originalContent = reverseEditReplace(currentContent, newText, oldText)
    const { hunks, additions, deletions } = computeDiffHunks(originalContent, currentContent)
    const base: FileDiff = {
      filePath,
      turnId,
      turnIds: [turnId],
      additions,
      deletions,
      diffHunks: hunks,
      status: 'written',
      isNewFile: originalContent === '',
      originalContent,
      currentContent
    }
    return withEditFileDisplayFallback(base, toolName, args, resultText, turnId)
  }

  const merged = mergeFileDiffIncrement(existing, toolName, args, resultText, turnId)
  if (!merged) return null

  if (toolName === 'EditFile' && postDisk !== '' && merged.currentContent !== postDisk) {
    const orig =
      merged.originalContent !== undefined
        ? merged.originalContent
        : merged.isNewFile
          ? ''
          : reconstructOriginalContent(merged)
    const { hunks, additions, deletions } = computeDiffHunks(orig, postDisk)
    const reconciled: FileDiff = {
      ...merged,
      additions,
      deletions,
      diffHunks: hunks,
      currentContent: postDisk,
      originalContent: orig
    }
    return withEditFileDisplayFallback(reconciled, toolName, args, resultText, turnId)
  }

  return withEditFileDisplayFallback(merged, toolName, args, resultText, turnId)
}
