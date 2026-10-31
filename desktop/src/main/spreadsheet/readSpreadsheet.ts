import { promises as fs } from 'fs'
import * as path from 'path'
import type { ReadSpreadsheetResult, SpreadsheetSheet } from '../../shared/viewer/spreadsheet'
import { SPREADSHEET_MAX_ROWS } from '../../shared/viewer/spreadsheet'
import { delimitedSheet, parseDelimited } from './delimited'
import { loadWorkbook, visibleWorksheets, worksheetModel } from './workbook'

const SPREADSHEET_MAX_BYTES = 40 * 1024 * 1024
const CACHE_LIMIT = 4

interface CachedWorkbook {
  stamp: string
  sheets: SpreadsheetSheet[]
}

const cache = new Map<string, Promise<CachedWorkbook>>()

async function buildSheets(absolutePath: string): Promise<SpreadsheetSheet[]> {
  const extension = path.extname(absolutePath).toLowerCase()
  const buffer = await fs.readFile(absolutePath)
  if (extension === '.csv' || extension === '.tsv') {
    const name = path.basename(absolutePath, path.extname(absolutePath))
    const parsed = parseDelimited(buffer.toString('utf-8'), extension === '.tsv' ? '\t' : ',', SPREADSHEET_MAX_ROWS)
    return [delimitedSheet(name, parsed)]
  }
  const workbook = await loadWorkbook(buffer)
  const date1904 = Boolean(workbook.properties?.date1904)
  return visibleWorksheets(workbook).map((worksheet) => worksheetModel(worksheet, date1904))
}

export async function readSpreadsheet(absolutePath: string, sheetIndex = 0): Promise<ReadSpreadsheetResult> {
  const stat = await fs.stat(absolutePath)
  if (!stat.isFile()) throw new Error(`Not a file: ${absolutePath}`)
  if (stat.size > SPREADSHEET_MAX_BYTES) throw new Error('File is larger than the 40 MiB spreadsheet limit')
  const stamp = `${stat.mtimeMs}:${stat.size}`
  let entry = cache.get(absolutePath)
  if (!entry || (await entry.catch(() => null))?.stamp !== stamp) {
    const pending = buildSheets(absolutePath).then((sheets) => ({ stamp, sheets }))
    entry = pending
    cache.set(absolutePath, pending)
    pending.catch(() => {
      if (cache.get(absolutePath) === pending) cache.delete(absolutePath)
    })
  } else {
    cache.delete(absolutePath)
    cache.set(absolutePath, entry)
  }
  while (cache.size > CACHE_LIMIT) cache.delete(cache.keys().next().value!)
  const { sheets } = await entry
  if (sheets.length === 0) throw new Error('The workbook has no visible sheets')
  const index = Math.min(Math.max(0, Math.floor(sheetIndex)), sheets.length - 1)
  return {
    sheets: sheets.map(({ name, tabColor }) => (tabColor ? { name, tabColor } : { name })),
    sheetIndex: index,
    sheet: sheets[index]!
  }
}
