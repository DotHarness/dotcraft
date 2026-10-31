import {
  SPREADSHEET_MAX_COLUMNS,
  SPREADSHEET_MAX_ROWS,
  type SpreadsheetCell,
  type SpreadsheetSheet
} from '../../shared/viewer/spreadsheet'

export interface DelimitedRows {
  rows: string[][]
  hasMoreRows: boolean
}

export function parseDelimited(text: string, delimiter: string, maxRows = Infinity): DelimitedRows {
  const rows: string[][] = []
  let row: string[] = []
  let field = ''
  let quoted = false
  let index = text.charCodeAt(0) === 0xfeff ? 1 : 0
  const finishRow = () => {
    row.push(field)
    rows.push(row)
    row = []
    field = ''
  }
  while (index < text.length) {
    const char = text[index]!
    if (quoted) {
      if (char === '"') {
        if (text[index + 1] === '"') {
          field += '"'
          index += 2
          continue
        }
        quoted = false
      } else {
        field += char
      }
      index++
      continue
    }
    if (char === '"' && field === '') {
      quoted = true
    } else if (char === delimiter) {
      row.push(field)
      field = ''
    } else if (char === '\r' || char === '\n') {
      if (char === '\r' && text[index + 1] === '\n') index++
      finishRow()
      if (rows.length >= maxRows) {
        return { rows, hasMoreRows: index + 1 < text.length }
      }
    } else {
      field += char
    }
    index++
  }
  if (field !== '' || row.length > 0) finishRow()
  return { rows, hasMoreRows: false }
}

const NUMERIC = /^[-+]?[$€£¥]?(?:\d{1,3}(?:,\d{3})+|\d+)?(?:\.\d+)?(?:[eE][-+]?\d+)?%?$/

export function looksNumeric(text: string): boolean {
  const value = text.trim()
  return /\d/.test(value) && NUMERIC.test(value)
}

const CHARACTER_PX = 7
const WIDTH_SAMPLE_ROWS = 500

export function delimitedSheet(name: string, parsed: DelimitedRows): SpreadsheetSheet {
  const widest = parsed.rows.reduce((max, row) => Math.max(max, row.length), 0)
  const columnCount = Math.min(widest, SPREADSHEET_MAX_COLUMNS)
  const rowCount = Math.min(parsed.rows.length, SPREADSHEET_MAX_ROWS)
  const lengths = new Array<number>(columnCount).fill(0)
  const rows = parsed.rows.slice(0, rowCount).map((source, rowIndex) => {
    const cells: Array<SpreadsheetCell | null> = []
    for (let column = 0; column < Math.min(source.length, columnCount); column++) {
      const text = source[column]!
      if (rowIndex < WIDTH_SAMPLE_ROWS) lengths[column] = Math.max(lengths[column]!, text.length)
      cells.push(text === '' ? null : looksNumeric(text) ? { text, numeric: true } : { text })
    }
    while (cells.length && cells[cells.length - 1] === null) cells.pop()
    return cells.length ? cells : null
  })
  return {
    name,
    rows,
    rowCount,
    columnCount,
    truncated: parsed.hasMoreRows || parsed.rows.length > rowCount || widest > columnCount,
    columnWidths: lengths.map((length) => Math.min(320, Math.max(64, length * CHARACTER_PX + 16))),
    hiddenRows: [],
    hiddenColumns: [],
    merges: [],
    frozenRows: 0,
    frozenColumns: 0
  }
}
