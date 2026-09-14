import { useEffect, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import type { PastedTextContext } from '../../../shared/composerContext'
import { useT } from '../../contexts/LocaleContext'
import { useLayerPresence } from '../../contexts/LayerContext'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'

export function ContextPreview({
  context,
  onClose,
  onRemove,
  onRestore,
  restoring,
}: {
  context: PastedTextContext
  onClose: () => void
  onRemove?: () => void
  onRestore?: () => void
  restoring: boolean
}): JSX.Element {
  const t = useT()
  const dialog = useRef<HTMLDialogElement>(null)
  const [text, setText] = useState<string | null>(null)
  const [error, setError] = useState('')
  useLayerPresence(true, true)
  useEffect(() => {
    dialog.current?.showModal()
  }, [])
  useEffect(() => {
    let current = true
    void window.api.workspace
      .readPastedText({ path: context.path })
      .then((result) => {
        if (current) setText(result.text)
      })
      .catch((reason) => {
        if (current)
          setError(String(reason instanceof Error ? reason.message : reason))
      })
    return () => {
      current = false
    }
  }, [context])
  return createPortal(
    <dialog
      ref={dialog}
      className="dc-context-preview"
      aria-label={t('composer.context.preview')}
      onCancel={onClose}
      onClick={(event) => {
        if (event.target === event.currentTarget) onClose()
      }}
    >
      <header>
        <div>
          <strong>{t('composer.context.pastedText')}</strong>
          <div className="dc-context-preview__source">
            {t('composer.context.characters', {
              count: context.characterCount ?? text?.length ?? 0,
            })}
          </div>
        </div>
        <IconButton
          icon={<X size={16} />}
          label={t('common.close')}
          onClick={onClose}
        />
      </header>
      <div className="dc-context-preview__body">
        {error ? (
          <p role="alert">{error}</p>
        ) : text === null ? (
          <p role="status">{t('composer.context.loading')}</p>
        ) : (
          <pre>{text}</pre>
        )}
      </div>
      <footer>
        {onRemove && <Button variant="ghost" size="sm" onClick={onRemove}>
          {t('composer.context.remove')}
        </Button>}
        <span className="dc-context-preview__spacer" />
        {onRestore && (
          <Button
            variant="secondary"
            size="sm"
            disabled={restoring || text === null || !!error}
            onClick={onRestore}
          >
            {t('composer.context.restore')}
          </Button>
        )}
        <Button variant="primary" size="sm" onClick={onClose}>
          {t('common.close')}
        </Button>
      </footer>
    </dialog>,
    document.body,
  )
}
