import { useCallback, useEffect, useRef, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { Folder, FolderOpen, FolderPlus, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { LayerBoundary } from '../../contexts/LayerContext'
import { ModalHeader } from '../ui/ModalHeader'
import { Button } from '../ui/Button'
import { Input } from '../ui/Input'
import { IconButton } from '../ui/IconButton'

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
  /** Edit only: disables the Remove project action (e.g. while the project is the active workspace). */
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

/**
 * Shared Create / Edit dialog for a local Project's multi-folder source list.
 *
 * `folders[0]` is always the primary (default folder for new chats and project
 * discovery); "Make primary" reorders a folder to the front so presentation order
 * stays stable. Create allows either
 * a from-scratch name (no folders) or attached existing folders; a blank name
 * defaults to the primary folder's name.
 *
 * This is presentation + local folder state only: the caller (useAddProjectFlow)
 * owns the create/save/remove IPC via the callbacks.
 */
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

  // A project keeps at least one folder in edit mode; in create mode a folder may
  // be removed back to the from-scratch empty state.
  const canRemoveFolder = !isEdit || folders.length > 1
  const multiFolder = folders.length > 1

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={isEdit ? t('addProject.editTitle') : t('addProject.createTitle')}
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) close()
      }}
    >
      <div style={panelStyle} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<Folder size={18} aria-hidden />}
          title={isEdit ? t('addProject.editTitle') : t('addProject.createTitle')}
          description={isEdit ? t('addProject.editDescription') : t('addProject.createDescription')}
          onClose={close}
          closeLabel={t('addProject.close')}
        />

        <label style={fieldLabelStyle} htmlFor="project-name">
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
          <p style={fieldHintStyle}>{t('addProject.nameDefaultHint', { name: primaryName })}</p>
        )}

        <div style={sectionLabelStyle}>{t('addProject.sourceFolders')}</div>

        {folders.length === 0 ? (
          <EmptyFolderDrop label={t('addProject.emptyAdd')} disabled={busy || picking} onClick={() => void addFolder()} />
        ) : (
          <div style={folderGroupStyle}>
            {folders.map((folder, index) => (
              <FolderRow
                key={folder.id}
                folder={folder}
                isPrimary={index === 0}
                first={index === 0}
                multiFolder={multiFolder}
                canRemove={canRemoveFolder}
                makePrimaryLabel={t('addProject.makePrimary')}
                primaryLabel={t('addProject.primary')}
                removeLabel={t('addProject.removeFolder')}
                onMakePrimary={() => makePrimary(folder.id)}
                onRemove={() => removeFolder(folder.id)}
              />
            ))}
            <AddFolderRow label={t('addProject.addFolder')} disabled={busy || picking} onClick={() => void addFolder()} />
          </div>
        )}

        <div style={{ ...footerStyle, justifyContent: isEdit ? 'space-between' : 'flex-end' }}>
          {isEdit && (
            <Button variant="danger" disabled={busy || removeProjectDisabled} onClick={onRemoveProject}>
              {t('addProject.removeProject')}
            </Button>
          )}
          <div style={{ display: 'inline-flex', gap: '8px' }}>
            <Button variant="secondary" disabled={busy} onClick={close}>
              {t('addProject.cancel')}
            </Button>
            <Button variant="primary" loading={busy} disabled={!canSubmit} onClick={submit}>
              {isEdit ? t('addProject.save') : t('addProject.create')}
            </Button>
          </div>
        </div>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body)
}

function FolderRow({
  folder,
  isPrimary,
  first,
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
  first: boolean
  /** "primary" is only meaningful once a project has more than one folder. */
  multiFolder: boolean
  canRemove: boolean
  makePrimaryLabel: string
  primaryLabel: string
  removeLabel: string
  onMakePrimary: () => void
  onRemove: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const revealed = hovered || focused
  const name = basename(folder.path)

  return (
    <div
      style={{ ...folderRowStyle, borderTop: first ? 'none' : '1px solid var(--border-subtle)' }}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        if (!event.currentTarget.contains(event.relatedTarget as Node | null)) setFocused(false)
      }}
    >
      <span style={folderIconStyle}>
        {isPrimary ? <FolderOpen size={16} strokeWidth={1.7} aria-hidden /> : <Folder size={16} strokeWidth={1.7} aria-hidden />}
      </span>
      <span style={folderTextStyle}>
        <span style={folderNameStyle}>{name}</span>
        <span style={folderPathStyle} title={folder.path}>
          {folder.path}
        </span>
      </span>
      <span style={folderActionsStyle}>
        {isPrimary && multiFolder && <span style={primaryBadgeStyle}>{primaryLabel}</span>}
        {!isPrimary && (
          <span style={{ opacity: revealed ? 1 : 0, transition: 'opacity 120ms ease', display: 'inline-flex' }}>
            <Button variant="ghost" size="sm" onClick={onMakePrimary}>
              {makePrimaryLabel}
            </Button>
          </span>
        )}
        {canRemove && (
          <span style={{ opacity: revealed ? 1 : 0, transition: 'opacity 120ms ease', display: 'inline-flex' }}>
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

function AddFolderRow({ label, disabled, onClick }: { label: string; disabled: boolean; onClick: () => void }): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        ...addFolderRowStyle,
        background: hovered && !disabled ? 'var(--bg-tertiary)' : 'transparent',
        color: hovered && !disabled ? 'var(--text-primary)' : 'var(--text-secondary)'
      }}
    >
      <FolderPlus size={15} strokeWidth={1.8} aria-hidden style={{ color: 'var(--text-dimmed)', flexShrink: 0 }} />
      <span>{label}</span>
    </button>
  )
}

function EmptyFolderDrop({ label, disabled, onClick }: { label: string; disabled: boolean; onClick: () => void }): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const active = hovered && !disabled
  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        ...emptyDropStyle,
        borderColor: active ? 'var(--border-active)' : 'var(--border-default)',
        background: active ? 'var(--bg-tertiary)' : 'transparent',
        color: active ? 'var(--text-secondary)' : 'var(--text-dimmed)'
      }}
    >
      <FolderPlus size={18} strokeWidth={1.7} aria-hidden />
      <span>{label}</span>
    </button>
  )
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--overlay-scrim)'
}

const panelStyle: CSSProperties = {
  width: '460px',
  maxWidth: 'calc(100vw - 48px)',
  maxHeight: 'calc(100vh - 96px)',
  overflow: 'auto',
  padding: '20px 22px',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  boxShadow: 'var(--shadow-level-3)'
}

const fieldLabelStyle: CSSProperties = {
  display: 'block',
  margin: '0 0 6px',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  color: 'var(--text-secondary)'
}

const fieldHintStyle: CSSProperties = {
  margin: '6px 2px 0',
  fontSize: 'var(--type-hint-size)',
  lineHeight: 'var(--type-hint-line-height)',
  color: 'var(--text-dimmed)'
}

const sectionLabelStyle: CSSProperties = {
  margin: '18px 0 6px',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  color: 'var(--text-secondary)'
}

const folderGroupStyle: CSSProperties = {
  borderRadius: '10px',
  background: 'var(--bg-primary)',
  border: '1px solid var(--border-default)',
  overflow: 'hidden'
}

const folderRowStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '24px minmax(0, 1fr) auto',
  alignItems: 'center',
  gap: '10px',
  padding: '9px 10px 9px 12px',
  minHeight: '48px'
}

const folderIconStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: 'var(--text-dimmed)',
  flexShrink: 0
}

const folderTextStyle: CSSProperties = {
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: '1px'
}

const folderNameStyle: CSSProperties = {
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const folderPathStyle: CSSProperties = {
  fontSize: 'var(--type-hint-size)',
  lineHeight: 'var(--type-hint-line-height)',
  color: 'var(--text-dimmed)',
  fontFamily: 'var(--font-mono)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

const folderActionsStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '4px',
  flexShrink: 0
}

const primaryBadgeStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  height: '20px',
  padding: '0 8px',
  borderRadius: '999px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-hint-size)',
  lineHeight: 1,
  whiteSpace: 'nowrap'
}

const addFolderRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  width: '100%',
  padding: '11px 12px',
  border: 'none',
  borderTop: '1px solid var(--border-subtle)',
  font: 'inherit',
  fontSize: 'var(--type-ui-size)',
  textAlign: 'left',
  cursor: 'pointer',
  transition: 'background 120ms ease, color 120ms ease'
}

const emptyDropStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  alignItems: 'center',
  justifyContent: 'center',
  gap: '8px',
  width: '100%',
  minHeight: '108px',
  padding: '20px',
  border: '1px dashed var(--border-default)',
  borderRadius: '10px',
  font: 'inherit',
  fontSize: 'var(--type-secondary-size)',
  cursor: 'pointer',
  transition: 'border-color 120ms ease, background 120ms ease, color 120ms ease'
}

const footerStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  marginTop: '22px'
}
