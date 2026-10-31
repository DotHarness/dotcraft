export const SPREADSHEET_MAX_ROWS = 2000
export const SPREADSHEET_MAX_COLUMNS = 100

export interface SpreadsheetCell {
  text: string
  formula?: string
  link?: string
  numeric?: boolean
  bold?: boolean
  italic?: boolean
  color?: string
  fill?: string
  align?: 'left' | 'center' | 'right'
}

/** Zero-based, inclusive cell range. */
export interface CellRange {
  r1: number
  c1: number
  r2: number
  c2: number
}

export interface SpreadsheetFilterHeader {
  row: number
  c1: number
  c2: number
}

export interface SpreadsheetSheet {
  name: string
  tabColor?: string
  rows: Array<Array<SpreadsheetCell | null> | null>
  rowCount: number
  columnCount: number
  truncated: boolean
  columnWidths: number[]
  hiddenRows: number[]
  hiddenColumns: number[]
  merges: CellRange[]
  frozenRows: number
  frozenColumns: number
  filterHeader?: SpreadsheetFilterHeader
}

export interface SpreadsheetSheetSummary {
  name: string
  tabColor?: string
}

export interface ReadSpreadsheetParams {
  absolutePath: string
  sheetIndex?: number
}

export interface ReadSpreadsheetResult {
  sheets: SpreadsheetSheetSummary[]
  sheetIndex: number
  sheet: SpreadsheetSheet
}

export function columnLetter(index: number): string {
  let name = ''
  let value = index + 1
  while (value > 0) {
    const rest = (value - 1) % 26
    name = String.fromCharCode(65 + rest) + name
    value = Math.floor((value - 1) / 26)
  }
  return name
}

function normalizeRange(range: CellRange): CellRange {
  return {
    r1: Math.min(range.r1, range.r2),
    c1: Math.min(range.c1, range.c2),
    r2: Math.max(range.r1, range.r2),
    c2: Math.max(range.c1, range.c2)
  }
}

export function cellAddress(row: number, column: number): string {
  return `${columnLetter(column)}${row + 1}`
}

export function rangeAddress(range: CellRange): string {
  const { r1, c1, r2, c2 } = normalizeRange(range)
  const start = cellAddress(r1, c1)
  return r1 === r2 && c1 === c2 ? start : `${start}:${cellAddress(r2, c2)}`
}

function parseCell(reference: string): { row: number; column: number } | null {
  const match = /^\$?([A-Za-z]{1,3})\$?(\d{1,7})$/.exec(reference.trim())
  if (!match) return null
  let column = 0
  for (const letter of match[1]!.toUpperCase()) column = column * 26 + (letter.charCodeAt(0) - 64)
  const row = Number(match[2])
  return row > 0 ? { row: row - 1, column: column - 1 } : null
}

export function parseRangeAddress(reference: string): CellRange | null {
  const [start, end = start, extra] = reference.split(':')
  if (extra !== undefined || !start) return null
  const first = parseCell(start)
  const last = parseCell(end!)
  if (!first || !last) return null
  return normalizeRange({ r1: first.row, c1: first.column, r2: last.row, c2: last.column })
}
