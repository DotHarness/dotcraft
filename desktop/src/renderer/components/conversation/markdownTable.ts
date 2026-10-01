import type { ExtraProps } from 'react-markdown'

type Element = NonNullable<ExtraProps['node']>
type ElementContent = Element['children'][number]

const COLUMN_SPANS: ReadonlyArray<readonly [maxChars: number, span: number]> = [
  [40, 4],
  [100, 6],
  [160, 8]
]
const WIDEST_COLUMN_SPAN = 14

function textLength(node: ElementContent): number {
  if (node.type === 'text') return node.value.length
  if (node.type !== 'element') return 0
  return node.children.reduce((total, child) => total + textLength(child), 0)
}

function rows(node: Element): Element[] {
  return node.children.flatMap((child) => {
    if (child.type !== 'element') return []
    if (child.tagName === 'tr') return [child]
    return rows(child)
  })
}

function cells(row: Element): Element[] {
  return row.children.filter((cell): cell is Element =>
    cell.type === 'element' && (cell.tagName === 'th' || cell.tagName === 'td')
  )
}

export function tableCellMinWidths(table: Element | undefined): ReadonlyMap<Element, string> {
  const widths = new Map<Element, string>()
  if (!table) return widths
  const tableRows = rows(table).map(cells)
  const longest: number[] = []
  for (const row of tableRows) {
    row.forEach((cell, index) => {
      longest[index] = Math.max(longest[index] ?? 0, textLength(cell))
    })
  }
  const columnWidths = longest.map((chars) => {
    const span = COLUMN_SPANS.find(([maxChars]) => chars <= maxChars)?.[1] ?? WIDEST_COLUMN_SPAN
    return `calc(var(--conversation-reading-width) * ${span} / 24)`
  })
  for (const row of tableRows) {
    row.forEach((cell, index) => widths.set(cell, columnWidths[index]))
  }
  return widths
}
