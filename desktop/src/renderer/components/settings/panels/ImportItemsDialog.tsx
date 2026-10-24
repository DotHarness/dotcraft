import { useId, useRef, useState, type JSX } from 'react'
import type { ImportCandidate, ImportSelection, ImportItemReference } from '@dotcraft/sdk/contracts'

import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { ModalDialog, ModalFooter } from '../../ui/ModalDialog'
import { ModalHeader } from '../../ui/ModalHeader'
import { ImportSourceIcon } from './ImportSourceIcon'
import { ImportSelectionRows, importItemKey, selectionFor } from './ImportSelectionRows'
import { importable } from './importPresentation'
import { useImportDialogFocus } from './useImportDialogFocus'

interface ImportItemsDialogProps {
  source: string
  items: ImportCandidate[]
  workspaceName: string
  workspacePath?: string
  /** Rejects with the reason to show inline; the owner closes the dialog on success. */
  onConfirm: (selection: ImportSelection, offered: ImportSelection, items: ImportItemReference[]) => Promise<void>
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
      const offered = selectionFor(items, new Set(items.filter(importable).map(importItemKey)))
      await onConfirm(selectionFor(items, selected), offered, references)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  return (
    <ModalDialog
      ref={dialogRef}
      titleId={titleId}
      width={560}
      onDismiss={busy ? undefined : onClose}
      header={(
        <ModalHeader
          icon={<ImportSourceIcon source={source} />}
          badgedIcon={false}
          title={t('settings.import.dialog.title')}
          titleId={titleId}
          description={t('settings.import.dialog.description')}
          onClose={busy ? undefined : onClose}
          closeLabel={t('common.close')}
        />
      )}
      error={error}
      footer={(
        <ModalFooter>
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
        </ModalFooter>
      )}
    >
      <ImportSelectionRows
        items={items}
        selected={selected}
        onChange={setSelected}
        disabled={busy}
        workspaceName={workspaceName}
        workspacePath={workspacePath}
      />
    </ModalDialog>
  )
}
