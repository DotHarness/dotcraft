import { useCallback, useEffect, useMemo, useRef, useState, type Dispatch, type KeyboardEvent, type MouseEvent, type SetStateAction } from 'react'
import { Copy, ListFilter } from 'lucide-react'
import type { FileRevealRequest } from '../../../../../shared/viewer/types'
import { parseRangeAddress, type SpreadsheetSheet, type SpreadsheetSheetSummary } from '../../../../../shared/viewer/spreadsheet'
import { useT } from '../../../../contexts/LocaleContext'
import { FindPanel } from '../../../../find/FindPanel'
import { useFindStore } from '../../../../stores/findStore'
import { ContextMenu, type ContextMenuEntry, type ContextMenuPosition } from '../../../ui/ContextMenu'
import { ViewerZoomControl } from '../ViewerZoomControl'
import { clampZoom } from '../viewerZoom'
import { FilterMenu } from './FilterMenu'
import { SheetGrid, type SheetGridHandle } from './SheetGrid'
import { FormulaBar, SheetTabs } from './SheetChrome'
import { selectionClipboard, type ClipboardPayload } from './sheetClipboard'
import { applyView, columnValues, detectHeader, EMPTY_VIEW, isViewActive, type SheetView, type SortDirection } from './sheetFilter'
import { findCells, nearestMatch, stepMatch } from './sheetFind'
import { baseRows, buildLayout, cellAt, displayKey, expandToMerges, toDisplayRange, toSheetRange } from './sheetLayout'
import {
  addressLabel,
  collapsed,
  extendTo,
  isArrowKey,
  moveSelection,
  selectAll,
  selectColumns,
  selectionRange,
  selectRows,
  type CellPoint,
  type GridSelection
} from './sheetSelection'
import { useSheetComments } from './useSheetComments'

const LINK_SCHEMES = /^(https?:|mailto:)/i

function writeClipboard({ text, html }: ClipboardPayload): Promise<void> {
  return navigator.clipboard.write([
    new ClipboardItem({
      'text/plain': new Blob([text], { type: 'text/plain' }),
      'text/html': new Blob([html], { type: 'text/html' })
    })
  ])
}

interface SpreadsheetSheetViewProps {
  absolutePath: string
  sheet: SpreadsheetSheet
  sheets: SpreadsheetSheetSummary[]
  sheetIndex: number
  onSelectSheet: (index: number) => void
  zoom: number
  setZoom: Dispatch<SetStateAction<number>>
  reveal: FileRevealRequest | null
  onRevealed: (revision: number) => void
}

export function SpreadsheetSheetView(props: SpreadsheetSheetViewProps): JSX.Element {
  const { absolutePath, sheet, zoom, setZoom, reveal, onRevealed } = props
  const t = useT()
  const grid = useRef<SheetGridHandle>(null)
  const [selection, setSelection] = useState<GridSelection | null>(null)
  const [dragging, setDragging] = useState(false)
  const [view, setView] = useState<SheetView>(EMPTY_VIEW)
  const [filter, setFilter] = useState<{ column: number; anchor: DOMRect } | null>(null)
  const [menu, setMenu] = useState<{ position: ContextMenuPosition; column: number | null } | null>(null)
  const [finding, setFinding] = useState(false)
  const [query, setQuery] = useState('')
  const [matchIndex, setMatchIndex] = useState(-1)

  const header = useMemo(() => detectHeader(sheet), [sheet])
  const base = useMemo(() => baseRows(sheet), [sheet])
  const layout = useMemo(
    () => buildLayout(sheet, applyView(sheet, header, view, base), isViewActive(view)),
    [sheet, header, view, base]
  )
  const selected = selection ? expandToMerges(layout, selectionRange(selection)) : null
  const selectedRange = selected ? toSheetRange(layout, selected) : null
  const matches = useMemo(() => (finding ? findCells(sheet, layout, query) : []), [finding, sheet, layout, query])
  const matchKeys = useMemo(() => new Set(matches.map((match) => displayKey(match.row, match.column))), [matches])
  const activeMatch = matches[matchIndex] ?? null

  const comments = useSheetComments({
    absolutePath,
    sheetName: sheet.name,
    layout,
    selected,
    selectedRange,
    showAction: !dragging && !filter && !menu
  })

  useEffect(() => {
    if (!dragging) return
    const stop = () => setDragging(false)
    window.addEventListener('pointerup', stop)
    window.addEventListener('pointercancel', stop)
    return () => {
      window.removeEventListener('pointerup', stop)
      window.removeEventListener('pointercancel', stop)
    }
  }, [dragging])

  const selectPoint = useCallback((point: CellPoint) => {
    setSelection(collapsed(point))
    grid.current?.reveal({ r1: point.row, c1: point.column, r2: point.row, c2: point.column })
  }, [])

  const anchor = useRef<CellPoint | null>(null)
  anchor.current = selection?.anchor ?? null
  useEffect(() => {
    if (!finding) return
    const index = nearestMatch(matches, anchor.current)
    setMatchIndex(index)
    if (index >= 0) selectPoint(matches[index]!)
  }, [finding, matches, selectPoint])

  useEffect(() => {
    if (!finding) return
    useFindStore.getState().closeFind()
    return useFindStore.subscribe((current, previous) => {
      if (current.open && !previous.open) setFinding(false)
    })
  }, [finding])

  const { findSaved, openCard } = comments
  useEffect(() => {
    if (!reveal) return
    const sheetName = reveal.location.sheet
    const range = reveal.location.range ? parseRangeAddress(reveal.location.range) : null
    if (sheetName && sheetName !== sheet.name) return
    const display = range ? toDisplayRange(layout, range) : null
    if (range && !display && isViewActive(view)) {
      setView(EMPTY_VIEW)
      return
    }
    if (display) {
      setSelection({ anchor: { row: display.r1, column: display.c1 }, focus: { row: display.r2, column: display.c2 } })
      grid.current?.reveal(display)
      openCard(findSaved(sheet.name, reveal.location.range!)?.id ?? null)
    }
    grid.current?.focus()
    onRevealed(reveal.revision)
  }, [reveal, sheet.name, layout, view, findSaved, openCard, onRevealed])

  const rowCount = layout.rows.length
  const columnCount = layout.columns.length
  const copy = () => {
    if (selected) void writeClipboard(selectionClipboard(sheet, layout, selected)).catch(() => undefined)
  }
  const changeView = (next: SheetView) => {
    setView(next)
    setSelection(null)
  }
  const step = (direction: 1 | -1) => {
    const index = stepMatch(matchIndex, matches.length, direction)
    setMatchIndex(index)
    if (index >= 0) selectPoint(matches[index]!)
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    const mod = event.ctrlKey || event.metaKey
    const key = event.key.toLowerCase()
    if (mod && !event.shiftKey && !event.altKey && key === 'f') {
      event.preventDefault()
      setFinding(true)
    } else if (mod && key === 'a') {
      event.preventDefault()
      setSelection(selectAll(rowCount, columnCount))
    } else if (mod && key === 'c') {
      event.preventDefault()
      copy()
    } else if (isArrowKey(event.key) && !mod && !event.altKey) {
      event.preventDefault()
      const next = moveSelection(selection, event.key, event.shiftKey, rowCount, columnCount)
      setSelection(next)
      if (next) grid.current?.reveal({ r1: next.focus.row, c1: next.focus.column, r2: next.focus.row, c2: next.focus.column })
    } else if (event.key === 'Escape') {
      openCard(null)
    }
  }

  const onContextMenu = (event: MouseEvent, point: CellPoint | null) => {
    if (!point) return
    event.preventDefault()
    if (!selected || point.row < selected.r1 || point.row > selected.r2 || point.column < selected.c1 || point.column > selected.c2) {
      setSelection(collapsed(point))
    }
    const sheetColumn = layout.columns[point.column]!
    const filterable = header && sheetColumn >= header.c1 && sheetColumn <= header.c2 ? sheetColumn : null
    setMenu({ position: { x: event.clientX, y: event.clientY }, column: filterable })
  }

  const active = selection ? cellAt(sheet, layout.rows[selection.anchor.row] ?? -1, layout.columns[selection.anchor.column] ?? -1) : null
  const menuItems: ContextMenuEntry[] = [{ label: t('viewer.sheet.copy'), icon: <Copy size={14} />, onClick: copy }]
  if (menu?.column != null) {
    const column = menu.column
    const anchor = new DOMRect(menu.position.x, menu.position.y, 0, 0)
    menuItems.push({ label: t('viewer.sheet.filterSort'), icon: <ListFilter size={14} />, onClick: () => setFilter({ column, anchor }) })
  }

  return (
    <>
      <FormulaBar
        address={selectedRange ? addressLabel(selectedRange, dragging) : ''}
        value={active ? (active.formula ? `=${active.formula}` : active.text) : ''}
      />
      <SheetGrid
        ref={grid}
        sheet={sheet}
        layout={layout}
        zoom={zoom}
        label={t('viewer.sheet.grid', { sheet: sheet.name })}
        selected={selected}
        header={header}
        activeFilters={new Set([
          ...Object.entries(view.excluded).filter(([, values]) => values.length).map(([column]) => Number(column)),
          ...(view.sort ? [view.sort.column] : [])
        ])}
        matches={matchKeys}
        activeMatch={activeMatch}
        overlays={comments.overlays}
        onZoom={setZoom}
        onCellDown={(point, extend) => {
          openCard(null)
          setSelection((current) => (extend ? extendTo(current, point) : collapsed(point)))
          setDragging(true)
        }}
        onCellEnter={(point) => {
          if (dragging) setSelection((current) => extendTo(current, point))
        }}
        onColumnDown={(column, extend) =>
          setSelection(selectColumns(extend && selection ? selection.anchor.column : column, column, rowCount))
        }
        onRowDown={(row, extend) => setSelection(selectRows(extend && selection ? selection.anchor.row : row, row, columnCount))}
        onLink={(url) => {
          if (LINK_SCHEMES.test(url)) void window.api.shell.openExternal(url).catch(() => undefined)
        }}
        onContextMenu={onContextMenu}
        onFilter={(column, anchor) => setFilter({ column, anchor })}
        onKeyDown={onKeyDown}
      />
      <SheetTabs
        sheets={props.sheets}
        active={props.sheetIndex}
        onSelect={props.onSelectSheet}
        trailing={
          <ViewerZoomControl
            zoom={zoom}
            onZoom={setZoom}
            onFit={() => setZoom(clampZoom(grid.current?.fitZoom() ?? 1))}
          />
        }
      />
      {menu && <ContextMenu position={menu.position} items={menuItems} onClose={() => setMenu(null)} />}
      {filter && header && (
        <FilterMenu
          anchor={filter.anchor}
          values={columnValues(sheet, header, base, filter.column)}
          excluded={view.excluded[filter.column] ?? []}
          sort={view.sort?.column === filter.column ? view.sort.direction : null}
          onExclude={(excluded) => changeView({ ...view, excluded: { ...view.excluded, [filter.column]: excluded } })}
          onSort={(direction: SortDirection) =>
            changeView({
              ...view,
              sort: view.sort?.column === filter.column && view.sort.direction === direction ? undefined : { column: filter.column, direction }
            })
          }
          onClose={() => setFilter(null)}
        />
      )}
      {finding && (
        <FindPanel
          label={t('find.title')}
          placeholder={t('find.placeholder')}
          query={query}
          hasMatches={matches.length > 0}
          counter={matches.length ? `${matchIndex + 1} / ${matches.length}` : t('find.noResults')}
          onChange={setQuery}
          onNext={() => step(1)}
          onPrevious={() => step(-1)}
          onClose={() => {
            setFinding(false)
            setQuery('')
            grid.current?.focus()
          }}
        />
      )}
    </>
  )
}
