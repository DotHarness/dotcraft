import { diffLines } from 'diff'
import type { DiffHunk, DiffLine } from '../types/toolCall'

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

  const CONTEXT = 3
  const hunks: DiffHunk[] = []
  let additions = 0
  let deletions = 0

  for (const line of lines) {
    if (line.type === 'add') additions++
    else if (line.type === 'remove') deletions++
  }

  const changedIndices = new Set<number>()
  for (let i = 0; i < lines.length; i++) {
    if (lines[i].type !== 'context') changedIndices.add(i)
  }

  if (changedIndices.size === 0) return { hunks: [], additions: 0, deletions: 0 }

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

  let oldLine = 1
  let newLine = 1
  let lineIdx = 0

  for (const [start, end] of ranges) {
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

    for (const l of hunkLines) {
      if (l.type !== 'add') oldLine++
      if (l.type !== 'remove') newLine++
      lineIdx++
    }
  }

  return { hunks, additions, deletions }
}

