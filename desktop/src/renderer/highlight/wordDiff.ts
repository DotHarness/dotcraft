import { diffWordsWithSpace } from 'diff'
import { MAX_LINE_DIFF_LENGTH, type CharRange } from './types'
import type { DiffHunk } from '../types/toolCall'

/** An entry is `undefined` when the row has no paired counterpart. */
export interface WordDiffRanges {
  deletion: (CharRange[] | undefined)[]
  addition: (CharRange[] | undefined)[]
}

export interface ChangedLinePair {
  removeIndex: number
  addIndex: number
}

/**
 * A run of removals pairs positionally with the run of additions that follows,
 * the same grouping the split view uses to lay a replacement out on one row.
 */
export function pairChangedLines(hunk: DiffHunk): ChangedLinePair[] {
  const pairs: ChangedLinePair[] = []
  let index = 0

  while (index < hunk.lines.length) {
    if (hunk.lines[index]?.type !== 'remove') {
      index++
      continue
    }
    const removeStart = index
    while (hunk.lines[index]?.type === 'remove') index++
    const addStart = index
    while (hunk.lines[index]?.type === 'add') index++

    const count = Math.min(addStart - removeStart, index - addStart)
    for (let offset = 0; offset < count; offset++) {
      pairs.push({ removeIndex: removeStart + offset, addIndex: addStart + offset })
    }
  }

  return pairs
}

export function computeWordDiff(
  hunks: DiffHunk[],
  deletionRow: number[][],
  additionRow: number[][],
  deletionCount: number,
  additionCount: number
): WordDiffRanges {
  const deletion = new Array<CharRange[] | undefined>(deletionCount).fill(undefined)
  const addition = new Array<CharRange[] | undefined>(additionCount).fill(undefined)

  hunks.forEach((hunk, hunkIndex) => {
    for (const pair of pairChangedLines(hunk)) {
      const before = hunk.lines[pair.removeIndex]?.content ?? ''
      const after = hunk.lines[pair.addIndex]?.content ?? ''
      const ranges = lineChangeRanges(before, after)
      if (ranges === undefined) continue

      const deletionIndex = deletionRow[hunkIndex]?.[pair.removeIndex] ?? -1
      const additionIndex = additionRow[hunkIndex]?.[pair.addIndex] ?? -1
      if (deletionIndex >= 0) deletion[deletionIndex] = ranges.deletion
      if (additionIndex >= 0) addition[additionIndex] = ranges.addition
    }
  })

  return { deletion, addition }
}

export function lineChangeRanges(
  before: string,
  after: string
): { deletion: CharRange[]; addition: CharRange[] } | undefined {
  // Word-diffing a minified line produces noise rather than insight.
  if (before.length > MAX_LINE_DIFF_LENGTH || after.length > MAX_LINE_DIFF_LENGTH) return undefined
  if (before === after) return { deletion: [], addition: [] }

  const changes = diffWordsWithSpace(before, after)
  const deletionRuns: Run[] = []
  const additionRuns: Run[] = []

  changes.forEach((change, index) => {
    const isLast = index === changes.length - 1
    if (change.added) {
      pushRun(additionRuns, change.value, true, isLast)
    } else if (change.removed) {
      pushRun(deletionRuns, change.value, true, isLast)
    } else {
      pushRun(deletionRuns, change.value, false, isLast)
      pushRun(additionRuns, change.value, false, isLast)
    }
  })

  return { deletion: toRanges(deletionRuns), addition: toRanges(additionRuns) }
}

/** `[changed, text]`; text accumulates in place as runs merge. */
type Run = [boolean, string]

function pushRun(runs: Run[], text: string, changed: boolean, isLast: boolean): void {
  const previous = runs[runs.length - 1]
  // A trailing unchanged character is the line ending its own way, not a gap.
  if (previous === undefined || isLast) {
    runs.push([changed, text])
    return
  }
  // Absorbing one unchanged character draws `a.b()` becoming `a.c()` as one mark, not two.
  const absorbsGap = !changed && text.length === 1 && previous[0]
  if (previous[0] === changed || absorbsGap) {
    previous[1] += text
    return
  }
  runs.push([changed, text])
}

function toRanges(runs: Run[]): CharRange[] {
  const ranges: CharRange[] = []
  let offset = 0
  for (const [changed, text] of runs) {
    if (changed) ranges.push({ start: offset, end: offset + text.length })
    offset += text.length
  }
  return ranges
}
