import { Annotation, RangeSet, StateEffect, StateField, Transaction, type EditorState } from '@codemirror/state'
import { Decoration, EditorView, GutterMarker, gutterLineClass } from '@codemirror/view'
import type { FileNavigationHint } from '../../../../shared/viewer/types'

export interface FileNavigationRange {
  line: number
  endLine: number
  anchor: number
}

export function fileNavigationRange(
  state: EditorState,
  hint: FileNavigationHint | undefined
): FileNavigationRange | undefined {
  const start = hint?.line
  if (start === undefined || !Number.isSafeInteger(start) || start < 1) return undefined
  const line = Math.min(state.doc.lines, start)
  const end = hint?.endLine
  const endLine = end !== undefined && Number.isSafeInteger(end) && end >= start
    ? Math.min(state.doc.lines, end)
    : line
  const target = state.doc.line(line)
  const requestedColumn = hint?.column
  const column = requestedColumn !== undefined && Number.isSafeInteger(requestedColumn) && requestedColumn > 0
    ? requestedColumn
    : 1
  return { line, endLine, anchor: Math.min(target.to, target.from + column - 1) }
}

const navigationTransaction = Annotation.define<boolean>()
const setNavigationRange = StateEffect.define<FileNavigationRange | undefined>()

export const fileNavigationField = StateField.define<FileNavigationRange | undefined>({
  create: () => undefined,
  update(value, transaction) {
    if (transaction.docChanged) return undefined
    for (const effect of transaction.effects)
      if (effect.is(setNavigationRange)) return effect.value
    if (transaction.selection && !transaction.annotation(navigationTransaction)) return undefined
    return value
  }
})

class NavigationMarker extends GutterMarker {
  elementClass = 'dc-file-editor__navigation-gutter'
}
const navigationMarker = new NavigationMarker()
const navigationLine = Decoration.line({ class: 'dc-file-editor__navigation-line' })

export const fileNavigation = [
  fileNavigationField,
  EditorView.decorations.of((view) => {
    const range = view.state.field(fileNavigationField)
    if (!range) return Decoration.none
    const lines = new Set<number>()
    for (const visible of view.visibleRanges) {
      const start = Math.max(range.line, view.state.doc.lineAt(visible.from).number)
      const end = Math.min(range.endLine, view.state.doc.lineAt(visible.to).number)
      for (let line = start; line <= end; line++) lines.add(view.state.doc.line(line).from)
    }
    return Decoration.set([...lines].sort((a, b) => a - b).map((from) => navigationLine.range(from)))
  }),
  gutterLineClass.compute([fileNavigationField], (state) => {
    const range = state.field(fileNavigationField)
    if (!range) return RangeSet.empty
    const markers = []
    for (let line = range.line; line <= range.endLine; line++)
      markers.push(navigationMarker.range(state.doc.line(line).from))
    return RangeSet.of(markers)
  }),
  EditorView.domEventHandlers({
    mousedown(event, view) {
      if (event.target instanceof Node && view.contentDOM.contains(event.target)) clearFileNavigation(view)
    },
    keydown(event, view) {
      if (/^(Arrow(?:Left|Right|Up|Down)|Home|End|PageUp|PageDown)$/.test(event.key)) clearFileNavigation(view)
    }
  })
]

export function clearFileNavigation(view: EditorView): void {
  if (view.state.field(fileNavigationField))
    view.dispatch({ effects: setNavigationRange.of(undefined), annotations: Transaction.addToHistory.of(false) })
}

export function navigateFile(view: EditorView, hint: FileNavigationHint | undefined): boolean {
  const range = fileNavigationRange(view.state, hint)
  view.dispatch({
    ...(range ? { selection: { anchor: range.anchor } } : {}),
    effects: [
      setNavigationRange.of(range),
      ...(range ? [EditorView.scrollIntoView(range.anchor, { y: 'center' })] : [])
    ],
    annotations: [navigationTransaction.of(true), Transaction.addToHistory.of(false)]
  })
  return Boolean(range)
}
