import { NO_ROW, type DiffSideModel } from '../../../highlight'
import type { DiffLine, FileDiff } from '../../../types/toolCall'

export type RowType = DiffLine['type'] | 'blank'

export type CellSide = 'deletion' | 'addition'

export interface DiffCell {
  num: string
  content: string
  type: RowType
  side: CellSide
  /** Index into that side's tokenized rows, or {@link NO_ROW}. */
  sideRow: number
}

export type UnifiedRow =
  | { kind: 'divider'; count: number }
  | { kind: 'line'; oldNum: string; newNum: string; cell: DiffCell }

export type SplitRow =
  | { kind: 'divider'; count: number }
  | { kind: 'line'; left: DiffCell; right: DiffCell }

const BLANK: DiffCell = { num: '', content: '', type: 'blank', side: 'deletion', sideRow: NO_ROW }

/** A removed line reads from the deletion side; everything else from the addition side. */
export function buildUnifiedRows(diff: FileDiff, sides: DiffSideModel): UnifiedRow[] {
  const rows: UnifiedRow[] = []
  let previousOldEnd = 1
  let previousNewEnd = 1

  diff.diffHunks.forEach((hunk, hunkIndex) => {
    const unchanged = unchangedBeforeHunk(hunk.oldStart, hunk.newStart, previousOldEnd, previousNewEnd)
    previousOldEnd = hunk.oldStart + hunk.oldLines
    previousNewEnd = hunk.newStart + hunk.newLines
    if (unchanged > 0) rows.push({ kind: 'divider', count: unchanged })

    let oldLineNum = hunk.oldStart
    let newLineNum = hunk.newStart

    hunk.lines.forEach((line, lineIndex) => {
      const oldNum = line.type === 'add' ? '' : String(oldLineNum)
      const newNum = line.type === 'remove' ? '' : String(newLineNum)
      if (line.type !== 'add') oldLineNum++
      if (line.type !== 'remove') newLineNum++

      const side: CellSide = line.type === 'remove' ? 'deletion' : 'addition'
      const sideRow = side === 'deletion'
        ? sides.deletionRow[hunkIndex][lineIndex]
        : sides.additionRow[hunkIndex][lineIndex]

      rows.push({
        kind: 'line',
        oldNum,
        newNum,
        cell: { num: '', content: line.content, type: line.type, side, sideRow }
      })
    })
  })

  return rows
}

/** Removal runs pair positionally with the addition run that follows; the shorter side is padded. */
export function buildSplitRows(diff: FileDiff, sides: DiffSideModel): SplitRow[] {
  const rows: SplitRow[] = []
  let previousOldEnd = 1
  let previousNewEnd = 1

  diff.diffHunks.forEach((hunk, hunkIndex) => {
    const unchanged = unchangedBeforeHunk(hunk.oldStart, hunk.newStart, previousOldEnd, previousNewEnd)
    previousOldEnd = hunk.oldStart + hunk.oldLines
    previousNewEnd = hunk.newStart + hunk.newLines
    if (unchanged > 0) rows.push({ kind: 'divider', count: unchanged })

    const deletionRow = sides.deletionRow[hunkIndex]
    const additionRow = sides.additionRow[hunkIndex]
    let oldLineNum = hunk.oldStart
    let newLineNum = hunk.newStart
    let index = 0

    const left = (lineIndex: number, content: string, type: RowType): DiffCell => ({
      num: String(oldLineNum++),
      content,
      type,
      side: 'deletion',
      sideRow: deletionRow[lineIndex]
    })
    const right = (lineIndex: number, content: string, type: RowType): DiffCell => ({
      num: String(newLineNum++),
      content,
      type,
      side: 'addition',
      sideRow: additionRow[lineIndex]
    })

    while (index < hunk.lines.length) {
      const line = hunk.lines[index]
      if (line === undefined) break

      if (line.type === 'context') {
        rows.push({
          kind: 'line',
          left: left(index, line.content, 'context'),
          right: right(index, line.content, 'context')
        })
        index++
        continue
      }

      if (line.type === 'remove') {
        const removes: number[] = []
        while (hunk.lines[index]?.type === 'remove') removes.push(index++)
        const adds: number[] = []
        while (hunk.lines[index]?.type === 'add') adds.push(index++)

        const count = Math.max(removes.length, adds.length)
        for (let offset = 0; offset < count; offset++) {
          const removeIndex = removes[offset]
          const addIndex = adds[offset]
          rows.push({
            kind: 'line',
            left: removeIndex === undefined
              ? BLANK
              : left(removeIndex, hunk.lines[removeIndex]?.content ?? '', 'remove'),
            right: addIndex === undefined
              ? BLANK
              : right(addIndex, hunk.lines[addIndex]?.content ?? '', 'add')
          })
        }
        continue
      }

      rows.push({ kind: 'line', left: BLANK, right: right(index, line.content, 'add') })
      index++
    }
  })

  return rows
}

const HUNK_CONTEXT_LINES = 3
const HUNK_MAX_LINES = 60
const PREFIX: Record<DiffLine['type'], string> = { context: ' ', add: '+', remove: '-' }

export function diffHunkExcerpt(
  diff: FileDiff,
  side: 'left' | 'right',
  startLine: number,
  endLine: number
): string | undefined {
  let budget = HUNK_MAX_LINES
  const parts = diff.diffHunks.flatMap((hunk) => {
    let oldLine = hunk.oldStart
    let newLine = hunk.newStart
    const numbered = hunk.lines.map((line) => {
      const entry = { line, oldLine, newLine }
      if (line.type !== 'add') oldLine++
      if (line.type !== 'remove') newLine++
      return entry
    })
    const hits = numbered.flatMap((entry, index) => {
      if (entry.line.type === (side === 'left' ? 'add' : 'remove')) return []
      const number = side === 'left' ? entry.oldLine : entry.newLine
      return number >= startLine && number <= endLine ? [index] : []
    })
    if (hits.length === 0 || budget <= 0) return []
    const first = Math.max(0, hits[0] - HUNK_CONTEXT_LINES)
    const last = Math.min(hits[hits.length - 1] + HUNK_CONTEXT_LINES + 1, first + budget)
    const slice = numbered.slice(first, last)
    budget -= slice.length
    const oldCount = slice.filter((entry) => entry.line.type !== 'add').length
    const newCount = slice.filter((entry) => entry.line.type !== 'remove').length
    const oldStart = oldCount === 0 ? slice[0].oldLine - 1 : slice[0].oldLine
    const newStart = newCount === 0 ? slice[0].newLine - 1 : slice[0].newLine
    return [
      `@@ -${oldStart},${oldCount} +${newStart},${newCount} @@`,
      ...slice.map((entry) => `${PREFIX[entry.line.type]}${entry.line.content}`)
    ].join('\n')
  })
  return parts.length > 0 ? parts.join('\n') : undefined
}

export function unchangedBeforeHunk(
  oldStart: number,
  newStart: number,
  previousOldEnd: number,
  previousNewEnd: number
): number {
  return Math.max(0, oldStart - previousOldEnd, newStart - previousNewEnd)
}

export function toRelativePath(filePath: string, workspacePath: string): string {
  if (!workspacePath) return filePath
  const workspace = workspacePath.replace(/\\/g, '/').replace(/\/$/, '')
  const file = filePath.replace(/\\/g, '/')
  return file.startsWith(`${workspace}/`) ? file.slice(workspace.length + 1) : filePath
}
