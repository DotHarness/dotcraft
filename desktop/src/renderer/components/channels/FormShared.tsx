import type { ChannelConnectionState } from './ChannelCard'
import { useT } from '../../contexts/LocaleContext'
import { useState } from 'react'
import { Eye, EyeOff } from 'lucide-react'

// ─── Shared style helpers ────────────────────────────────────────────────────

function inputFocusHandler(
  e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>
): void {
  const el = e.currentTarget
  el.style.borderColor = 'var(--accent)'
  el.style.boxShadow = '0 0 0 2px rgba(74,127,165,0.15)'
}

function inputBlurHandler(
  e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>
): void {
  const el = e.currentTarget
  el.style.borderColor = 'var(--border-default)'
  el.style.boxShadow = 'none'
}

export const formStyles = {
  header: {
    display: 'flex',
    alignItems: 'center',
    gap: '12px',
    marginBottom: '20px'
  } as React.CSSProperties,

  headerLogo: {
    borderRadius: '8px',
    flexShrink: 0,
    backgroundColor: 'var(--bg-secondary)'
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

  input: {
    width: '100%',
    boxSizing: 'border-box',
    height: '36px',
    padding: '0 10px',
    fontSize: '13px',
    borderRadius: '6px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    color: 'var(--text-primary)',
    outline: 'none',
    transition: 'border-color 120ms ease, box-shadow 120ms ease'
  } as React.CSSProperties,

  fieldGroup: {
    marginBottom: '14px'
  } as React.CSSProperties,

  inputFocus: inputFocusHandler,
  inputBlur: inputBlurHandler
}

// ─── StatusPill ──────────────────────────────────────────────────────────────

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

// ─── FieldCard ───────────────────────────────────────────────────────────────

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

// ─── FormActions ─────────────────────────────────────────────────────────────

interface FormActionsProps {
  saving: boolean
  onSave: () => void
}

export function FormActions({ saving, onSave }: FormActionsProps): JSX.Element {
  const t = useT()
  return (
    <button
      type="button"
      onClick={onSave}
      disabled={saving}
      style={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: '8px',
        width: '100%',
        height: '38px',
        border: 'none',
        borderRadius: '8px',
        backgroundColor: saving ? 'var(--border-active)' : 'var(--accent)',
        color: saving ? 'var(--text-secondary)' : 'var(--on-accent)',
        fontSize: '13px',
        fontWeight: 600,
        cursor: saving ? 'default' : 'pointer',
        transition: 'background-color 120ms ease',
        marginTop: '4px'
      }}
      onMouseEnter={(e) => {
        if (!saving) {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent-hover)'
        }
      }}
      onMouseLeave={(e) => {
        if (!saving) {
          ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--accent)'
        }
      }}
    >
      {saving && (
        <svg
          width="14"
          height="14"
          viewBox="0 0 14 14"
          style={{ animation: 'spin 0.8s linear infinite' }}
          aria-hidden
        >
          <circle cx="7" cy="7" r="5" fill="none" stroke="currentColor" strokeWidth="2" strokeDasharray="20" strokeDashoffset="10" />
        </svg>
      )}
      {saving ? t('channels.saving') : t('channels.save')}
    </button>
  )
}

interface SecretInputProps {
  value: string
  placeholder?: string
  ariaLabel?: string
  disabled?: boolean
  onChange: (value: string) => void
  onFocus?: (
    e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>
  ) => void
  onBlur?: (
    e: React.FocusEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>
  ) => void
  style?: React.CSSProperties
}

export function SecretInput({
  value,
  placeholder,
  ariaLabel,
  disabled = false,
  onChange,
  onFocus,
  onBlur,
  style
}: SecretInputProps): JSX.Element {
  const t = useT()
  const [visible, setVisible] = useState(false)

  return (
    <div style={{ position: 'relative' }}>
      <input
        type={visible ? 'text' : 'password'}
        value={value}
        placeholder={placeholder}
        aria-label={ariaLabel}
        disabled={disabled}
        onChange={(event) => onChange(event.target.value)}
        onFocus={onFocus}
        onBlur={onBlur}
        style={{
          ...formStyles.input,
          paddingRight: '36px',
          opacity: disabled ? 0.65 : 1,
          cursor: disabled ? 'not-allowed' : 'text',
          ...style
        }}
      />
      <button
        type="button"
        aria-label={visible ? t('common.hideSecret') : t('common.showSecret')}
        aria-pressed={visible}
        disabled={disabled}
        onClick={() => setVisible((current) => !current)}
        style={{
          position: 'absolute',
          right: '8px',
          top: '50%',
          transform: 'translateY(-50%)',
          border: 'none',
          background: 'transparent',
          color: 'var(--text-secondary)',
          display: 'inline-flex',
          alignItems: 'center',
          justifyContent: 'center',
          cursor: disabled ? 'not-allowed' : 'pointer',
          opacity: disabled ? 0.45 : 0.85,
          transition: 'opacity 120ms ease'
        }}
        onMouseEnter={(event) => {
          if (!disabled) event.currentTarget.style.opacity = '1'
        }}
        onMouseLeave={(event) => {
          event.currentTarget.style.opacity = disabled ? '0.45' : '0.85'
        }}
      >
        {visible ? <EyeOff size={14} strokeWidth={1.5} aria-hidden /> : <Eye size={14} strokeWidth={1.5} aria-hidden />}
      </button>
    </div>
  )
}
