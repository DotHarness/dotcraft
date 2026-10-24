import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { FolderOpen, Store } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { usePluginStore, type MarketplaceEntry } from '../../stores/pluginStore'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { Input, Textarea } from '../ui/Input'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import styles from './AddMarketplaceDialog.module.css'

interface AddMarketplaceDialogProps {
  /** Hides the local-folder picker, which cannot reach a remote workspace host. */
  allowLocalFolder: boolean
  onClose: () => void
  onAdded: (marketplace: MarketplaceEntry, alreadyAdded: boolean) => void
}

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

  return (
    <ModalDialog
      titleId={titleId}
      width={460}
      onDismiss={busy ? undefined : onClose}
      onSubmit={() => void handleSubmit()}
      bodyClassName={styles.body}
      header={(
        <ModalHeader
          icon={<Store size={18} aria-hidden />}
          title={t('plugins.marketplace.add.title')}
          titleId={titleId}
          description={t('plugins.marketplace.add.description')}
          onClose={busy ? undefined : onClose}
          closeLabel={t('common.cancel')}
        />
      )}
      error={error}
      footer={(
        <ModalFooter>
          <Button variant="secondary" disabled={busy} onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button type="submit" variant="primary" loading={busy} disabled={!canSubmit}>
            {busy ? t('plugins.marketplace.add.submitting') : t('plugins.marketplace.add.submit')}
          </Button>
        </ModalFooter>
      )}
    >
      <div className={styles.field}>
        <label htmlFor={sourceId} className={styles.label}>{t('plugins.marketplace.add.source')}</label>
        <div className={styles.sourceRow}>
          <Input
            ref={sourceRef}
            id={sourceId}
            value={source}
            disabled={busy}
            placeholder={t('plugins.marketplace.add.sourcePlaceholder')}
            onChange={(event) => setSource(event.target.value)}
            className={styles.grow}
          />
          {allowLocalFolder && (
            <IconButton
              label={t('plugins.marketplace.add.browse')}
              tooltipLabel={t('plugins.marketplace.add.browse')}
              disabled={busy}
              icon={<FolderOpen size={16} aria-hidden />}
              onClick={() => void handlePickFolder()}
            />
          )}
        </div>
      </div>

      <div className={styles.field}>
        <label htmlFor={refId} className={styles.label}>{t('plugins.marketplace.add.ref')}</label>
        <Input
          id={refId}
          value={ref}
          disabled={busy || localSource}
          placeholder={t('plugins.marketplace.add.refPlaceholder')}
          onChange={(event) => setRef(event.target.value)}
        />
      </div>

      <div className={styles.field}>
        <label htmlFor={sparseId} className={styles.label}>{t('plugins.marketplace.add.sparsePaths')}</label>
        <Textarea
          id={sparseId}
          mono
          value={sparsePaths}
          disabled={busy || localSource}
          rows={4}
          placeholder={t('plugins.marketplace.add.sparsePathsPlaceholder')}
          onChange={(event) => setSparsePaths(event.target.value)}
        />
        <span className={styles.hint}>{t('plugins.marketplace.add.sparsePathsHint')}</span>
      </div>
    </ModalDialog>
  )
}

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
