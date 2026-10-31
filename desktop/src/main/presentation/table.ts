import type { SlideTable, TableCell } from '../../shared/viewer/presentation'
import { emu, fillNode, parseFill } from './fill'
import type { ShapeContext } from './elements'
import { parseTextBody, withRunOverrides } from './text'
import { schemeColor } from './theme'
import { boolAttr, child, children, numberAttr, type XmlNode } from './xml'

const NO_STYLE_NO_GRID = '{2D5ABB26-0587-4C30-8999-92F81FD0307C}'
const NO_STYLE_GRID = '{5940675A-B579-460E-94D1-54222C63F5DA}'

interface CellLook {
  fill?: string
  border?: string
  color?: string
  bold?: boolean
}

function tableLook(tblPr: XmlNode | undefined, context: ShapeContext): (row: number) => CellLook {
  const styleId = child(tblPr, 'tableStyleId')?.text.trim()
  const firstRow = boolAttr(tblPr, 'firstRow') ?? false
  const banded = boolAttr(tblPr, 'bandRow') ?? false
  if (!styleId || styleId === NO_STYLE_NO_GRID) return () => ({})
  if (styleId === NO_STYLE_GRID) return () => ({ border: schemeColor('tx1', context) })
  const header: CellLook = { fill: schemeColor('accent1', context), color: schemeColor('lt1', context), bold: true }
  const light = schemeColor('accent1', context, 20000)
  const dark = schemeColor('accent1', context, 40000)
  const border = schemeColor('lt1', context)
  return (row) => {
    if (firstRow && row === 0) return { ...header, border }
    const bodyRow = firstRow ? row - 1 : row
    return { fill: banded && bodyRow % 2 === 0 ? dark : light, border }
  }
}

export function parseTable(tbl: XmlNode | undefined, context: ShapeContext): SlideTable | undefined {
  if (!tbl) return undefined
  const columns = children(child(tbl, 'tblGrid'), 'gridCol').map((column) => emu(numberAttr(column, 'w')))
  if (columns.length === 0) return undefined
  const look = tableLook(child(tbl, 'tblPr'), context)
  const rows = children(tbl, 'tr').map((tr, rowIndex) => {
    const style = look(rowIndex)
    const levels = withRunOverrides(context.styles.plain, {
      ...(style.color ? { color: style.color } : {}),
      ...(style.bold ? { bold: true } : {})
    })
    const cells = children(tr, 'tc').map((tc): TableCell | null => {
      if (boolAttr(tc, 'hMerge') || boolAttr(tc, 'vMerge')) return null
      const tcPr = child(tc, 'tcPr')
      const text = parseTextBody(child(tc, 'txBody'), levels, [], context) ?? { paragraphs: [], anchor: 'top', insets: { left: 0, top: 0, right: 0, bottom: 0 }, wrap: true }
      const margin = (name: string, fallback: number) => emu(numberAttr(tcPr, name) ?? fallback)
      text.insets = { left: margin('marL', 91440), top: margin('marT', 45720), right: margin('marR', 91440), bottom: margin('marB', 45720) }
      text.anchor = tcPr?.attrs.anchor === 'ctr' ? 'middle' : tcPr?.attrs.anchor === 'b' ? 'bottom' : 'top'
      text.wrap = true
      const cell: TableCell = { text }
      const own = parseFill(fillNode(tcPr), context)
      const fill = own === null ? undefined : own && 'color' in own ? own.color : style.fill
      if (fill) cell.fill = fill
      if (style.border) cell.border = style.border
      const colSpan = numberAttr(tc, 'gridSpan') ?? 1
      const rowSpan = numberAttr(tc, 'rowSpan') ?? 1
      if (colSpan > 1) cell.colSpan = colSpan
      if (rowSpan > 1) cell.rowSpan = rowSpan
      return cell
    })
    return { height: emu(numberAttr(tr, 'h')), cells }
  })
  return { columns, rows }
}
