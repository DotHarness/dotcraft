import { useLayoutEffect, useRef, useState } from 'react'
import { Check } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { Textarea } from '../ui/Input'
import { Button } from '../ui/Button'
import { VoiceInputControl } from './VoiceInputControl'
import { useCommentVoice } from './useCommentVoice'

export function ContextCommentEditor({
  initialComment = '',
  mode = 'create',
  onSave,
  onCancel,
}: {
  initialComment?: string
  mode?: 'create' | 'edit'
  onSave: (comment: string) => void
  onCancel: () => void
}): JSX.Element {
  const t = useT()
  const [comment, setComment] = useState(initialComment)
  const input = useRef<HTMLTextAreaElement>(null)
  const voice = useCommentVoice(comment, setComment)
  const hasText = comment.trim().length > 0
  useLayoutEffect(() => {
    const element = input.current
    if (!element) return
    element.style.height = 'auto'
    const height = Math.min(160, element.scrollHeight)
    element.style.height = `${height}px`
    element.style.overflowY = element.scrollHeight > 160 ? 'auto' : 'hidden'
  }, [comment])
  return (
    <div
      className="dc-context-comment-editor"
      data-mode={mode}
      role="group"
      aria-label={t('composer.context.comment')}
    >
      <Textarea
        ref={input}
        autoFocus
        rows={1}
        bare
        aria-label={t('composer.context.comment')}
        placeholder={t('composer.context.commentPlaceholder')}
        value={comment}
        onChange={(event) => setComment(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Escape') {
            event.preventDefault()
            event.stopPropagation()
            onCancel()
          } else if (
            event.key === 'Enter' &&
            !event.shiftKey &&
            !event.nativeEvent.isComposing &&
            !voice.busy
          ) {
            event.preventDefault()
            onSave(comment)
          }
        }}
      />
      <div className="dc-context-comment-editor__actions">
        <span hidden={hasText && !voice.busy && mode !== 'edit'}>
          <VoiceInputControl
            threadId={voice.origin}
            enableShortcut={false}
            compact
          />
        </span>
        {mode === 'edit' ? (
          <>
            <Button size="sm" variant="secondary" onClick={onCancel}>
              {t('common.cancel')}
            </Button>
            <Button
              size="sm"
              variant="primary"
              disabled={voice.busy}
              onClick={() => onSave(comment)}
            >
              {t('composer.context.save')}
            </Button>
          </>
        ) : hasText && !voice.busy ? (
          <Button
            variant="primary"
            size="iconSm"
            className="dc-context-comment-editor__submit"
            aria-label={t('composer.context.save')}
            onClick={() => onSave(comment)}
          >
            <Check size={17} aria-hidden />
          </Button>
        ) : null}
      </div>
    </div>
  )
}
