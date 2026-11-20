import { Button, CatalogPage, CatalogToolbarButton, IconButton, Markdown, Menu, Textarea, type CatalogPageBreadcrumb } from '@dotcraft/plugin'
import { ChevronDown, Code2, Database, Eye, MoreHorizontal, Tag } from 'lucide-react'
import { useEffect, useMemo, useRef, useState, type JSX } from 'react'
import { deleteEntry, readSnapshot, saveEntry, type EntryConflictReason, type EntrySnapshot } from '../data/entrySave'
import { isValidMemoryName, memoryName, parseMemoryFile, type MemoryFileFields } from '../data/memoryFile'
import type { MemoryFs } from '../data/memoryFs'
import { errorMessage } from '../data/rpc'
import type { MemoryStore } from '../data/stores'
import { useT } from '../i18n'
import type { MemoryType } from '../policy/policy'
import { memoryStudioHost } from '../runtime'
import { Notice } from '../shared/ui'
import { useStoreLabel } from './EntriesTab'
import styles from './entries.module.css'

export type EntryTarget =
  | { mode: 'existing'; storeKey: string; fileName: string }
  | { mode: 'new'; storeKey: string; body: string }

interface Conflict {
  reason: EntryConflictReason
  current: EntrySnapshot
}

type BodyMode = 'edit' | 'preview'

const EMPTY_FIELDS: MemoryFileFields = { name: '', description: '', type: '', body: '' }

function fieldsFrom(snapshot: EntrySnapshot): MemoryFileFields {
  if (snapshot.fileText == null) return EMPTY_FIELDS
  const parsed = parseMemoryFile(snapshot.fileText)
  return { name: memoryName(snapshot.fileName ?? ''), description: parsed.description, type: parsed.type, body: parsed.body }
}

export function EntryEditor({ fs, stores, target, types, breadcrumb, onClose }: {
  fs: MemoryFs
  stores: readonly MemoryStore[]
  target: EntryTarget
  types: readonly MemoryType[]
  breadcrumb: CatalogPageBreadcrumb
  onClose(changed: boolean): void
}): JSX.Element {
  const t = useT()
  const label = useStoreLabel()
  const [storeKey, setStoreKey] = useState(target.storeKey)
  const store = stores.find((candidate) => candidate.key === storeKey) ?? null
  const [snapshot, setSnapshot] = useState<EntrySnapshot | null>(null)
  const [fields, setFields] = useState<MemoryFileFields>(() => ({
    ...EMPTY_FIELDS, type: types[0]?.name ?? '', body: target.mode === 'new' ? target.body : ''
  }))
  const [loaded, setLoaded] = useState<MemoryFileFields | null>(null)
  const [bodyMode, setBodyMode] = useState<BodyMode>(target.mode === 'new' ? 'edit' : 'preview')
  const bodyRef = useRef<HTMLDivElement>(null)
  const focusSource = useRef(false)
  const [conflict, setConflict] = useState<Conflict | null>(null)
  const [busy, setBusy] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const readOnly = !store?.writable
  const invalidHeader = snapshot?.fileText != null && parseMemoryFile(snapshot.fileText).invalidHeader

  useEffect(() => {
    if (!store) return
    let cancelled = false
    setSnapshot(null)
    readSnapshot(fs, store.path, target.mode === 'existing' ? target.fileName : null).then((next) => {
      if (cancelled) return
      setSnapshot(next)
      if (target.mode === 'existing') {
        const read = fieldsFrom(next)
        const initial = { ...read, type: read.type || types[0]?.name || '' }
        setFields(initial)
        setLoaded(initial)
      }
    }, (error: unknown) => { if (!cancelled) setLoadError(errorMessage(error)) })
    return () => { cancelled = true }
  // The draft survives a store switch; only the snapshot is re-read.
  }, [fs, store?.path, target])

  // Keeps an undeclared type selectable so opening an entry never changes its type.
  const typeNames = useMemo(() => {
    const names = types.map((type) => type.name)
    if (fields.type && !names.includes(fields.type)) names.push(fields.type)
    return names
  }, [types, fields.type])
  const typeDescriptions = useMemo(() => new Map(types.map((type) => [type.name, type.description])), [types])

  const nameValid = isValidMemoryName(fields.name.trim())
  const nameInvalid = fields.name !== '' && !nameValid
  const descriptionValid = fields.description.trim() !== '' && !/[\r\n]/.test(fields.description)
  const canSave = !readOnly && !invalidHeader && snapshot != null && nameValid && descriptionValid && fields.type !== '' && !busy
  const dirty = target.mode === 'new' || (loaded != null && (
    fields.name !== loaded.name || fields.description !== loaded.description || fields.type !== loaded.type || fields.body !== loaded.body
  ))
  const set = <K extends keyof MemoryFileFields>(key: K, value: MemoryFileFields[K]): void => setFields((previous) => ({ ...previous, [key]: value }))

  const save = async (base: EntrySnapshot): Promise<void> => {
    const host = memoryStudioHost()
    setBusy(true)
    try {
      const outcome = await saveEntry(fs, base, fields)
      if (outcome.kind === 'conflict') {
        setConflict({ reason: outcome.reason, current: outcome.current })
        return
      }
      host.ui.showToast({ tone: 'success', message: t('editor.saved') })
      onClose(true)
    } catch (error) {
      host.ui.showToast({ tone: 'error', message: t('editor.saveFailed'), description: errorMessage(error) })
    } finally {
      setBusy(false)
    }
  }

  const remove = async (): Promise<void> => {
    const host = memoryStudioHost()
    if (!snapshot) return
    const confirmed = await host.ui.confirm({
      title: t('editor.deleteTitle', { name: fields.name }),
      message: t('editor.deleteMessage'),
      confirmLabel: t('editor.delete'),
      danger: true
    })
    if (!confirmed) return
    setBusy(true)
    try {
      const outcome = await deleteEntry(fs, snapshot)
      if (outcome.kind === 'conflict') {
        setConflict({ reason: outcome.reason, current: outcome.current })
        return
      }
      onClose(true)
    } catch (error) {
      host.ui.showToast({ tone: 'error', message: t('editor.deleteFailed'), description: errorMessage(error) })
    } finally {
      setBusy(false)
    }
  }

  const useCurrent = (): void => {
    if (!conflict || conflict.reason === 'nameTaken') return
    setSnapshot(conflict.current)
    if (conflict.current.fileText != null) {
      const current = fieldsFrom(conflict.current)
      setFields(current)
      setLoaded(current)
    }
    setConflict(null)
  }

  const sourceField = (): HTMLTextAreaElement | null => bodyRef.current?.querySelector('textarea') ?? null
  useEffect(() => {
    const field = sourceField()
    if (!field) return
    field.style.height = 'auto'
    field.style.height = `${field.scrollHeight + 2}px`
    if (focusSource.current) {
      focusSource.current = false
      field.focus()
    }
  }, [bodyMode, fields.body, readOnly])

  const editSource = (): void => {
    if (readOnly) return
    focusSource.current = true
    setBodyMode('edit')
  }

  const cancel = (): void => {
    if (target.mode === 'new' || !loaded) {
      onClose(false)
      return
    }
    setFields(loaded)
    setConflict(null)
    setBodyMode('preview')
  }

  const conflictText = conflict?.reason === 'indexChanged' ? conflict.current.indexText : conflict?.current.fileText
  const deletedMeanwhile = conflict?.reason === 'fileChanged' && conflict.current.fileText == null
  const writableStores = stores.filter((candidate) => candidate.writable)
  return (
    <CatalogPage
      title={fields.name || t('editor.newTitle')}
      breadcrumb={breadcrumb}
      actions={readOnly ? undefined : (
        <>
          {target.mode === 'existing' ? (
            <Menu items={[{ label: t('editor.delete'), danger: true, disabled: busy || snapshot == null, onSelect: () => void remove() }]}>
              <CatalogToolbarButton icon={<MoreHorizontal size={15} />} label={t('editor.moreActions')} onClick={() => undefined} />
            </Menu>
          ) : null}
          {dirty ? (
            <>
              <Button variant="ghost" size="toolbar" onClick={cancel}>{t('editor.cancel')}</Button>
              <Button variant="primary" size="toolbar" loading={busy} disabled={!canSave} onClick={() => snapshot && void save(snapshot)}>{t('editor.save')}</Button>
            </>
          ) : null}
        </>
      )}
      titleEditor={{
        value: fields.name,
        placeholder: t('editor.namePlaceholder'),
        ariaLabel: t('editor.name'),
        invalid: nameInvalid,
        readOnly,
        onValueChange: (name) => set('name', name)
      }}
      descriptionEditor={{
        value: fields.description,
        placeholder: t('editor.descriptionPlaceholder'),
        ariaLabel: t('editor.description'),
        readOnly,
        onValueChange: (description) => set('description', description)
      }}
      meta={(
        <>
          {readOnly ? (
            <span className={styles.capsule}><Tag size={14} />{fields.type || t('entries.untyped')}</span>
          ) : (
            <Menu items={typeNames.map((name) => ({ label: name, checked: fields.type === name, onSelect: () => set('type', name) }))}>
              <button type="button" className={styles.capsule} aria-label={t('editor.type')} title={typeDescriptions.get(fields.type) ?? t('entries.undeclaredType')}>
                <Tag size={14} />{fields.type || t('entries.untyped')}<ChevronDown size={14} className={styles.capsuleChevron} />
              </button>
            </Menu>
          )}
          {target.mode === 'new' && writableStores.length > 1 ? (
            <Menu items={writableStores.map((candidate) => ({
              label: label(candidate),
              checked: candidate.key === storeKey,
              onSelect: () => { setStoreKey(candidate.key); setConflict(null) }
            }))}>
              <button type="button" className={styles.capsule} aria-label={t('editor.store')}>
                <Database size={14} />{store ? label(store) : ''}<ChevronDown size={14} className={styles.capsuleChevron} />
              </button>
            </Menu>
          ) : null}
        </>
      )}
    >
      <div className={styles.editor}>
        {loadError ? <Notice tone="error">{loadError}</Notice> : null}
        {readOnly && store ? <Notice>{t('editor.readOnly', { name: label(store) })}</Notice> : null}
        {invalidHeader ? <Notice tone="error">{t('editor.invalidHeader')}</Notice> : null}
        {nameInvalid ? <Notice tone="error">{t('editor.nameHint')}</Notice> : null}
        {conflict ? (
          <Notice tone="warning">
            <div>{t(deletedMeanwhile ? 'editor.conflict.deleted' : `editor.conflict.${conflict.reason}` as const)}</div>
            {conflictText != null ? <pre className={styles.current}>{conflictText}</pre> : null}
            {conflict.reason === 'nameTaken' ? null : (
              <div className={styles.actions}>
                {deletedMeanwhile ? null : <Button variant="secondary" size="sm" onClick={useCurrent}>{t('editor.useCurrent')}</Button>}
                <Button variant="secondary" size="sm" disabled={!canSave} onClick={() => void save(conflict.current)}>{t('editor.saveMine')}</Button>
              </div>
            )}
          </Notice>
        ) : null}

        <div ref={bodyRef} className={styles.body} data-mode={bodyMode === 'edit' && !readOnly ? 'source' : 'preview'}>
          {bodyMode === 'edit' && !readOnly ? (
            <Textarea
              className={styles.bodyField}
              aria-label={t('editor.body')}
              value={fields.body}
              mono
              onChange={(event) => set('body', event.target.value)}
              onKeyDown={(event) => { if (event.key === 'Escape') setBodyMode('preview') }}
            />
          ) : (
            <div className={styles.preview} onDoubleClick={editSource}>
              {fields.body.trim()
                ? <Markdown content={fields.body} />
                : readOnly
                  ? <span className={styles.previewEmpty}>{t('editor.previewEmpty')}</span>
                  : <button type="button" className={styles.previewEmpty} onClick={editSource}>{t('editor.previewEmpty')}</button>}
            </div>
          )}
          {readOnly ? null : (
            <span className={styles.bodyToggle}>
              <IconButton
                size={28}
                label={bodyMode === 'preview' ? t('editor.viewSource') : t('editor.viewPreview')}
                tooltipLabel={bodyMode === 'preview' ? t('editor.viewSource') : t('editor.viewPreview')}
                tooltipPlacement="bottom"
                icon={bodyMode === 'preview' ? <Code2 size={16} /> : <Eye size={16} />}
                onClick={() => (bodyMode === 'preview' ? editSource() : setBodyMode('preview'))}
              />
            </span>
          )}
        </div>

      </div>
    </CatalogPage>
  )
}
