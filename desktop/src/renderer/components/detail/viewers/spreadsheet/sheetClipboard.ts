import type { SpreadsheetSheet } from '../../../../../shared/viewer/spreadsheet'
import { cellAt, displayKey, normalizeDisplay, type DisplayRange, type SheetLayout } from './sheetLayout'

export interface ClipboardPayload {
  text: string
  html: string
}

function tsvField(value: string): string {
  return /[\t\r\n"]/.test(value) ? `"${value.replace(/"/g, '""')}"` : value
}

function escapeHtml(value: string): string {
  return value.replace(/[&<>"]/g, (char) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' })[char]!)
}

export function selectionClipboard(sheet: SpreadsheetSheet, layout: SheetLayout, selection: DisplayRange): ClipboardPayload {
  const { r1, c1, r2, c2 } = normalizeDisplay(selection)
  const lines: string[] = []
  const htmlRows: string[] = []
  for (let row = r1; row <= Math.min(r2, layout.rows.length - 1); row++) {
    const fields: string[] = []
    const htmlCells: string[] = []
    for (let column = c1; column <= Math.min(c2, layout.columns.length - 1); column++) {
      const key = displayKey(row, column)
      const covered = layout.covered.has(key)
      const text = covered ? '' : cellAt(sheet, layout.rows[row]!, layout.columns[column]!)?.text ?? ''
      fields.push(tsvField(text))
      if (covered) continue
      const merge = layout.merges.get(key)
      const span = merge
        ? `${merge.r2 > merge.r1 ? ` rowspan="${Math.min(merge.r2, r2) - row + 1}"` : ''}${merge.c2 > merge.c1 ? ` colspan="${Math.min(merge.c2, c2) - column + 1}"` : ''}`
        : ''
      htmlCells.push(`<td${span}>${escapeHtml(text)}</td>`)
    }
    lines.push(fields.join('\t'))
    htmlRows.push(`<tr>${htmlCells.join('')}</tr>`)
  }
  return { text: lines.join('\n'), html: `<table>${htmlRows.join('')}</table>` }
}
