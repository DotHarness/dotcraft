import { useId, useRef, useState, type ComponentProps, type JSX, type ReactNode } from 'react'
import type { ImportSelection } from '@dotcraft/sdk/contracts'
import type { LocalImportItem, LocalImportRunParams, LocalImportSource } from '../../../../shared/localImport'
import { useLocale, useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { ModalDialog, ModalFooter } from '../../ui/ModalDialog'
import { ModalHeader } from '../../ui/ModalHeader'
import { ImportSourceIcon } from './ImportSourceIcon'
import { ImportOptionRow } from './ImportOptionRow'
import { importCategoryIcon, importGroupIcon } from './ImportSelectionRows'
import { categoryInfo, importable } from './importPresentation'
import { reasonText } from './importOutcomes'
import { useImportDialogFocus } from './useImportDialogFocus'
import styles from './ImportDialog.module.css'

const canImport = (item: LocalImportItem): boolean => item.destination !== 'unavailable' && importable(item.candidate)

function selectionFor(items: LocalImportItem[]): ImportSelection {
  return {
    all: false,
    user: [...new Set(items.filter(item => item.destination === 'personal').map(item => item.candidate.category))],
    workspace: [...new Set(items.filter(item => item.destination !== 'personal' && item.candidate.category !== 'sessions')
      .map(item => item.candidate.category))],
    sessions: items.some(item => item.candidate.category === 'sessions')
  }
}

export function LocalImportItemsDialog({ entry, blocked, onConfirm, onClose }: {
  entry: LocalImportSource
  blocked: boolean
  onConfirm: (params: LocalImportRunParams) => Promise<void>
  onClose: () => void
}): JSX.Element {
  const t = useT()
  const locale = useLocale()
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const [selected, setSelected] = useState(() => new Set(entry.items
    .filter(item => canImport(item) && item.destination !== 'newProject').map(item => item.key)))
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  useImportDialogFocus(dialogRef, onClose, busy)

  const offered = entry.items.filter(canImport)
  const chosen = offered.filter(item => selected.has(item.key))
  const personal = entry.items.filter(item => item.destination === 'personal')
  const chats = entry.items.filter(item => item.destination === 'chats')
  const projects = new Map<string, LocalImportItem[]>()
  for (const item of entry.items) {
    if (item.destination !== 'project' && item.destination !== 'newProject') continue
    const key = item.projectPath ?? item.key
    const group = projects.get(key)
    if (group) group.push(item)
    else projects.set(key, [item])
  }

  function toggleItems(items: LocalImportItem[], checked: boolean): void {
    setSelected(previous => {
      const next = new Set(previous)
      for (const item of items) {
        if (checked) next.add(item.key)
        else next.delete(item.key)
      }
      return next
    })
  }

  function rowSelection(items: LocalImportItem[]) {
    const eligible = items.filter(canImport)
    const count = eligible.filter(item => selected.has(item.key)).length
    return {
      checked: count > 0 && count === eligible.length,
      indeterminate: count > 0 && count < eligible.length,
      disabled: busy || eligible.length === 0,
      onChange: (checked: boolean) => toggleItems(eligible, checked)
    }
  }

  async function confirm(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await onConfirm({
        source: entry.source,
        selection: selectionFor(chosen),
        offered: selectionFor(offered),
        items: chosen.map(({ key, candidate }) => ({ key, fingerprint: candidate.fingerprint }))
      })
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      setBusy(false)
    }
  }

  function itemRow(item: LocalImportItem): JSX.Element {
    const candidate = item.candidate
    const eligible = canImport(item)
    const title = candidate.title || t(`settings.import.category.${candidate.category}`)
    const reason = reasonText({ errorCode: candidate.reason, error: candidate.fallbackText }, t)
    const description = reason ?? (candidate.state === 'current' ? t('settings.import.status.existing')
      : !eligible ? t(candidate.state === 'deferred' ? 'settings.import.status.deferred' : 'settings.import.status.unsupported')
      : candidate.category === 'sessions' ? undefined
      : t(`settings.import.category.${candidate.category}`))
    return (
      <ImportOptionRow
        key={item.key}
        nested
        title={title}
        description={description}
        icon={importCategoryIcon(candidate.category)}
         info={eligible && !reason && candidate.category !== 'sessions' ? categoryInfo(candidate.category, [candidate], locale, t) : undefined}
         infoLabel={t('settings.import.setup.itemInfo', { item: title })}
         checked={selected.has(item.key)}
         disabled={busy || !eligible}
         onChange={checked => toggleItems([item], checked)}
      />
    )
  }

  return (
    <ModalDialog
      ref={dialogRef}
      titleId={titleId}
      width={560}
      onDismiss={busy ? undefined : onClose}
      header={
        <ModalHeader
          icon={<ImportSourceIcon source={entry.source} />}
          badgedIcon={false}
          title={t('settings.import.dialog.title')}
          titleId={titleId}
          description={t('settings.import.local.dialogDescription')}
          onClose={busy ? undefined : onClose}
          closeLabel={t('common.close')}
        />
      }
      error={error}
      footer={
        <ModalFooter>
          <Button variant="secondary" disabled={busy} onClick={onClose}>{t('common.cancel')}</Button>
          <Button variant="primary" loading={busy} disabled={chosen.length === 0 || busy || blocked} onClick={() => void confirm()}>
            {t('settings.import.dialog.confirm')}
          </Button>
        </ModalFooter>
      }
    >
      <div className={styles.options}>
        {personal.length > 0 && (
          <DestinationOptions
            title={t('settings.import.setup.user')}
            description={t('settings.import.setup.userHint')}
            icon={importGroupIcon('user')}
            selection={rowSelection(personal)}
          >
            {personal.map(itemRow)}
          </DestinationOptions>
        )}
        {projects.size > 0 && (
          <DestinationOptions title={t('projectsRail.title')} icon={importGroupIcon('workspace')} defaultExpanded>
            {[...projects.entries()].map(([key, items]) => {
              const { projectPath, projectName, destination } = items[0]
              return (
                <DestinationOptions
                  key={key}
                  nested
                  title={projectName || projectPath || items[0].candidate.title}
                  description={destination === 'newProject' && projectPath
                    ? t('settings.import.local.addProject', { path: projectPath })
                    : projectPath ?? undefined}
                  icon={importGroupIcon('workspace')}
                  selection={rowSelection(items)}
                >
                  {items.map(itemRow)}
                </DestinationOptions>
              )
            })}
          </DestinationOptions>
        )}
        {chats.length > 0 && (
          <DestinationOptions
            title={t('settings.import.category.sessions')}
            description={t('settings.import.local.chatsDestination')}
            icon={importGroupIcon('sessions')}
            selection={rowSelection(chats)}
          >
            {chats.map(itemRow)}
          </DestinationOptions>
        )}
      </div>
    </ModalDialog>
  )
}

function DestinationOptions({ title, description, icon, selection, nested, defaultExpanded = false, children }: {
  title: string
  description?: string
  icon: ReactNode
  selection?: Pick<ComponentProps<typeof ImportOptionRow>, 'checked' | 'indeterminate' | 'disabled' | 'onChange'>
  nested?: boolean
  defaultExpanded?: boolean
  children: ReactNode
}): JSX.Element {
  const t = useT()
  const id = useId()
  const [expanded, setExpanded] = useState(defaultExpanded)
  return (
    <div className={nested ? undefined : styles.group}>
      <ImportOptionRow
        title={title}
        description={description}
        icon={icon}
        nested={nested}
        expanded={expanded}
        expandLabel={t(expanded ? 'settings.import.setup.hideDetails' : 'settings.import.setup.showDetails', { group: title })}
        controlsId={expanded ? id : undefined}
        onToggleExpanded={() => setExpanded(!expanded)}
        {...selection}
      />
      {expanded && (
        <div id={id} role="group" aria-label={title} className={nested ? styles.projectItems : styles.nested}>
          {children}
        </div>
      )}
    </div>
  )
}
