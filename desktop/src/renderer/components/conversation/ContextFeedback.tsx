import { FeedbackImage } from './FeedbackImage'
import { useState } from 'react'
import { MessageSquare, Pencil, X } from 'lucide-react'
import type { ComposerContextRecord } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'
import { PillDropdown } from '../ui/PillDropdown'
import { IconButton } from '../ui/IconButton'
import { ContextCommentEditor } from './ContextCommentEditor'

type Feedback = Exclude<ComposerContextRecord, { kind: 'pastedText' }>

export function ContextFeedback({
  onEdit,
  onRemove,
  contexts,
}: {
  onEdit?: (id: string, comment: string) => void
  onRemove?: (id: string) => void
  contexts: Feedback[]
}): JSX.Element | null {
  const t = useT()
  const [editing, setEditing] = useState<string | null>(null)
  if (!contexts.length) return null
  const comments = contexts.filter((context) => context.kind === 'diffAnnotation').length
  const annotations = contexts.length - comments
  const commentsLabel =
    comments === 1
      ? t('composer.context.commentSingle')
      : t('composer.context.commentCount', { count: comments })
  const annotationsLabel =
    annotations === 1
      ? t('composer.context.feedbackSingle')
      : t('composer.context.feedbackCount', { count: annotations })
  const label = !annotations
    ? commentsLabel
    : !comments
      ? annotationsLabel
      : t('composer.context.mixedSummary', { annotations: annotationsLabel, comments: commentsLabel })
  return (
    <div className="dc-feedback-attachments">
      <PillDropdown
        label={label}
        ariaLabel={label}
        icon={<MessageSquare size={14} />}
        panelMinWidth={294}
        panelMaxHeight={400}
      >
        {() => (
          <ol className="dc-feedback-attachments__list">
            {contexts.map((context, index) => (
              <li key={context.id}>
                <div className="dc-feedback-attachments__heading">
                  <span>
                    {index + 1}.{' '}
                    {context.kind === 'responseAnnotation'
                      ? t('composer.context.reply')
                      : context.kind === 'pageReference'
                        ? context.title
                        : `${context.path.split(/[\\/]/).pop()}:${context.startLine}–${context.endLine} (${context.side === 'left' ? '−' : '+'})`}
                  </span>
                  {onEdit && <IconButton
                    size={24}
                    radius={6}
                    icon={<Pencil size={14} />}
                    label={t('composer.context.edit')}
                    tooltipLabel={t('composer.context.edit')}
                    onClick={() => setEditing(context.id)}
                  />}
                  {onRemove && <IconButton
                    size={24}
                    radius={6}
                    icon={<X size={14} />}
                    label={t('composer.context.remove')}
                    tooltipLabel={t('composer.context.remove')}
                    onClick={() => onRemove(context.id)}
                  />}

                </div>
                {context.kind === 'pageReference' && (
                  <div className="dc-feedback-attachments__source">
                    {context.url}
                  </div>
                )}
                {(context.kind === 'pageReference'
                  ? context.text
                  : context.selectedText) && (
                  <>
                    <span className="dc-feedback-attachments__source">
                      {t('composer.context.selectedText')}
                    </span>
                    <p>
                      {context.kind === 'pageReference'
                        ? context.text
                        : context.selectedText}
                    </p>
                  </>
                )}
                {context.kind === 'pageReference' && context.image && (
                  <FeedbackImage image={context.image} title={context.title} />
                )}
                {editing === context.id ? (
                  <ContextCommentEditor
                    mode="edit"
                    initialComment={context.comment}
                    onCancel={() => setEditing(null)}
                    onSave={(comment) => {
                      onEdit?.(context.id, comment)
                      setEditing(null)
                    }}
                  />
                ) : context.comment ? (
                  <>
                    <span className="dc-feedback-attachments__source">
                      {t('composer.context.comment')}
                    </span>
                    <p>{context.comment}</p>
                  </>
                ) : null}
              </li>
            ))}
          </ol>
        )}
      </PillDropdown>
      {onRemove && <IconButton
        size={24}
        radius={6}
        icon={<X size={14} />}
        label={t(annotations ? 'composer.context.removeFeedback' : 'composer.context.removeComments')}
        tooltipLabel={t(annotations ? 'composer.context.removeFeedback' : 'composer.context.removeComments')}
        onClick={() => contexts.forEach(context => onRemove(context.id))}
      />}

    </div>
  )
}
