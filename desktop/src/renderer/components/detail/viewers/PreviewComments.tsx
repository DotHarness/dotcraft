import { MessageSquarePlus, MessageSquareText } from 'lucide-react'
import type { FileAnnotationContext, LineCommentContext } from '../../../../shared/composerContext'
import { useT } from '../../../contexts/LocaleContext'
import { useComposerContextStore } from '../../../stores/composerContextStore'
import { fileTargetHasComment, useLineCommentDraftStore } from '../../../stores/lineCommentDraftStore'
import { useViewerTabStore } from '../../../stores/viewerTabStore'
import { LineCommentCard } from '../../code/LineCommentCard'
import { Button } from '../../ui/Button'
import './preview-comments.css'
import { sameFilePath } from '../../../../shared/workspaceProjectKey'

export interface PreviewBox {
  top: number
  left: number
  width: number
  height: number
}

export interface VisibleSpan {
  left: number
  right: number
}

const CARD_WIDTH = 384
const EMPTY: LineCommentContext[] = []

const isFileAnnotation = (path: string) => (entry: { kind: string; path?: string }): entry is FileAnnotationContext =>
  entry.kind === 'fileAnnotation' && entry.path !== undefined && sameFilePath(entry.path, path)

interface FileAnnotations {
  threadId: string | null
  saved: FileAnnotationContext[]
  drafts: FileAnnotationContext[]
  start: (annotation: Omit<FileAnnotationContext, 'id' | 'kind' | 'path' | 'comment'>) => string | null
}

export function useFileAnnotations(path: string): FileAnnotations {
  const threadId = useViewerTabStore((state) => state.currentThreadId)
  const contexts = useComposerContextStore((state) => state.getContexts(threadId ?? ''))
  const pending = useLineCommentDraftStore((state) => (threadId ? state.getDrafts(threadId) : EMPTY))
  const saved = contexts.filter(isFileAnnotation(path))
  const drafts = pending.filter(isFileAnnotation(path))
  const start: FileAnnotations['start'] = (annotation) => {
    if (!threadId) return null
    if (fileTargetHasComment(threadId, saved, path, annotation.target)) {
      const existing = [...saved, ...drafts].find((entry) => entry.target === annotation.target)
      return existing?.id ?? null
    }
    const id = crypto.randomUUID()
    useLineCommentDraftStore.getState().open(threadId, { ...annotation, id, kind: 'fileAnnotation', path, comment: '' })
    return id
  }
  return { threadId, saved, drafts, start }
}

export function PreviewCommentAction({ box, onComment }: { box: PreviewBox; onComment: () => void }): JSX.Element {
  const t = useT()
  return (
    <div
      className="dc-preview-comment__action"
      style={{ top: box.top + box.height + 6, left: box.left }}
      onPointerDown={(event) => {
        event.preventDefault()
        event.stopPropagation()
      }}
    >
      <Button size="sm" variant="ghost" iconLeft={<MessageSquarePlus size={14} />} onClick={onComment}>
        {t('lineComment.comment')}
      </Button>
    </div>
  )
}

export function PreviewCommentMarker({
  box,
  annotation,
  onOpen
}: {
  box: PreviewBox
  annotation: FileAnnotationContext
  onOpen: () => void
}): JSX.Element {
  const t = useT()
  return (
    <button
      type="button"
      className="dc-preview-comment__marker"
      style={{ top: box.top - 8, left: box.left + box.width - 10 }}
      aria-label={t('viewer.comment.open', { target: annotation.target })}
      title={annotation.comment}
      onPointerDown={(event) => event.stopPropagation()}
      onClick={onOpen}
    >
      <MessageSquareText size={11} strokeWidth={2} aria-hidden />
    </button>
  )
}

export function PreviewCommentCard({
  box,
  visible,
  threadId,
  annotation,
  saved,
  onClose
}: {
  box: PreviewBox
  visible?: VisibleSpan
  threadId: string
  annotation: FileAnnotationContext
  saved: boolean
  onClose: () => void
}): JSX.Element {
  const contexts = useComposerContextStore.getState()
  const drafts = useLineCommentDraftStore.getState()
  const preferred = box.left - 6
  const left = visible
    ? Math.max(visible.left, Math.min(preferred, visible.right - Math.min(CARD_WIDTH, visible.right - visible.left - 12) - 6))
    : preferred
  return (
    <div
      className="dc-preview-comment__card"
      style={{
        top: box.top + box.height + 4,
        left: Math.max(0, left),
        width: visible ? Math.min(CARD_WIDTH, visible.right - visible.left - 12) : undefined
      }}
      onPointerDown={(event) => event.stopPropagation()}
      onKeyDown={(event) => event.stopPropagation()}
      onCopy={(event) => event.stopPropagation()}
    >
      {saved ? (
        <LineCommentCard
          key={annotation.id}
          saved
          label={annotation.target}
          initialComment={annotation.comment}
          onSubmit={(value) => contexts.addContext(threadId, { ...annotation, comment: value })}
          onDelete={() => {
            contexts.removeContext(threadId, annotation.id)
            onClose()
          }}
        />
      ) : (
        <LineCommentCard
          key={annotation.id}
          saved={false}
          label={annotation.target}
          initialComment={annotation.comment}
          onChange={(value) => drafts.update(threadId, annotation.id, value)}
          onSubmit={(value) => {
            contexts.addContext(threadId, { ...annotation, comment: value })
            drafts.close(threadId, annotation.id)
            onClose()
          }}
          onClose={() => {
            drafts.close(threadId, annotation.id)
            onClose()
          }}
        />
      )}
    </div>
  )
}
