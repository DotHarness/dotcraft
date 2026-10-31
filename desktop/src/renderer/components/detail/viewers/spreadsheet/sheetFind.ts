import type { SpreadsheetSheet } from '../../../../../shared/viewer/spreadsheet'
import { cellAt, displayKey, type SheetLayout } from './sheetLayout'
import type { CellPoint } from './sheetSelection'

export function findCells(sheet: SpreadsheetSheet, layout: SheetLayout, query: string): CellPoint[] {
  const needle = query.trim().toLocaleLowerCase()
  if (!needle) return []
  const matches: CellPoint[] = []
  layout.rows.forEach((sheetRow, row) => {
    const cells = sheet.rows[sheetRow]
    if (!cells) return
    layout.columns.forEach((sheetColumn, column) => {
      if (layout.covered.has(displayKey(row, column))) return
      const text = cellAt(sheet, sheetRow, sheetColumn)?.text
      if (text && text.toLocaleLowerCase().includes(needle)) matches.push({ row, column })
    })
  })
  return matches
}

export function nearestMatch(matches: CellPoint[], from: CellPoint | null): number {
  if (!matches.length) return -1
  if (!from) return 0
  const index = matches.findIndex((match) => match.row > from.row || (match.row === from.row && match.column >= from.column))
  return index < 0 ? 0 : index
}

export function stepMatch(index: number, count: number, direction: 1 | -1): number {
  if (count === 0) return -1
  return (((index < 0 ? (direction === 1 ? -1 : 0) : index) + direction) % count + count) % count
}
