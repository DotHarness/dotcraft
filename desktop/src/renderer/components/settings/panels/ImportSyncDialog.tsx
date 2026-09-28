import { useId, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { ListChecks, SlidersHorizontal } from 'lucide-react'
import type { ImportSelection } from '@dotcraft/sdk/contracts'

import { useT } from '../../../contexts/LocaleContext'
import { LayerBoundary } from '../../../contexts/LayerContext'
import { Button } from '../../ui/Button'
import { ModalHeader } from '../../ui/ModalHeader'
import { ImportOptionRow } from './ImportOptionRow'
import { importCategoryIcon, importGroupIcon } from './ImportSelectionRows'
import { SETUP_CATEGORIES } from './importPresentation'
import { useImportDialogFocus } from './useImportDialogFocus'
import styles from './ImportDialog.module.css'

type SyncScope = 'user' | 'workspace'
const SCOPES: readonly SyncScope[] = ['user', 'workspace']

interface ImportSyncDialogProps {
  selection: ImportSelection
  onSave: (value: ImportSelection) => Promise<void>
  onClose: () => void
}

export function ImportSyncDialog({ selection, onSave, onClose }: ImportSyncDialogProps): JSX.Element {
  const t = useT()
  const id = useId()
  const listId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const [value, setValue] = useState(selection)
  const [expanded, setExpanded] = useState<Set<SyncScope>>(() => new Set())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useImportDialogFocus(dialogRef, onClose, busy)
  const empty = !value.all && value.user.length === 0 && value.workspace.length === 0 && !value.sessions

  async function save(): Promise<void> {
    setBusy(true)
    try {
      await onSave(value)
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  function toggleExpanded(scope: SyncScope): void {
    setExpanded(previous => {
      const next = new Set(previous)
      if (next.has(scope)) next.delete(scope)
      else next.add(scope)
      return next
    })
  }

  return createPortal(
    <LayerBoundary>
      <div className={styles.scrim} role="dialog" aria-modal="true" aria-labelledby={id}>
        <div ref={dialogRef} className={styles.dialog}>
          <ModalHeader
            icon={<SlidersHorizontal size={18} />}
            titleId={id}
            title={t('settings.import.setup.syncContent')}
            description={t('settings.import.syncDialog.description')}
            onClose={busy ? undefined : onClose}
            closeLabel={t('common.close')}
          />
          <div className={styles.options}>
            <ImportOptionRow
              icon={<ListChecks size={18} strokeWidth={1.8} />}
              title={t('settings.import.syncDialog.all')}
              description={t('settings.import.syncDialog.allHint')}
              checked={value.all}
              disabled={busy}
              onChange={all => setValue({ ...value, all })}
            />
          </div>
          <div className={styles.options}>
            {SCOPES.map(scope => {
              const categories = SETUP_CATEGORIES.filter(category => scope === 'user' || category !== 'plugins')
              const picked = value.all ? categories.length : categories.filter(category => value[scope].includes(category)).length
              const label = t(`settings.import.setup.${scope}`)
              const open = expanded.has(scope)
              const nestedId = `${listId}-${scope}`
              return (
                <div key={scope} className={styles.group}>
                  <ImportOptionRow
                    icon={importGroupIcon(scope)}
                    title={label}
                    description={t('settings.import.syncDialog.selected', { selected: picked, total: categories.length })}
                    expanded={open}
                    expandLabel={t(open ? 'settings.import.setup.hideDetails' : 'settings.import.setup.showDetails', { group: label })}
                    controlsId={nestedId}
                    onToggleExpanded={() => toggleExpanded(scope)}
                    checked={picked === categories.length}
                    indeterminate={picked > 0 && picked < categories.length}
                    disabled={busy || value.all}
                    onChange={checked => setValue({ ...value, [scope]: checked ? [...categories] : [] })}
                  />
                  {open && (
                    <div id={nestedId} role="group" aria-label={label} className={styles.nested}>
                      {categories.map(category => (
                        <ImportOptionRow
                          key={category}
                          nested
                          icon={importCategoryIcon(category)}
                          title={t(`settings.import.category.${category}`)}
                          checked={value.all || value[scope].includes(category)}
                          disabled={busy || value.all}
                          onChange={checked => setValue({
                            ...value,
                            [scope]: checked ? [...value[scope], category] : value[scope].filter(c => c !== category)
                          })}
                        />
                      ))}
                    </div>
                  )}
                </div>
              )
            })}
            <div className={styles.group}>
              <ImportOptionRow
                icon={importGroupIcon('sessions')}
                title={t('settings.import.category.sessions')}
                checked={value.all || value.sessions}
                disabled={busy || value.all}
                onChange={sessions => setValue({ ...value, sessions })}
              />
            </div>
          </div>
          {error && <p role="alert" className={styles.error}>{error}</p>}
          <p className={styles.note}>{t(empty ? 'settings.import.syncDialog.empty' : 'settings.import.syncDialog.footer')}</p>
          <div className={styles.footer}>
            <Button disabled={busy} onClick={onClose}>{t('common.cancel')}</Button>
            <Button variant="primary" loading={busy} disabled={empty} onClick={() => void save()}>{t('settings.save')}</Button>
          </div>
        </div>
      </div>
    </LayerBoundary>,
    document.body
  )
}
