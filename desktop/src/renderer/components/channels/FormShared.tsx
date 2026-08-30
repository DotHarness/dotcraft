import type { ChannelConnectionState } from './ChannelCard'
import { useT } from '../../contexts/LocaleContext'
import { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'
import { Input } from '../ui/Input'

export const formStyles = {
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    marginBottom: '20px'
  } as React.CSSProperties,

  headerTitle: {
    fontSize: '16px',
    fontWeight: 700,
    color: 'var(--text-primary)',
    lineHeight: 1.3
  } as React.CSSProperties,

  label: {
    display: 'block',
    fontSize: '12px',
    fontWeight: 500,
    color: 'var(--text-secondary)',
    marginBottom: '6px'
  } as React.CSSProperties,

  fieldGroup: {
    marginBottom: '14px'
  } as React.CSSProperties
}

interface StatusPillProps {
  status: ChannelConnectionState
  label: string
}

const pillColors: Record<ChannelConnectionState, { bg: string; text: string }> = {
  connected: { bg: 'rgba(52, 199, 89, 0.15)', text: 'var(--success)' },
  enabledNotConnected: { bg: 'rgba(255, 149, 0, 0.15)', text: 'var(--warning)' },
  connecting: { bg: 'rgba(255, 149, 0, 0.15)', text: 'var(--warning)' },
  error: { bg: 'rgba(255, 69, 58, 0.15)', text: 'var(--error, #ff453a)' },
  stopped: { bg: 'var(--bg-tertiary)', text: 'var(--text-dimmed)' },
  notConfigured: { bg: 'var(--bg-tertiary)', text: 'var(--text-dimmed)' }
}

export function StatusPill({ status, label }: StatusPillProps): JSX.Element {
  const colors = pillColors[status]
  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: '5px',
        padding: '2px 8px',
        borderRadius: '10px',
        fontSize: '11px',
        fontWeight: 500,
        backgroundColor: colors.bg,
        color: colors.text,
        marginTop: '3px'
      }}
    >
      <span
        aria-hidden
        style={{
          width: '6px',
          height: '6px',
          borderRadius: '50%',
          backgroundColor: colors.text,
          display: 'inline-block',
          flexShrink: 0
        }}
      />
      {label}
    </span>
  )
}

interface FieldCardProps {
  children: React.ReactNode
}

export function FieldCard({ children }: FieldCardProps): JSX.Element {
  return (
    <div
      style={{
        backgroundColor: 'var(--bg-secondary)',
        borderRadius: '10px',
        border: '1px solid var(--border-default)',
        padding: '16px',
        marginBottom: '12px'
      }}
    >
      {children}
    </div>
  )
}

interface FormActionsProps {
  saving: boolean
  onSave: () => void
}

export function FormActions({ saving, onSave }: FormActionsProps): JSX.Element {
  const t = useT()
  return (
    <Button
      variant="primary"
      onClick={onSave}
      loading={saving}
      style={{ width: '100%', marginTop: '4px' }}
    >
      {saving ? t('channels.saving') : t('channels.save')}
    </Button>
  )
}

interface SecretInputProps {
  value: string
  placeholder?: string
  ariaLabel?: string
  disabled?: boolean
  onChange: (value: string) => void
  /** Monospace value — for keys, tokens, and other literal secrets. */
  mono?: boolean
}

export function SecretInput({
  value,
  placeholder,
  ariaLabel,
  disabled = false,
  onChange,
  mono = false
}: SecretInputProps): JSX.Element {
  const t = useT()
  const [visible, setVisible] = useState(false)

  return (
    <div style={{ position: 'relative' }}>
      <Input
        type={visible ? 'text' : 'password'}
        value={value}
        placeholder={placeholder}
        aria-label={ariaLabel}
        disabled={disabled}
        mono={mono}
        onChange={(event) => onChange(event.target.value)}
        style={{ paddingRight: '36px' }}
      />
      <IconButton
        size={24}
        label={visible ? t('common.hideSecret') : t('common.showSecret')}
        aria-pressed={visible}
        disabled={disabled}
        onClick={() => setVisible((current) => !current)}
        style={{
          position: 'absolute',
          right: '8px',
          top: '50%',
          transform: 'translateY(-50%)',
          borderRadius: 4
        }}
        icon={visible ? <EyeOff size={14} strokeWidth={1.5} aria-hidden /> : <Eye size={14} strokeWidth={1.5} aria-hidden />}
      />
    </div>
  )
}
