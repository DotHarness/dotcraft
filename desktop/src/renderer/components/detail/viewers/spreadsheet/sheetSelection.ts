import { cellAddress, rangeAddress, type CellRange } from '../../../../../shared/viewer/spreadsheet'
import { normalizeDisplay, type DisplayRange } from './sheetLayout'

export interface CellPoint {
  row: number
  column: number
}

export interface GridSelection {
  anchor: CellPoint
  focus: CellPoint
}

export function selectionRange(selection: GridSelection): DisplayRange {
  return normalizeDisplay({
    r1: selection.anchor.row,
    c1: selection.anchor.column,
    r2: selection.focus.row,
    c2: selection.focus.column
  })
}

export function collapsed(point: CellPoint): GridSelection {
  return { anchor: point, focus: point }
}

export function extendTo(selection: GridSelection | null, point: CellPoint): GridSelection {
  return selection ? { anchor: selection.anchor, focus: point } : collapsed(point)
}

export function selectAll(rowCount: number, columnCount: number): GridSelection | null {
  if (rowCount <= 0 || columnCount <= 0) return null
  return { anchor: { row: 0, column: 0 }, focus: { row: rowCount - 1, column: columnCount - 1 } }
}

export function selectColumns(from: number, to: number, rowCount: number): GridSelection | null {
  if (rowCount <= 0) return null
  return { anchor: { row: 0, column: from }, focus: { row: rowCount - 1, column: to } }
}

export function selectRows(from: number, to: number, columnCount: number): GridSelection | null {
  if (columnCount <= 0) return null
  return { anchor: { row: from, column: 0 }, focus: { row: to, column: columnCount - 1 } }
}

const ARROWS: Record<string, [number, number]> = {
  ArrowUp: [-1, 0],
  ArrowDown: [1, 0],
  ArrowLeft: [0, -1],
  ArrowRight: [0, 1]
}

export function isArrowKey(key: string): boolean {
  return key in ARROWS
}

export function moveSelection(
  selection: GridSelection | null,
  key: string,
  extend: boolean,
  rowCount: number,
  columnCount: number
): GridSelection | null {
  const delta = ARROWS[key]
  if (!delta || rowCount <= 0 || columnCount <= 0) return selection
  if (!selection) return collapsed({ row: 0, column: 0 })
  const from = extend ? selection.focus : selection.anchor
  const point = {
    row: Math.min(rowCount - 1, Math.max(0, from.row + delta[0])),
    column: Math.min(columnCount - 1, Math.max(0, from.column + delta[1]))
  }
  return extend ? { anchor: selection.anchor, focus: point } : collapsed(point)
}

export function addressLabel(range: CellRange, dragging: boolean): string {
  const rows = range.r2 - range.r1 + 1
  const columns = range.c2 - range.c1 + 1
  if (dragging && (rows > 1 || columns > 1)) return `${rows}R x ${columns}C`
  return rows === 1 && columns === 1 ? cellAddress(range.r1, range.c1) : rangeAddress(range)
}

export function rangeTarget(sheet: string, range: CellRange): string {
  return `${sheet}!${rangeAddress(range)}`
}
