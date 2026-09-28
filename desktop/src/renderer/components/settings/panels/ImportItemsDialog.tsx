import { useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import type { ImportCandidate, ImportSelection, ImportItemReference } from '@dotcraft/sdk/contracts'

import { LayerBoundary } from '../../../contexts/LayerContext'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { ModalHeader } from '../../ui/ModalHeader'
import { ImportSourceIcon } from './ImportSourceIcon'
import { ImportSelectionRows, importItemKey, selectionFor } from './ImportSelectionRows'
import { importable } from './importPresentation'
import styles from './ImportDialog.module.css'
import { useImportDialogFocus } from './useImportDialogFocus'

interface ImportItemsDialogProps {
  source: string
  items: ImportCandidate[]
  workspaceName: string
  workspacePath?: string
  /** Rejects with the reason to show inline; the owner closes the dialog on success. */
  onConfirm: (selection: ImportSelection, items: ImportItemReference[]) => Promise<void>
  onClose: () => void
}

export function ImportItemsDialog({
  source,
  items,
  workspaceName,
  workspacePath,
  onConfirm,
  onClose
}: ImportItemsDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const [selected, setSelected] = useState(() => new Set(items.filter(importable).map(importItemKey)))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useImportDialogFocus(dialogRef, onClose, busy)

  async function handleConfirm(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      const references = items
        .filter(item => selected.has(importItemKey(item)))
        .map(({ source, sourceId, fingerprint }) => ({ source, sourceId, fingerprint }))
      await onConfirm(selectionFor(items, selected), references)
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
      <div ref={dialogRef} className={styles.dialog} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<ImportSourceIcon source={source} />}
          badgedIcon={false}
          title={t('settings.import.dialog.title')}
          titleId={titleId}
          description={t('settings.import.dialog.description')}
          onClose={busy ? undefined : onClose}
          closeLabel={t('common.close')}
        />

        <ImportSelectionRows
          items={items}
          selected={selected}
          onChange={setSelected}
          disabled={busy}
          workspaceName={workspaceName}
          workspacePath={workspacePath}
        />

        {error && <p role="alert" className={styles.error}>{error}</p>}

        <div className={styles.footer}>
          <Button variant="secondary" disabled={busy} onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            variant="primary"
            loading={busy}
            disabled={selected.size === 0}
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
