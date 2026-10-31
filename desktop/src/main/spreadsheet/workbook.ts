import type { Cell, Workbook, Worksheet } from 'exceljs'
import {
  SPREADSHEET_MAX_COLUMNS,
  SPREADSHEET_MAX_ROWS,
  parseRangeAddress,
  type CellRange,
  type SpreadsheetCell,
  type SpreadsheetFilterHeader,
  type SpreadsheetSheet
} from '../../shared/viewer/spreadsheet'
import { dateToSerial, formatCellNumber, generalNumber } from './numberFormat'

const MERGE_VALUE_TYPE = 1
const DEFAULT_COLUMN_CHARACTERS = 8.43
const CHARACTER_PX = 7

interface MergeDimensions {
  top: number
  left: number
  bottom: number
  right: number
}

type ExcelModule = typeof import('exceljs')

export async function loadWorkbook(buffer: Buffer): Promise<Workbook> {
  const imported = (await import('exceljs')) as ExcelModule & { default?: ExcelModule }
  const excel = imported.default ?? imported
  const workbook = new excel.Workbook()
  await workbook.xlsx.load(buffer as unknown as ArrayBuffer)
  return workbook
}

function hexColor(color: { argb?: string } | undefined): string | undefined {
  const argb = color?.argb
  if (!argb || !/^[0-9a-f]{6,8}$/i.test(argb)) return undefined
  return `#${argb.slice(-6).toUpperCase()}`
}

function isLink(value: string): boolean {
  return /^(https?:|mailto:)/i.test(value)
}

function cellText(cell: Cell, date1904: boolean): Pick<SpreadsheetCell, 'text' | 'numeric'> {
  const value = cell.value as unknown
  const format = cell.numFmt
  const result =
    value && typeof value === 'object' && ('formula' in value || 'sharedFormula' in value)
      ? (value as { result?: unknown }).result
      : value
  if (typeof result === 'number') return { text: formatCellNumber(result, format, date1904), numeric: true }
  if (result instanceof Date) {
    const serial = dateToSerial(result, date1904)
    return { text: format ? formatCellNumber(serial, format, date1904) : result.toISOString().slice(0, 10), numeric: true }
  }
  if (typeof result === 'boolean') return { text: result ? 'TRUE' : 'FALSE' }
  if (result && typeof result === 'object' && 'error' in result) return { text: String((result as { error: unknown }).error) }
  if (result === null || result === undefined) return { text: '' }
  try {
    return { text: cell.text ?? '' }
  } catch {
    return { text: typeof result === 'string' ? result : generalNumber(Number(result)) }
  }
}

function readCell(cell: Cell, date1904: boolean): SpreadsheetCell | null {
  const { text, numeric } = cellText(cell, date1904)
  const formula = cell.formula
  const hyperlink = cell.hyperlink
  if (!text && !formula) return null
  const entry: SpreadsheetCell = { text }
  if (numeric) entry.numeric = true
  if (formula) entry.formula = formula
  if (typeof hyperlink === 'string' && isLink(hyperlink)) entry.link = hyperlink
  const { font, fill, alignment } = cell
  if (font?.bold) entry.bold = true
  if (font?.italic) entry.italic = true
  const color = hexColor(font?.color)
  if (color) entry.color = color
  if (fill?.type === 'pattern' && fill.pattern === 'solid') {
    const background = hexColor(fill.fgColor)
    if (background) entry.fill = background
  }
  const horizontal = alignment?.horizontal
  if (horizontal === 'left' || horizontal === 'center' || horizontal === 'right') entry.align = horizontal
  return entry
}

function filterHeaderFor(worksheet: Worksheet): SpreadsheetFilterHeader | undefined {
  const tables = worksheet.getTables() as unknown as Array<{ table?: { tableRef?: string; ref?: string; headerRow?: boolean } }>
  for (const entry of tables) {
    const reference = entry.table?.tableRef ?? entry.table?.ref
    const range = reference ? parseRangeAddress(reference) : null
    if (range && entry.table?.headerRow !== false) return { row: range.r1, c1: range.c1, c2: range.c2 }
  }
  const filter = worksheet.autoFilter as unknown
  const reference =
    typeof filter === 'string'
      ? filter
      : filter && typeof filter === 'object' && 'from' in filter && 'to' in filter
        ? `${String((filter as { from: unknown }).from)}:${String((filter as { to: unknown }).to)}`
        : null
  const range = reference ? parseRangeAddress(reference) : null
  return range ? { row: range.r1, c1: range.c1, c2: range.c2 } : undefined
}

function mergesWithin(worksheet: Worksheet, rowCount: number, columnCount: number): CellRange[] {
  const merges = (worksheet as unknown as { _merges?: Record<string, MergeDimensions | undefined> })._merges ?? {}
  const ranges: CellRange[] = []
  for (const merge of Object.values(merges)) {
    if (!merge || merge.top > rowCount || merge.left > columnCount) continue
    ranges.push({
      r1: merge.top - 1,
      c1: merge.left - 1,
      r2: Math.min(merge.bottom, rowCount) - 1,
      c2: Math.min(merge.right, columnCount) - 1
    })
  }
  return ranges
}

export function worksheetModel(worksheet: Worksheet, date1904: boolean): SpreadsheetSheet {
  const totalRows = worksheet.rowCount
  const totalColumns = worksheet.columnCount
  const rowCount = Math.min(totalRows, SPREADSHEET_MAX_ROWS)
  const columnCount = Math.min(totalColumns, SPREADSHEET_MAX_COLUMNS)
  const rows: SpreadsheetSheet['rows'] = []
  const hiddenRows: number[] = []
  for (let rowNumber = 1; rowNumber <= rowCount; rowNumber++) {
    const row = worksheet.findRow(rowNumber)
    if (!row) {
      rows.push(null)
      continue
    }
    if (row.hidden) hiddenRows.push(rowNumber - 1)
    const cells: Array<SpreadsheetCell | null> = []
    row.eachCell({ includeEmpty: false }, (cell, columnNumber) => {
      if (columnNumber > columnCount || (cell.type as number) === MERGE_VALUE_TYPE) return
      const entry = readCell(cell, date1904)
      if (!entry) return
      while (cells.length < columnNumber - 1) cells.push(null)
      cells[columnNumber - 1] = entry
    })
    rows.push(cells.length ? cells : null)
  }

  const defaultCharacters = worksheet.properties.defaultColWidth ?? DEFAULT_COLUMN_CHARACTERS
  const columnWidths: number[] = []
  const hiddenColumns: number[] = []
  for (let columnNumber = 1; columnNumber <= columnCount; columnNumber++) {
    const column = worksheet.getColumn(columnNumber)
    if (column.hidden) hiddenColumns.push(columnNumber - 1)
    const characters = column.width ?? defaultCharacters
    columnWidths.push(Math.min(480, Math.max(24, Math.round(characters * CHARACTER_PX + 5))))
  }

  const view = worksheet.views?.[0] as { state?: string; xSplit?: number; ySplit?: number } | undefined
  const frozen = view?.state === 'frozen'
  const tabColor = hexColor(worksheet.properties.tabColor)
  const filterHeader = filterHeaderFor(worksheet)
  return {
    name: worksheet.name,
    ...(tabColor ? { tabColor } : {}),
    rows,
    rowCount,
    columnCount,
    truncated: totalRows > rowCount || totalColumns > columnCount,
    columnWidths,
    hiddenRows,
    hiddenColumns,
    merges: mergesWithin(worksheet, rowCount, columnCount),
    frozenRows: frozen ? Math.min(view?.ySplit ?? 0, rowCount) : 0,
    frozenColumns: frozen ? Math.min(view?.xSplit ?? 0, columnCount) : 0,
    ...(filterHeader && filterHeader.row < rowCount ? { filterHeader } : {})
  }
}

export function visibleWorksheets(workbook: Workbook): Worksheet[] {
  return workbook.worksheets.filter((worksheet) => worksheet.state !== 'hidden' && worksheet.state !== 'veryHidden')
}
