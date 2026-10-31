import { useState } from 'react'
import { Pencil, SlidersHorizontal, X } from 'lucide-react'
import type {
  ComposerContextRecord,
  FileAnnotationContext,
  LineCommentContext,
  PageReferenceContext,
  ResponseAnnotationContext,
  VisualizationChangesContext
} from '../../../shared/composerContext'
import { isLineCommentContext } from '../../../shared/messageComments'
import { useT } from '../../contexts/LocaleContext'
import { useConversationStore } from '../../stores/conversationStore'
import { useViewerTabStore } from '../../stores/viewerTabStore'
import { openFileViewer } from '../../utils/conversationDeepLink'
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
  const comments = contexts.filter(isLineCommentContext)
  const pages = contexts.filter((context): context is PageReferenceContext => context.kind === 'pageReference')
  const replies = contexts.filter((context): context is ResponseAnnotationContext => context.kind === 'responseAnnotation')
  const visualizations = contexts.filter((context): context is VisualizationChangesContext => context.kind === 'visualizationChanges')
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
      {visualizations.map((visualization) => (
        <FeedbackPill
          key={visualization.id}
          label={t('composer.context.visualizationChanges', { title: visualization.title })}
          removeLabel={t('composer.context.removeVisualizationChanges')}
          onRemove={onRemove && (() => onRemove(visualization.id))}
          icon={<SlidersHorizontal size={14} aria-hidden />}
        >
          <div className="dc-feedback-list">
            {visualization.text.split('\n').map((line, index) => (
              <div key={index} className="dc-feedback-row">
                <div className="dc-feedback-text">{line}</div>
              </div>
            ))}
          </div>
        </FeedbackPill>
      ))}
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

function fileName(path: string): string {
  return path.split(/[/\\]/).pop() || path
}

function LineCommentList({ comments }: { comments: LineCommentContext[] }): JSX.Element {
  const t = useT()
  const workspacePath = useConversationStore((state) => state.workspacePath)
  const remoteWorkspaceActive = useConversationStore((state) => state.remoteWorkspaceActive)
  const viewerScopeId = useViewerTabStore((state) => state.currentThreadId)
  const chip = (comment: LineCommentContext, targetPath: string, label: string, onOpen?: (threadId: string) => void) => (
    <FileRefChip
      className="dc-feedback-file"
      displayPath={comment.path}
      targetPath={targetPath}
      label={label}
      workspacePath={workspacePath}
      activeThreadId={viewerScopeId}
      remoteWorkspaceActive={remoteWorkspaceActive}
      onOpen={onOpen}
    />
  )
  const openTarget = (comment: FileAnnotationContext) => (threadId: string) => {
    void openFileViewer({
      threadId,
      workspacePath,
      absolutePath: comment.path,
      forceNew: false,
      reveal: { targetKind: comment.targetKind, location: comment.location },
      t
    })
  }
  return (
    <div className="dc-feedback-list">
      {comments.map((comment) => (
        <div key={comment.id} className="dc-feedback-row">
          {comment.kind === 'fileAnnotation' ? (
            <div className="dc-feedback-meta">
              {chip(comment, comment.path, fileName(comment.path), openTarget(comment))}
              <span>{comment.target}</span>
            </div>
          ) : (
            <div className="dc-feedback-meta">
              {chip(comment, `${comment.path}:${comment.startLine}`, toWorkspaceRelativePath(workspacePath, comment.path))}
              <span className="dc-feedback-side">{comment.side === 'left' ? 'L' : 'R'}</span>
              <span>
                {comment.startLine === comment.endLine
                  ? comment.startLine
                  : `${comment.startLine}–${comment.endLine}`}
              </span>
            </div>
          )}
          {comment.comment && <div className="dc-feedback-text">{comment.comment}</div>}
        </div>
      ))}
    </div>
  )
}
