import { useEffect, useId, useMemo, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { FolderOpen, Store } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { usePluginStore, type MarketplaceEntry } from '../../stores/pluginStore'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { ModalHeader } from '../ui/ModalHeader'

interface AddMarketplaceDialogProps {
  /** Hides the local-folder picker, which cannot reach a remote workspace host. */
  allowLocalFolder: boolean
  onClose: () => void
  onAdded: (marketplace: MarketplaceEntry, alreadyAdded: boolean) => void
}

/**
 * Collects a marketplace source and adds it. Fetch failures stay inline rather than
 * becoming a transient notification, because the user has to correct the input.
 */
export function AddMarketplaceDialog({
  allowLocalFolder,
  onClose,
  onAdded
}: AddMarketplaceDialogProps): JSX.Element {
  const t = useT()
  const addMarketplace = usePluginStore((s) => s.addMarketplace)
  const titleId = useId()
  const sourceId = useId()
  const refId = useId()
  const sparseId = useId()
  const sourceRef = useRef<HTMLInputElement>(null)
  const [source, setSource] = useState('')
  const [ref, setRef] = useState('')
  const [sparsePaths, setSparsePaths] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Repository-only fields are disabled for a directory source so the request is not
  // rejected for a combination the user can see is impossible. The server stays authoritative.
  const localSource = useMemo(() => looksLikeLocalPath(source), [source])
  const canSubmit = source.trim() !== '' && !busy

  useEffect(() => {
    sourceRef.current?.focus()
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape' && !busy) onClose()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [busy, onClose])

  async function handlePickFolder(): Promise<void> {
    let path: string | null
    try {
      path = await window.api.workspace.pickFolder({ title: t('plugins.marketplace.add.pickTitle') })
    } catch {
      return
    }
    if (!path) return
    setSource(path)
    setError(null)
  }

  async function handleSubmit(): Promise<void> {
    if (!canSubmit) return
    setBusy(true)
    setError(null)
    try {
      const result = await addMarketplace({
        source: source.trim(),
        ref: localSource ? undefined : ref.trim() || undefined,
        sparsePaths: localSource ? undefined : parseSparsePaths(sparsePaths)
      })
      onAdded(result.marketplace, result.alreadyAdded)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy) onClose()
      }}
    >
      <div style={panelStyle} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<Store size={18} aria-hidden />}
          title={t('plugins.marketplace.add.title')}
          titleId={titleId}
          description={t('plugins.marketplace.add.description')}
          onClose={busy ? undefined : onClose}
          closeLabel={t('common.cancel')}
        />

        <form
          onSubmit={(event) => {
            event.preventDefault()
            void handleSubmit()
          }}
        >
          <div style={fieldStyle}>
            <label htmlFor={sourceId} style={labelStyle}>{t('plugins.marketplace.add.source')}</label>
            <div style={sourceRowStyle}>
              <input
                ref={sourceRef}
                id={sourceId}
                value={source}
                disabled={busy}
                placeholder={t('plugins.marketplace.add.sourcePlaceholder')}
                onChange={(event) => setSource(event.target.value)}
                style={inputStyle}
              />
              {allowLocalFolder && (
                <IconButton
                  label={t('plugins.marketplace.add.browse')}
                  tooltipLabel={t('plugins.marketplace.add.browse')}
                  disabled={busy}
                  icon={<FolderOpen size={15} aria-hidden />}
                  onClick={() => void handlePickFolder()}
                />
              )}
            </div>
          </div>

          <div style={fieldStyle}>
            <label htmlFor={refId} style={labelStyle}>{t('plugins.marketplace.add.ref')}</label>
            <input
              id={refId}
              value={ref}
              disabled={busy || localSource}
              placeholder={t('plugins.marketplace.add.refPlaceholder')}
              onChange={(event) => setRef(event.target.value)}
              style={inputStyle}
            />
          </div>

          <div style={fieldStyle}>
            <label htmlFor={sparseId} style={labelStyle}>{t('plugins.marketplace.add.sparsePaths')}</label>
            <textarea
              id={sparseId}
              value={sparsePaths}
              disabled={busy || localSource}
              rows={4}
              placeholder={t('plugins.marketplace.add.sparsePathsPlaceholder')}
              onChange={(event) => setSparsePaths(event.target.value)}
              style={textareaStyle}
            />
            <span style={hintStyle}>{t('plugins.marketplace.add.sparsePathsHint')}</span>
          </div>

          {error && (
            <p role="alert" style={errorStyle}>{error}</p>
          )}

          <div style={footerStyle}>
            <Button variant="secondary" disabled={busy} onClick={onClose}>
              {t('common.cancel')}
            </Button>
            <Button type="submit" variant="primary" loading={busy} disabled={!canSubmit}>
              {busy ? t('plugins.marketplace.add.submitting') : t('plugins.marketplace.add.submit')}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

/** Splits the textarea into one sparse path per line, dropping blanks. */
export function parseSparsePaths(value: string): string[] {
  return value
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line !== '')
}

/** Mirrors the server heuristic for recognizing a directory source. */
export function looksLikeLocalPath(value: string): boolean {
  const source = value.trim()
  if (source === '') return false
  return (
    source === '.'
    || source === '..'
    || source.startsWith('./')
    || source.startsWith('.\\')
    || source.startsWith('../')
    || source.startsWith('..\\')
    || source.startsWith('~/')
    || source.startsWith('~\\')
    || source.startsWith('/')
    || source.startsWith('\\\\')
    || /^[A-Za-z]:[\\/]/.test(source)
  )
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  backgroundColor: 'var(--overlay-scrim)'
}

const panelStyle: CSSProperties = {
  backgroundColor: 'var(--bg-secondary)',
  borderRadius: '10px',
  boxShadow: 'var(--shadow-level-3)',
  padding: '24px',
  width: '460px',
  maxWidth: 'calc(100vw - 48px)',
  maxHeight: 'calc(100vh - 96px)',
  overflowY: 'auto'
}

const fieldStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: '6px', marginBottom: '16px' }

const labelStyle: CSSProperties = { fontSize: '12.5px', color: 'var(--text-secondary)' }

const sourceRowStyle: CSSProperties = { display: 'flex', alignItems: 'center', gap: '8px' }

const inputStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  height: '32px',
  boxSizing: 'border-box',
  padding: '0 10px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '13px'
}

const textareaStyle: CSSProperties = {
  boxSizing: 'border-box',
  padding: '8px 10px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  fontSize: '13px',
  fontFamily: 'var(--font-mono)',
  resize: 'vertical'
}

const hintStyle: CSSProperties = { fontSize: '11.5px', color: 'var(--text-tertiary)' }

const errorStyle: CSSProperties = {
  margin: '0 0 16px',
  padding: '10px 12px',
  borderRadius: '8px',
  background: 'color-mix(in srgb, var(--error) 10%, transparent)',
  color: 'var(--error)',
  fontSize: '12.5px',
  lineHeight: 1.5
}

const footerStyle: CSSProperties = { display: 'flex', gap: '8px', justifyContent: 'flex-end' }
