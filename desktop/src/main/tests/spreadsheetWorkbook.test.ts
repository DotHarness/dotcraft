import { afterEach, describe, expect, it } from 'vitest'
import { mkdtempSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import ExcelJS from 'exceljs'
import { readSpreadsheet } from '../spreadsheet/readSpreadsheet'
import { formatCellNumber } from '../spreadsheet/numberFormat'

const tempDirs: string[] = []

afterEach(() => {
  for (const dir of tempDirs.splice(0)) rmSync(dir, { recursive: true, force: true })
})

async function writeWorkbook(build: (workbook: ExcelJS.Workbook) => void): Promise<string> {
  const workbook = new ExcelJS.Workbook()
  build(workbook)
  const dir = mkdtempSync(join(tmpdir(), 'spreadsheet-test-'))
  tempDirs.push(dir)
  const file = join(dir, 'model.xlsx')
  writeFileSync(file, Buffer.from(await workbook.xlsx.writeBuffer()))
  return file
}

describe('readSpreadsheet', () => {
  it('builds the visible sheets with display text, formulas, links, merges, hidden columns, and frozen panes', async () => {
    const file = await writeWorkbook((workbook) => {
      const sheet = workbook.addWorksheet('Revenue', {
        properties: { tabColor: { argb: 'FF22AA44' } },
        views: [{ state: 'frozen', xSplit: 1, ySplit: 1 }]
      })
      sheet.addTable({
        name: 'Sales',
        ref: 'A1',
        headerRow: true,
        columns: [{ name: 'Region' }, { name: 'Amount' }],
        rows: [['North', 1200], ['South', 800]]
      })
      sheet.getCell('D1').value = { formula: 'SUM(B2:B3)', result: 2000 }
      sheet.getCell('D2').value = 0.256
      sheet.getCell('D2').numFmt = '0.0%'
      sheet.getCell('D3').value = new Date(Date.UTC(2024, 0, 31))
      sheet.getCell('D3').numFmt = 'yyyy-mm-dd'
      sheet.getCell('E1').value = { text: 'Site', hyperlink: 'https://example.com/' }
      sheet.getCell('E2').value = 'Merged'
      sheet.getCell('E2').font = { bold: true }
      sheet.mergeCells('E2:F3')
      sheet.getColumn(3).hidden = true
      workbook.addWorksheet('Secret', { state: 'hidden' }).getCell('A1').value = 'hidden'
      workbook.addWorksheet('Notes').getCell('A1').value = 'second'
    })

    const result = await readSpreadsheet(file)
    expect(result.sheets).toEqual([{ name: 'Revenue', tabColor: '#22AA44' }, { name: 'Notes' }])
    const { sheet } = result
    expect(sheet.rows[0]?.[3]).toEqual({ text: '2000', numeric: true, formula: 'SUM(B2:B3)' })
    expect(sheet.rows[1]?.[3]).toMatchObject({ text: '25.6%', numeric: true })
    expect(sheet.rows[2]?.[3]).toMatchObject({ text: '2024-01-31', numeric: true })
    expect(sheet.rows[0]?.[4]).toMatchObject({ text: 'Site', link: 'https://example.com/' })
    expect(sheet.rows[1]?.[4]).toMatchObject({ text: 'Merged', bold: true })
    expect(sheet.rows[1]?.[5] ?? null).toBeNull()
    expect(sheet.merges).toEqual([{ r1: 1, c1: 4, r2: 2, c2: 5 }])
    expect(sheet.hiddenColumns).toEqual([2])
    expect(sheet).toMatchObject({ frozenRows: 1, frozenColumns: 1, filterHeader: { row: 0, c1: 0, c2: 1 }, truncated: false })

    const second = await readSpreadsheet(file, 1)
    expect(second.sheetIndex).toBe(1)
    expect(second.sheet.rows[0]?.[0]).toEqual({ text: 'second' })
  })

  it('reports truncation when a sheet exceeds the row bound', async () => {
    const file = await writeWorkbook((workbook) => {
      workbook.addWorksheet('Big').getCell('A2500').value = 1
    })
    const { sheet } = await readSpreadsheet(file)
    expect(sheet.rowCount).toBe(2000)
    expect(sheet.truncated).toBe(true)
  })

  it('reads csv and tsv files as a single sheet named after the file', async () => {
    const dir = mkdtempSync(join(tmpdir(), 'spreadsheet-test-'))
    tempDirs.push(dir)
    const file = join(dir, 'people.tsv')
    writeFileSync(file, 'name\tage\nAda\t36\n')
    const result = await readSpreadsheet(file)
    expect(result.sheets).toEqual([{ name: 'people' }])
    expect(result.sheet.rows[1]).toEqual([{ text: 'Ada' }, { text: '36', numeric: true }])
  })
})

describe('formatCellNumber', () => {
  it('applies common number, percent, currency, negative, and date formats', () => {
    expect(formatCellNumber(1234.5, '#,##0.00')).toBe('1,234.50')
    expect(formatCellNumber(-1234.5, '#,##0;(#,##0)')).toBe('(1,235)')
    expect(formatCellNumber(-2, '0.0')).toBe('-2.0')
    expect(formatCellNumber(0.5, '0%')).toBe('50%')
    expect(formatCellNumber(1500, '"$"#,##0')).toBe('$1,500')
    expect(formatCellNumber(12345, '0.00E+00')).toBe('1.23E+04')
    expect(formatCellNumber(45322.75, 'm/d/yyyy h:mm AM/PM')).toBe('1/31/2024 6:00 PM')
    expect(formatCellNumber(45322, 'mmm d, yyyy')).toBe('Jan 31, 2024')
    expect(formatCellNumber(0.1 + 0.2, undefined)).toBe('0.3')
  })
})
