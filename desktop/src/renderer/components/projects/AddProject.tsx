import { useCallback, useEffect, useRef, useState, type CSSProperties, type JSX, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { FolderOpen, FolderPlus } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'

/**
 * Shared "Add new project" flow used by both the composer project selector and
 * the sidebar projects rail, so the two surfaces stay consistent:
 *  - "Start from scratch": names a project, creates `<Documents>/<name>` as a git
 *    repository, then switches to it (which runs the setup wizard).
 *  - "Use an existing folder": opens the native folder picker and switches to it.
 */
export interface AddProjectFlow {
  /** Opens the "Name project" dialog for the from-scratch path. */
  beginScratch(): void
  /** Opens the native folder picker and switches to the chosen folder. */
  chooseExistingFolder(): Promise<void>
  /** The "Name project" dialog element (rendered via portal) or null when closed. */
  dialog: JSX.Element | null
  /** True while a project is being created. */
  busy: boolean
}

export function useAddProjectFlow(): AddProjectFlow {
  const t = useT()
  const [nameOpen, setNameOpen] = useState(false)
  const [name, setName] = useState('')
  const [busy, setBusy] = useState(false)

  const beginScratch = useCallback((): void => {
    setName(t('addProject.defaultName'))
    setNameOpen(true)
  }, [t])

  const chooseExistingFolder = useCallback(async (): Promise<void> => {
    try {
      const path = await window.api.workspace.pickFolder()
      if (!path) return
      await window.api.workspace.switch(path)
    } catch (err) {
      addToast(t('addProject.openFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    }
  }, [t])

  const closeDialog = useCallback((): void => {
    if (busy) return
    setNameOpen(false)
    setName('')
  }, [busy])

  const confirmScratch = useCallback(async (): Promise<void> => {
    const trimmed = name.trim()
    if (!trimmed || busy) return
    setBusy(true)
    try {
      const { path, gitInitialized } = await window.api.workspace.createLocalProject({ name: trimmed })
      setNameOpen(false)
      setName('')
      if (!gitInitialized) addToast(t('addProject.gitUnavailable'), 'warning')
      await window.api.workspace.switch(path)
    } catch (err) {
      addToast(t('addProject.createFailed', { error: err instanceof Error ? err.message : String(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }, [busy, name, t])

  const dialog = nameOpen ? (
    <NameProjectDialog
      value={name}
      busy={busy}
      onChange={setName}
      onCancel={closeDialog}
      onConfirm={() => { void confirmScratch() }}
    />
  ) : null

  return { beginScratch, chooseExistingFolder, dialog, busy }
}

/**
 * The two "Add new project" choices, rendered with a shared menu-row treatment so
 * the composer flyout and the sidebar dropdown look identical.
 */
export function AddProjectMenuOptions({
  onStartFromScratch,
  onUseExistingFolder,
  disabled
}: {
  onStartFromScratch: () => void
  onUseExistingFolder: () => void
  disabled?: boolean
}): JSX.Element {
  const t = useT()
  return (
    <>
      <AddProjectOptionButton
        icon={<FolderPlus size={14} strokeWidth={1.8} aria-hidden />}
        label={t('addProject.fromScratch')}
        disabled={disabled}
        onClick={onStartFromScratch}
      />
      <AddProjectOptionButton
        icon={<FolderOpen size={14} strokeWidth={1.8} aria-hidden />}
        label={t('addProject.useExisting')}
        disabled={disabled}
        onClick={onUseExistingFolder}
      />
    </>
  )
}

function AddProjectOptionButton({
  icon,
  label,
  disabled,
  onClick
}: {
  icon: ReactNode
  label: string
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      type="button"
      role="menuitem"
      disabled={disabled}
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        width: '100%',
        minHeight: '32px',
        border: 'none',
        borderRadius: '6px',
        background: !disabled && hovered ? 'var(--bg-tertiary)' : 'transparent',
        color: disabled ? 'var(--text-tertiary)' : 'var(--text-primary)',
        display: 'flex',
        alignItems: 'center',
        gap: '8px',
        padding: '0 8px',
        font: 'inherit',
        textAlign: 'left',
        cursor: disabled ? 'default' : 'pointer',
        whiteSpace: 'nowrap'
      }}
    >
      {icon}
      <span style={{ overflow: 'hidden', textOverflow: 'ellipsis' }}>{label}</span>
    </button>
  )
}

function NameProjectDialog({
  value,
  busy,
  onChange,
  onCancel,
  onConfirm
}: {
  value: string
  busy: boolean
  onChange: (value: string) => void
  onCancel: () => void
  onConfirm: () => void
}): JSX.Element {
  const t = useT()
  const inputRef = useRef<HTMLInputElement>(null)
  const canConfirm = value.trim().length > 0 && !busy

  useEffect(() => {
    const input = inputRef.current
    if (!input) return
    input.focus()
    input.select()
  }, [])

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
      if (event.key === 'Enter' && canConfirm) onConfirm()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [canConfirm, onCancel, onConfirm])

  return createPortal(
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('addProject.nameTitle')}
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onCancel()
      }}
    >
      <div style={cardStyle} onMouseDown={(event) => event.stopPropagation()}>
        <h2 style={{ margin: 0, fontSize: '16px', fontWeight: 600, color: 'var(--text-primary)' }}>
          {t('addProject.nameTitle')}
        </h2>
        <p style={{ margin: '4px 0 16px', fontSize: '13px', color: 'var(--text-secondary)' }}>
          {t('addProject.nameSubtitle')}
        </p>
        <input
          ref={inputRef}
          value={value}
          disabled={busy}
          onChange={(event) => onChange(event.target.value)}
          placeholder={t('addProject.defaultName')}
          style={inputStyle}
        />
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '20px' }}>
          <button
            type="button"
            onClick={onCancel}
            disabled={busy}
            style={{ ...dialogButtonStyle, background: 'var(--bg-tertiary)', color: 'var(--text-primary)' }}
          >
            {t('addProject.cancel')}
          </button>
          <button
            type="button"
            onClick={onConfirm}
            disabled={!canConfirm}
            style={{
              ...dialogButtonStyle,
              background: 'var(--text-primary)',
              color: 'var(--bg-primary)',
              fontWeight: 600,
              opacity: canConfirm ? 1 : 0.55,
              cursor: canConfirm ? 'pointer' : 'default'
            }}
          >
            {t('addProject.save')}
          </button>
        </div>
      </div>
    </div>,
    document.body
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

const cardStyle: CSSProperties = {
  width: '420px',
  maxWidth: 'calc(100vw - 48px)',
  padding: '22px',
  borderRadius: '10px',
  background: 'var(--bg-secondary)',
  boxShadow: 'var(--shadow-level-3)'
}

const inputStyle: CSSProperties = {
  width: '100%',
  height: '42px',
  borderRadius: '8px',
  border: 'none',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)',
  padding: '0 12px',
  font: 'inherit',
  fontSize: '13px',
  outline: 'none',
  boxSizing: 'border-box'
}

const dialogButtonStyle: CSSProperties = {
  minWidth: '88px',
  height: '40px',
  border: 'none',
  borderRadius: '8px',
  padding: '0 16px',
  font: 'inherit',
  cursor: 'pointer'
}
