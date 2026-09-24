import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { Plus } from 'lucide-react'
import type { DiffAnnotationContext } from '../../../../shared/composerContext'
import type { FileDiff } from '../../../types/toolCall'
import { useThreadStore } from '../../../stores/threadStore'
import { useConversationStore } from '../../../stores/conversationStore'
import { useComposerContextStore } from '../../../stores/composerContextStore'
import {
  lineHasComment,
  useLineCommentDraftStore,
} from '../../../stores/lineCommentDraftStore'
import {
  modelReviewCommentsForFile,
  parseModelReviewComments,
} from '../../../utils/modelReviewComments'
import { useT } from '../../../contexts/LocaleContext'
import {
  LineCommentCard,
  ModelLineCommentCard,
  lineCommentLabel,
} from '../../code/LineCommentCard'
import { buildUnifiedRows } from './diffRows'
import type { DiffModel } from './useDiffModel'

type Side = 'left' | 'right'
interface Selection {
  side: Side
  anchor: number
  line: number
}
interface FeedbackValue {
  select: (side: Side, line: number, extend: boolean) => void
  beginComment: (side: Side, line: number, drag: boolean) => void
  isSelected: (side: Side, line: number) => boolean
  dragging: boolean
  render: (side: Side, line: number) => ReactNode
}
const Context = createContext<FeedbackValue | null>(null)
const RowHeights = createContext<{
  heights: Record<string, number>
  measure: (key: string, height: number) => void
} | null>(null)

const EMPTY: DiffAnnotationContext[] = []

function span(selection: Selection): [number, number] {
  return [Math.min(selection.anchor, selection.line), Math.max(selection.anchor, selection.line)]
}

function covers(selection: Selection, side: Side, line: number): boolean {
  const [start, end] = span(selection)
  return selection.side === side && line >= start && line <= end
}

export function useDiffComments(): FeedbackValue | null {
  return useContext(Context)
}

export function DiffFeedbackProvider({
  diff,
  model,
  workspacePath,
  children,
}: {
  diff: FileDiff
  model: DiffModel
  workspacePath: string
  children: ReactNode
}): JSX.Element {
  const t = useT()
  const threadId = useThreadStore((s) => s.activeThreadId)
  const turns = useConversationStore((s) => s.turns)
  const contexts = useComposerContextStore((s) => s.getContexts(threadId ?? ''))
  const drafts = useLineCommentDraftStore((s) =>
    threadId ? s.getDrafts(threadId) : EMPTY,
  )
  const [selection, setSelection] = useState<Selection | null>(null)
  const [dragging, setDragging] = useState(false)
  const selectionRef = useRef(selection)
  selectionRef.current = selection
  const [heights, setHeights] = useState<Record<string, number>>({})
  const measure = useCallback(
    (key: string, height: number) =>
      setHeights((previous) =>
        previous[key] === height ? previous : { ...previous, [key]: height },
      ),
    [],
  )
  const modelComments = useMemo(
    () =>
      modelReviewCommentsForFile(
        parseModelReviewComments(turns),
        diff.filePath,
        workspacePath,
      ),
    [turns, diff.filePath, workspacePath],
  )
  const userComments = contexts.filter(
    (entry): entry is DiffAnnotationContext =>
      entry.kind === 'diffAnnotation' && entry.path === diff.filePath,
  )
  const fileDrafts = drafts.filter((entry) => entry.path === diff.filePath)
  const rows = useMemo(
    () => buildUnifiedRows(diff, model.sides),
    [diff, model.sides],
  )

  function openDraft(side: Side, startLine: number, endLine: number) {
    if (!threadId || lineHasComment(threadId, userComments, diff.filePath, side, endLine))
      return
    const selectedText = rows
      .flatMap((row) => {
        if (row.kind !== 'line') return []
        const number = Number(side === 'left' ? row.oldNum : row.newNum)
        return number >= startLine && number <= endLine
          ? [row.cell.content]
          : []
      })
      .join('\n')
    useLineCommentDraftStore.getState().open(threadId, {
      kind: 'diffAnnotation',
      id: crypto.randomUUID(),
      path: diff.filePath,
      side,
      startLine,
      endLine,
      selectedText,
      comment: '',
    })
  }

  function select(side: Side, line: number, extend: boolean) {
    const current = selectionRef.current
    const next =
      extend && current?.side === side
        ? { ...current, line }
        : { side, anchor: line, line }
    selectionRef.current = next
    setSelection(next)
  }

  function beginComment(side: Side, line: number, drag: boolean) {
    const current = selectionRef.current
    const range =
      current && covers(current, side, line)
        ? current
        : { side, anchor: line, line }
    if (drag) {
      selectionRef.current = range
      setSelection(range)
      setDragging(true)
      return
    }
    openDraft(side, ...span(range))
    setSelection(null)
  }

  useEffect(() => {
    if (!dragging) return
    function move(event: PointerEvent) {
      const row = document
        .elementFromPoint?.(event.clientX, event.clientY)
        ?.closest<HTMLElement>('[data-comment-line]')
      const current = selectionRef.current
      if (!row || !current || row.dataset.commentSide !== current.side) return
      const line = Number(row.dataset.commentLine)
      if (line !== current.line) setSelection({ ...current, line })
    }
    function finish() {
      setDragging(false)
      const current = selectionRef.current
      if (!current) return
      openDraft(current.side, ...span(current))
      setSelection(null)
    }
    window.addEventListener('pointermove', move)
    window.addEventListener('pointerup', finish, { once: true })
    return () => {
      window.removeEventListener('pointermove', move)
      window.removeEventListener('pointerup', finish)
    }
  })

  function isSelected(side: Side, line: number) {
    return selection !== null && covers(selection, side, line)
  }

  function render(side: Side, line: number) {
    const saved = userComments.filter(
      (entry) => entry.side === side && entry.endLine === line,
    )
    const open = fileDrafts.filter(
      (entry) => entry.side === side && entry.endLine === line,
    )
    const models =
      side === 'right'
        ? modelComments.filter((entry) => entry.endLine === line)
        : []
    if (!threadId || (!saved.length && !open.length && !models.length))
      return null
    const contextsStore = useComposerContextStore.getState()
    const draftsStore = useLineCommentDraftStore.getState()
    return (
      <>
        {models.map((entry) => (
          <ModelLineCommentCard
            key={entry.id}
            label={lineCommentLabel(t, 'right', entry.startLine, entry.endLine)}
            title={entry.title}
            body={entry.body}
          />
        ))}
        {saved.map((entry) => (
          <LineCommentCard
            key={entry.id}
            saved
            label={lineCommentLabel(t, entry.side, entry.startLine, entry.endLine)}
            initialComment={entry.comment}
            onSubmit={(comment) =>
              contextsStore.addContext(threadId, { ...entry, comment })
            }
            onDelete={() => contextsStore.removeContext(threadId, entry.id)}
          />
        ))}
        {open.map((entry) => (
          <LineCommentCard
            key={entry.id}
            saved={false}
            label={lineCommentLabel(t, entry.side, entry.startLine, entry.endLine)}
            initialComment={entry.comment}
            onChange={(comment) => draftsStore.update(threadId, entry.id, comment)}
            onSubmit={(comment) => {
              contextsStore.addContext(threadId, { ...entry, comment })
              draftsStore.close(threadId, entry.id)
            }}
            onClose={() => draftsStore.close(threadId, entry.id)}
          />
        ))}
      </>
    )
  }

  return (
    <Context.Provider
      value={
        threadId
          ? { select, beginComment, isSelected, dragging, render }
          : null
      }
    >
      <RowHeights.Provider value={{ heights, measure }}>
        {children}
      </RowHeights.Provider>
    </Context.Provider>
  )
}

export function DiffFeedbackRow({
  index,
  side,
  children,
}: {
  index: number
  side: 'deletion' | 'addition'
  children: ReactNode
}): JSX.Element {
  const context = useContext(RowHeights)
  const ref = useRef<HTMLDivElement>(null)
  const measure = context?.measure
  useLayoutEffect(() => {
    const element = ref.current
    if (!element || !measure || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(() =>
      measure(`${index}:${side}`, element.getBoundingClientRect().height),
    )
    observer.observe(element)
    return () => observer.disconnect()
  }, [index, side, measure])
  const height = context
    ? Math.max(
        context.heights[`${index}:deletion`] ?? 0,
        context.heights[`${index}:addition`] ?? 0,
      )
    : undefined
  return (
    <div style={{ minHeight: height }}>
      <div ref={ref}>{children}</div>
    </div>
  )
}

export function DiffFeedbackGutter({
  side,
  value,
  children,
}: {
  side: Side
  value: string
  children: ReactNode
}): JSX.Element {
  const feedback = useContext(Context)
  const t = useT()
  if (!feedback || !value) return <>{children}</>
  return (
    <button
      type="button"
      className="dc-diff-feedback__gutter"
      aria-label={t(
        side === 'left' ? 'lineComment.selectOldLine' : 'lineComment.selectNewLine',
        { line: value },
      )}
      onClick={(event) => feedback.select(side, Number(value), event.shiftKey)}
    >
      {children}
    </button>
  )
}

export function DiffCommentAdd({
  side,
  line,
}: {
  side: Side
  line: string
}): JSX.Element | null {
  const feedback = useContext(Context)
  const t = useT()
  if (!feedback || !line) return null
  return (
    <button
      type="button"
      className="dc-line-comment-add"
      data-dragging={(feedback.dragging && feedback.isSelected(side, Number(line))) || undefined}
      aria-label={t(
        side === 'left' ? 'lineComment.addOldLine' : 'lineComment.addNewLine',
        { line },
      )}
      onPointerDown={(event) => {
        if (event.button !== 0) return
        event.preventDefault()
        feedback.beginComment(side, Number(line), true)
      }}
      onKeyDown={(event) => {
        if (event.key !== 'Enter' && event.key !== ' ') return
        event.preventDefault()
        feedback.beginComment(side, Number(line), false)
      }}
    >
      <Plus size={14} strokeWidth={2.4} aria-hidden />
    </button>
  )
}

export function DiffLineFeedback({
  side,
  line,
}: {
  side: Side
  line: string
}): ReactNode {
  return useContext(Context)?.render(side, Number(line)) ?? null
}
