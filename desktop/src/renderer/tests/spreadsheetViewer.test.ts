import { describe, expect, it } from 'vitest'
import {
  columnLetter,
  parseRangeAddress,
  rangeAddress,
  type SpreadsheetCell,
  type SpreadsheetSheet
} from '../../shared/viewer/spreadsheet'
import { selectionClipboard } from '../components/detail/viewers/spreadsheet/sheetClipboard'
import { applyView, columnValues, detectHeader, EMPTY_VIEW } from '../components/detail/viewers/spreadsheet/sheetFilter'
import { findCells, nearestMatch, stepMatch } from '../components/detail/viewers/spreadsheet/sheetFind'
import { baseRows, buildLayout, expandToMerges, toDisplayRange, toSheetRange } from '../components/detail/viewers/spreadsheet/sheetLayout'
import { addressLabel, moveSelection, rangeTarget } from '../components/detail/viewers/spreadsheet/sheetSelection'

function sheetOf(rows: Array<Array<string | number | null>>, extra: Partial<SpreadsheetSheet> = {}): SpreadsheetSheet {
  const cells = rows.map((row) =>
    row.map((value): SpreadsheetCell | null =>
      value === null ? null : typeof value === 'number' ? { text: String(value), numeric: true } : { text: value }
    )
  )
  const columnCount = Math.max(...rows.map((row) => row.length))
  return {
    name: 'Revenue',
    rows: cells,
    rowCount: rows.length,
    columnCount,
    truncated: false,
    columnWidths: new Array(columnCount).fill(80),
    hiddenRows: [],
    hiddenColumns: [],
    merges: [],
    frozenRows: 0,
    frozenColumns: 0,
    ...extra
  }
}

const sales = sheetOf([
  ['Region', 'Amount'],
  ['North', 30],
  ['South', 4],
  ['East', 200],
  ['South', null]
])

describe('cell addresses', () => {
  it('names columns past Z and round-trips range references', () => {
    expect([0, 25, 26, 701, 702].map(columnLetter)).toEqual(['A', 'Z', 'AA', 'ZZ', 'AAA'])
    expect(parseRangeAddress('$B$6:d8')).toEqual({ r1: 5, c1: 1, r2: 7, c2: 3 })
    expect(parseRangeAddress('C3')).toEqual({ r1: 2, c1: 2, r2: 2, c2: 2 })
    expect(parseRangeAddress('A0')).toBeNull()
    expect(rangeAddress({ r1: 7, c1: 3, r2: 5, c2: 1 })).toBe('B6:D8')
    expect(rangeTarget('Revenue', { r1: 0, c1: 0, r2: 0, c2: 0 })).toBe('Revenue!A1')
  })

  it('labels the selection by address, or by size while dragging', () => {
    expect(addressLabel({ r1: 1, c1: 1, r2: 4, c2: 3 }, false)).toBe('B2:D5')
    expect(addressLabel({ r1: 1, c1: 1, r2: 3, c2: 2 }, true)).toBe('3R x 2C')
    expect(addressLabel({ r1: 0, c1: 0, r2: 0, c2: 0 }, true)).toBe('A1')
  })

  it('moves the active cell and extends the far corner within the grid', () => {
    const start = { anchor: { row: 0, column: 0 }, focus: { row: 0, column: 0 } }
    expect(moveSelection(start, 'ArrowUp', false, 5, 2)).toEqual(start)
    const extended = moveSelection(moveSelection(start, 'ArrowDown', true, 5, 2), 'ArrowRight', true, 5, 2)
    expect(extended).toEqual({ anchor: { row: 0, column: 0 }, focus: { row: 1, column: 1 } })
    expect(moveSelection(extended, 'ArrowRight', false, 5, 2)).toEqual({
      anchor: { row: 0, column: 1 },
      focus: { row: 0, column: 1 }
    })
  })
})

describe('sheet layout', () => {
  it('skips hidden rows and columns and maps ranges between sheet and display space', () => {
    const sheet = sheetOf([['a', 'b', 'c'], ['d', 'e', 'f'], ['g', 'h', 'i']], { hiddenRows: [1], hiddenColumns: [1] })
    const layout = buildLayout(sheet, baseRows(sheet), false)
    expect(layout.rows).toEqual([0, 2])
    expect(layout.columns).toEqual([0, 2])
    expect(toDisplayRange(layout, { r1: 1, c1: 1, r2: 2, c2: 2 })).toEqual({ r1: 1, c1: 1, r2: 1, c2: 1 })
    expect(toSheetRange(layout, { r1: 0, c1: 0, r2: 1, c2: 1 })).toEqual({ r1: 0, c1: 0, r2: 2, c2: 2 })
  })

  it('grows a selection to cover every merge it touches', () => {
    const sheet = sheetOf([['a', 'b', 'c'], ['d', 'e', 'f'], ['g', 'h', 'i']], { merges: [{ r1: 0, c1: 1, r2: 1, c2: 2 }] })
    const layout = buildLayout(sheet, baseRows(sheet), false)
    expect(layout.covered.has('1:2')).toBe(true)
    expect(expandToMerges(layout, { r1: 1, c1: 0, r2: 1, c2: 1 })).toEqual({ r1: 0, c1: 0, r2: 1, c2: 2 })
  })
})

describe('filter and sort view', () => {
  it('detects a text header row above data, or uses the declared one', () => {
    expect(detectHeader(sales)).toEqual({ row: 0, c1: 0, c2: 1 })
    expect(detectHeader(sheetOf([[1, 2], [3, 4]]))).toBeNull()
    expect(detectHeader(sheetOf([['x', 'x'], ['a', 'b']]))).toBeNull()
    const declared = { row: 2, c1: 0, c2: 0 }
    expect(detectHeader({ ...sales, filterHeader: declared })).toBe(declared)
  })

  it('keeps the header in place, filters by excluded values, and sorts numbers numerically with blanks last', () => {
    const header = detectHeader(sales)!
    const rows = baseRows(sales)
    expect(applyView(sales, header, EMPTY_VIEW, rows)).toBe(rows)
    expect(applyView(sales, header, { excluded: { 0: ['South'] } }, rows)).toEqual([0, 1, 3])
    expect(applyView(sales, header, { excluded: {}, sort: { column: 1, direction: 'asc' } }, rows)).toEqual([0, 2, 1, 3, 4])
    expect(applyView(sales, header, { excluded: {}, sort: { column: 1, direction: 'desc' } }, rows)).toEqual([0, 3, 1, 2, 4])
    expect(applyView(sales, header, { excluded: {}, sort: { column: 0, direction: 'asc' } }, rows)).toEqual([0, 3, 1, 2, 4])
    expect(columnValues(sales, header, rows, 0)).toEqual(['East', 'North', 'South'])
    expect(columnValues(sales, header, rows, 1)).toEqual(['4', '30', '200', ''])
  })
})

describe('copy and find', () => {
  it('copies the selection as tab separated text and an HTML table with merges spanned', () => {
    const sheet = sheetOf([['a\tb', 'x'], ['<c>', 'd']], { merges: [{ r1: 0, c1: 1, r2: 1, c2: 1 }] })
    const layout = buildLayout(sheet, baseRows(sheet), false)
    expect(selectionClipboard(sheet, layout, { r1: 0, c1: 0, r2: 1, c2: 1 })).toEqual({
      text: '"a\tb"\tx\n<c>\t',
      html: '<table><tr><td>a\tb</td><td rowspan="2">x</td></tr><tr><td>&lt;c&gt;</td></tr></table>'
    })
  })

  it('finds cells case-insensitively in display order and steps through matches', () => {
    const layout = buildLayout(sales, baseRows(sales), false)
    const matches = findCells(sales, layout, 'SOUTH')
    expect(matches).toEqual([{ row: 2, column: 0 }, { row: 4, column: 0 }])
    expect(findCells(sales, layout, '  ')).toEqual([])
    expect(nearestMatch(matches, { row: 3, column: 0 })).toBe(1)
    expect(nearestMatch(matches, { row: 4, column: 1 })).toBe(0)
    expect(stepMatch(1, 2, 1)).toBe(0)
    expect(stepMatch(-1, 2, -1)).toBe(1)
  })
})
