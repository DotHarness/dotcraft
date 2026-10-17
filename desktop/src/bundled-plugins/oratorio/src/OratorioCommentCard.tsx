import { useLayoutEffect, useRef, type ReactNode } from 'react'
import { useOratorioI18n } from './oratorio-i18n'
import { Textarea, UserAvatar } from './ui'

const MAX_HEIGHT = 240

export function OratorioCommentCard({ value, onChange, placeholder, ariaLabel, autoFocus = false, onSubmit, onCancel, leading, actions }: {
  value: string
  onChange: (value: string) => void
  placeholder: string
  ariaLabel: string
  autoFocus?: boolean
  onSubmit?: () => void
  onCancel?: () => void
  leading?: ReactNode
  actions: ReactNode
}): JSX.Element {
  const { t } = useOratorioI18n()
  const body = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    const input = body.current?.querySelector('textarea')
    if (!input) return
    input.style.height = 'auto'
    input.style.height = `${Math.min(MAX_HEIGHT, input.scrollHeight)}px`
    input.style.overflowY = input.scrollHeight > MAX_HEIGHT ? 'auto' : 'hidden'
  }, [value])

  return (
    <div
      className="ora-comment-card"
      onPointerDown={(event) => {
        if ((event.target as HTMLElement).closest('textarea, button, a, input, [role="button"]')) return
        event.preventDefault()
        body.current?.querySelector('textarea')?.focus()
      }}
    >
      <div className="ora-comment-card__header">
        <span className="ora-comment-card__identity"><UserAvatar size={24} /><span>{t('you')}</span></span>
      </div>
      <div className="ora-comment-card__body" ref={body}>
        <Textarea
          bare
          rows={1}
          autoFocus={autoFocus}
          value={value}
          placeholder={placeholder}
          aria-label={ariaLabel}
          onChange={(event) => onChange(event.target.value)}
          onKeyDown={(event) => {
            if (event.nativeEvent.isComposing) return
            if (event.key === 'Escape' && onCancel) {
              event.preventDefault()
              onCancel()
            } else if (event.key === 'Enter' && !event.shiftKey && !event.altKey && onSubmit) {
              event.preventDefault()
              onSubmit()
            }
          }}
        />
      </div>
      <div className="ora-comment-card__footer">
        {leading}
        <span className="ora-comment-card__spacer" />
        {actions}
      </div>
    </div>
  )
}
