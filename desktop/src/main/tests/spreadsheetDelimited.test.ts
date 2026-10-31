import { describe, expect, it } from 'vitest'
import { delimitedSheet, looksNumeric, parseDelimited } from '../spreadsheet/delimited'
import { SPREADSHEET_MAX_COLUMNS } from '../../shared/viewer/spreadsheet'

describe('parseDelimited', () => {
  it('reads quoted fields with delimiters, escaped quotes, and line breaks across CRLF records', () => {
    const text = '﻿name,note\r\n"Smith, J","said ""hi""\r\nthen left"\r\nplain,\r\n'
    expect(parseDelimited(text, ',').rows).toEqual([
      ['name', 'note'],
      ['Smith, J', 'said "hi"\r\nthen left'],
      ['plain', '']
    ])
  })

  it('splits tab separated text and keeps a final record without a line break', () => {
    expect(parseDelimited('a\tb\n1\t2', '\t').rows).toEqual([['a', 'b'], ['1', '2']])
  })

  it('stops after the row limit and reports that more rows follow', () => {
    expect(parseDelimited('1\n2\n3\n', ',', 2)).toEqual({ rows: [['1'], ['2']], hasMoreRows: true })
    expect(parseDelimited('1\n2\n', ',', 2)).toEqual({ rows: [['1'], ['2']], hasMoreRows: false })
  })
})

describe('delimitedSheet', () => {
  it('marks numeric cells, drops empty cells, and reports truncation past the column bound', () => {
    const wide = Array.from({ length: SPREADSHEET_MAX_COLUMNS + 5 }, (_, index) => String(index))
    const sheet = delimitedSheet('data', { rows: [['Region', '1,200.50', ''], wide], hasMoreRows: false })
    expect(sheet.columnCount).toBe(SPREADSHEET_MAX_COLUMNS)
    expect(sheet.truncated).toBe(true)
    expect(sheet.rows[0]).toEqual([{ text: 'Region' }, { text: '1,200.50', numeric: true }])
    expect(sheet.rows[1]).toHaveLength(SPREADSHEET_MAX_COLUMNS)
  })

  it('recognizes numbers, percentages, and currency but not codes or text', () => {
    expect(['42', '-3.5', '12%', '$1,000', '1e5'].every(looksNumeric)).toBe(true)
    expect(['A1', '12-34', '', 'n/a', '1,00'].some(looksNumeric)).toBe(false)
  })
})
