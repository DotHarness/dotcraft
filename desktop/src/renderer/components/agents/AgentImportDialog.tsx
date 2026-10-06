import { useId, useMemo, useRef, useState, type DragEvent, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle, Box, Plug, Upload } from 'lucide-react'
import { LayerBoundary } from '../../contexts/LayerContext'
import { useT } from '../../contexts/LocaleContext'
import { SegmentedControl } from '../settings/ui/SegmentedControl'
import { useImportDialogFocus } from '../settings/panels/useImportDialogFocus'
import { Button } from '../ui/Button'
import { Checkbox } from '../ui/Checkbox'
import { Input, Textarea } from '../ui/Input'
import { ModalHeader } from '../ui/ModalHeader'
import { RobotAvatar } from './RobotAvatar'
import {
  AGENT_PACKAGE_MAX_BYTES,
  commitAgentImport,
  discardAgentImport,
  installable,
  packageKey,
  uploadAgentPackage,
  type AgentImportPackage,
  type AgentImportPreview
} from './agentPackages'
import styles from './AgentPackageDialog.module.css'

type Translate = ReturnType<typeof useT>

interface AgentImportDialogProps {
  onImported: (profile: { id: string; source: string }, dotnetPluginIds: string[]) => void
  onClose: () => void
}

export function AgentImportDialog({ onImported, onClose }: AgentImportDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const inputRef = useRef<HTMLInputElement>(null)
  const [preview, setPreview] = useState<AgentImportPreview | null>(null)
  const [reading, setReading] = useState(false)
  const [dragging, setDragging] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [source, setSource] = useState<'user' | 'workspace'>('workspace')
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const close = (): void => {
    if (preview) void discardAgentImport(preview.importId).catch(() => undefined)
    onClose()
  }
  useImportDialogFocus(dialogRef, close, busy || reading)

  async function read(file: File | undefined): Promise<void> {
    if (!file || reading) return
    setError(null)
    if (!/\.(zip|md)$/i.test(file.name)) {
      setError(t('agentBuilder.import.typeError'))
      return
    }
    if (file.size > AGENT_PACKAGE_MAX_BYTES) {
      setError(t('agentBuilder.import.sizeError'))
      return
    }
    setReading(true)
    try {
      const next = await uploadAgentPackage(file)
      setPreview(next)
      setName(next.name)
      setDescription(next.description ?? '')
      setSelected(new Set(next.packages.filter(installable).map(packageKey)))
    } catch (err) {
      setError(t('agentBuilder.import.readFailed', { error: err instanceof Error ? err.message : String(err) }))
    } finally {
      setReading(false)
    }
  }

  function chooseAnother(): void {
    if (preview) void discardAgentImport(preview.importId).catch(() => undefined)
    setPreview(null)
    setError(null)
  }

  function onDrop(event: DragEvent<HTMLButtonElement>): void {
    event.preventDefault()
    setDragging(false)
    void read(event.dataTransfer.files[0])
  }

  const chosen = useMemo(
    () => preview?.packages.filter((pkg) => installable(pkg) && selected.has(packageKey(pkg))) ?? [],
    [preview, selected]
  )
  const trimmedName = name.trim()
  const nameTaken = preview != null && preview.nameTaken && trimmedName === preview.name.trim()
  const canImport = preview != null && trimmedName.length > 0 && !nameTaken && preview.problems.length === 0

  async function confirm(): Promise<void> {
    if (!preview || !canImport) return
    setBusy(true)
    setError(null)
    try {
      const result = await commitAgentImport({
        importId: preview.importId,
        name: trimmedName,
        description: description.trim(),
        source,
        packages: chosen.map(({ kind, name: pkgName }) => ({ kind, name: pkgName }))
      })
      onImported(result.profile, chosen.filter((pkg) => pkg.kind === 'plugin' && pkg.dotnet).map((pkg) => pkg.name))
    } catch (err) {
      setError(t('agentBuilder.import.failed', { error: err instanceof Error ? err.message : String(err) }))
      setBusy(false)
    }
  }

  const unresolved = preview
    ? [...preview.unresolved.skills, ...preview.unresolved.mcpServers, ...preview.unresolved.plugins]
    : []

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className={styles.scrim}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy && !reading) close()
      }}
    >
      <div ref={dialogRef} className={styles.dialog} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={preview ? <RobotAvatar name={trimmedName} size={36} /> : <Upload size={18} strokeWidth={2} aria-hidden />}
          badgedIcon={!preview}
          title={t('agentBuilder.import.title')}
          titleId={titleId}
          description={preview ? t('agentBuilder.import.reviewDescription') : t('agentBuilder.import.description')}
          onClose={busy || reading ? undefined : close}
        />

        {preview ? (
          <div className={styles.body}>
            <label className={styles.field}>
              <span className={styles.fieldLabel}>{t('agentBuilder.import.name')}</span>
              <Input value={name} invalid={nameTaken} disabled={busy} onChange={(event) => setName(event.target.value)} />
              {nameTaken && <span className={styles.fieldError}>{t('agentBuilder.import.nameTaken')}</span>}
            </label>
            <label className={styles.field}>
              <span className={styles.fieldLabel}>{t('agentBuilder.import.descriptionLabel')}</span>
              <Textarea rows={2} value={description} disabled={busy} onChange={(event) => setDescription(event.target.value)} />
            </label>
            <div className={styles.fieldRow}>
              <span className={styles.fieldLabel}>{t('agentBuilder.import.saveTo')}</span>
              <SegmentedControl
                value={source}
                ariaLabel={t('agentBuilder.import.saveTo')}
                disabled={busy}
                onChange={setSource}
                options={[
                  { value: 'user', label: t('agentBuilder.import.saveToUser') },
                  { value: 'workspace', label: t('agentBuilder.import.saveToWorkspace') }
                ]}
              />
            </div>

            {preview.problems.length > 0 && (
              <div className={styles.section}>
                <span className={styles.sectionTitle}>{t('agentBuilder.import.problems')}</span>
                <ul className={styles.problems}>
                  {preview.problems.map((problem) => <li key={problem}>{problem}</li>)}
                </ul>
              </div>
            )}

            {preview.packages.length > 0 && (
              <div className={styles.section}>
                <span className={styles.sectionTitle}>{t('agentBuilder.import.packages')}</span>
                <div className={styles.rows}>
                  {preview.packages.map((pkg) => (
                    <ImportPackageRow
                      key={packageKey(pkg)}
                      pkg={pkg}
                      checked={selected.has(packageKey(pkg))}
                      disabled={busy}
                      t={t}
                      onChange={(checked) => setSelected((previous) => {
                        const next = new Set(previous)
                        if (checked) next.add(packageKey(pkg))
                        else next.delete(packageKey(pkg))
                        return next
                      })}
                    />
                  ))}
                </div>
              </div>
            )}

            {unresolved.length > 0 && (
              <p className={styles.warning}>
                <AlertTriangle size={14} strokeWidth={2} aria-hidden />
                <span>{t('agentBuilder.import.unresolved', { names: unresolved.join(', ') })}</span>
              </p>
            )}
          </div>
        ) : (
          <div className={styles.body}>
            <button
              type="button"
              className={styles.drop}
              data-active={dragging || undefined}
              data-busy={reading || undefined}
              disabled={reading}
              onClick={() => inputRef.current?.click()}
              onDragOver={(event) => {
                event.preventDefault()
                setDragging(true)
              }}
              onDragLeave={() => setDragging(false)}
              onDrop={onDrop}
            >
              <Upload size={20} strokeWidth={1.8} aria-hidden />
              <span className={styles.dropTitle}>{reading ? t('agentBuilder.import.reading') : t('agentBuilder.import.dropPrompt')}</span>
              <span className={styles.dropHint}>{t('agentBuilder.import.sizeLimit')}</span>
            </button>
            <input
              ref={inputRef}
              type="file"
              accept=".zip,.md,application/zip,text/markdown"
              hidden
              onChange={(event) => {
                void read(event.target.files?.[0])
                event.target.value = ''
              }}
            />
            <p className={styles.warning}>
              <AlertTriangle size={14} strokeWidth={2} aria-hidden />
              <span>{t('agentBuilder.import.trustWarning')}</span>
            </p>
          </div>
        )}

        {error && <p role="alert" className={styles.error}>{error}</p>}

        {preview && (
          <div className={styles.footer}>
            <Button variant="ghost" disabled={busy} onClick={chooseAnother}>
              {t('agentBuilder.import.another')}
            </Button>
            <Button variant="secondary" disabled={busy} onClick={close}>
              {t('common.cancel')}
            </Button>
            <Button variant="primary" loading={busy} disabled={!canImport} onClick={() => void confirm()}>
              {t('agentBuilder.import.confirm')}
            </Button>
          </div>
        )}
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

function ImportPackageRow({ pkg, checked, disabled, t, onChange }: {
  pkg: AgentImportPackage
  checked: boolean
  disabled: boolean
  t: Translate
  onChange: (checked: boolean) => void
}): JSX.Element {
  const checkboxId = useId()
  const canInstall = installable(pkg)
  const hint = [
    t(pkg.kind === 'skill' ? 'agentBuilder.package.skill' : 'agentBuilder.package.plugin'),
    importStateHint(pkg, t),
    pkg.dotnet ? t('agentBuilder.package.dotnet') : null
  ].filter(Boolean).join(' · ')
  return (
    <div className={styles.row}>
      <span className={styles.rowIcon} aria-hidden="true">
        {pkg.kind === 'skill' ? <Box size={16} strokeWidth={1.8} /> : <Plug size={16} strokeWidth={1.8} />}
      </span>
      <label htmlFor={canInstall ? checkboxId : undefined} className={styles.rowText}>
        <span className={styles.rowTitle}>{pkg.displayName || pkg.name}</span>
        <span className={styles.rowHint}>{hint}</span>
      </label>
      {canInstall ? (
        <Checkbox id={checkboxId} checked={checked} disabled={disabled} ariaLabel={pkg.displayName || pkg.name} onChange={onChange} />
      ) : (
        <span className={styles.rowStatus}>
          {pkg.state === 'installed' ? t('agentBuilder.import.state.installedShort') : t('agentBuilder.import.state.unavailableShort')}
        </span>
      )}
    </div>
  )
}

function importStateHint(pkg: AgentImportPackage, t: Translate): string {
  const marketplace = pkg.marketplaceName ?? ''
  switch (pkg.state) {
    case 'installed':
      return pkg.installedVersion
        ? t('agentBuilder.import.state.installedVersion', { version: pkg.installedVersion })
        : t('agentBuilder.import.state.installed')
    case 'bundled':
      return t('agentBuilder.import.state.bundled')
    case 'marketplace':
      return t('agentBuilder.import.state.marketplace', { marketplace })
    case 'addMarketplace':
      return t('agentBuilder.import.state.addMarketplace', { marketplace })
    default:
      return pkg.reason === 'localMarketplace'
        ? t('agentBuilder.import.state.localMarketplace', { marketplace })
        : t('agentBuilder.import.state.notOffered', { marketplace })
  }
}
