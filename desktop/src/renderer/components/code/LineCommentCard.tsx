import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import type { DiffAnnotationContext } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { Textarea } from '../ui/Input'
import { VoiceInputControl } from '../conversation/VoiceInputControl'
import { useCommentVoice } from '../conversation/useCommentVoice'
import './line-comment.css'

type Translate = ReturnType<typeof useT>

export function lineCommentLabel(
  t: Translate,
  side: DiffAnnotationContext['side'],
  startLine: number,
  endLine: number
): string {
  const prefix = side === 'left' ? 'L' : 'R'
  return startLine === endLine
    ? t('lineComment.line', { line: `${prefix}${endLine}` })
    : t('lineComment.lines', { start: `${prefix}${startLine}`, end: `${prefix}${endLine}` })
}

function LineCommentShell({
  author,
  label,
  children,
  footer,
  onPointerDown
}: {
  author: string
  label: string
  children: ReactNode
  footer?: ReactNode
  onPointerDown?: (event: React.PointerEvent<HTMLDivElement>) => void
}): JSX.Element {
  return (
    <div className="dc-line-comment" data-find-skip>
      <div className="dc-line-comment__card" onPointerDown={onPointerDown}>
        <div className="dc-line-comment__header">
          <span className="dc-line-comment__author">{author}</span>
          <span>{label}</span>
        </div>
        <div className="dc-line-comment__body">{children}</div>
        {footer && <div className="dc-line-comment__footer">{footer}</div>}
      </div>
    </div>
  )
}

export function LineCommentCard({
  label,
  saved,
  initialComment,
  onChange,
  onSubmit,
  onClose,
  onDelete
}: {
  label: string
  saved: boolean
  initialComment: string
  onChange?: (comment: string) => void
  onSubmit: (comment: string) => void
  onClose?: () => void
  onDelete?: () => void
}): JSX.Element {
  const t = useT()
  const [comment, setComment] = useState(initialComment)
  const [editing, setEditing] = useState(!saved)
  const input = useRef<HTMLTextAreaElement>(null)
  const update = (value: string) => {
    setComment(value)
    onChange?.(value)
  }
  const voice = useCommentVoice(comment, update)
  const canSubmit = comment.length > 0 && !voice.busy

  useLayoutEffect(() => {
    const element = input.current
    if (!element) return
    element.style.height = 'auto'
    element.style.height = `${Math.min(240, element.scrollHeight)}px`
    element.style.overflowY = element.scrollHeight > 240 ? 'auto' : 'hidden'
  }, [comment])

  function submit() {
    if (!canSubmit) return
    onSubmit(comment)
    if (saved) {
      setEditing(false)
      input.current?.blur()
    }
  }

  function cancel() {
    if (!saved) {
      onClose?.()
      return
    }
    setComment(initialComment)
    setEditing(false)
    input.current?.blur()
  }

  const footer = editing ? (
    <>
      {saved && onDelete && (
        <Button size="sm" variant="danger" onClick={onDelete}>
          {t('lineComment.delete')}
        </Button>
      )}
      <span className="dc-line-comment__spacer" />
      <Button size="sm" variant="ghost" onClick={cancel}>
        {t('common.cancel')}
      </Button>
      <VoiceInputControl threadId={voice.origin} enableShortcut={false} compact />
      <Button size="sm" variant="primary" disabled={!canSubmit} onClick={submit}>
        {saved ? t('lineComment.save') : t('lineComment.comment')}
      </Button>
    </>
  ) : onDelete ? (
    <>
      <span className="dc-line-comment__spacer" />
      <Button size="sm" variant="ghost" onClick={onDelete}>
        {t('lineComment.delete')}
      </Button>
    </>
  ) : undefined

  return (
    <LineCommentShell
      author={t('lineComment.you')}
      label={label}
      footer={footer}
      onPointerDown={(event) => {
        if ((event.target as HTMLElement).closest('textarea, button, a, input, [role="button"]')) return
        event.preventDefault()
        input.current?.focus()
      }}
    >
      <Textarea
        ref={input}
        bare
        rows={1}
        autoFocus={!saved}
        aria-label={label}
        placeholder={t('lineComment.placeholder')}
        value={comment}
        onFocus={() => setEditing(true)}
        onBlur={(event) => {
          const card = event.currentTarget.closest('.dc-line-comment')
          if (saved && comment === initialComment && !card?.contains(event.relatedTarget as Node | null))
            setEditing(false)
        }}
        onChange={(event) => update(event.target.value)}
        onKeyDown={(event) => {
          if (event.nativeEvent.isComposing) return
          if (event.key === 'Escape' && !saved) {
            event.preventDefault()
            event.stopPropagation()
            onClose?.()
          } else if (event.key === 'Enter' && !event.shiftKey && !event.altKey) {
            event.preventDefault()
            submit()
          }
        }}
      />
    </LineCommentShell>
  )
}

export function ModelLineCommentCard({
  label,
  title,
  body
}: {
  label: string
  title: string
  body: string
}): JSX.Element {
  const t = useT()
  return (
    <LineCommentShell author={t('lineComment.modelAuthor')} label={label}>
      {title && <strong className="dc-line-comment__title">{title}</strong>}
      <p className="dc-line-comment__text">{body}</p>
    </LineCommentShell>
  )
}
