import { useState } from 'react'
import { Pencil, X } from 'lucide-react'
import type {
  ComposerContextRecord,
  DiffAnnotationContext,
  PageReferenceContext,
  ResponseAnnotationContext
} from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { toWorkspaceRelativePath } from '../../utils/workspacePaths'
import { IconButton } from '../ui/IconButton'
import { ContextCommentEditor } from './ContextCommentEditor'
import { FeedbackImage } from './FeedbackImage'
import { FeedbackPill } from './FeedbackPill'
import { FileRefChip } from './FileRefChip'

type Feedback = Exclude<ComposerContextRecord, { kind: 'pastedText' }>

const selectionLabel = {
  text: 'composer.context.selectedText',
  element: 'composer.context.selectedElement',
  region: 'composer.context.selectedRegion'
} as const

export function ContextFeedback({
  onEdit,
  onRemove,
  contexts,
}: {
  onEdit?: (id: string, comment: string) => void
  onRemove?: (id: string) => void
  contexts: Feedback[]
}): JSX.Element {
  const t = useT()
  const [editing, setEditing] = useState<string | null>(null)
  const comments = contexts.filter((context): context is DiffAnnotationContext => context.kind === 'diffAnnotation')
  const pages = contexts.filter((context): context is PageReferenceContext => context.kind === 'pageReference')
  const replies = contexts.filter((context): context is ResponseAnnotationContext => context.kind === 'responseAnnotation')
  const annotations = pages.length + replies.length
  const removeAll = (group: Feedback[]): (() => void) | undefined =>
    onRemove && (() => group.forEach((context) => onRemove(context.id)))
  return (
    <>
      {annotations > 0 && (
        <FeedbackPill
          label={annotations === 1
            ? t('composer.context.feedbackSingle')
            : t('composer.context.feedbackCount', { count: annotations })}
          removeLabel={t('composer.context.removeFeedback')}
          onRemove={removeAll([...pages, ...replies])}
          keepOpen={editing !== null}
        >
          <div className="dc-feedback-list">
            {pages.map((page) => (
              <div key={page.id} className="dc-feedback-row">
                <div className="dc-feedback-meta dc-feedback-meta--selection">
                  {page.image && <FeedbackImage image={page.image} />}
                  {t(selectionLabel[page.selectionKind])}
                </div>
                {page.text && <div className="dc-feedback-selection" title={page.text}>{page.text}</div>}
                {page.comment && <div className="dc-feedback-text">{page.comment}</div>}
              </div>
            ))}
            {replies.length > 0 && (
              <ol className="dc-feedback-list">
                {replies.map((reply, index) => (
                  <li key={reply.id} className="dc-feedback-annotation">
                    <span className="dc-feedback-annotation__ordinal">{index + 1}.</span>
                    <div className="dc-feedback-annotation__content">
                      <div>
                        <div className="dc-feedback-label">{t('composer.context.selectedTextLabel')}</div>
                        <div className="dc-feedback-value">{reply.selectedText}</div>
                      </div>
                      {editing === reply.id ? (
                        <ContextCommentEditor
                          mode="edit"
                          initialComment={reply.comment}
                          onCancel={() => setEditing(null)}
                          onSave={(comment) => {
                            onEdit?.(reply.id, comment)
                            setEditing(null)
                          }}
                        />
                      ) : reply.comment.trim() ? (
                        <div>
                          <div className="dc-feedback-label">{t('composer.context.userCommentLabel')}</div>
                          <div className="dc-feedback-value">{reply.comment}</div>
                        </div>
                      ) : null}
                    </div>
                    {(onEdit || onRemove) && editing !== reply.id && (
                      <div className="dc-feedback-annotation__actions">
                        {onEdit && <IconButton
                          size={24}
                          radius={6}
                          icon={<Pencil size={14} />}
                          label={t('composer.context.edit', { number: index + 1 })}
                          tooltipLabel={t('composer.context.edit', { number: index + 1 })}
                          onClick={() => setEditing(reply.id)}
                        />}
                        {onRemove && <IconButton
                          size={24}
                          radius={6}
                          icon={<X size={14} />}
                          label={t('composer.context.removeAnnotation', { number: index + 1 })}
                          tooltipLabel={t('composer.context.removeAnnotation', { number: index + 1 })}
                          onClick={() => onRemove(reply.id)}
                        />}
                      </div>
                    )}
                  </li>
                ))}
              </ol>
            )}
          </div>
        </FeedbackPill>
      )}
      {comments.length > 0 && (
        <FeedbackPill
          label={comments.length === 1
            ? t('composer.context.commentSingle')
            : t('composer.context.commentCount', { count: comments.length })}
          removeLabel={t('composer.context.removeComments')}
          onRemove={removeAll(comments)}
        >
          <LineCommentList comments={comments} />
        </FeedbackPill>
      )}
    </>
  )
}

function LineCommentList({ comments }: { comments: DiffAnnotationContext[] }): JSX.Element {
  const workspacePath = useConversationStore((state) => state.workspacePath)
  const remoteWorkspaceActive = useConversationStore((state) => state.remoteWorkspaceActive)
  const viewerScopeId = useViewerTabStore((state) => state.currentThreadId)
  return (
    <div className="dc-feedback-list">
      {comments.map((comment) => (
        <div key={comment.id} className="dc-feedback-row">
          <div className="dc-feedback-meta">
            <FileRefChip
              className="dc-feedback-file"
              displayPath={comment.path}
              targetPath={`${comment.path}:${comment.startLine}`}
              label={toWorkspaceRelativePath(workspacePath, comment.path)}
              workspacePath={workspacePath}
              activeThreadId={viewerScopeId}
              remoteWorkspaceActive={remoteWorkspaceActive}
            />
            <span className="dc-feedback-side">{comment.side === 'left' ? 'L' : 'R'}</span>
            <span>
              {comment.startLine === comment.endLine
                ? comment.startLine
                : `${comment.startLine}–${comment.endLine}`}
            </span>
          </div>
          {comment.comment && <div className="dc-feedback-text">{comment.comment}</div>}
        </div>
      ))}
    </div>
  )
}
