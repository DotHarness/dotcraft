import { useCallback, useEffect, useRef, useState, type JSX } from 'react'
import { Folder, FolderOpen, FolderPlus, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { IconButton } from '../ui/IconButton'
import styles from './ProjectDialog.module.css'

export interface FolderEntry {
  id: string
  path: string
}

export interface ProjectDialogResult {
  /** Effective display name (falls back to the primary folder name when blank). */
  name: string
  /** Ordered folders; index 0 is the primary. Empty means "start from scratch". */
  folders: FolderEntry[]
}

interface ProjectDialogProps {
  mode: 'create' | 'edit'
  initialName: string
  initialFolders: FolderEntry[]
  busy: boolean
  removeProjectDisabled?: boolean
  onSubmit: (result: ProjectDialogResult) => void
  onRemoveProject?: () => void
  onClose: () => void
}

let uidCounter = 0
function nextFolderId(): string {
  uidCounter += 1
  return `folder-${uidCounter}`
}

function basename(p: string): string {
  const trimmed = p.replace(/[\\/]+$/, '')
  const parts = trimmed.split(/[\\/]/)
  return parts[parts.length - 1] || p
}

function samePath(a: string, b: string): boolean {
  return a.replace(/[\\/]+$/, '').toLowerCase() === b.replace(/[\\/]+$/, '').toLowerCase()
}

export function ProjectDialog({
  mode,
  initialName,
  initialFolders,
  busy,
  removeProjectDisabled = false,
  onSubmit,
  onRemoveProject,
  onClose
}: ProjectDialogProps): JSX.Element {
  const t = useT()
  const [name, setName] = useState(initialName)
  const [folders, setFolders] = useState<FolderEntry[]>(initialFolders)
  const [picking, setPicking] = useState(false)
  const nameRef = useRef<HTMLInputElement>(null)

  const isEdit = mode === 'edit'
  const primaryName = folders[0] ? basename(folders[0].path) : ''
  const canSubmit = isEdit ? folders.length > 0 : name.trim().length > 0 || folders.length > 0

  useEffect(() => {
    nameRef.current?.focus()
  }, [])

  const close = useCallback((): void => {
    if (busy) return
    onClose()
  }, [busy, onClose])

  const submit = useCallback((): void => {
    if (!canSubmit || busy) return
    onSubmit({ name: name.trim() || primaryName, folders })
  }, [busy, canSubmit, folders, name, onSubmit, primaryName])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') close()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [close])

  const addFolder = useCallback(async (): Promise<void> => {
    if (picking) return
    setPicking(true)
    try {
      const path = await window.api.workspace.pickFolder()
      if (!path) return
      setFolders((prev) => (prev.some((f) => samePath(f.path, path)) ? prev : [...prev, { id: nextFolderId(), path }]))
    } finally {
      setPicking(false)
    }
  }, [picking])

  function makePrimary(id: string): void {
    setFolders((prev) => {
      const target = prev.find((f) => f.id === id)
      if (!target) return prev
      return [target, ...prev.filter((f) => f.id !== id)]
    })
  }

  function removeFolder(id: string): void {
    setFolders((prev) => prev.filter((f) => f.id !== id))
  }

  const canRemoveFolder = !isEdit || folders.length > 1
  const multiFolder = folders.length > 1

  return (
    <ModalDialog
      ariaLabel={isEdit ? t('addProject.editTitle') : t('addProject.createTitle')}
      width={460}
      onDismiss={close}
      bodyClassName={styles.body}
      header={(
        <ModalHeader
          icon={<Folder size={18} aria-hidden />}
          title={isEdit ? t('addProject.editTitle') : t('addProject.createTitle')}
          description={isEdit ? t('addProject.editDescription') : t('addProject.createDescription')}
          onClose={close}
          closeLabel={t('addProject.close')}
        />
      )}
      footer={(
        <ModalFooter
          leading={isEdit ? (
            <Button variant="danger" disabled={busy || removeProjectDisabled} onClick={onRemoveProject}>
              {t('addProject.removeProject')}
            </Button>
          ) : undefined}
        >
          <Button variant="secondary" disabled={busy} onClick={close}>
            {t('addProject.cancel')}
          </Button>
          <Button variant="primary" loading={busy} disabled={!canSubmit} onClick={submit}>
            {isEdit ? t('addProject.save') : t('addProject.create')}
          </Button>
        </ModalFooter>
      )}
    >
      <div className={styles.field}>
        <label className={styles.label} htmlFor="project-name">
          {t('addProject.nameLabel')}
        </label>
        <Input
          id="project-name"
          ref={nameRef}
          value={name}
          disabled={busy}
          onChange={(event) => setName(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && canSubmit) {
              event.preventDefault()
              submit()
            }
          }}
          placeholder={primaryName || t('addProject.defaultName')}
        />
        {!name.trim() && primaryName && (
          <p className={styles.hint}>{t('addProject.nameDefaultHint', { name: primaryName })}</p>
        )}
      </div>

      <div className={styles.field}>
        <div className={styles.label}>{t('addProject.sourceFolders')}</div>
        {folders.length === 0 ? (
          <button type="button" className={styles.emptyDrop} disabled={busy || picking} onClick={() => void addFolder()}>
            <FolderPlus size={18} strokeWidth={1.7} aria-hidden />
            <span>{t('addProject.emptyAdd')}</span>
          </button>
        ) : (
          <div className={styles.folders}>
            {folders.map((folder, index) => (
              <FolderRow
                key={folder.id}
                folder={folder}
                isPrimary={index === 0}
                multiFolder={multiFolder}
                canRemove={canRemoveFolder}
                makePrimaryLabel={t('addProject.makePrimary')}
                primaryLabel={t('addProject.primary')}
                removeLabel={t('addProject.removeFolder')}
                onMakePrimary={() => makePrimary(folder.id)}
                onRemove={() => removeFolder(folder.id)}
              />
            ))}
            <button type="button" className={styles.addFolder} disabled={busy || picking} onClick={() => void addFolder()}>
              <FolderPlus size={15} strokeWidth={1.8} aria-hidden className={styles.addFolderIcon} />
              <span>{t('addProject.addFolder')}</span>
            </button>
          </div>
        )}
      </div>
    </ModalDialog>
  )
}

function FolderRow({
  folder,
  isPrimary,
  multiFolder,
  canRemove,
  makePrimaryLabel,
  primaryLabel,
  removeLabel,
  onMakePrimary,
  onRemove
}: {
  folder: FolderEntry
  isPrimary: boolean
  multiFolder: boolean
  canRemove: boolean
  makePrimaryLabel: string
  primaryLabel: string
  removeLabel: string
  onMakePrimary: () => void
  onRemove: () => void
}): JSX.Element {
  return (
    <div className={styles.folder}>
      <span className={styles.folderIcon}>
        {isPrimary ? <FolderOpen size={16} strokeWidth={1.7} aria-hidden /> : <Folder size={16} strokeWidth={1.7} aria-hidden />}
      </span>
      <span className={styles.folderText}>
        <span className={styles.folderName}>{basename(folder.path)}</span>
        <span className={styles.folderPath} title={folder.path}>
          {folder.path}
        </span>
      </span>
      <span className={styles.folderActions}>
        {isPrimary && multiFolder && <span className={styles.primaryBadge}>{primaryLabel}</span>}
        {!isPrimary && (
          <span className={styles.reveal}>
            <Button variant="ghost" size="sm" onClick={onMakePrimary}>
              {makePrimaryLabel}
            </Button>
          </span>
        )}
        {canRemove && (
          <span className={styles.reveal}>
            <IconButton
              icon={<X size={14} aria-hidden />}
              label={removeLabel}
              tooltipLabel={removeLabel}
              size={24}
              radius={6}
              onClick={onRemove}
            />
          </span>
        )}
      </span>
    </div>
  )
}
