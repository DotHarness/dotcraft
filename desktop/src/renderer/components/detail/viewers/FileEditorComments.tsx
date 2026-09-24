import { useEffect, useMemo, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { StateEffect, StateField, type EditorState } from '@codemirror/state'
import { Decoration, EditorView, WidgetType, type DecorationSet } from '@codemirror/view'
import { MessageSquarePlus } from 'lucide-react'
import type { DiffAnnotationContext } from '../../../../shared/composerContext'
import { useT } from '../../../contexts/LocaleContext'
import { useConversationStore } from '../../../stores/conversationStore'
import { useComposerContextStore } from '../../../stores/composerContextStore'
import { useFileEditorStore } from '../../../stores/fileEditorStore'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { lineHasComment, useLineCommentDraftStore } from '../../../stores/lineCommentDraftStore'
import { modelReviewCommentsForFile, parseModelReviewComments } from '../../../utils/modelReviewComments'
import { LineCommentCard, ModelLineCommentCard, lineCommentLabel } from '../../code/LineCommentCard'
import { Button } from '../../ui/Button'

interface Anchor {
  line: number
  dom: HTMLElement
}

const setAnchors = StateEffect.define<Anchor[]>()

class CommentWidget extends WidgetType {
  constructor(readonly dom: HTMLElement) {
    super()
  }
  eq(other: CommentWidget): boolean {
    return other.dom === this.dom
  }
  toDOM(): HTMLElement {
    return this.dom
  }
  ignoreEvent(): boolean {
    return true
  }
}

function decorations(state: EditorState, anchors: Anchor[]): DecorationSet {
  return Decoration.set(
    anchors.map((anchor) =>
      Decoration.widget({ widget: new CommentWidget(anchor.dom), block: true, side: 1 }).range(
        state.doc.line(Math.min(Math.max(1, anchor.line), state.doc.lines)).to
      )
    ),
    true
  )
}

export const lineCommentField = StateField.define<DecorationSet>({
  create: () => Decoration.none,
  update(value, transaction) {
    let next = value.map(transaction.changes)
    for (const effect of transaction.effects)
      if (effect.is(setAnchors)) next = decorations(transaction.state, effect.value)
    return next
  },
  provide: (field) => EditorView.decorations.from(field)
})

const EMPTY: DiffAnnotationContext[] = []

function containerFor(containers: Map<string, HTMLElement>, id: string): HTMLElement {
  let element = containers.get(id)
  if (!element) {
    element = document.createElement('div')
    element.className = 'dc-file-editor__comment'
    containers.set(id, element)
  }
  return element
}

export function FileEditorComments({
  view,
  tabId,
  absolutePath,
  preview
}: {
  view: EditorView | null
  tabId: string
  absolutePath: string
  preview: boolean
}): JSX.Element | null {
  const t = useT()
  const threadId = useViewerTabStore((state) => state.currentThreadId)
  const workspacePath = useViewerTabStore((state) => state.currentWorkspacePath)
  const turns = useConversationStore((state) => state.turns)
  const contexts = useComposerContextStore((state) => state.getContexts(threadId ?? ''))
  const drafts = useLineCommentDraftStore((state) => (threadId ? state.getDrafts(threadId) : EMPTY))
  const containers = useRef(new Map<string, HTMLElement>())
  const [, setScrolled] = useState(0)

  const saved = contexts.filter(
    (entry): entry is DiffAnnotationContext =>
      entry.kind === 'diffAnnotation' && entry.path === absolutePath && entry.side === 'right'
  )
  const open = drafts.filter((entry) => entry.path === absolutePath && entry.side === 'right')
  const models = useMemo(
    () =>
      workspacePath
        ? modelReviewCommentsForFile(parseModelReviewComments(turns), absolutePath, workspacePath)
        : [],
    [turns, absolutePath, workspacePath]
  )

  const container = (id: string) => containerFor(containers.current, id)
  const entries = threadId && !preview
    ? [
        ...models.map((entry) => ({ id: entry.id, line: entry.endLine })),
        ...saved.map((entry) => ({ id: entry.id, line: entry.endLine })),
        ...open.map((entry) => ({ id: entry.id, line: entry.endLine }))
      ]
    : []
  const anchorKey = entries.map((entry) => `${entry.id}@${entry.line}`).join('|')

  useEffect(() => {
    if (!view) return
    const anchors = anchorKey
      ? anchorKey.split('|').map((part) => {
          const at = part.lastIndexOf('@')
          return { line: Number(part.slice(at + 1)), dom: containerFor(containers.current, part.slice(0, at)) }
        })
      : []
    view.dispatch({ effects: setAnchors.of(anchors) })
  }, [view, anchorKey])

  useEffect(() => {
    if (!view || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() => view.requestMeasure())
    for (const element of containers.current.values()) observer.observe(element)
    return () => observer.disconnect()
  }, [view, anchorKey])

  useEffect(() => {
    if (!view) return
    const onScroll = () => setScrolled((value) => value + 1)
    view.scrollDOM.addEventListener('scroll', onScroll)
    return () => view.scrollDOM.removeEventListener('scroll', onScroll)
  }, [view])

  if (!view || !threadId) return null

  const range = view.state.selection.main
  const head = !range.empty && view.hasFocus ? view.coordsAtPos(range.head) : null

  function comment() {
    if (!view || !threadId) return
    const { doc } = view.state
    const { from, to } = view.state.selection.main
    const startLine = doc.lineAt(from).number
    const last = doc.lineAt(to)
    const endLine = to === last.from && last.number > startLine ? last.number - 1 : last.number
    view.dispatch({ selection: { anchor: to } })
    if (lineHasComment(threadId, saved, absolutePath, 'right', endLine)) return
    useLineCommentDraftStore.getState().open(threadId, {
      kind: 'diffAnnotation',
      id: crypto.randomUUID(),
      path: absolutePath,
      side: 'right',
      startLine,
      endLine,
      selectedText: doc.sliceString(doc.line(startLine).from, doc.line(endLine).to),
      comment: ''
    })
    if (preview) void useFileEditorStore.getState().switchMode(tabId, 'source')
  }

  const contextsStore = useComposerContextStore.getState()
  const draftsStore = useLineCommentDraftStore.getState()

  return (
    <>
      {head && (
        <div
          className="dc-file-editor__selection-actions"
          style={{ top: head.bottom + 6, left: head.left }}
          onPointerDown={(event) => event.preventDefault()}
        >
          <Button size="sm" variant="ghost" iconLeft={<MessageSquarePlus size={14} />} onClick={comment}>
            {t('lineComment.comment')}
          </Button>
        </div>
      )}
      {!preview &&
        models.map((entry) =>
          createPortal(
            <ModelLineCommentCard
              label={lineCommentLabel(t, 'right', entry.startLine, entry.endLine)}
              title={entry.title}
              body={entry.body}
            />,
            container(entry.id),
            entry.id
          )
        )}
      {!preview &&
        saved.map((entry) =>
          createPortal(
            <LineCommentCard
              saved
              label={lineCommentLabel(t, 'right', entry.startLine, entry.endLine)}
              initialComment={entry.comment}
              onSubmit={(value) => contextsStore.addContext(threadId, { ...entry, comment: value })}
              onDelete={() => contextsStore.removeContext(threadId, entry.id)}
            />,
            container(entry.id),
            entry.id
          )
        )}
      {!preview &&
        open.map((entry) =>
          createPortal(
            <LineCommentCard
              saved={false}
              label={lineCommentLabel(t, 'right', entry.startLine, entry.endLine)}
              initialComment={entry.comment}
              onChange={(value) => draftsStore.update(threadId, entry.id, value)}
              onSubmit={(value) => {
                contextsStore.addContext(threadId, { ...entry, comment: value })
                draftsStore.close(threadId, entry.id)
              }}
              onClose={() => draftsStore.close(threadId, entry.id)}
            />,
            container(entry.id),
            entry.id
          )
        )}
    </>
  )
}
