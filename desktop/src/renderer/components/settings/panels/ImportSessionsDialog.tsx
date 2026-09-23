import { useEffect, useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { MessageSquare } from 'lucide-react'

import { LayerBoundary } from '../../../contexts/LayerContext'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { Checkbox } from '../../ui/Checkbox'
import { ModalHeader } from '../../ui/ModalHeader'
import { ImportSourceIcon } from './ImportSourceIcon'
import styles from './ImportSessionsDialog.module.css'

interface ImportSessionsDialogProps {
  source: string
  sourceLabel: string
  count: number
  /** Rejects with the reason to show inline; the owner closes the dialog on success. */
  onConfirm: () => Promise<void>
  onClose: () => void
}

export function ImportSessionsDialog({
  source,
  sourceLabel,
  count,
  onConfirm,
  onClose
}: ImportSessionsDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const confirmRef = useRef<HTMLButtonElement>(null)
  const [chatsSelected, setChatsSelected] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const chatsTitle = t('settings.import.dialog.chats', { count })

  // Whatever opened the dialog gets the focus back when it closes, so this runs
  // before anything here moves the focus.
  useEffect(() => {
    const opener = document.activeElement
    return () => {
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus()
    }
  }, [])

  useEffect(() => {
    confirmRef.current?.focus()
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !busy) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [busy, onClose])

  async function handleConfirm(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await onConfirm()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className={styles.scrim}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy) onClose()
      }}
    >
      <div className={styles.dialog} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<ImportSourceIcon source={source} />}
          badgedIcon={false}
          title={t('settings.import.dialog.title')}
          titleId={titleId}
          description={t('settings.import.dialog.description')}
          onClose={busy ? undefined : onClose}
          closeLabel={t('common.close')}
        />

        <div className={styles.items}>
          <div className={styles.item}>
            <MessageSquare size={18} strokeWidth={1.8} aria-hidden className={styles.itemIcon} />
            <span className={styles.itemText}>
              <span className={styles.itemTitle}>{chatsTitle}</span>
              <span className={styles.itemHint}>
                {t('settings.import.dialog.chatsHint', { source: sourceLabel })}
              </span>
            </span>
            <Checkbox
              checked={count > 0 && chatsSelected}
              disabled={count === 0 || busy}
              onChange={setChatsSelected}
              ariaLabel={chatsTitle}
            />
          </div>
        </div>

        {error && <p role="alert" className={styles.error}>{error}</p>}

        <div className={styles.footer}>
          <Button variant="secondary" disabled={busy} onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            ref={confirmRef}
            variant="primary"
            loading={busy}
            disabled={count === 0 || !chatsSelected}
            onClick={() => void handleConfirm()}
          >
            {t('settings.import.dialog.confirm')}
          </Button>
        </div>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}
