import {
  forwardRef,
  useCallback,
  useImperativeHandle,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type KeyboardEvent,
  type MouseEvent,
  type ReactNode
} from 'react'
import { ChevronDown, ListFilter } from 'lucide-react'
import { columnLetter, type SpreadsheetFilterHeader, type SpreadsheetSheet } from '../../../../../shared/viewer/spreadsheet'
import type { PreviewBox, VisibleSpan } from '../PreviewComments'
import { useWheelZoom } from '../viewerZoom'
import { cellStyle } from './cellStyle'
import {
  HEADER_HEIGHT,
  ROW_HEIGHT,
  cellAt,
  columnAtOffset,
  displayKey,
  rowHeaderWidth,
  type DisplayRange,
  type SheetLayout
} from './sheetLayout'
import type { CellPoint } from './sheetSelection'

const OVERSCAN_ROWS = 8
const OVERSCAN_COLUMNS = 3
const CHARACTER_PX = 7

export interface GridOverlay {
  key: string
  range: DisplayRange
  render: (box: PreviewBox, visible: VisibleSpan) => ReactNode
}

export interface SheetGridHandle {
  focus: () => void
  reveal: (range: DisplayRange) => void
  fitZoom: () => number
}

interface SheetGridProps {
  sheet: SpreadsheetSheet
  layout: SheetLayout
  zoom: number
  label: string
  selected: DisplayRange | null
  header: SpreadsheetFilterHeader | null
  activeFilters: Set<number>
  matches: Set<string>
  activeMatch: CellPoint | null
  overlays: GridOverlay[]
  onZoom: (update: (current: number) => number) => void
  onCellDown: (point: CellPoint, extend: boolean) => void
  onCellEnter: (point: CellPoint) => void
  onColumnDown: (column: number, extend: boolean) => void
  onRowDown: (row: number, extend: boolean) => void
  onLink: (url: string) => void
  onContextMenu: (event: MouseEvent, point: CellPoint | null) => void
  onFilter: (column: number, anchor: DOMRect) => void
  onKeyDown: (event: KeyboardEvent<HTMLDivElement>) => void
}

interface Viewport {
  top: number
  left: number
  width: number
  height: number
}

function contains(range: DisplayRange | null, row: number, column: number): boolean {
  return Boolean(range && row >= range.r1 && row <= range.r2 && column >= range.c1 && column <= range.c2)
}

export const SheetGrid = forwardRef<SheetGridHandle, SheetGridProps>(function SheetGrid(props, ref) {
  const { sheet, layout, zoom, selected, header, activeFilters, matches, activeMatch, overlays } = props
  const scrollRef = useRef<HTMLDivElement>(null)
  const [viewport, setViewport] = useState<Viewport>({ top: 0, left: 0, width: 0, height: 0 })
  const frame = useRef(0)

  const rowHeight = ROW_HEIGHT * zoom
  const headerHeight = HEADER_HEIGHT * zoom
  const headerWidth = rowHeaderWidth((layout.rows[layout.rows.length - 1] ?? 0) + 1) * zoom
  const lefts = layout.columnLefts
  const frozenHeight = layout.frozenRows * rowHeight
  const frozenWidth = (lefts[layout.frozenColumns] ?? 0) * zoom
  const width = headerWidth + (lefts[lefts.length - 1] ?? 0) * zoom
  const height = headerHeight + layout.rows.length * rowHeight
  const bodyTop = headerHeight + frozenHeight

  const measure = useCallback(() => {
    const element = scrollRef.current
    if (!element) return
    setViewport({ top: element.scrollTop, left: element.scrollLeft, width: element.clientWidth, height: element.clientHeight })
  }, [])

  useLayoutEffect(() => {
    measure()
    const element = scrollRef.current
    if (!element) return
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [measure])

  useWheelZoom(scrollRef, props.onZoom)

  const canvasBox = (range: DisplayRange): PreviewBox => {
    const r2 = Math.min(range.r2, layout.rows.length - 1)
    const c2 = Math.min(range.c2, layout.columns.length - 1)
    return {
      top: headerHeight + range.r1 * rowHeight,
      left: headerWidth + (lefts[range.c1] ?? 0) * zoom,
      width: ((lefts[c2 + 1] ?? 0) - (lefts[range.c1] ?? 0)) * zoom,
      height: (r2 - range.r1 + 1) * rowHeight
    }
  }

  useImperativeHandle(ref, () => ({
    focus: () => scrollRef.current?.focus({ preventScroll: true }),
    reveal: (range) => {
      const element = scrollRef.current
      if (!element) return
      const box = canvasBox(range)
      const visibleTop = element.scrollTop + (range.r1 < layout.frozenRows ? headerHeight : bodyTop)
      const visibleLeft = element.scrollLeft + (range.c1 < layout.frozenColumns ? headerWidth : headerWidth + frozenWidth)
      if (range.r1 >= layout.frozenRows) {
        if (box.top < visibleTop) element.scrollTop = box.top - bodyTop
        else if (box.top + rowHeight > element.scrollTop + element.clientHeight) {
          element.scrollTop = Math.min(box.top - bodyTop, box.top + Math.min(box.height, element.clientHeight / 2) - element.clientHeight)
        }
      }
      if (range.c1 >= layout.frozenColumns) {
        const cellWidth = ((lefts[range.c1 + 1] ?? 0) - (lefts[range.c1] ?? 0)) * zoom
        if (box.left < visibleLeft) element.scrollLeft = box.left - headerWidth - frozenWidth
        else if (box.left + cellWidth > element.scrollLeft + element.clientWidth) {
          element.scrollLeft = box.left + cellWidth - element.clientWidth
        }
      }
    },
    fitZoom: () => {
      const element = scrollRef.current
      const content = rowHeaderWidth((layout.rows[layout.rows.length - 1] ?? 0) + 1) + (lefts[lefts.length - 1] ?? 0)
      return element ? (element.clientWidth - 2) / content : 1
    }
  }))

  const onScroll = () => {
    cancelAnimationFrame(frame.current)
    frame.current = requestAnimationFrame(measure)
  }

  const rowCount = layout.rows.length
  const columnCount = layout.columns.length
  const firstRow = Math.max(layout.frozenRows, Math.floor(viewport.top / rowHeight) + layout.frozenRows - OVERSCAN_ROWS)
  const lastRow = Math.min(rowCount - 1, Math.ceil((viewport.top + viewport.height - headerHeight) / rowHeight) + OVERSCAN_ROWS)
  const firstColumn = Math.max(
    layout.frozenColumns,
    columnAtOffset(lefts, (viewport.left + frozenWidth) / zoom) - OVERSCAN_COLUMNS
  )
  const lastColumn = Math.min(
    columnCount - 1,
    columnAtOffset(lefts, (viewport.left + viewport.width - headerWidth) / zoom) + OVERSCAN_COLUMNS
  )
  const range = (from: number, to: number) => Array.from({ length: Math.max(0, to - from + 1) }, (_, index) => from + index)
  const frozenRowIndices = range(0, layout.frozenRows - 1)
  const frozenColumnIndices = range(0, layout.frozenColumns - 1)
  const bodyRows = range(firstRow, lastRow)
  const bodyColumns = range(firstColumn, lastColumn)

  const position = (box: PreviewBox, originTop: number): CSSProperties => ({
    top: box.top - originTop,
    left: box.left,
    width: box.width,
    height: box.height
  })

  const renderCell = (row: number, column: number, originTop: number): ReactNode => {
    const key = displayKey(row, column)
    if (layout.covered.has(key)) return null
    const merge = layout.merges.get(key)
    const box = canvasBox(merge ?? { r1: row, c1: column, r2: row, c2: column })
    const sheetRow = layout.rows[row]!
    const sheetColumn = layout.columns[column]!
    const cell = cellAt(sheet, sheetRow, sheetColumn)
    const text = cell?.text ?? ''
    const isHeader = header && sheetRow === header.row && sheetColumn >= header.c1 && sheetColumn <= header.c2
    const filtered = activeFilters.has(sheetColumn)
    const active = activeMatch?.row === row && activeMatch.column === column
    return (
      <div
        key={key}
        className="dc-sheet__cell"
        data-cell={key}
        data-numeric={cell?.numeric || undefined}
        data-selected={contains(selected, row, column) || undefined}
        data-match={active ? 'active' : matches.has(key) ? '' : undefined}
        data-header={isHeader || undefined}
        title={text.length * CHARACTER_PX * zoom > box.width - 12 * zoom ? text : undefined}
        style={{ ...position(box, originTop), ...cellStyle(cell) }}
      >
        {cell?.link ? <span className="dc-sheet__link" data-link={cell.link}>{text}</span> : text}
        {isHeader && (
          <button
            type="button"
            className="dc-sheet__filter"
            data-active={filtered || undefined}
            aria-label={`${columnLetter(sheetColumn)} ${text}`}
            aria-haspopup="dialog"
            onPointerDown={(event) => event.stopPropagation()}
            onClick={(event) => props.onFilter(sheetColumn, event.currentTarget.getBoundingClientRect())}
          >
            {filtered ? <ListFilter size={12} aria-hidden /> : <ChevronDown size={12} aria-hidden />}
          </button>
        )}
      </div>
    )
  }

  const renderMerged = (rows: number[], columns: number[], originTop: number): ReactNode[] => {
    const nodes: ReactNode[] = []
    const rowSet = new Set(rows)
    const columnSet = new Set(columns)
    for (const [key, merge] of layout.merges) {
      if (rowSet.has(merge.r1) && columnSet.has(merge.c1)) continue
      const rowHit = rows.some((row) => row >= merge.r1 && row <= merge.r2)
      const columnHit = columns.some((column) => column >= merge.c1 && column <= merge.c2)
      const sameQuadrant =
        (merge.r1 < layout.frozenRows) === (rows[0] !== undefined && rows[0] < layout.frozenRows) &&
        (merge.c1 < layout.frozenColumns) === (columns[0] !== undefined && columns[0] < layout.frozenColumns)
      if (rowHit && columnHit && sameQuadrant) nodes.push(<span key={`m${key}`}>{renderCell(merge.r1, merge.c1, originTop)}</span>)
    }
    return nodes
  }

  const cells = (rows: number[], columns: number[], originTop: number): ReactNode[] => [
    ...rows.flatMap((row) => columns.map((column) => renderCell(row, column, originTop))),
    ...renderMerged(rows, columns, originTop)
  ]

  const columnHeader = (column: number) => (
    <div
      key={`c${column}`}
      className="dc-sheet__heading dc-sheet__heading--column"
      data-column={column}
      data-selected={selected && column >= selected.c1 && column <= selected.c2 ? '' : undefined}
      style={{ left: headerWidth + (lefts[column] ?? 0) * zoom, width: ((lefts[column + 1] ?? 0) - (lefts[column] ?? 0)) * zoom, height: headerHeight }}
    >
      {columnLetter(layout.columns[column]!)}
    </div>
  )

  const rowHeader = (row: number, originTop: number) => (
    <div
      key={`r${row}`}
      className="dc-sheet__heading dc-sheet__heading--row"
      data-row={row}
      data-selected={selected && row >= selected.r1 && row <= selected.r2 ? '' : undefined}
      style={{ top: headerHeight + row * rowHeight - originTop, width: headerWidth, height: rowHeight }}
    >
      {layout.rows[row]! + 1}
    </div>
  )

  const overlaysIn = (frozenRow: boolean, frozenColumn: boolean, originTop: number) => {
    const visible = frozenColumn
      ? { left: 0, right: viewport.width }
      : { left: viewport.left + headerWidth + frozenWidth, right: viewport.left + viewport.width }
    return overlays
      .filter((overlay) => (overlay.range.r1 < layout.frozenRows) === frozenRow && (overlay.range.c1 < layout.frozenColumns) === frozenColumn)
      .map((overlay) => {
        const box = canvasBox(overlay.range)
        return <span key={overlay.key}>{overlay.render({ ...box, top: box.top - originTop }, visible)}</span>
      })
  }

  const pointFrom = (target: EventTarget | null): CellPoint | null => {
    const element = target instanceof Element ? target.closest<HTMLElement>('[data-cell]') : null
    if (!element?.dataset.cell) return null
    const [row, column] = element.dataset.cell.split(':').map(Number)
    return { row: row!, column: column! }
  }

  return (
    <div
      ref={scrollRef}
      className="dc-sheet__scroll"
      tabIndex={0}
      role="region"
      aria-label={props.label}
      data-viewer-find
      style={{ '--dc-sheet-zoom': zoom } as CSSProperties}
      onScroll={onScroll}
      onKeyDown={props.onKeyDown}
      onPointerDown={(event) => {
        if (event.button !== 0) return
        const target = event.target instanceof Element ? event.target : null
        const columnHeading = target?.closest<HTMLElement>('[data-column]')
        const rowHeading = target?.closest<HTMLElement>('[data-row]')
        const point = pointFrom(event.target)
        if (!columnHeading && !rowHeading && !point) return
        event.preventDefault()
        scrollRef.current?.focus({ preventScroll: true })
        if (columnHeading) props.onColumnDown(Number(columnHeading.dataset.column), event.shiftKey)
        else if (rowHeading) props.onRowDown(Number(rowHeading.dataset.row), event.shiftKey)
        else if (point) props.onCellDown(point, event.shiftKey)
      }}
      onPointerOver={(event) => {
        const point = pointFrom(event.target)
        if (point) props.onCellEnter(point)
      }}
      onClick={(event) => {
        const link = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-link]')?.dataset.link : undefined
        if (link) props.onLink(link)
      }}
      onContextMenu={(event) => props.onContextMenu(event, pointFrom(event.target))}
    >
      <div className="dc-sheet__canvas" style={{ width, height }}>
        <div className="dc-sheet__top" style={{ height: headerHeight + frozenHeight }}>
          <div className="dc-sheet__pane">
            {bodyColumns.map(columnHeader)}
            {cells(frozenRowIndices, bodyColumns, 0)}
            {overlaysIn(true, false, 0)}
          </div>
          <div className="dc-sheet__pinned" style={{ width: headerWidth + frozenWidth }}>
            <div className="dc-sheet__heading dc-sheet__corner" style={{ width: headerWidth, height: headerHeight }} />
            {frozenColumnIndices.map(columnHeader)}
            {frozenRowIndices.map((row) => rowHeader(row, 0))}
            {cells(frozenRowIndices, frozenColumnIndices, 0)}
            {overlaysIn(true, true, 0)}
          </div>
        </div>
        <div className="dc-sheet__body" style={{ height: height - bodyTop }}>
          <div className="dc-sheet__pane">
            {cells(bodyRows, bodyColumns, bodyTop)}
            {overlaysIn(false, false, bodyTop)}
          </div>
          <div className="dc-sheet__pinned" style={{ width: headerWidth + frozenWidth }}>
            {bodyRows.map((row) => rowHeader(row, bodyTop))}
            {cells(bodyRows, frozenColumnIndices, bodyTop)}
            {overlaysIn(false, true, bodyTop)}
          </div>
        </div>
      </div>
    </div>
  )
})
