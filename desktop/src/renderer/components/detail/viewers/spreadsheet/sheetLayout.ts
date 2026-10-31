import type { CellRange, SpreadsheetCell, SpreadsheetSheet } from '../../../../../shared/viewer/spreadsheet'

export const ROW_HEIGHT = 24
export const HEADER_HEIGHT = 24
const DEFAULT_COLUMN_WIDTH = 96

export interface DisplayRange {
  r1: number
  c1: number
  r2: number
  c2: number
}

export interface SheetLayout {
  rows: number[]
  columns: number[]
  rowPosition: Map<number, number>
  columnPosition: Map<number, number>
  /** Prefix sums of display column widths at 100% zoom; one longer than `columns`. */
  columnLefts: number[]
  frozenRows: number
  frozenColumns: number
  merges: Map<string, DisplayRange>
  covered: Set<string>
}

export const displayKey = (row: number, column: number) => `${row}:${column}`

export function cellAt(sheet: SpreadsheetSheet, row: number, column: number): SpreadsheetCell | null {
  return sheet.rows[row]?.[column] ?? null
}

function positions(indices: number[]): Map<number, number> {
  return new Map(indices.map((value, index) => [value, index]))
}

function mapped(position: Map<number, number>, first: number, last: number): number[] {
  const indices: number[] = []
  for (let index = first; index <= last; index++) {
    const display = position.get(index)
    if (display !== undefined) indices.push(display)
  }
  return indices
}

function visibleIndices(count: number, hidden: number[]): number[] {
  const skip = new Set(hidden)
  const indices: number[] = []
  for (let index = 0; index < count; index++) if (!skip.has(index)) indices.push(index)
  return indices
}

export function baseRows(sheet: SpreadsheetSheet): number[] {
  return visibleIndices(sheet.rowCount, sheet.hiddenRows)
}

export function buildLayout(sheet: SpreadsheetSheet, rows: number[], reordered: boolean): SheetLayout {
  const columns = visibleIndices(sheet.columnCount, sheet.hiddenColumns)
  const rowPosition = positions(rows)
  const columnPosition = positions(columns)
  const columnLefts = [0]
  for (const column of columns) {
    columnLefts.push(columnLefts[columnLefts.length - 1]! + (sheet.columnWidths[column] ?? DEFAULT_COLUMN_WIDTH))
  }
  let frozenRows = 0
  while (frozenRows < rows.length && rows[frozenRows]! < sheet.frozenRows) frozenRows++
  let frozenColumns = 0
  while (frozenColumns < columns.length && columns[frozenColumns]! < sheet.frozenColumns) frozenColumns++

  const merges = new Map<string, DisplayRange>()
  const covered = new Set<string>()
  for (const merge of sheet.merges) {
    const displayRows = mapped(rowPosition, merge.r1, merge.r2)
    const displayColumns = mapped(columnPosition, merge.c1, merge.c2)
    if (!displayRows.length || !displayColumns.length) continue
    const r1 = Math.min(...displayRows)
    const r2 = Math.max(...displayRows)
    if (reordered && r2 - r1 + 1 !== displayRows.length) continue
    if (rows[r1] !== merge.r1 || columns[displayColumns[0]!] !== merge.c1) continue
    const range = { r1, c1: displayColumns[0]!, r2, c2: displayColumns[displayColumns.length - 1]! }
    if (range.r1 === range.r2 && range.c1 === range.c2) continue
    merges.set(displayKey(range.r1, range.c1), range)
    for (let row = range.r1; row <= range.r2; row++) {
      for (let column = range.c1; column <= range.c2; column++) {
        if (row !== range.r1 || column !== range.c1) covered.add(displayKey(row, column))
      }
    }
  }
  return { rows, columns, rowPosition, columnPosition, columnLefts, frozenRows, frozenColumns, merges, covered }
}

export function expandToMerges(layout: SheetLayout, range: DisplayRange): DisplayRange {
  let next = normalizeDisplay(range)
  for (let changed = true; changed; ) {
    changed = false
    for (const merge of layout.merges.values()) {
      const touches = merge.r1 <= next.r2 && merge.r2 >= next.r1 && merge.c1 <= next.c2 && merge.c2 >= next.c1
      const contained = merge.r1 >= next.r1 && merge.r2 <= next.r2 && merge.c1 >= next.c1 && merge.c2 <= next.c2
      if (touches && !contained) {
        next = {
          r1: Math.min(next.r1, merge.r1),
          c1: Math.min(next.c1, merge.c1),
          r2: Math.max(next.r2, merge.r2),
          c2: Math.max(next.c2, merge.c2)
        }
        changed = true
      }
    }
  }
  return next
}

export function normalizeDisplay(range: DisplayRange): DisplayRange {
  return {
    r1: Math.min(range.r1, range.r2),
    c1: Math.min(range.c1, range.c2),
    r2: Math.max(range.r1, range.r2),
    c2: Math.max(range.c1, range.c2)
  }
}

export function toSheetRange(layout: SheetLayout, range: DisplayRange): CellRange {
  const { r1, c1, r2, c2 } = normalizeDisplay(range)
  let rowMin = Infinity
  let rowMax = -Infinity
  for (let index = r1; index <= r2; index++) {
    const row = layout.rows[index]
    if (row === undefined) continue
    rowMin = Math.min(rowMin, row)
    rowMax = Math.max(rowMax, row)
  }
  return { r1: rowMin, c1: layout.columns[c1] ?? c1, r2: rowMax, c2: layout.columns[c2] ?? c2 }
}

export function toDisplayRange(layout: SheetLayout, range: CellRange): DisplayRange | null {
  const rows = mapped(layout.rowPosition, range.r1, range.r2)
  const columns = mapped(layout.columnPosition, range.c1, range.c2)
  if (!rows.length || !columns.length) return null
  const r1 = rows.reduce((a, b) => Math.min(a, b))
  const r2 = rows.reduce((a, b) => Math.max(a, b))
  if (r2 - r1 + 1 !== rows.length) return null
  return { r1, r2, c1: Math.min(...columns), c2: Math.max(...columns) }
}

export function rowHeaderWidth(rowCount: number): number {
  return Math.max(40, String(rowCount).length * 8 + 16)
}

export function columnAtOffset(lefts: number[], offset: number): number {
  let low = 0
  let high = lefts.length - 2
  while (low < high) {
    const middle = (low + high + 1) >> 1
    if (lefts[middle]! <= offset) low = middle
    else high = middle - 1
  }
  return Math.max(0, low)
}
