import type { SpreadsheetFilterHeader, SpreadsheetSheet } from '../../../../../shared/viewer/spreadsheet'
import { cellAt } from './sheetLayout'

export type SortDirection = 'asc' | 'desc'

export interface SheetView {
  sort?: { column: number; direction: SortDirection }
  excluded: Record<number, string[]>
}

export const EMPTY_VIEW: SheetView = { excluded: {} }

const HEADER_SCAN_ROWS = 20

export function detectHeader(sheet: SpreadsheetSheet): SpreadsheetFilterHeader | null {
  if (sheet.filterHeader) return sheet.filterHeader
  for (let row = 0; row < Math.min(sheet.rowCount, HEADER_SCAN_ROWS); row++) {
    const cells = sheet.rows[row]
    if (!cells?.some((cell) => cell && cell.text.trim())) continue
    const filled = cells.flatMap((cell, column) => (cell && cell.text.trim() ? [{ cell, column }] : []))
    const labels = new Set(filled.map(({ cell }) => cell.text.trim().toLowerCase()))
    const hasData = sheet.rows.slice(row + 1).some((next) => next?.some((cell) => cell && cell.text.trim()))
    if (filled.length < 2 || labels.size !== filled.length || filled.some(({ cell }) => cell.numeric || cell.formula) || !hasData) {
      return null
    }
    return { row, c1: filled[0]!.column, c2: filled[filled.length - 1]!.column }
  }
  return null
}

export function isViewActive(view: SheetView): boolean {
  return Boolean(view.sort) || Object.values(view.excluded).some((values) => values.length > 0)
}

export function columnValues(sheet: SpreadsheetSheet, header: SpreadsheetFilterHeader, rows: number[], column: number): string[] {
  const values = new Set<string>()
  for (const row of rows) if (row > header.row) values.add(cellAt(sheet, row, column)?.text ?? '')
  return [...values].sort(compareText)
}

function compareText(left: string, right: string): number {
  if (left === '' || right === '') return left === right ? 0 : left === '' ? 1 : -1
  return left.localeCompare(right, undefined, { numeric: true, sensitivity: 'base' })
}

function numberValue(text: string): number | null {
  const cleaned = text.replace(/[,$€£¥%\s]/g, '').replace(/^\((.*)\)$/, '-$1')
  if (!cleaned || !/^[-+]?(\d+\.?\d*|\.\d+)(e[-+]?\d+)?$/i.test(cleaned)) return null
  return Number(cleaned)
}

export function applyView(sheet: SpreadsheetSheet, header: SpreadsheetFilterHeader | null, view: SheetView, rows: number[]): number[] {
  if (!header || !isViewActive(view)) return rows
  const fixed = rows.filter((row) => row <= header.row)
  const exclusions = Object.entries(view.excluded)
    .filter(([, values]) => values.length > 0)
    .map(([column, values]) => [Number(column), new Set(values)] as const)
  const data = rows.filter(
    (row) => row > header.row && exclusions.every(([column, values]) => !values.has(cellAt(sheet, row, column)?.text ?? ''))
  )
  if (view.sort) {
    const { column, direction } = view.sort
    const sign = direction === 'asc' ? 1 : -1
    data.sort((left, right) => {
      const a = cellAt(sheet, left, column)?.text ?? ''
      const b = cellAt(sheet, right, column)?.text ?? ''
      if (a === '' || b === '') return compareText(a, b)
      const x = numberValue(a)
      const y = numberValue(b)
      const order = x !== null && y !== null ? x - y : x !== null ? -1 : y !== null ? 1 : compareText(a, b)
      return order === 0 ? left - right : order * sign
    })
  }
  return [...fixed, ...data]
}
