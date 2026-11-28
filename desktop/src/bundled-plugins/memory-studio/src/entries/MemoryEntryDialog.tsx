import {
  Button,
  IconButton,
  Markdown,
  Menu,
  ModalDialog,
  ModalFooter,
  ModalHeader,
  Textarea,
  type DesktopPluginHost,
  type DesktopPluginSurfaceComponent,
  type MenuEntry
} from '@dotcraft/plugin'
import { ChevronDown, Code2, Database, Eye, Tag, Trash2 } from 'lucide-react'
import { useEffect, useId, useMemo, useRef, useState, type JSX, type KeyboardEvent as ReactKeyboardEvent } from 'react'
import { MemoryIcon } from '../MemoryIcon'
import { deleteEntry, readSnapshot, saveEntry, type EntryConflictReason, type EntrySnapshot } from '../data/entrySave'
import { isWithin } from '../data/hostPath'
import { isValidMemoryName, memoryName, parseMemoryFile, type MemoryFileFields } from '../data/memoryFile'
import { errorMessage, isUnsupported } from '../data/rpc'
import { listStores, PERSONAL_STORE_ID, type MemoryStore } from '../data/stores'
import { useMemoryFs, usePolicyState, useWorkspaceName, useWorkspacePath } from '../hooks/useMemoryData'
import { useT } from '../i18n'
import { Notice } from '../shared/ui'
import { closeMemoryDialog, notifyMemoryChanged, useMemoryDialogRequest, type MemoryDialogRequest } from '../state/memoryDialog'
import { useStoreLabel } from './EntriesTab'
import styles from './MemoryEntryDialog.module.css'

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

function defaultWritableStore(stores: readonly MemoryStore[], requestedKey?: string): MemoryStore | null {
  const writable = stores.filter((candidate) => candidate.writable)
  if (requestedKey) {
    const requested = writable.find((candidate) => candidate.key === requestedKey)
    if (requested) return requested
  }
  return writable.find((candidate) => candidate.kind === 'workspace') ?? writable[0] ?? null
}

function resolveExistingStore(
  stores: readonly MemoryStore[],
  request: Extract<MemoryDialogRequest, { mode: 'existing' }>
): MemoryStore | null {
  if (request.storeKey) {
    const exact = stores.find((store) => store.key === request.storeKey)
    if (exact) return exact
  }
  if (request.storeId === PERSONAL_STORE_ID) return stores.find((store) => store.kind === 'personal') ?? null
  if (request.storeId) return stores.find((store) => store.kind === 'attached' && store.id === request.storeId) ?? null
  if (request.path) {
    const matched = stores
      .filter((store) => store.kind !== 'attached' && store.kind !== 'personal' && isWithin(request.path!, store.path, request.workspacePath ?? null))
      .sort((a, b) => b.path.length - a.path.length)[0]
    if (matched) return matched
  }
  return stores.find((store) => store.kind === 'workspace') ?? stores[0] ?? null
}

export const MemoryEntryOverlay: DesktopPluginSurfaceComponent<'app.overlay'> = ({ host }) => {
  const request = useMemoryDialogRequest()
  if (!request) return null
  return <MemoryEntryDialog key={request.id} host={host} request={request} onClose={closeMemoryDialog} />
}

export function MemoryEntryDialog({ host, request, onClose }: {
  host: DesktopPluginHost
  request: MemoryDialogRequest
  onClose(): void
}): JSX.Element {
  const t = useT()
  const fs = useMemoryFs(host)
  const workspacePath = useWorkspacePath(host)
  const label = useStoreLabel(useWorkspaceName(host))
  const policy = usePolicyState(host)
  const types = policy.value?.effective.types ?? []

  const isNew = request.mode === 'new'
  const titleId = useId()
  const nameId = useId()
  const bodyRef = useRef<HTMLDivElement>(null)
  const focusSource = useRef(false)

  const [stores, setStores] = useState<MemoryStore[] | null>(null)
  const [storeKey, setStoreKey] = useState<string>(request.storeKey ?? '')
  const [snapshot, setSnapshot] = useState<EntrySnapshot | null>(null)
  const [fields, setFields] = useState<MemoryFileFields>(() => ({
    ...EMPTY_FIELDS,
    name: isNew ? '' : memoryName(request.fileName),
    body: isNew ? request.body : ''
  }))
  const [loaded, setLoaded] = useState<MemoryFileFields | null>(null)
  const [bodyMode, setBodyMode] = useState<BodyMode>(isNew ? 'edit' : 'preview')
  const [conflict, setConflict] = useState<Conflict | null>(null)
  const [busy, setBusy] = useState(false)
  const [loadError, setLoadError] = useState<string | null>(null)
  const [unsupported, setUnsupported] = useState(false)

  const writableStores = useMemo(
    () => (stores ?? []).filter((candidate) => candidate.writable),
    [stores]
  )
  const store = useMemo(() => {
    if (!stores) return null
    if (request.mode === 'new') {
      return writableStores.find((candidate) => candidate.key === storeKey) ?? defaultWritableStore(writableStores, request.storeKey)
    }
    return resolveExistingStore(stores, request)
  }, [stores, writableStores, storeKey, request])

  const readOnly = !isNew && store != null && !store.writable
  const invalidHeader = !isNew && snapshot?.fileText != null && parseMemoryFile(snapshot.fileText).invalidHeader

  useEffect(() => {
    if (isNew) document.getElementById(nameId)?.focus()
  }, [isNew, nameId])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !event.defaultPrevented && !busy) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [busy, onClose])

  useEffect(() => {
    let cancelled = false
    setStores(null)
    setLoadError(null)
    setUnsupported(false)
    listStores(host.appServer).then(
      (listed) => {
        if (cancelled) return
        setStores(listed)
        if (request.mode === 'new') {
          const initial = defaultWritableStore(listed, request.storeKey)
          if (initial) setStoreKey(initial.key)
        }
      },
      (error: unknown) => {
        if (cancelled) return
        if (isUnsupported(error)) setUnsupported(true)
        else setLoadError(errorMessage(error))
      }
    )
    return () => { cancelled = true }
  }, [host.appServer, workspacePath, request])

  useEffect(() => {
    if (fields.type !== '' || types.length === 0) return
    if (!isNew && loaded == null) return
    setFields((previous) => (previous.type === '' ? { ...previous, type: types[0].name } : previous))
  }, [fields.type, types, isNew, loaded])

  useEffect(() => {
    if (!store) {
      setSnapshot(null)
      return
    }
    let cancelled = false
    setSnapshot(null)
    setConflict(null)
    const fileName = request.mode === 'existing' ? request.fileName : null
    readSnapshot(fs, store.path, fileName).then(
      (next) => {
        if (cancelled) return
        setSnapshot(next)
        if (request.mode === 'existing') {
          const read = fieldsFrom(next)
          const initial = { ...read, type: read.type || types[0]?.name || '' }
          setFields(initial)
          setLoaded(initial)
        }
      },
      (error: unknown) => { if (!cancelled) setLoadError(errorMessage(error)) }
    )
    return () => { cancelled = true }
  }, [fs, store?.path, request])

  useEffect(() => {
    if (!focusSource.current || bodyMode !== 'edit') return
    focusSource.current = false
    bodyRef.current?.querySelector('textarea')?.focus()
  }, [bodyMode])

  // Keeps an undeclared type selectable so opening an entry never changes its type.
  const typeNames = useMemo(() => {
    const names = types.map((type) => type.name)
    if (fields.type && !names.includes(fields.type)) names.push(fields.type)
    return names
  }, [types, fields.type])

  const typeDescriptions = useMemo(
    () => new Map(types.map((type) => [type.name, type.description])),
    [types]
  )

  const set = <K extends keyof MemoryFileFields>(key: K, value: MemoryFileFields[K]): void => {
    setFields((previous) => ({ ...previous, [key]: value }))
    if (key === 'name' && conflict?.reason === 'nameTaken') setConflict(null)
  }

  const typeMenuItems = useMemo<MenuEntry[]>(
    () => typeNames.map((name) => ({
      label: name,
      checked: name === fields.type,
      disabled: busy || unsupported || readOnly,
      onSelect: () => set('type', name)
    })),
    [typeNames, fields.type, busy, unsupported, readOnly]
  )

  const storeMenuItems = useMemo<MenuEntry[]>(
    () => writableStores.map((candidate) => ({
      label: label(candidate),
      checked: candidate.key === store?.key,
      disabled: busy || unsupported,
      onSelect: () => {
        setStoreKey(candidate.key)
        setConflict(null)
      }
    })),
    [writableStores, label, store?.key, busy, unsupported]
  )

  const nameValid = isValidMemoryName(fields.name.trim())
  const nameInvalid = fields.name !== '' && !nameValid
  const descriptionValid = fields.description.trim() !== '' && !/[\r\n]/.test(fields.description)
  const dirty = isNew || (loaded != null && (
    fields.name !== loaded.name || fields.description !== loaded.description || fields.type !== loaded.type || fields.body !== loaded.body
  ))
  const canSave = !unsupported && !readOnly && !invalidHeader && store != null && snapshot != null && nameValid && descriptionValid && fields.type !== '' && !busy && dirty

  const save = async (base: EntrySnapshot): Promise<void> => {
    setBusy(true)
    try {
      const outcome = await saveEntry(fs, base, fields)
      if (outcome.kind === 'conflict') {
        setConflict({ reason: outcome.reason, current: outcome.current })
        return
      }
      notifyMemoryChanged()
      host.ui.showToast({ tone: 'success', message: t('editor.saved') })
      onClose()
    } catch (error) {
      host.ui.showToast({ tone: 'error', message: t('editor.saveFailed'), description: errorMessage(error) })
    } finally {
      setBusy(false)
    }
  }

  const remove = async (): Promise<void> => {
    if (!snapshot || isNew) return
    const confirmed = await host.ui.confirm({
      title: t('editor.deleteTitle', { name: fields.name || memoryName(request.fileName) }),
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
      notifyMemoryChanged()
      onClose()
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

  const editSource = (): void => {
    if (readOnly || busy || unsupported) return
    focusSource.current = true
    setBodyMode('edit')
  }

  const handleTextareaKeyDown = (event: ReactKeyboardEvent<HTMLTextAreaElement>): void => {
    if ((event.metaKey || event.ctrlKey) && event.key === 'Enter' && canSave && snapshot) {
      event.preventDefault()
      void save(snapshot)
      return
    }
    if (event.key === 'Escape' && !isNew) {
      event.preventDefault()
      event.stopPropagation()
      setBodyMode('preview')
    }
  }

  const conflictText = conflict?.reason === 'indexChanged' ? conflict.current.indexText : conflict?.current.fileText
  const deletedMeanwhile = conflict?.reason === 'fileChanged' && conflict.current.fileText == null
  const conflictMessage = conflict
    ? t(deletedMeanwhile ? 'editor.conflict.deleted' : `editor.conflict.${conflict.reason}` as const)
    : null

  return (
    <ModalDialog
      titleId={titleId}
      width={500}
      onDismiss={busy ? undefined : onClose}
      onSubmit={() => { if (canSave && snapshot) void save(snapshot) }}
      bodyClassName={styles.body}
      header={(
        <ModalHeader
          icon={<MemoryIcon size={18} />}
          title={isNew ? t('editor.newTitle') : t('view.title')}
          titleId={titleId}
          onClose={busy ? undefined : onClose}
          closeLabel={t('editor.cancel')}
        />
      )}
      error={loadError}
      footer={(
        <ModalFooter
          leading={!isNew && !readOnly && store ? (
            <button
              type="button"
              className={styles.deleteButton}
              disabled={busy || snapshot == null}
              onClick={() => void remove()}
            >
              <Trash2 size={14} aria-hidden />
              <span>{t('editor.delete')}</span>
            </button>
          ) : undefined}
        >
          <Button variant="secondary" disabled={busy} onClick={onClose}>
            {t('editor.cancel')}
          </Button>
          {!readOnly ? (
            <Button type="submit" variant="primary" loading={busy} disabled={!canSave}>
              {t('editor.save')}
            </Button>
          ) : null}
        </ModalFooter>
      )}
    >
      {unsupported ? <Notice>{t('unsupported.entries')}</Notice> : null}
      {readOnly && store ? <Notice>{t('editor.readOnly', { name: label(store) })}</Notice> : null}
      {invalidHeader ? <Notice tone="error">{t('editor.invalidHeader')}</Notice> : null}

      {conflict && conflictMessage ? (
        <Notice tone="warning">
          <div>{conflictMessage}</div>
          {conflictText != null ? <pre className={styles.current}>{conflictText}</pre> : null}
          {conflict.reason === 'nameTaken' ? null : (
            <div className={styles.conflictActions}>
              {!isNew && !deletedMeanwhile ? (
                <Button variant="secondary" size="sm" onClick={useCurrent}>
                  {t('editor.useCurrent')}
                </Button>
              ) : null}
              <Button variant="secondary" size="sm" disabled={!canSave} onClick={() => void save(conflict.current)}>
                {t('editor.saveMine')}
              </Button>
            </div>
          )}
        </Notice>
      ) : null}

      <div className={styles.pills}>
        {readOnly ? (
          <span className={`${styles.pill} ${styles.pillTinted}`}>
            <Tag size={13} aria-hidden />
            <span>{fields.type || t('entries.untyped')}</span>
          </span>
        ) : (
          <Menu items={typeMenuItems}>
            <button
              type="button"
              className={`${styles.pill} ${styles.pillTinted}`}
              aria-label={t('editor.type')}
              title={typeDescriptions.get(fields.type) ?? t('entries.undeclaredType')}
              disabled={busy || unsupported || typeMenuItems.length === 0}
            >
              <Tag size={13} aria-hidden />
              <span>{fields.type || t('entries.untyped')}</span>
              <ChevronDown size={13} className={styles.pillChevron} aria-hidden />
            </button>
          </Menu>
        )}

        {isNew && writableStores.length > 1 && store ? (
          <Menu items={storeMenuItems}>
            <button
              type="button"
              className={styles.pill}
              aria-label={t('editor.store')}
              disabled={busy || unsupported}
            >
              <Database size={13} aria-hidden />
              <span className={styles.pillText}>{label(store)}</span>
              <ChevronDown size={13} className={styles.pillChevron} aria-hidden />
            </button>
          </Menu>
        ) : !isNew && store ? (
          <span className={styles.pill}>
            <Database size={13} aria-hidden />
            <span className={styles.pillText}>{label(store)}</span>
          </span>
        ) : null}
      </div>

      <div className={styles.head}>
        <input
          id={nameId}
          type="text"
          autoFocus={isNew}
          readOnly={readOnly}
          className={styles.titleInput}
          value={fields.name}
          aria-label={t('editor.name')}
          aria-invalid={nameInvalid || undefined}
          disabled={busy || unsupported}
          placeholder={t('editor.namePlaceholder')}
          spellCheck={false}
          onChange={(event) => set('name', event.target.value)}
        />
        {nameInvalid ? <span className={styles.fieldError}>{t('editor.nameHint')}</span> : null}
        {readOnly && !fields.description ? null : (
          <input
            type="text"
            readOnly={readOnly}
            className={styles.descriptionInput}
            value={fields.description}
            aria-label={t('editor.description')}
            disabled={busy || unsupported}
            placeholder={readOnly ? undefined : t('editor.descriptionPlaceholder')}
            spellCheck={false}
            onChange={(event) => set('description', event.target.value)}
          />
        )}
      </div>

      <div ref={bodyRef} className={styles.bodySurface} data-mode={bodyMode === 'edit' && !readOnly ? 'source' : 'preview'}>
        {bodyMode === 'edit' && !readOnly ? (
          <Textarea
            frameless
            className={isNew ? styles.textarea : `${styles.textarea} ${styles.textareaWithToggle}`}
            value={fields.body}
            aria-label={t('editor.body')}
            placeholder={t('editor.bodyPlaceholder')}
            rows={5}
            disabled={busy || unsupported}
            onChange={(event) => set('body', event.target.value)}
            onKeyDown={handleTextareaKeyDown}
          />
        ) : (
          <div className={styles.previewBox} onDoubleClick={editSource}>
            {fields.body.trim()
              ? <Markdown content={fields.body} />
              : readOnly
                ? <span className={styles.previewEmpty}>{t('editor.previewEmpty')}</span>
                : <button type="button" className={styles.previewEmpty} onClick={editSource}>{t('editor.previewEmpty')}</button>}
          </div>
        )}
        {!isNew && !readOnly ? (
          <span className={styles.bodyToggle}>
            <IconButton
              size={28}
              label={bodyMode === 'preview' ? t('editor.viewSource') : t('editor.viewPreview')}
              tooltipLabel={bodyMode === 'preview' ? t('editor.viewSource') : t('editor.viewPreview')}
              tooltipPlacement="bottom"
              icon={bodyMode === 'preview' ? <Code2 size={15} /> : <Eye size={15} />}
              onClick={() => (bodyMode === 'preview' ? editSource() : setBodyMode('preview'))}
            />
          </span>
        ) : null}
      </div>
    </ModalDialog>
  )
}
