import { useEffect, useId, useMemo, useRef, useState, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { Box, Download, Plug } from 'lucide-react'
import { LayerBoundary } from '../../contexts/LayerContext'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useImportDialogFocus } from '../settings/panels/useImportDialogFocus'
import { Button } from '../ui/Button'
import { Checkbox } from '../ui/Checkbox'
import { ModalHeader } from '../ui/ModalHeader'
import { packageKey, planAgentExport, readAgentExport, type AgentExportPackage, type AgentExportPlan } from './agentPackages'
import styles from './AgentPackageDialog.module.css'

interface AgentExportDialogProps {
  id: string
  source: string
  onClose: () => void
}

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

export function AgentExportDialog({ id, source, onClose }: AgentExportDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const dialogRef = useRef<HTMLDivElement>(null)
  const [plan, setPlan] = useState<AgentExportPlan | null>(null)
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useImportDialogFocus(dialogRef, onClose, busy)

  useEffect(() => {
    let cancelled = false
    planAgentExport(id, source)
      .then((next) => {
        if (cancelled) return
        setPlan(next)
        setSelected(new Set(next.packages.filter((pkg) => pkg.reasons.length > 0).map(packageKey)))
      })
      .catch((err) => {
        if (!cancelled) setError(t('agentBuilder.export.failed', { error: err instanceof Error ? err.message : String(err) }))
      })
    return () => { cancelled = true }
  }, [id, source, t])

  const embeddedBytes = useMemo(
    () => plan?.packages.filter((pkg) => !pkg.marketplaceName && selected.has(packageKey(pkg))).reduce((sum, pkg) => sum + pkg.bytes, 0) ?? 0,
    [plan, selected]
  )
  const tooLarge = plan != null && embeddedBytes > plan.maximumBytes

  async function confirm(): Promise<void> {
    if (!plan || tooLarge) return
    setBusy(true)
    setError(null)
    try {
      const packages = plan.packages.filter((pkg) => selected.has(packageKey(pkg))).map(({ kind, name }) => ({ kind, name }))
      const bytes = await readAgentExport(id, source, packages)
      const { saved } = await window.api.shell.saveFileAs({ data: bytes, suggestedName: plan.fileName })
      if (saved) {
        addToast(t('agentBuilder.export.done'), 'success')
        onClose()
      } else {
        setBusy(false)
      }
    } catch (err) {
      setError(t('agentBuilder.export.failed', { error: err instanceof Error ? err.message : String(err) }))
      setBusy(false)
    }
  }

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby={titleId}
      className={styles.scrim}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && !busy) onClose()
      }}
    >
      <div ref={dialogRef} className={styles.dialog} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<Download size={18} strokeWidth={2} aria-hidden />}
          title={t('agentBuilder.export.title', { name: id })}
          titleId={titleId}
          description={t('agentBuilder.export.description')}
          onClose={busy ? undefined : onClose}
        />

        <div className={styles.body}>
          {plan == null ? (
            !error && <p className={styles.note}>{t('agentBuilder.export.loading')}</p>
          ) : plan.packages.length === 0 ? (
            <p className={styles.note}>{t('agentBuilder.export.noPackages')}</p>
          ) : (
            <div className={styles.section}>
              <span className={styles.sectionTitle}>{t('agentBuilder.export.packages')}</span>
              <div className={styles.rows}>
                {plan.packages.map((pkg) => (
                  <ExportPackageRow
                    key={packageKey(pkg)}
                    pkg={pkg}
                    checked={selected.has(packageKey(pkg))}
                    disabled={busy}
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
          {plan != null && <p className={styles.note}>{t('agentBuilder.export.mcpNote')}</p>}
        </div>

        {(error || tooLarge) && (
          <p role="alert" className={styles.error}>{error ?? t('agentBuilder.export.tooLarge')}</p>
        )}

        <div className={styles.footer}>
          <span className={styles.footerNote}>
            {plan != null && t('agentBuilder.export.size', { size: formatBytes(embeddedBytes), limit: formatBytes(plan.maximumBytes) })}
          </span>
          <Button variant="secondary" disabled={busy} onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button variant="primary" loading={busy} disabled={plan == null || tooLarge} onClick={() => void confirm()}>
            {t('agentBuilder.export.confirm')}
          </Button>
        </div>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

function ExportPackageRow({ pkg, checked, disabled, onChange }: {
  pkg: AgentExportPackage
  checked: boolean
  disabled: boolean
  onChange: (checked: boolean) => void
}): JSX.Element {
  const t = useT()
  const checkboxId = useId()
  const hint = [
    t(pkg.kind === 'skill' ? 'agentBuilder.package.skill' : 'agentBuilder.package.plugin'),
    pkg.reasons.length > 0 ? t('agentBuilder.export.used') : null,
    pkg.marketplaceName ? t('agentBuilder.export.reference', { marketplace: pkg.marketplaceName }) : formatBytes(pkg.bytes),
    pkg.dotnet ? t('agentBuilder.package.dotnet') : null
  ].filter(Boolean).join(' · ')
  return (
    <div className={styles.row}>
      <span className={styles.rowIcon} aria-hidden="true">
        {pkg.kind === 'skill' ? <Box size={16} strokeWidth={1.8} /> : <Plug size={16} strokeWidth={1.8} />}
      </span>
      <label htmlFor={checkboxId} className={styles.rowText}>
        <span className={styles.rowTitle}>{pkg.displayName || pkg.name}</span>
        <span className={styles.rowHint}>{hint}</span>
      </label>
      <Checkbox id={checkboxId} checked={checked} disabled={disabled} ariaLabel={pkg.displayName || pkg.name} onChange={onChange} />
    </div>
  )
}
