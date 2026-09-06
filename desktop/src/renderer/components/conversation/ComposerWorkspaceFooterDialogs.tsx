import { useEffect, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { Input, Textarea } from '../ui/Input'
import { Button } from '../ui/Button'

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
    <div
      role="dialog"
      aria-modal="true"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onCancel()
      }}
    >
      <div
        style={{
          width: '420px',
          maxWidth: 'calc(100vw - 48px)',
          padding: '22px',
          borderRadius: '10px',
          background: 'var(--bg-secondary)',
          boxShadow: 'var(--shadow-level-3)'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 style={{ margin: '0 0 16px', fontSize: '18px', color: 'var(--text-primary)' }}>{title}</h2>
        <label style={{ display: 'grid', gap: '8px', color: 'var(--text-primary)', fontSize: '13px', fontWeight: 600 }}>
          {t('workspaceFooter.branchName')}
          <Input
            frameless
            value={value}
            autoFocus
            onChange={(e) => onChange(e.target.value)}
            style={{
              height: '42px',
              borderRadius: '8px',
              background: 'var(--bg-tertiary)',
              padding: '0 12px',
              font: 'inherit'
            }}
          />
        </label>
        {error && <div style={{ marginTop: '8px', color: 'var(--error)', fontSize: '12px' }}>{error}</div>}
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '20px' }}>
          <Button
            variant="primary"
            disabled={Boolean(error) || busy}
            onClick={onConfirm}
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
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

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
      if ((event.metaKey || event.ctrlKey) && event.key === 'Enter' && !busy) onConfirm()
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [busy, onCancel, onConfirm])

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="create-changelist-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onCancel()
      }}
    >
      <div
        style={{
          width: '420px',
          maxWidth: 'calc(100vw - 48px)',
          padding: '22px',
          borderRadius: '10px',
          background: 'var(--bg-secondary)',
          boxShadow: 'var(--shadow-level-3)'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2 id="create-changelist-title" style={{ margin: '0 0 16px', fontSize: '18px', color: 'var(--text-primary)' }}>
          {t('workspaceFooter.createChangelistTitle')}
        </h2>
        <label style={{ display: 'grid', gap: '8px', color: 'var(--text-primary)', fontSize: '13px', fontWeight: 600 }}>
          {t('workspaceFooter.changelistDescription')}
          <Textarea
            frameless
            value={value}
            autoFocus
            rows={4}
            onChange={(e) => onChange(e.target.value)}
            style={{
              minHeight: '96px',
              borderRadius: '8px',
              background: 'var(--bg-tertiary)',
              padding: '10px 12px',
              font: 'inherit'
            }}
          />
        </label>
        <div style={{ display: 'flex', justifyContent: 'flex-end', gap: '10px', marginTop: '20px' }}>
          <Button
            variant="primary"
            disabled={busy}
            onClick={onConfirm}
          >
            {t('workspaceFooter.create')}
          </Button>
        </div>
      </div>
    </div>
  )
}
