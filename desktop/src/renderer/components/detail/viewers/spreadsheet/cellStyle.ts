import type { CSSProperties } from 'react'
import type { SpreadsheetCell } from '../../../../../shared/viewer/spreadsheet'

function luminance(hex: string): number {
  const value = Number.parseInt(hex.slice(1), 16)
  const channel = (shift: number) => {
    const c = ((value >> shift) & 0xff) / 255
    return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4
  }
  return 0.2126 * channel(16) + 0.7152 * channel(8) + 0.0722 * channel(0)
}

function contrast(a: string, b: string): number {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x) as [number, number]
  return (light + 0.05) / (dark + 0.05)
}

export function cellStyle(cell: SpreadsheetCell | null): CSSProperties {
  if (!cell) return {}
  const style: CSSProperties = {}
  if (cell.bold) style.fontWeight = 600
  if (cell.italic) style.fontStyle = 'italic'
  if (cell.align) style.textAlign = cell.align
  if (cell.fill) {
    style.backgroundColor = cell.fill
    const ink = cell.color && contrast(cell.color, cell.fill) >= 3 ? cell.color : luminance(cell.fill) > 0.4 ? '#1f1f1f' : '#f5f5f5'
    style.color = ink
  } else if (cell.color) {
    const tone = luminance(cell.color)
    if (tone > 0.02 && tone < 0.85) style.color = cell.color
  }
  return style
}
