import { GitBranch, ListChecks } from 'lucide-react'
import { useEffect, useId, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { Input, Textarea } from '../ui/Input'
import { Button } from '../ui/Button'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import styles from './ComposerWorkspaceFooterDialogs.module.css'

export function normalizeBranchName(value: string): string {
  return value.trim().replace(/^\/+/, '')
}

export function branchNameError(value: string, t: ReturnType<typeof useT>): string | null {
  const branch = normalizeBranchName(value)
  if (!branch) return t('workspaceFooter.branchRequired')
  if (branch.endsWith('/')) return t('workspaceFooter.branchCannotEndSlash')
  return null
}

export function CreateBranchDialog({
  value,
  busy,
  title,
  confirmLabel,
  onChange,
  onCancel,
  onConfirm
}: {
  value: string
  busy: boolean
  title: string
  confirmLabel: string
  onChange: (value: string) => void
  onCancel: () => void
  onConfirm: () => void
}): JSX.Element {
  const t = useT()
  const titleId = useId()
  const error = branchNameError(value, t)

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
      if (event.key === 'Enter' && !error && !busy) onConfirm()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [busy, error, onCancel, onConfirm])

  return (
    <ModalDialog
      titleId={titleId}
      width={420}
      onDismiss={onCancel}
      header={<ModalHeader icon={<GitBranch size={18} strokeWidth={2} aria-hidden />} title={title} titleId={titleId} />}
      error={error}
      footer={(
        <ModalFooter>
          <Button variant="primary" disabled={Boolean(error) || busy} onClick={onConfirm}>
            {confirmLabel}
          </Button>
        </ModalFooter>
      )}
    >
      <label className={styles.field}>
        {t('workspaceFooter.branchName')}
        <Input
          frameless
          value={value}
          autoFocus
          onChange={(e) => onChange(e.target.value)}
          className={styles.input}
        />
      </label>
    </ModalDialog>
  )
}

export function CreateChangelistDialog({
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
  const titleId = useId()

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
      if ((event.metaKey || event.ctrlKey) && event.key === 'Enter' && !busy) onConfirm()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [busy, onCancel, onConfirm])

  return (
    <ModalDialog
      titleId={titleId}
      width={420}
      onDismiss={onCancel}
      header={(
        <ModalHeader
          icon={<ListChecks size={18} strokeWidth={2} aria-hidden />}
          title={t('workspaceFooter.createChangelistTitle')}
          titleId={titleId}
        />
      )}
      footer={(
        <ModalFooter>
          <Button variant="primary" disabled={busy} onClick={onConfirm}>
            {t('workspaceFooter.create')}
          </Button>
        </ModalFooter>
      )}
    >
      <label className={styles.field}>
        {t('workspaceFooter.changelistDescription')}
        <Textarea
          frameless
          value={value}
          autoFocus
          rows={4}
          onChange={(e) => onChange(e.target.value)}
          className={`${styles.input} ${styles.textarea}`}
        />
      </label>
    </ModalDialog>
  )
}
