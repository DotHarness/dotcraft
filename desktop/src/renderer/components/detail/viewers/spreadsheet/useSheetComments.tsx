import { useState } from 'react'
import type { FileAnnotationContext } from '../../../../../shared/composerContext'
import { parseRangeAddress, rangeAddress, type CellRange } from '../../../../../shared/viewer/spreadsheet'
import { PreviewCommentAction, PreviewCommentCard, PreviewCommentMarker, useFileAnnotations } from '../PreviewComments'
import type { GridOverlay } from './SheetGrid'
import { mapsBack, toDisplayRange, type DisplayRange, type SheetLayout } from './sheetLayout'
import { rangeTarget } from './sheetSelection'

function annotationRange(annotation: FileAnnotationContext): CellRange | null {
  return annotation.targetKind === 'range' && annotation.location.range ? parseRangeAddress(annotation.location.range) : null
}

interface SheetComments {
  overlays: GridOverlay[]
  openCard: (id: string | null) => void
  findSaved: (sheet: string, range: string) => FileAnnotationContext | undefined
}

export function useSheetComments({
  absolutePath,
  sheetName,
  layout,
  selected,
  selectedRange,
  showAction
}: {
  absolutePath: string
  sheetName: string
  layout: SheetLayout
  selected: DisplayRange | null
  selectedRange: CellRange | null
  showAction: boolean
}): SheetComments {
  const { threadId, saved, drafts, start } = useFileAnnotations(absolutePath)
  const [openId, setOpenId] = useState<string | null>(null)
  const onSheet = (annotation: FileAnnotationContext) => annotation.location.sheet === sheetName && annotationRange(annotation) !== null
  const savedHere = saved.filter(onSheet)
  const draftsHere = drafts.filter(onSheet)
  const overlays: GridOverlay[] = []
  if (selected) {
    overlays.push({ key: 'selection', range: selected, render: (box) => <div className="dc-sheet__selection" style={box} /> })
  }
  if (!threadId) return { overlays, openCard: setOpenId, findSaved: () => undefined }

  const place = (annotation: FileAnnotationContext) => {
    const range = annotationRange(annotation)
    return range ? toDisplayRange(layout, range) : null
  }
  const selectedTarget = selectedRange ? rangeTarget(sheetName, selectedRange) : null
  const cardOpenForSelection =
    draftsHere.some((entry) => entry.target === selectedTarget) ||
    savedHere.some((entry) => entry.id === openId && entry.target === selectedTarget)

  for (const annotation of savedHere) {
    const range = place(annotation)
    if (!range) continue
    if (annotation.id === openId) {
      overlays.push({
        key: `card:${annotation.id}`,
        range,
        render: (box, visible) => (
          <PreviewCommentCard box={box} visible={visible} threadId={threadId} annotation={annotation} saved onClose={() => setOpenId(null)} />
        )
      })
    } else {
      overlays.push({
        key: `marker:${annotation.id}`,
        range,
        render: (box) => <PreviewCommentMarker box={box} annotation={annotation} onOpen={() => setOpenId(annotation.id)} />
      })
    }
  }
  for (const annotation of draftsHere) {
    const range = place(annotation)
    if (!range) continue
    overlays.push({
      key: `draft:${annotation.id}`,
      range,
      render: (box, visible) => (
        <PreviewCommentCard box={box} visible={visible} threadId={threadId} annotation={annotation} saved={false} onClose={() => undefined} />
      )
    })
  }
  if (showAction && selected && selectedRange && selectedTarget && !cardOpenForSelection && mapsBack(layout, selected, selectedRange)) {
    overlays.push({
      key: 'action',
      range: selected,
      render: (box) => (
        <PreviewCommentAction
          box={box}
          onComment={() => {
            const id = start({
              target: selectedTarget,
              targetKind: 'range',
              location: {
                sheet: sheetName,
                range: rangeAddress(selectedRange),
                rowCount: selectedRange.r2 - selectedRange.r1 + 1,
                columnCount: selectedRange.c2 - selectedRange.c1 + 1
              }
            })
            if (id && savedHere.some((entry) => entry.id === id)) setOpenId(id)
          }}
        />
      )
    })
  }
  return {
    overlays,
    openCard: setOpenId,
    findSaved: (sheet, range) => savedHere.find((entry) => entry.location.sheet === sheet && entry.location.range === range)
  }
}
