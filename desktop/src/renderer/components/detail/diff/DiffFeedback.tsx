import {
  createContext,
  useCallback,
  useContext,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { Pencil, X } from 'lucide-react'
import type { DiffAnnotationContext } from '../../../../shared/composerContext'
import type { FileDiff } from '../../../types/toolCall'
import { useThreadStore } from '../../../stores/threadStore'
import { useConversationStore } from '../../../stores/conversationStore'
import { useComposerContextStore } from '../../../stores/composerContextStore'
import {
  modelReviewCommentsForFile,
  parseModelReviewComments,
} from '../../../utils/modelReviewComments'
import { useT } from '../../../contexts/LocaleContext'
import { ContextCommentEditor } from '../../conversation/ContextCommentEditor'
import { IconButton } from '../../ui/IconButton'
import { buildUnifiedRows } from './diffRows'
import type { DiffModel } from './useDiffModel'

type Side = 'left' | 'right'
interface FeedbackValue {
  select: (side: Side, line: number, extend: boolean) => void
  render: (side: Side, line: number) => ReactNode
}
const Context = createContext<FeedbackValue | null>(null)
const RowHeights = createContext<{
  heights: Record<string, number>
  measure: (key: string, height: number) => void
} | null>(null)

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
  const [draft, setDraft] = useState<DiffAnnotationContext | null>(null)
  const selectionAnchor = useRef<{ side: Side; line: number } | null>(null)
  const [editing, setEditing] = useState(false)
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
  const rows = useMemo(
    () => buildUnifiedRows(diff, model.sides),
    [diff, model.sides],
  )
  function select(side: Side, line: number, extend: boolean) {
    if (!extend || selectionAnchor.current?.side !== side)
      selectionAnchor.current = { side, line }
    const anchor = selectionAnchor.current!.line
    const startLine = Math.min(anchor, line),
      endLine = Math.max(anchor, line)
    const selectedText = rows
      .flatMap((row) => {
        if (row.kind !== 'line') return []
        const number = Number(side === 'left' ? row.oldNum : row.newNum)
        return number >= startLine && number <= endLine
          ? [row.cell.content]
          : []
      })
      .join('\n')
    setDraft({
      kind: 'diffAnnotation',
      id: crypto.randomUUID(),
      path: diff.filePath,
      side,
      startLine,
      endLine,
      selectedText,
      comment: '',
    })
    setEditing(false)
  }
  function render(side: Side, line: number) {
    const current =
      draft?.side === side && draft.endLine === line ? draft : null
    const users = userComments.filter(
      (entry) =>
        entry.side === side &&
        entry.endLine === line &&
        entry.id !== current?.id,
    )
    const models =
      side === 'right'
        ? modelComments.filter((entry) => entry.endLine === line)
        : []
    if (!current && !users.length && !models.length) return null
    return (
      <div className="dc-diff-feedback">
        {users.map((entry) => (
          <div className="dc-diff-feedback__comment" key={entry.id}>
            <header>
              <span>
                {t('composer.context.userComment')} · {entry.startLine}–
                {entry.endLine}
              </span>
              <IconButton
                size={24}
                radius={6}
                icon={<Pencil size={14} />}
                label={t('composer.context.edit')}
                tooltipLabel={t('composer.context.edit')}
                onClick={() => {
                  setDraft(entry)
                  setEditing(true)
                }}
              />
              <IconButton
                size={24}
                radius={6}
                icon={<X size={14} />}
                label={t('composer.context.remove')}
                tooltipLabel={t('composer.context.remove')}
                onClick={() => {
                  if (threadId)
                    useComposerContextStore
                      .getState()
                      .removeContext(threadId, entry.id)
                }}
              />
            </header>
            <p>{entry.comment || entry.selectedText}</p>
          </div>
        ))}
        {models.map((entry) => (
          <div
            className="dc-diff-feedback__comment"
            key={entry.id}
            data-source="model"
          >
            <header>
              {t('composer.context.modelComment')} · {entry.startLine}–
              {entry.endLine}
            </header>
            <strong>{entry.title}</strong>
            <p>{entry.body}</p>
          </div>
        ))}
        {current && (
          <ContextCommentEditor
            key={current.id}
            mode={editing ? 'edit' : 'create'}
            initialComment={current.comment}
            onCancel={() => setDraft(null)}
            onSave={(comment) => {
              if (threadId)
                useComposerContextStore
                  .getState()
                  .addContext(threadId, { ...current, comment })
              setDraft(null)
            }}
          />
        )}
      </div>
    )
  }
  return (
    <Context.Provider value={threadId ? { select, render } : null}>
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
        side === 'left'
          ? 'composer.context.oldLine'
          : 'composer.context.newLine',
        { line: value },
      )}
      onClick={(event) => feedback.select(side, Number(value), event.shiftKey)}
    >
      {children}
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
