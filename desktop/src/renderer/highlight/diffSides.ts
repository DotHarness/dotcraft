/**
 * Highlighting a hunk's rows in display order interleaves old and new text into
 * source that never existed, so a comment opening on a removed line mis-colors
 * everything after it. Each side is instead rebuilt whole and tokenized on its
 * own, per hunk when the whole-file contents are missing.
 */
import { normalizeNewlines, splitLines } from './tokenize'
import type { HighlightSegment } from './types'
import type { DiffHunk, FileDiff } from '../types/toolCall'

/** Row has no counterpart on this side. */
export const NO_ROW = -1

export interface DiffSideModel {
  deletion: HighlightSegment[]
  addition: HighlightSegment[]
  /** `[hunkIndex][lineIndex]` to a row index, or {@link NO_ROW}. */
  deletionRow: number[][]
  additionRow: number[][]
  deletionText: string[]
  additionText: string[]
  /** True when both sides came from whole-file contents, so no segment seam falls inside. */
  exact: boolean
}

export function buildDiffSides(diff: FileDiff): DiffSideModel {
  return buildExact(diff) ?? buildPerHunk(diff.diffHunks)
}

/**
 * Bails out when the contents disagree with the hunks: a stale `originalContent`
 * would line up tokens against text the view is not showing, worse than a seam.
 */
function buildExact(diff: FileDiff): DiffSideModel | undefined {
  const { originalContent, currentContent } = diff
  if (originalContent === undefined || currentContent === undefined) return undefined

  const oldText = normalizeNewlines(originalContent)
  const newText = normalizeNewlines(currentContent)
  const oldLines = splitLines(oldText)
  const newLines = splitLines(newText)

  const deletionIndices: number[] = []
  const additionIndices: number[] = []
  const deletionText: string[] = []
  const additionText: string[] = []
  const deletionRow: number[][] = []
  const additionRow: number[][] = []

  for (const hunk of diff.diffHunks) {
    const hunkDeletion: number[] = []
    const hunkAddition: number[] = []
    let oldLine = hunk.oldStart
    let newLine = hunk.newStart

    for (const line of hunk.lines) {
      if (line.type === 'add') {
        hunkDeletion.push(NO_ROW)
      } else {
        const index = oldLine - 1
        if (oldLines[index] !== line.content) return undefined
        hunkDeletion.push(deletionIndices.length)
        deletionIndices.push(index)
        deletionText.push(line.content)
        oldLine++
      }

      if (line.type === 'remove') {
        hunkAddition.push(NO_ROW)
      } else {
        const index = newLine - 1
        if (newLines[index] !== line.content) return undefined
        hunkAddition.push(additionIndices.length)
        additionIndices.push(index)
        additionText.push(line.content)
        newLine++
      }
    }

    deletionRow.push(hunkDeletion)
    additionRow.push(hunkAddition)
  }

  return {
    deletion: deletionIndices.length === 0 ? [] : [{ text: oldText, lineIndices: deletionIndices }],
    addition: additionIndices.length === 0 ? [] : [{ text: newText, lineIndices: additionIndices }],
    deletionRow,
    additionRow,
    deletionText,
    additionText,
    exact: true
  }
}

function buildPerHunk(hunks: DiffHunk[]): DiffSideModel {
  const deletion: HighlightSegment[] = []
  const addition: HighlightSegment[] = []
  const deletionText: string[] = []
  const additionText: string[] = []
  const deletionRow: number[][] = []
  const additionRow: number[][] = []

  for (const hunk of hunks) {
    const hunkDeletion: number[] = []
    const hunkAddition: number[] = []
    const oldLines: string[] = []
    const newLines: string[] = []

    for (const line of hunk.lines) {
      if (line.type === 'add') {
        hunkDeletion.push(NO_ROW)
      } else {
        hunkDeletion.push(deletionText.length)
        oldLines.push(line.content)
        deletionText.push(line.content)
      }

      if (line.type === 'remove') {
        hunkAddition.push(NO_ROW)
      } else {
        hunkAddition.push(additionText.length)
        newLines.push(line.content)
        additionText.push(line.content)
      }
    }

    if (oldLines.length > 0) {
      deletion.push({ text: oldLines.join('\n'), lineIndices: range(oldLines.length) })
    }
    if (newLines.length > 0) {
      addition.push({ text: newLines.join('\n'), lineIndices: range(newLines.length) })
    }
    deletionRow.push(hunkDeletion)
    additionRow.push(hunkAddition)
  }

  return { deletion, addition, deletionRow, additionRow, deletionText, additionText, exact: false }
}

function range(length: number): number[] {
  return Array.from({ length }, (_unused, index) => index)
}
